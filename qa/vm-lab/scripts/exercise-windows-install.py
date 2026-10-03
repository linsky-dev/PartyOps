"""在已核验的 Windows Guest 内执行当前安装包的真实安装诊断。

本脚本只产生安装后诊断，不生成十三项完整生命周期的 accepted result；
因此即使安装成功，`runtime_environment_passed` 仍保持 false。
"""
from __future__ import annotations

import argparse
import base64
import json
import sys
import time
import uuid
from pathlib import Path

import paramiko

HERE = Path(__file__).resolve().parents[1]
REPO = HERE.parent.parent
sys.path.insert(0, str(HERE))
from evidence import now, safe_child, sha256, write_json
from identity import probe, runtime_binding
from lab import fingerprint, inventory, load_configuration
from provenance import bind_package
from providers import QemuLab
from windows_interactive_install import resume_install, run_install, stage_package
from windows_remote import WinRMFiles
from winrm_memory import prepare_win7_transport

PS2_JSON = r"""
Add-Type -AssemblyName System.Web.Extensions
$json=New-Object Web.Script.Serialization.JavaScriptSerializer
"""


def legacy_install_script(filename: str, expected_sha256: str) -> str:
    # 参数仅来自已核验的发行包文件名，避免将任意路径带入提升的安装进程。
    import re

    if re.fullmatch(r"PartyOps_[0-9A-Za-z.-]+_windows7_(amd64|x86)\.exe", filename) is None:
        raise RuntimeError("WIN7_PACKAGE_FILENAME_INVALID")
    if re.fullmatch(r"[0-9a-f]{64}", expected_sha256) is None:
        raise RuntimeError("WIN7_PACKAGE_SHA256_INVALID")
    return PS2_JSON + "$installer='C:\\PartyOps-QA\\incoming\\" + filename + "'\n$expectedHash='" + expected_sha256 + "'\n" + r"""
$file=[IO.File]::OpenRead($installer)
try {$hash=[Security.Cryptography.SHA256]::Create();$actualHash=[BitConverter]::ToString($hash.ComputeHash($file)).Replace('-','').ToLowerInvariant()} finally {$file.Dispose();$hash.Clear()}
if($actualHash -ne $expectedHash) {throw 'WINDOWS_GUEST_PACKAGE_HASH_MISMATCH'}
$installDir='C:\PartyOps QA\中文 程序'
$log='C:\PartyOps-QA\install.log'
$arguments='/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /DIR="'+$installDir+'" /LOG="'+$log+'"'
$info=New-Object Diagnostics.ProcessStartInfo
$info.FileName=$installer
$info.Arguments=$arguments
$info.UseShellExecute=$false
$info.CreateNoWindow=$true
$process=New-Object Diagnostics.Process
$process.StartInfo=$info
if(-not $process.Start()) {throw 'WINDOWS_INSTALLER_START_FAILED'}
$processHandle=$process.Handle
if($processHandle -eq [IntPtr]::Zero) {throw 'WINDOWS_INSTALLER_PROCESS_HANDLE_UNAVAILABLE'}
if(-not $process.WaitForExit(6600000)) {throw 'WINDOWS_INSTALLER_TIMEOUT_CHECK_GUEST_BEFORE_RETRY'}
if($null -eq $process.ExitCode) {throw 'WINDOWS_INSTALLER_EXIT_CODE_UNAVAILABLE'}
$exitCode=[int]$process.ExitCode
$process.Dispose()
$services=@(Get-WmiObject Win32_Service|Where-Object {$_.Name -eq 'PartyOpsHost' -or $_.Name -eq 'PartyOpsUpdateService'}|ForEach-Object {
  @{name=[string]$_.Name;status=[string]$_.State;start_type=[string]$_.StartMode;path=[string]$_.PathName}
})
$keys=@('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{1C8EFC63-CAFC-46EF-A5E3-D3D119B5BB3A}_is1','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{1C8EFC63-CAFC-46EF-A5E3-D3D119B5BB3A}_is1')
$registration=@($keys|Where-Object {Test-Path -LiteralPath $_}|ForEach-Object {$item=Get-ItemProperty -LiteralPath $_; @{version=[string]$item.DisplayVersion;install_dir=[string]$item.InstallLocation}})
$machine=$null
$app=Join-Path $installDir 'PartyOps.exe'
if(Test-Path -LiteralPath $app) {
  $binary=New-Object IO.BinaryReader([IO.File]::OpenRead($app))
  try {
    if($binary.ReadUInt16() -ne 23117) {throw 'INSTALLED_PE_DOS_HEADER_INVALID'}
    $binary.BaseStream.Position=60
    $offset=$binary.ReadUInt32()
    $binary.BaseStream.Position=$offset
    if($binary.ReadUInt32() -ne 17744) {throw 'INSTALLED_PE_HEADER_INVALID'}
    $machine=[int]$binary.ReadUInt16()
  }finally {$binary.Close()}
}
$result=@{exit_code=[int]$exitCode;install_dir=[string]$installDir;app_exists=[bool](Test-Path -LiteralPath (Join-Path $installDir 'PartyOps.exe'));
launcher_exists=[bool](Test-Path -LiteralPath (Join-Path $installDir 'PartyOpsLauncher.exe'));
formatter_exists=[bool](Test-Path -LiteralPath (Join-Path $installDir 'formatter-host\PartyOps.DocumentFormatter.Host.exe'));
uninstaller_exists=[bool](Test-Path -LiteralPath (Join-Path $installDir 'unins000.exe'));install_log_exists=[bool](Test-Path -LiteralPath $log);
services=@($services);registrations=@($registration);installed_pe_machine=$machine;installer_sha256=[string]$actualHash;powershell_version=[string]$PSVersionTable.PSVersion.ToString();transport='winrm-ntlm';standard_user_first_start_verified=$false}
[Console]::WriteLine($json.Serialize($result))
"""


def verify_legacy_install(result: dict, version: str, package_id: str, expected_sha256: str):
    rows = result.get("registrations", [])
    if len(rows) != 1 or rows[0].get("version") != version:
        raise RuntimeError("WINDOWS_INSTALLED_VERSION_MISMATCH")
    if str(rows[0].get("install_dir", "")).rstrip("\\").casefold() != r"C:\PartyOps QA\中文 程序".casefold():
        raise RuntimeError("WINDOWS_INSTALLED_DIRECTORY_MISMATCH")
    expected_machine = {"windows7_amd64": 0x8664, "windows7_x86": 0x14C}.get(package_id)
    if expected_machine is None or result.get("installed_pe_machine") != expected_machine:
        raise RuntimeError("WINDOWS_INSTALLED_PE_ARCHITECTURE_MISMATCH")
    if result.get("installer_sha256") != expected_sha256:
        raise RuntimeError("WINDOWS_GUEST_PACKAGE_HASH_MISMATCH")


def install_legacy(lab, target, state, package, destination, resume=False):
    """原版 Win7 不安装新版 PowerShell；安装诊断与普通用户生命周期分开记录。"""
    if not resume:
        prepare_win7_transport(lab, target, destination)
    client = WinRMFiles(lab, target)
    identity = json.loads(client.powershell(PS2_JSON + r"""
$marker=$json.DeserializeObject([IO.File]::ReadAllText('C:\ProgramData\PartyOps-VM-Lab\identity.json',[Text.Encoding]::UTF8))
$principal=New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
[Console]::WriteLine($json.Serialize(@{uuid=[string]$marker['uuid'];purpose=[string]$marker['purpose'];administrator=[bool]$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator);hardware_uuid=[string](Get-WmiObject Win32_ComputerSystemProduct).UUID}))
"""))
    if identity.get("uuid") != state["uuid"] or str(identity.get("hardware_uuid", "")).casefold() != state["uuid"].casefold() or identity.get("purpose") != "disposable-qa":
        raise RuntimeError("WINDOWS_GUEST_UUID_MISMATCH")
    if identity.get("administrator") is not True:
        raise RuntimeError("WINDOWS_GUEST_ADMIN_TOKEN_REQUIRED")
    path = Path(package["path"])
    script = legacy_install_script(path.name, package["sha256"])
    if resume:
        result = resume_install(lab, target, destination, package)
    else:
        stage_package(client, package, destination)
        result = run_install(lab, target, destination, package, script)
    write_json(destination / "install-result.json", result)
    if result.get("install_log_exists") is True:
        client.get(r"C:\PartyOps-QA\install.log", destination / "install.log")
    if result.get("exit_code") == 0:
        verify_legacy_install(result, package["version"], package["id"], package["sha256"])
    return identity, result


def encoded_powershell(script: str) -> str:
    script = "[Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)\n" + script
    payload = base64.b64encode(script.encode("utf-16le")).decode("ascii")
    return f"powershell.exe -NoProfile -NonInteractive -EncodedCommand {payload}"


def execute(client: paramiko.SSHClient, script: str, timeout: int = 600) -> str:
    _stdin, stdout, _stderr = client.exec_command(encoded_powershell(script), timeout=timeout)
    # 同时排空两条流，避免 PowerShell CLIXML 进度输出填满 SSH 窗口后互相等待。
    channel = stdout.channel
    output_parts, error_parts = [], []
    deadline = time.monotonic() + timeout
    while True:
        while channel.recv_ready():
            output_parts.append(channel.recv(65536))
        while channel.recv_stderr_ready():
            error_parts.append(channel.recv_stderr(65536))
        if channel.exit_status_ready() and not channel.recv_ready() and not channel.recv_stderr_ready():
            break
        if time.monotonic() >= deadline:
            channel.close()
            raise RuntimeError("WINDOWS_REMOTE_COMMAND_TIMEOUT; guest process status must be checked before retry")
        time.sleep(0.05)
    status = channel.recv_exit_status()
    output = b"".join(output_parts).decode("utf-8", errors="replace").strip()
    error = b"".join(error_parts).decode("utf-8", errors="replace").strip()
    if status:
        raise RuntimeError(f"WINDOWS_REMOTE_COMMAND_FAILED ({status}): {error[-3000:]}")
    return output


def connect(lab: QemuLab, spec: dict) -> paramiko.SSHClient:
    client = paramiko.SSHClient()
    client.load_system_host_keys()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    client.connect(
        "127.0.0.1", port=spec["ssh_port"], username="partyopsqa",
        key_filename=str(lab.root / "keys" / "guest_ed25519"),
        allow_agent=False, look_for_keys=False, timeout=10,
        banner_timeout=10, auth_timeout=10,
    )
    return client


def exercise(target: str, artifact_root: Path = REPO / "artifacts", *, lab=None, resume: Path | None = None,
             repair: bool = False) -> dict:
    if lab is None:
        matrix, media = load_configuration()
        lab = QemuLab(matrix, Path(matrix["defaults"]["primary_root"]), media)
    matrix = lab.matrix
    spec = matrix["targets"].get(target)
    if not spec or spec["os"] != "windows":
        raise RuntimeError("WINDOWS_TARGET_REQUIRED")
    state = lab.state(target)
    if not lab.live(state):
        raise RuntimeError("OWNED_WINDOWS_GUEST_NOT_RUNNING")
    system = probe(lab, target)
    environment = runtime_binding(lab, target)
    if repair and (resume is not None or target not in {"win7-x64", "win7-x86"}
                   or system.get("installed_package") is not True):
        raise RuntimeError("WIN7_REPAIR_REQUIRES_EXISTING_INSTALL")
    if resume is None and not repair and system.get("installed_package") is not False:
        raise RuntimeError("CLEAN_INSTALL_REQUIRES_PACKAGE_ABSENT")
    packages, errors = inventory(matrix, artifact_root)
    package_id = next(key for key, item in matrix["packages"].items() if target in item["required_targets"])
    if package_id in errors:
        raise RuntimeError(f"{errors[package_id]}: {package_id}")
    package = bind_package(lab, packages[package_id], fingerprint())
    if resume is not None:
        if target not in {"win7-x64", "win7-x86"}:
            raise RuntimeError("WIN7_INSTALL_RESUME_TARGET_REQUIRED")
        context_path = safe_child(lab.root / "reports" / target, resume / "context.json")
        destination = context_path.parent
        context = json.loads(context_path.read_text(encoding="utf-8"))
        if (context.get("target") != target or context.get("package") != package
                or context.get("environment") != environment
                or context.get("restore_generation") != state.get("restore_generation")):
            raise RuntimeError("WIN7_INSTALL_RESUME_CONTEXT_CHANGED")
        # 不覆盖首次现场；安装登记可能已经由原任务写入。
        system = context["guest_identity_before"]
        run_id = destination.name
    else:
        run_id = "install-" + uuid.uuid4().hex[:12]
        destination = lab.root / "reports" / target / run_id
        destination.mkdir(parents=True, exist_ok=False)
        write_json(destination / "context.json", {"target": target, "generated_at": now(),
            "package": package, "environment": environment, "guest_identity_before": system,
            "restore_generation": state.get("restore_generation"), "runtime_environment_passed": False,
            "installation_mode": "repair" if repair else "clean"})

    if spec.get("winrm_port"):
        identity, install_result = install_legacy(lab, target, state, package, destination, resume=resume is not None)
        if install_result.get("exit_code") != 0:
            raise RuntimeError(f"WINDOWS_INSTALLER_FAILED: {install_result.get('exit_code')}")
        required = ("app_exists", "launcher_exists", "formatter_exists", "uninstaller_exists", "install_log_exists")
        if not all(install_result.get(key) is True for key in required):
            raise RuntimeError("WINDOWS_INSTALLED_LAYOUT_INCOMPLETE")
    else:
        identity, install_result = install_modern(lab, spec, state, package, destination)

    after = probe(lab, target)
    if after.get("installed_package") is not True:
        raise RuntimeError("WINDOWS_INSTALLED_PACKAGE_NOT_REGISTERED")

    report = {
        "schema_version": 1, "generated_at": now(), "run_id": run_id,
        "target": target, "status": "partial", "scope": "installed-package-diagnostic-only",
        "installation_mode": context.get("installation_mode", "clean") if resume is not None else ("repair" if repair else "clean"),
        "runtime_environment_passed": False, "real_environment_passed": False,
        "guest_uuid": state["uuid"], "package": package,
        "environment": environment, "restore_generation": state.get("restore_generation"),
        "guest_identity_before": system, "guest_identity_after": after,
        "identity": identity, "install": install_result,
        "standard_user_first_start_verified": False,
        "evidence": {"install_log": {"path": "install.log", "sha256": sha256(destination / "install.log")}},
        "remaining_required_cases": matrix["required_cases"],
    }
    if spec.get("winrm_port"):
        report["remaining_transport_requirements"] = ["WIN7_STANDARD_USER_PREPARE_AND_INTERACTIVE_PROBE_REQUIRED:exercise-win7-standard-user.py", "WIN7_FULL_BUSINESS_DRIVER_NOT_PORTED_TO_WINRM"]
    write_json(destination / "installed-probe.json", report)
    write_json(lab.root / "state" / f"installed-probe-{target}.json", {
        "generated_at": report["generated_at"], "status": "partial",
        "report_path": str(destination), "package_sha256": package["sha256"],
        "runtime_environment_passed": False,
    })
    return {"status": "partial", "report_path": str(destination),
            "package_sha256": package["sha256"], "runtime_environment_passed": False}


def install_modern(lab, spec, state, package, destination):
    package_path = Path(package["path"])
    client = connect(lab, spec)
    try:
        identity = json.loads(execute(client, r"""
$ErrorActionPreference = 'Stop'
$identity = Get-Content -LiteralPath 'C:\ProgramData\PartyOps-VM-Lab\identity.json' -Raw | ConvertFrom-Json
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
[ordered]@{ uuid=$identity.uuid; purpose=$identity.purpose; administrator=$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) } | ConvertTo-Json -Compress
"""))
        if identity.get("uuid") != state["uuid"] or identity.get("purpose") != "disposable-qa":
            raise RuntimeError("WINDOWS_GUEST_UUID_MISMATCH")
        if identity.get("administrator") is not True:
            raise RuntimeError("WINDOWS_GUEST_ADMIN_TOKEN_REQUIRED")
        execute(client, "New-Item -ItemType Directory -Path 'C:\\PartyOps-QA\\incoming' -Force | Out-Null")
        remote_file = f"/C:/PartyOps-QA/incoming/{package_path.name}"
        with client.open_sftp() as sftp:
            sftp.put(str(package_path), remote_file)
        remote_hash = execute(
            client,
            f"(Get-FileHash -Algorithm SHA256 -LiteralPath 'C:\\PartyOps-QA\\incoming\\{package_path.name}').Hash.ToLowerInvariant()",
        ).strip().lower()
        if remote_hash != package["sha256"]:
            raise RuntimeError("WINDOWS_GUEST_PACKAGE_HASH_MISMATCH")

        install_result = json.loads(execute(client, f"""
$ErrorActionPreference = 'Stop'
$installer = 'C:\\PartyOps-QA\\incoming\\{package_path.name}'
$installDir = 'C:\\PartyOps QA\\中文 程序'
$log = 'C:\\PartyOps-QA\\install.log'
$arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /DIR="' + $installDir + '" /LOG="' + $log + '"'
$process = Start-Process -FilePath $installer -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
$services = @('PartyOpsHost','PartyOpsUpdateService') | ForEach-Object {{
  $service = Get-Service -Name $_ -ErrorAction SilentlyContinue
  if ($service) {{ [ordered]@{{name=$service.Name; status=$service.Status.ToString(); start_type=$service.StartType.ToString()}} }}
}}
[ordered]@{{
  exit_code = $process.ExitCode
  install_dir = $installDir
  app_exists = Test-Path -LiteralPath (Join-Path $installDir 'PartyOps.exe')
  launcher_exists = Test-Path -LiteralPath (Join-Path $installDir 'PartyOpsLauncher.exe')
  formatter_exists = Test-Path -LiteralPath (Join-Path $installDir 'formatter-host\\PartyOps.DocumentFormatter.Host.exe')
  uninstaller_exists = Test-Path -LiteralPath (Join-Path $installDir 'unins000.exe')
  install_log_exists = Test-Path -LiteralPath $log
  services = @($services)
}} | ConvertTo-Json -Compress -Depth 5
""", timeout=3600))
        write_json(destination / "install-result.json", install_result)
        with client.open_sftp() as sftp:
            sftp.get("/C:/PartyOps-QA/install.log", str(destination / "install.log"))
        if install_result.get("exit_code") != 0:
            raise RuntimeError(f"WINDOWS_INSTALLER_FAILED: {install_result.get('exit_code')}")
        required = ("app_exists", "launcher_exists", "formatter_exists", "uninstaller_exists", "install_log_exists")
        if not all(install_result.get(key) is True for key in required):
            raise RuntimeError("WINDOWS_INSTALLED_LAYOUT_INCOMPLETE")
    finally:
        client.close()

    return identity, install_result


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("target")
    parser.add_argument("--artifacts", type=Path, default=REPO / "artifacts")
    parser.add_argument("--resume", type=Path, help="仅续收原版Win7既有安装任务，不重新启动安装器")
    parser.add_argument("--repair", action="store_true", help="原版Win7现有程序覆盖修复；不计干净安装或旧版本升级通过")
    args = parser.parse_args()
    try:
        print(json.dumps(exercise(args.target, args.artifacts, resume=args.resume, repair=args.repair), ensure_ascii=False, indent=2))
        return 0
    except (OSError, ValueError, RuntimeError, KeyError, json.JSONDecodeError) as exc:
        print(json.dumps({"status": "blocked", "error": str(exc)}, ensure_ascii=False, indent=2))
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
