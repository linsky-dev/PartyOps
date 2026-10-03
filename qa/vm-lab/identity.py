"""采集 Guest 原始系统身份，校验原版介质和干净冷启动快照。"""
from __future__ import annotations

import base64
import json
import re
import shlex
import time
import uuid
from pathlib import Path

from evidence import now, sha256, write_json

LINUX_PROBE = r'''
import json, os, pathlib, platform, subprocess
def read(name):
    p = pathlib.Path(name)
    return p.read_text(errors='replace') if p.exists() else ''
def command(args):
    p = subprocess.run(args, stdout=subprocess.PIPE, stderr=subprocess.PIPE, universal_newlines=True)
    return p.stdout.strip() if p.returncode == 0 else ''
print(json.dumps(dict(os='linux', arch=platform.machine(), uid=os.getuid(),
    boot_id=read('/proc/sys/kernel/random/boot_id').strip(), marker=read('/etc/partyops-vm-lab.json'),
    hardware_uuid=command(['sudo','-n','cat','/sys/class/dmi/id/product_uuid']),
    os_release_raw=read('/etc/os-release'), os_version_raw=read('/etc/os-version'),
    deepin_version_raw=read('/etc/deepin-version'),
    system_release_raw=read('/etc/openEuler-release'),
    package_arch=command(['sh','-c','if command -v dpkg >/dev/null; then dpkg --print-architecture; else rpm --eval "%{_arch}"; fi']),
    installed_package=pathlib.Path('/opt/partyops').exists(),
    graphical_session=command(['loginctl','list-sessions','--no-legend']))))
'''


def parse_fields(raw: str) -> dict:
    result = {}
    for line in raw.splitlines():
        if "=" in line and not line.lstrip().startswith("#"):
            key, value = line.split("=", 1)
            result[key.strip()] = value.strip().strip('"\'')
    return result


def normalize_linux(raw: dict) -> dict:
    """不通过 ID_LIKE 把 Deepin/Debian 当成 UOS。"""
    fields = parse_fields(raw.get("os_release_raw", ""))
    detail = parse_fields(raw.get("os_version_raw", ""))
    distro = fields.get("ID", "").casefold()
    result = {**raw, "distribution_id": distro,
              "arch": {"loong64": "loongarch64"}.get(raw.get("arch"), raw.get("arch")),
              "distribution": {"uos": "UOS", "openeuler": "openEuler", "deepin": "Deepin"}.get(distro, fields.get("NAME", distro)),
              "os_release": fields.get("VERSION_ID", "")}
    # 派生身份只根据上面读取的系统文件计算，不接受 payload 中预填的构建号和版本类型。
    result.pop("os_build", None)
    result.pop("edition", None)
    if distro == "uos":
        result["os_build"] = detail.get("MinorVersion", "")
        description = " ".join(str(value) for value in [fields.get("VERSION", ""), detail.get("EditionName", ""), detail.get("EditionName[en_US]", "")])
        result["edition"] = "Professional" if re.search(r"professional|专业", description, re.IGNORECASE) else description.strip()
    elif distro == "openeuler":
        description = fields.get("VERSION", "") + " " + raw.get("system_release_raw", "")
        match = re.search(r"LTS[ -]SP(\d+)", description, re.IGNORECASE)
        result["os_build"] = "LTS-SP" + match[1] if match else ""
    elif distro == "deepin":
        # 25 系发行版号和具体更新版本分别取自 Guest 文件；不使用目标名或 ISO 元数据补值。
        versions = {str(value).strip() for value in (fields.get("VERSION_ID", ""), fields.get("BUILD_ID", ""),
                    detail.get("MinorVersion", ""), raw.get("deepin_version_raw", ""))
                    if re.fullmatch(r"\d+\.\d+\.\d+", str(value).strip())}
        if len(versions) > 1:
            raise RuntimeError("GUEST_DEEPIN_VERSION_FIELDS_CONFLICT")
        if versions:
            result["os_build"] = versions.pop()
        version_id = fields.get("VERSION_ID", "")
        if re.fullmatch(r"25(?:\.\d+){0,2}", version_id):
            result["os_release"] = "25"
    return result


def validate_identity(system: dict, spec: dict, state: dict) -> None:
    if spec.get("backend") == "native-host":
        from native_windows import validate_identity as validate_native_identity
        validate_native_identity(system, spec)
        return
    for field in ("os", "arch", "distribution", "distribution_id", "os_release", "os_build", "edition"):
        if spec.get(field) and system.get(field) != spec[field]:
            raise RuntimeError(f"GUEST_IDENTITY_MISMATCH:{field}; expected={spec[field]}; actual={system.get(field)}")
    if not system.get("boot_id"):
        raise RuntimeError("GUEST_BOOT_ID_MISSING")
    if str(system.get("hardware_uuid", "")).casefold() != state["uuid"].casefold():
        raise RuntimeError("GUEST_HARDWARE_UUID_MISMATCH")
    if system["os"] == "windows":
        from windows_execution import validate_os_identity
        validate_os_identity(system, spec)
        marker = system.get("marker", {})
        if not isinstance(marker, dict) or marker.get("uuid") != state["uuid"] or marker.get("purpose") != "disposable-qa":
            raise RuntimeError("GUEST_OWNERSHIP_MISMATCH")
    if system["os"] == "linux":
        if system.get("uid") == 0:
            raise RuntimeError("STANDARD_USER_REQUIRED")
        marker = json.loads(system.get("marker", "{}"))
        if marker != {"uuid": state["uuid"], "purpose": "disposable-qa"}:
            raise RuntimeError("GUEST_OWNERSHIP_MISMATCH")
        expected_arch = {"x86_64": {"amd64", "x86_64"}, "aarch64": {"arm64", "aarch64"}, "loongarch64": {"loong64"}}
        if system.get("package_arch") not in expected_arch.get(spec["arch"], set()):
            raise RuntimeError("GUEST_PACKAGE_MANAGER_ARCH_MISMATCH")
        if spec.get("package_arch") and system.get("package_arch") != spec["package_arch"]:
            raise RuntimeError("GUEST_PACKAGE_MANAGER_ARCH_MISMATCH")


def probe(lab, target: str) -> dict:
    if lab.matrix["targets"][target].get("backend") == "native-host":
        from native_windows import probe as native_probe
        return native_probe(lab, target)
    state = lab.state(target)
    if not lab.live(state):
        raise RuntimeError("OWNED_GUEST_NOT_RUNNING")
    spec = lab.matrix["targets"][target]
    if spec["os"] == "linux":
        output = lab.ssh(target, "python3 -c " + shlex.quote(LINUX_PROBE), timeout=45)
        result = normalize_linux(json.loads(output))
    elif spec["os"] == "windows":
        # WMI 支持 Windows 7 SP1 PowerShell 2.0，不依赖新版 Cmdlet。
        script = r'''$ErrorActionPreference='Stop'
[Console]::OutputEncoding=New-Object Text.UTF8Encoding($false)
$s=Get-WmiObject Win32_OperatingSystem
$c=Get-WmiObject Win32_ComputerSystemProduct
__WINDOWS_MACHINE_PROBE__
$r=if($s.Caption -match 'Windows 11'){'11'}elseif($s.Caption -match 'Windows 10'){'10'}elseif($s.Caption -match 'Windows 7' -and $s.ServicePackMajorVersion -eq 1){'7 SP1'}else{$s.Caption}
$marker=[IO.File]::ReadAllText('C:\ProgramData\PartyOps-VM-Lab\identity.json')
# 同时检查 32/64 位安装登记与服务，避免自定义中文路径被当作未安装。
$installed=$false
foreach($root in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall','HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall')) {
  if(Test-Path -LiteralPath $root) {
    foreach($entry in @(Get-ItemProperty -Path ($root+'\*') -ErrorAction SilentlyContinue)) {
      if($entry.PSChildName -eq '{1C8EFC63-CAFC-46EF-A5E3-D3D119B5BB3A}_is1' -or $entry.DisplayName -match 'PartyOps') { $installed=$true }
    }
  }
}
foreach($root in @($env:ProgramFiles,${env:ProgramFiles(x86)})) {
  if($root -and (Test-Path -LiteralPath (Join-Path $root 'PartyOps'))) { $installed=$true }
}
if(@(Get-Service -Name 'PartyOpsHost','PartyOpsUpdateService' -ErrorAction SilentlyContinue).Count -gt 0) { $installed=$true }
$secure=$false
try { $secure=Confirm-SecureBootUEFI -ErrorAction Stop } catch {}
$tpmVersion=''
try { $tpm=Get-WmiObject -Namespace 'root\CIMV2\Security\MicrosoftTpm' -Class Win32_Tpm -ErrorAction Stop; if($tpm){$tpmVersion=([string]$tpm.SpecVersion).Split(',')[0].Trim()} } catch {}
$bypasses=$false
if(Test-Path -LiteralPath 'HKLM:\SYSTEM\Setup\LabConfig') { $checks=Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\Setup\LabConfig'; foreach($name in @('BypassTPMCheck','BypassSecureBootCheck','BypassCPUCheck','BypassRAMCheck','BypassStorageCheck')) {if($checks.$name -eq 1){$bypasses=$true}} }
# PS2 的默认输出格式器按 80 列插入换行，必须直接写 stdout 保持 JSON 字符串完整。
[Console]::WriteLine('{"os":"windows","arch":"'+$a+'","os_isa":"'+$a+'","identity_process_isa":"'+$machineParts[0]+'","isa_probe_api":"'+$machineParts[2]+'","os_version":"'+$s.Version+'","os_build":"'+$s.BuildNumber+'","os_release":"'+$r+'","boot_id":"'+$s.LastBootUpTime+'","hardware_uuid":"'+$c.UUID+'","secure_boot_enabled":'+([string]$secure).ToLower()+',"tpm_spec_version":"'+$tpmVersion+'","installation_requirement_bypasses":'+([string]$bypasses).ToLower()+',"installed_package":'+([string]$installed).ToLower()+',"marker":'+$marker+'}')
'''
        from windows_execution import MACHINE_PROBE
        script = script.replace("__WINDOWS_MACHINE_PROBE__", MACHINE_PROBE)
        encoded = base64.b64encode(script.encode("utf-16le")).decode()
        result = json.loads(lab.ssh(target, "powershell.exe -NoProfile -EncodedCommand " + encoded, timeout=45).strip())
    else:
        raise RuntimeError("USE_MACOS_GUEST_PROBE")
    result.update(environment_type=state["environment_type"], generated_at=now())
    write_json(lab.vm_dir(target) / "guest-system.json", result)
    validate_identity(result, spec, state)
    return result


def runtime_binding(lab, target: str) -> dict:
    """预期值只能读取控制器登记状态，不能从待导入报告获取。"""
    if lab.matrix["targets"][target].get("backend") == "native-host":
        from native_windows import runtime_binding as native_binding
        return native_binding(lab, target)
    state = lab.state(target)
    spec = lab.matrix["targets"][target]
    media = lab.media.get(spec["media"], {})
    if not media.get("sha256") or state.get("source_sha256") != media["sha256"]:
        raise RuntimeError("VM_MEDIA_IDENTITY_MISMATCH")
    baseline = state.get("clean_baseline", {})
    if not baseline or baseline.get("name") not in state.get("snapshots", []):
        raise RuntimeError("CLEAN_BASELINE_REQUIRED")
    path = lab.vm_dir(target) / "baseline-identity.json"
    if not path.is_file() or sha256(path) != baseline.get("identity_sha256"):
        raise RuntimeError("BASELINE_IDENTITY_CHANGED")
    system = json.loads(path.read_text(encoding="utf-8"))
    validate_identity(system, spec, state)
    if system.get("installed_package") is not False or baseline.get("cold_boot_verified") is not True:
        raise RuntimeError("BASELINE_NOT_CLEAN_OR_COLD_BOOT_MISSING")
    if not baseline.get("boot_id_before") or baseline.get("boot_id_before") == system["boot_id"]:
        raise RuntimeError("BASELINE_COLD_BOOT_NOT_PROVEN")
    if not state.get("installation_media_detached"):
        raise RuntimeError("INSTALLATION_MEDIA_STILL_ATTACHED")
    if lab.snapshot_record(target, baseline["name"]) != baseline.get("snapshot_record"):
        raise RuntimeError("BASELINE_SNAPSHOT_CHANGED")
    binding = {"vm_uuid": state["uuid"], "media_sha256": media["sha256"],
               "baseline_id": baseline["id"], "identity_sha256": baseline["identity_sha256"]}
    if spec.get("firmware"):
        import firmware
        binding.update(firmware.runtime_binding(lab, target, state))
    return binding


def wait_stopped(lab, target: str, timeout: int = 180) -> None:
    """只有正常退出才继续；超时保留 VM，不用强制断电冒充关机。"""
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        state = lab.state(target)
        if not lab.live(state) and not lab.process_active(state):
            if lab.matrix["targets"][target].get("firmware"):
                import firmware
                firmware.checkpoint_stopped(lab, target, state)
            state.update(status="stopped", pid=None)
            lab.save(target, state)
            return
        time.sleep(2)
    raise RuntimeError("GUEST_SHUTDOWN_TIMEOUT")


def wait_probe(lab, target: str, timeout: int = 300) -> dict:
    deadline = time.monotonic() + timeout
    error = "GUEST_NOT_READY"
    while time.monotonic() < deadline:
        try:
            return probe(lab, target)
        except RuntimeError as exc:
            error = str(exc)
            # 身份矛盾不是启动慢，立即阻断。
            if any(code in error for code in ("MISMATCH", "STANDARD_USER_REQUIRED")):
                raise
        time.sleep(5)
    raise RuntimeError("GUEST_READINESS_TIMEOUT: " + error)


def seal_baseline(lab, target: str, acceleration: str = "tcg", timeout: int = 300) -> dict:
    """可续跑的卸载光盘、正常关机、冷启动、再次关机和基础快照事务。"""
    if lab.matrix["targets"][target].get("backend") == "native-host":
        raise RuntimeError("NATIVE_HOST_COMMAND_NOT_ALLOWED:baseline")
    state = lab.state(target)
    if state.get("clean_baseline"):
        return runtime_binding(lab, target)
    spec = lab.matrix["targets"][target]
    if sha256(Path(state["base"])) != lab.media[spec["media"]]["sha256"]:
        raise RuntimeError("VM_MEDIA_IDENTITY_MISMATCH")
    transaction = state.get("baseline_transaction")
    if not transaction:
        before = probe(lab, target)
        if before.get("installed_package") is not False:
            raise RuntimeError("CLEAN_BASELINE_PACKAGE_ALREADY_INSTALLED")
        transaction = {"id": str(uuid.uuid4()), "boot_id_before": before["boot_id"],
                       "phase": "stop_before", "started_at": now()}
        state["baseline_transaction"] = transaction
        lab.save(target, state)
    if transaction["phase"] == "stop_before":
        lab.stop(target)
        wait_stopped(lab, target, timeout)
        state = lab.state(target)
        state.update(installation_media_detached=True, seed=None)
        transaction["phase"] = "cold_start"
        state["baseline_transaction"] = transaction
        lab.save(target, state)
    if transaction["phase"] == "cold_start":
        lab.start(target, acceleration, False)
        after = wait_probe(lab, target, timeout)
        if after["boot_id"] == transaction["boot_id_before"]:
            raise RuntimeError("BASELINE_COLD_BOOT_NOT_PROVEN")
        if after.get("installed_package") is not False:
            raise RuntimeError("CLEAN_BASELINE_PACKAGE_ALREADY_INSTALLED")
        path = lab.vm_dir(target) / "baseline-identity.json"
        write_json(path, after)
        transaction.update(phase="stop_after", identity_sha256=sha256(path))
        state = lab.state(target)
        state["baseline_transaction"] = transaction
        lab.save(target, state)
    lab.stop(target)
    wait_stopped(lab, target, timeout)
    name = "clean-original-" + transaction["id"][:8]
    state = lab.snapshot(target, name)
    state["clean_baseline"] = {"name": name, "id": transaction["id"],
                               "boot_id_before": transaction["boot_id_before"],
                               "identity_sha256": transaction["identity_sha256"],
                               "cold_boot_verified": True, "created_at": now()}
    state["clean_baseline"]["snapshot_record"] = lab.snapshot_record(target, name)
    state["temporary"] = False
    state.pop("baseline_transaction", None)
    lab.save(target, state)
    return runtime_binding(lab, target)
