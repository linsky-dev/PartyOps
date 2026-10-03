"""本机 QEMU 全系统环境、介质校验和有界进程控制。"""
from __future__ import annotations

import ctypes
import functools
import io
import json
import lzma
import os
import secrets
import shutil
import socket
import subprocess
import time
import uuid
from pathlib import Path
from xml.sax.saxutils import escape

import psutil
import yaml
from evidence import checked_id, now, safe_child, sha256, write_json

WIN7_WSMAN_PROVIDER_SETUP = r"""
# 原版 PowerShell 2 的 WSMan 由内置管理单元提供，没有可导入的同名模块。
if (-not (Get-PSProvider -PSProvider WSMan -ErrorAction SilentlyContinue)) {
  $snapin=Get-PSSnapin -Registered -Name Microsoft.WSMan.Management -ErrorAction SilentlyContinue
  if (-not $snapin) {throw 'WINRM_WSMAN_SNAPIN_NOT_REGISTERED'}
  Add-PSSnapin -Name Microsoft.WSMan.Management -ErrorAction Stop
}
if (-not (Get-PSProvider -PSProvider WSMan -ErrorAction SilentlyContinue)) {throw 'WINRM_WSMAN_PROVIDER_UNAVAILABLE'}
if (-not (Get-PSDrive -Name WSMan -ErrorAction SilentlyContinue)) {throw 'WINRM_WSMAN_DRIVE_UNAVAILABLE'}
"""


WIN7_WINRM_SERVICE_SETUP = r"""
Set-Service WinRM -StartupType Automatic
# 原版 Win7 的 quickconfig 可能留下延迟启动标志；仅固定本 Guest 的管理服务。
$serviceKey='HKLM:\SYSTEM\CurrentControlSet\Services\WinRM'
New-ItemProperty -Path $serviceKey -Name DelayedAutoStart -PropertyType DWord -Value 0 -Force | Out-Null
if ((Get-ItemProperty -Path $serviceKey -Name DelayedAutoStart).DelayedAutoStart -ne 0) {throw 'WINRM_DELAYED_START_RESET_FAILED'}
Start-Service WinRM
"""

WIN7_WINRM_LISTENER_SETUP = r"""
# 使用 Windows 自带 winrm.cmd，而不是在原版 PS2 上返回拒绝访问的 WSMan New-Item。
$winrm=Join-Path $env:SystemRoot 'System32\winrm.cmd'
$listener='winrm/config/listener?Address=*+Transport=HTTP'
if (-not (Test-Path -LiteralPath $winrm -PathType Leaf)) {throw 'WINRM_NATIVE_COMMAND_MISSING'}
function Invoke-LabWinRM([string]$operation) {
  $previousPreference=$ErrorActionPreference
  try {
    # 缺少 Listener 的 get 会写 stderr；PS2 不能因此在读取原生退出码前退出。
    $ErrorActionPreference='Continue'
    $nativeOutput=@(& $winrm $operation $listener 2>&1)
    return @{exit_code=$LASTEXITCODE}
  } finally {$ErrorActionPreference=$previousPreference}
}
if ((Invoke-LabWinRM 'get').exit_code -ne 0) {
  if ((Invoke-LabWinRM 'create').exit_code -ne 0) {throw 'WINRM_LISTENER_CREATE_FAILED'}
}
if ((Invoke-LabWinRM 'get').exit_code -ne 0) {throw 'WINRM_LISTENER_VERIFY_FAILED'}
"""


def run(args: list[str], *, timeout: int = 60, log: Path | None = None, input_text: str | None = None) -> str:
    flags = subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0
    try:
        result = subprocess.run(args, capture_output=True, text=True, encoding="utf-8",
                                errors="replace", timeout=timeout, creationflags=flags, check=False,
                                input=input_text)
    except subprocess.TimeoutExpired as exc:
        if log:
            write_json(log.with_suffix(".timeout.json"), {"status": "failed", "error": "PROCESS_TIMEOUT", "seconds": timeout})
        raise RuntimeError(f"PROCESS_TIMEOUT: {Path(args[0]).name}, {timeout}s") from exc
    output = result.stdout + result.stderr
    if log:
        log.parent.mkdir(parents=True, exist_ok=True)
        log.write_text(output, encoding="utf-8")
    if result.returncode:
        raise RuntimeError(f"PROCESS_FAILED ({result.returncode}): {Path(args[0]).name}\n{output[-3000:]}")
    return result.stdout


def free_port(minimum: int = 1) -> int:
    for _ in range(32):
        with socket.socket() as sock:
            # Windows 动态端口起点可被配置为 1024；端口 0 的连续重试仍
            # 可能全落到 5900 以下，不能用它分配 QEMU VNC 的 display。
            requested = 0 if minimum <= 1 else minimum + secrets.randbelow(65536 - minimum)
            try:
                sock.bind(("127.0.0.1", requested))
            except OSError:
                continue
            port = sock.getsockname()[1]
            if port >= minimum:
                return port
    raise RuntimeError("NO_USABLE_LOOPBACK_PORT")


def process_exists(pid: int | None) -> bool:
    """只读查询；Windows 上绝不以 os.kill(pid, 0) 探测进程。"""
    if not pid:
        return False
    if os.name == "nt":
        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel.OpenProcess.restype = ctypes.c_void_p
        kernel.OpenProcess.argtypes = [ctypes.c_uint32, ctypes.c_int, ctypes.c_uint32]
        kernel.GetExitCodeProcess.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_uint32)]
        kernel.CloseHandle.argtypes = [ctypes.c_void_p]
        handle = kernel.OpenProcess(0x1000, False, pid)
        if not handle:
            return ctypes.get_last_error() == 5
        try:
            code = ctypes.c_uint32()
            return not kernel.GetExitCodeProcess(handle, ctypes.byref(code)) or code.value == 259
        finally:
            kernel.CloseHandle(handle)
    return Path(f"/proc/{pid}").exists()


def exclusive_media(function):
    """同一介质只允许一个写入者，防止续跑与下载任务同时破坏断点文件。"""
    @functools.wraps(function)
    def wrapped(self, media_id):
        self.initialize()
        lock = self.root / "state" / ("download-" + checked_id(media_id) + ".lock")
        if lock.exists():
            previous = json.loads(lock.read_text(encoding="utf-8"))
            if process_exists(previous.get("pid")):
                raise RuntimeError(f"MEDIA_DOWNLOAD_IN_PROGRESS:{media_id}; pid={previous['pid']}")
            lock.replace(lock.with_suffix(".stale-" + uuid.uuid4().hex + ".json"))
        try:
            with lock.open("x", encoding="utf-8") as output:
                json.dump({"pid": os.getpid(), "started_at": now(), "media": media_id}, output)
        except FileExistsError:
            raise RuntimeError("MEDIA_DOWNLOAD_IN_PROGRESS:" + media_id) from None
        try:
            return function(self, media_id)
        finally:
            lock.unlink(missing_ok=True)
    return wrapped


def qmp(port: int, command: str, arguments: dict | None = None) -> dict:
    with socket.create_connection(("127.0.0.1", port), timeout=5) as sock:
        stream = sock.makefile("rwb")
        greeting = json.loads(stream.readline())
        if "QMP" not in greeting:
            raise RuntimeError("INVALID_QMP_SERVER")
        for name, params in (("qmp_capabilities", {}), (command, arguments or {})):
            stream.write((json.dumps({"execute": name, "arguments": params}) + "\n").encode())
            stream.flush()
            while True:
                message = json.loads(stream.readline())
                if "error" in message:
                    raise RuntimeError(f"QMP_ERROR: {message['error']}")
                if "return" in message:
                    break
        return message["return"]


class QemuLab:
    def __init__(self, matrix: dict, root: Path, media: dict):
        self.matrix, self.root, self.media = matrix, root.resolve(), media
        if self.root == Path(self.root.anchor):
            raise ValueError("LAB_ROOT_CANNOT_BE_DRIVE_ROOT")
        self.defaults = matrix["defaults"]
        self.qemu_home = Path(self.defaults["qemu_home"])

    def initialize(self) -> None:
        self.root.mkdir(parents=True, exist_ok=True)
        marker = self.root / ".partyops-lab.json"
        if not marker.exists():
            write_json(marker, {"schema_version": 1, "id": str(uuid.uuid4()), "created_at": now(), "purpose": "PartyOps disposable QA lab"})
        for directory in ("downloads", "bases", "vms", "state", "reports", "keys"):
            (self.root / directory).mkdir(exist_ok=True)

    def binary(self, name: str) -> str:
        path = self.qemu_home / (name + (".exe" if os.name == "nt" else ""))
        if not path.is_file():
            raise RuntimeError(f"MISSING_TOOL: {name}; run scripts/bootstrap-windows.ps1")
        return str(path)

    def vm_dir(self, target: str) -> Path:
        spec = self.matrix["targets"].get(target, self.matrix.get("historical_targets", {}).get(target, {}))
        if spec.get("backend") == "native-host":
            raise RuntimeError("NATIVE_HOST_VM_OPERATION_NOT_ALLOWED")
        root = Path(self.defaults["fallback_root"]).resolve() if spec.get("vm_storage") == "fallback" else self.root
        return safe_child(root / "vms", root / "vms" / checked_id(spec.get("vm_folder", target)))

    def state(self, target: str) -> dict:
        path = self.vm_dir(target) / "vm.json"
        if not path.is_file():
            raise RuntimeError(f"VM_NOT_CREATED: {target}")
        return json.loads(path.read_text(encoding="utf-8"))

    def save(self, target: str, state: dict) -> None:
        write_json(self.vm_dir(target) / "vm.json", state)

    def live(self, state: dict) -> bool:
        if not state.get("qmp_port"):
            return False
        try:
            # 同时校验实例 UUID，避免端口被无关程序/其他 VM 复用。
            identity = qmp(state["qmp_port"], "query-uuid")
            return identity.get("UUID") == state["uuid"]
        except (OSError, ValueError, RuntimeError):
            return False

    def process_active(self, state: dict) -> bool:
        """PID 会被宿主复用，须同时核对 QEMU 命令行与登记 UUID。"""
        if not process_exists(state.get("pid")):
            return False
        try:
            arguments = psutil.Process(state["pid"]).cmdline()
            return bool(arguments and Path(arguments[0]).name.lower().startswith("qemu-system-")
                        and state["uuid"] in arguments)
        except psutil.NoSuchProcess:
            return False
        except psutil.AccessDenied:
            # 无法证明已退出时保留阻断，不终止未知进程。
            return True

    def check_space(self, directory: Path, additional_gib: int = 0) -> None:
        probe = directory
        while not probe.exists():
            probe = probe.parent
        reserve = self.defaults["reserve_primary_gib"]
        if str(probe).lower().startswith("e:"):
            reserve = self.defaults["reserve_fallback_gib"]
        if shutil.disk_usage(probe).free < (reserve + additional_gib) * 1024**3:
            raise RuntimeError(f"DISK_HEADROOM: {probe}; reserve={reserve} GiB, additional={additional_gib} GiB")

    @exclusive_media
    def fetch(self, media_id: str) -> Path:
        self.initialize()
        item = self.media.get(checked_id(media_id))
        if not item:
            raise RuntimeError(f"MISSING_MEDIA: {media_id}; configure verified local media or official URL")
        if item.get("blocked_reason"):
            raise RuntimeError(item["blocked_reason"])
        download_root = self.root / "downloads"
        if item.get("storage_root") == "fallback":
            fallback = Path(self.defaults["fallback_root"]).resolve()
            if fallback == Path(fallback.anchor):
                raise RuntimeError("FALLBACK_ROOT_CANNOT_BE_DRIVE_ROOT")
            download_root = fallback / "downloads"
            download_root.mkdir(parents=True, exist_ok=True)
        elif item.get("storage_root") not in (None, "primary"):
            raise RuntimeError("MEDIA_STORAGE_ROOT_INVALID")
        destination = safe_child(download_root, download_root / item["filename"])
        expected = item.get("sha256", "")
        if len(expected) != 64:
            raise RuntimeError("MEDIA_CHECKSUM_REQUIRED")
        size_bytes = item.get("size_bytes", 0)
        if type(size_bytes) is not int or size_bytes < 0:
            raise RuntimeError("MEDIA_SIZE_INVALID")
        if not destination.exists():
            # 官方大 ISO 不能一律按 2 GiB 预留；按已公布长度加上磁盘安全余量。
            download_gib = (size_bytes + 1024**3 - 1) // 1024**3
            self.check_space(destination.parent, max(2, download_gib))
            if item.get("local_path"):
                source = Path(item["local_path"])
                if sha256(source) != expected:
                    raise RuntimeError("MEDIA_HASH_MISMATCH")
                shutil.copyfile(source, destination)
            else:
                from media_download import download, download_aria2

                part = destination.with_suffix(destination.suffix + ".part")
                aria2 = self.root / "tools/aria2-1.37.0/aria2-1.37.0-win-64bit-build1/aria2c.exe"
                if aria2.is_file():
                    download_aria2(item, part, self.check_space, aria2)
                else:
                    download(item, part, self.check_space)
                if sha256(part) != expected:
                    raise RuntimeError("MEDIA_HASH_MISMATCH")
                part.replace(destination)
        if size_bytes and destination.stat().st_size != size_bytes:
            raise RuntimeError("MEDIA_SIZE_MISMATCH")
        if sha256(destination) != expected:
            raise RuntimeError("MEDIA_HASH_MISMATCH")
        write_json(self.root / "state" / f"media-{media_id}.json", {
            **item, "verified_at": now(), "local_path": str(destination), "sha256": expected})
        return destination

    def prepare_base(self, media_id: str) -> Path:
        source = self.fetch(media_id)
        item = self.media[media_id]
        if item["format"] == "iso":
            return source
        self.check_space(self.root / "bases", item.get("minimum_expanded_gib", 16))
        base = self.root / "bases" / (media_id + ".qcow2")
        receipt = base.with_suffix(".json")
        if base.exists():
            if not receipt.exists() or json.loads(receipt.read_text())["sha256"] != sha256(base):
                raise RuntimeError("BASE_IMAGE_CHANGED")
            return base
        temporary = base.with_suffix(".partial")
        if item["format"] == "qcow2.xz":
            with lzma.open(source, "rb") as compressed, temporary.open("wb") as expanded:
                while chunk := compressed.read(4 * 1024 * 1024):
                    self.check_space(self.root / "bases")
                    expanded.write(chunk)
        elif item["format"] == "qcow2":
            shutil.copyfile(source, temporary)
        else:
            raise RuntimeError("UNSUPPORTED_MEDIA_FORMAT")
        info = json.loads(run([self.binary("qemu-img"), "info", "--output=json", str(temporary)]))
        if info["format"] != "qcow2" or info.get("backing-filename"):
            raise RuntimeError("UNTRUSTED_IMAGE_BACKING_CHAIN")
        run([self.binary("qemu-img"), "check", str(temporary)], timeout=300)
        temporary.replace(base)
        write_json(receipt, {"source_sha256": item["sha256"], "sha256": sha256(base),
                             "created_at": now(), "image_info": info})
        return base

    def cloud_seed(self, directory: Path, instance: str) -> Path:
        import pycdlib
        key = self.root / "keys" / "guest_ed25519"
        if not key.exists():
            run(["ssh-keygen", "-q", "-t", "ed25519", "-N", "", "-f", str(key)])
        public = key.with_suffix(".pub").read_text().strip()
        user_data = {"users": [{"name": "partyopsqa", "groups": ["wheel"],
                               "sudo": "ALL=(ALL) NOPASSWD:ALL", "shell": "/bin/bash",
                               "lock_passwd": True, "ssh_authorized_keys": [public]}],
                     "disable_root": True, "ssh_pwauth": False,
                     "timezone": "Asia/Shanghai", "hostname": "partyops-qa",
                     "growpart": {"mode": "auto", "devices": ["/"]}, "resize_rootfs": True}
        user_bytes = ("#cloud-config\n" + yaml.safe_dump(user_data)).encode()
        meta_bytes = yaml.safe_dump({"instance-id": instance, "local-hostname": "partyops-qa"}).encode()
        iso = pycdlib.PyCdlib()
        iso.new(interchange_level=3, joliet=3, rock_ridge="1.09", vol_ident="cidata")
        for iso_name, name, data in (("USER_DAT.;1", "user-data", user_bytes), ("META_DAT.;1", "meta-data", meta_bytes)):
            iso.add_fp(io.BytesIO(data), len(data), iso_path="/" + iso_name,
                       rr_name=name, joliet_path="/" + name)
        target = directory / "seed.iso"
        iso.write(str(target))
        iso.close()
        return target

    def windows_seed(self, directory: Path, instance: str, target: str) -> Path:
        """生成仅供一次性 Windows 验收 Guest 使用的无人值守介质。

        测试账户密码只写入本地临时 ISO，不写入仓库、日志或 VM 状态；Guest
        就绪后只允许实验室专用 SSH 公钥登录。该介质同时固定北京时间，并
        写入可由远程执行器核验的 VM UUID，避免误操作宿主或其他虚拟机。
        """
        if self.matrix["targets"].get(target, {}).get("backend") == "native-host":
            raise RuntimeError("NATIVE_HOST_VM_OPERATION_NOT_ALLOWED")
        self.require_windows_backend(target)
        import pycdlib

        key = self.root / "keys" / "guest_ed25519"
        if not key.exists():
            run(["ssh-keygen", "-q", "-t", "ed25519", "-N", "", "-f", str(key)])
        public_key = key.with_suffix(".pub").read_text(encoding="utf-8").strip()
        if not public_key.startswith("ssh-ed25519 "):
            raise RuntimeError("WINDOWS_SEED_INVALID_SSH_KEY")
        # 避免 XML、PowerShell 与 Windows 本地账户密码的转义歧义。
        password = "Pq7!" + secrets.token_hex(18)
        spec = self.matrix["targets"][target]
        if spec["os"] != "windows":
            raise RuntimeError("WINDOWS_SEED_REQUIRES_WINDOWS_TARGET")
        legacy = spec.get("os_release") == "7 SP1"
        setup_script = f"""$ErrorActionPreference = 'Stop'
$labRoot = 'C:\\ProgramData\\PartyOps-VM-Lab'
New-Item -ItemType Directory -Path $labRoot -Force | Out-Null
Set-TimeZone -Id 'China Standard Time'
Set-Content -LiteralPath (Join-Path $labRoot 'identity.json') -Encoding utf8 -Value '{{"uuid":"{instance}","purpose":"disposable-qa","timezone":"Asia/Shanghai"}}'
$capability = Get-WindowsCapability -Online -Name 'OpenSSH.Server~~~~0.0.1.0'
if ($capability.State -ne 'Installed') {{ Add-WindowsCapability -Online -Name $capability.Name | Out-Null }}
Set-Service -Name sshd -StartupType Automatic
Start-Service -Name sshd
# 仅在一次性、回环映射的实验室 Guest 中允许本地管理员的 SSH 令牌执行
# 安装器；宿主与产品安装后的权限策略不受影响。
New-ItemProperty -Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System' -Name 'LocalAccountTokenFilterPolicy' -PropertyType DWord -Value 1 -Force | Out-Null
New-Item -ItemType Directory -Path 'C:\\ProgramData\\ssh' -Force | Out-Null
Set-Content -LiteralPath 'C:\\ProgramData\\ssh\\administrators_authorized_keys' -Encoding ascii -Value '{public_key}'
& icacls.exe 'C:\\ProgramData\\ssh\\administrators_authorized_keys' /inheritance:r /grant '*S-1-5-18:F' /grant '*S-1-5-32-544:F' | Out-Null
if (-not (Get-NetFirewallRule -Name 'PartyOps-QA-SSHD' -ErrorAction SilentlyContinue)) {{
  New-NetFirewallRule -Name 'PartyOps-QA-SSHD' -DisplayName 'PartyOps QA SSH (isolated guest)' -Direction Inbound -Protocol TCP -LocalPort 22 -Action Allow | Out-Null
}}
powercfg.exe /change standby-timeout-ac 0
powercfg.exe /change monitor-timeout-ac 0
Set-Content -LiteralPath (Join-Path $labRoot 'ready.json') -Encoding utf8 -Value ('{{"ready":true,"generated_at":"' + (Get-Date -Format o) + '"}}')
"""
        # Win11 在无 TPM/Secure Boot 的隔离 QEMU 中仅用于安装生命周期验收；
        # LabConfig 明确作用于一次性 Guest，不改变宿主安全配置。
        command = (
            "powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "
            "\"$v=(Get-Volume -FileSystemLabel 'PARTYOPS_QA').DriveLetter; "
            "& ($v + ':\\setup.ps1')\""
        )
        unattend = f"""<?xml version="1.0" encoding="utf-8"?>
<unattend xmlns="urn:schemas-microsoft-com:unattend">
  <settings pass="windowsPE">
    <component name="Microsoft-Windows-International-Core-WinPE" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS" xmlns:wcm="http://schemas.microsoft.com/WMIConfig/2002/State">
      <SetupUILanguage><UILanguage>zh-CN</UILanguage></SetupUILanguage>
      <InputLocale>0804:00000804</InputLocale><SystemLocale>zh-CN</SystemLocale><UILanguage>zh-CN</UILanguage><UserLocale>zh-CN</UserLocale>
    </component>
    <component name="Microsoft-Windows-Setup" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS" xmlns:wcm="http://schemas.microsoft.com/WMIConfig/2002/State">
      <RunSynchronous>
        <RunSynchronousCommand wcm:action="add"><Order>1</Order><Path>reg.exe add HKLM\\SYSTEM\\Setup\\LabConfig /v BypassTPMCheck /t REG_DWORD /d 1 /f</Path></RunSynchronousCommand>
        <RunSynchronousCommand wcm:action="add"><Order>2</Order><Path>reg.exe add HKLM\\SYSTEM\\Setup\\LabConfig /v BypassSecureBootCheck /t REG_DWORD /d 1 /f</Path></RunSynchronousCommand>
        <RunSynchronousCommand wcm:action="add"><Order>3</Order><Path>reg.exe add HKLM\\SYSTEM\\Setup\\LabConfig /v BypassRAMCheck /t REG_DWORD /d 1 /f</Path></RunSynchronousCommand>
      </RunSynchronous>
      <DiskConfiguration><Disk wcm:action="add"><DiskID>0</DiskID><WillWipeDisk>true</WillWipeDisk><CreatePartitions>
        <CreatePartition wcm:action="add"><Order>1</Order><Type>Primary</Type><Size>100</Size></CreatePartition>
        <CreatePartition wcm:action="add"><Order>2</Order><Type>Primary</Type><Extend>true</Extend></CreatePartition>
      </CreatePartitions><ModifyPartitions>
        <ModifyPartition wcm:action="add"><Order>1</Order><PartitionID>1</PartitionID><Active>true</Active><Format>NTFS</Format><Label>System</Label></ModifyPartition>
        <ModifyPartition wcm:action="add"><Order>2</Order><PartitionID>2</PartitionID><Format>NTFS</Format><Label>Windows</Label><Letter>C</Letter></ModifyPartition>
      </ModifyPartitions></Disk><WillShowUI>OnError</WillShowUI></DiskConfiguration>
      <ImageInstall><OSImage><InstallFrom><MetaData wcm:action="add"><Key>/IMAGE/INDEX</Key><Value>1</Value></MetaData></InstallFrom><InstallTo><DiskID>0</DiskID><PartitionID>2</PartitionID></InstallTo><WillShowUI>OnError</WillShowUI></OSImage></ImageInstall>
      <UserData><AcceptEula>true</AcceptEula><FullName>PartyOps QA</FullName><Organization>PartyOps</Organization></UserData>
    </component>
  </settings>
  <settings pass="specialize">
    <component name="Microsoft-Windows-Shell-Setup" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS"><ComputerName>PARTYOPS-QA</ComputerName><TimeZone>China Standard Time</TimeZone></component>
  </settings>
  <settings pass="oobeSystem">
    <component name="Microsoft-Windows-International-Core" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS"><InputLocale>0804:00000804</InputLocale><SystemLocale>zh-CN</SystemLocale><UILanguage>zh-CN</UILanguage><UserLocale>zh-CN</UserLocale></component>
    <component name="Microsoft-Windows-Shell-Setup" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS" xmlns:wcm="http://schemas.microsoft.com/WMIConfig/2002/State">
      <OOBE><HideEULAPage>true</HideEULAPage><HideLocalAccountScreen>true</HideLocalAccountScreen><HideOnlineAccountScreens>true</HideOnlineAccountScreens><ProtectYourPC>3</ProtectYourPC></OOBE>
      <UserAccounts><LocalAccounts><LocalAccount wcm:action="add"><Name>partyopsqa</Name><Group>Administrators</Group><DisplayName>PartyOps QA</DisplayName><Password><Value>{escape(password)}</Value><PlainText>true</PlainText></Password></LocalAccount></LocalAccounts></UserAccounts>
      <AutoLogon><Enabled>true</Enabled><LogonCount>99</LogonCount><Username>partyopsqa</Username><Password><Value>{escape(password)}</Value><PlainText>true</PlainText></Password></AutoLogon>
      <FirstLogonCommands><SynchronousCommand wcm:action="add"><Order>1</Order><Description>Prepare isolated PartyOps QA guest</Description><CommandLine>{escape(command)}</CommandLine><RequiresUserInput>false</RequiresUserInput></SynchronousCommand></FirstLogonCommands>
    </component>
  </settings>
</unattend>
"""
        unattend = unattend.replace('processorArchitecture="amd64"', 'processorArchitecture="' + ("x86" if spec["arch"] == "i686" else "amd64") + '"')
        if not legacy:
            # 官方公开 GVLK 只供 Windows Setup 选择 Pro；不提供激活或零售授权。
            # https://learn.microsoft.com/en-us/windows-server/get-started/kms-client-activation-keys
            image_name = "Windows " + spec["os_release"] + " Pro"
            unattend = unattend.replace("<Key>/IMAGE/INDEX</Key><Value>1</Value>",
                                        "<Key>/IMAGE/NAME</Key><Value>" + escape(image_name) + "</Value>")
            unattend = unattend.replace("<UserData><AcceptEula>",
                "<UserData><ProductKey><Key>W269N-WFGWX-YVC9B-4J6C9-T83GX</Key>"
                "<WillShowUI>OnError</WillShowUI></ProductKey><AcceptEula>")
        if legacy:
            # Win7 没有 Get-Volume/Set-TimeZone/OpenSSH Capability，使用内置 WMI/WinRM。
            command = ("powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "
                       "\"$v=Get-WmiObject Win32_LogicalDisk | Where-Object {$_.VolumeName -eq 'PARTYOPS_QA'}; "
                       "& ($v.DeviceID + '\\setup.ps1')\"")
            old_command = unattend.split("<CommandLine>", 1)[1].split("</CommandLine>", 1)[0]
            unattend = unattend.replace(old_command, escape(command))
            unattend = unattend.replace('processorArchitecture="amd64"', 'processorArchitecture="' + ("x86" if spec["arch"] == "i686" else "amd64") + '"')
            unattend = unattend.replace("zh-CN", "en-US").replace("0804:00000804", "0409:00000409")
            unattend = unattend.replace("<Key>/IMAGE/INDEX</Key><Value>1</Value>", "<Key>/IMAGE/NAME</Key><Value>Windows 7 ULTIMATE</Value>")
            unattend = unattend.replace("<HideOnlineAccountScreens>true</HideOnlineAccountScreens>", "")
            unattend = unattend.replace("<HideLocalAccountScreen>true</HideLocalAccountScreen>", "")
            setup_script = f"""$ErrorActionPreference = 'Stop'
$labRoot = 'C:\\ProgramData\\PartyOps-VM-Lab'
New-Item -ItemType Directory -Path $labRoot -Force | Out-Null
tzutil.exe /s 'China Standard Time'
Set-Content -LiteralPath (Join-Path $labRoot 'identity.json') -Encoding utf8 -Value '{{"uuid":"{instance}","purpose":"disposable-qa"}}'
{WIN7_WINRM_SERVICE_SETUP}
{WIN7_WSMAN_PROVIDER_SETUP}
{WIN7_WINRM_LISTENER_SETUP}
New-ItemProperty -Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System' -Name 'LocalAccountTokenFilterPolicy' -PropertyType DWord -Value 1 -Force | Out-Null
netsh.exe advfirewall firewall add rule name='PartyOps-QA-WinRM' dir=in action=allow protocol=TCP localport=5985
if ($LASTEXITCODE -ne 0) {{ throw 'WINRM_LAB_FIREWALL_RULE_FAILED' }}
powercfg.exe /change standby-timeout-ac 0
powercfg.exe /change monitor-timeout-ac 0
Set-Content -LiteralPath (Join-Path $labRoot 'ready.json') -Encoding utf8 -Value '{{"ready":true,"transport":"winrm-ntlm"}}'
"""
            # 本机专用一次性凭据，只写实验室 Guest 目录；不写报告或命令行。
        write_json(directory / "guest-credential.local.json", {"username": "partyopsqa", "password": password, "uuid": instance})
        iso = pycdlib.PyCdlib()
        iso.new(interchange_level=3, joliet=3, vol_ident="PARTYOPS_QA")
        files = (
            ("/AUTOUNAT.XML;1", "/Autounattend.xml", unattend.encode("utf-8")),
            ("/SETUP.PS1;1", "/setup.ps1", setup_script.encode("utf-8-sig")),
        )
        for iso_path, joliet_path, data in files:
            iso.add_fp(io.BytesIO(data), len(data), iso_path=iso_path, joliet_path=joliet_path)
        target = directory / "windows-unattend.iso"
        iso.write(str(target))
        iso.close()
        write_json(directory / "windows-unattend.json", {
            "schema_version": 1, "created_at": now(), "guest_uuid": instance,
            "timezone": "Asia/Shanghai", "remote_transport": "winrm-ntlm" if legacy else "ssh-public-key",
            "iso_sha256": sha256(target), "contains_disposable_credential": True,
        })
        return target

    def require_windows_backend(self, target: str) -> None:
        """当前 Windows QEMU 构建没有 TPM 后端；禁止误用 x86 BIOS/免检模板创建 ARM Guest。"""
        spec = self.matrix["targets"][target]
        if spec["os"] == "windows" and spec["arch"] in {"arm64", "aarch64"}:
            # 2026-09-08 本地 -tpmdev help 返回 invalid option。下一阶段须接入本地
            # WSL QEMU/swtpm、受信 ArmVirt 固件及成对 TPM/UEFI 快照后才替换此门禁。
            raise RuntimeError("WINDOWS_ARM64_SECURE_BOOT_TPM2_PROVIDER_REQUIRED")

    def create(self, target: str) -> dict:
        self.initialize()
        spec = self.matrix["targets"][target]
        if spec["backend"] == "native-host":
            raise RuntimeError("NATIVE_HOST_VM_OPERATION_NOT_ALLOWED")
        if spec["backend"] != "qemu":
            raise RuntimeError(f"NATIVE_BACKEND_REQUIRED: {spec['backend']}; use macos-doctor")
        directory = self.vm_dir(target)
        if (directory / "vm.json").exists():
            return self.state(target)
        item = self.media.get(spec["media"])
        if item is None:
            raise RuntimeError(f"MISSING_MEDIA: {spec['media']}")
        if item.get("blocked_reason"):
            raise RuntimeError(item["blocked_reason"])
        self.require_windows_backend(target)
        if {"loong64": "loongarch64"}.get(item["arch"], item["arch"]) != spec["arch"]:
            raise RuntimeError("MEDIA_ARCHITECTURE_MISMATCH")
        if spec["arch"] == "loongarch64":
            import firmware
            firmware.sources(self, target)
        base = self.prepare_base(spec["media"])
        directory.mkdir(parents=True, exist_ok=True)
        disk = directory / "system.qcow2"
        if disk.exists():
            raise RuntimeError("INCOMPLETE_VM_CREATE: existing disk preserved; inspect before recovery")
        size = f"{self.defaults['disk_gib']}G"
        command = [self.binary("qemu-img"), "create", "-f", "qcow2"]
        if item["format"] != "iso":
            command += ["-F", "qcow2", "-b", str(base)]
        run(command + [str(disk), size])
        identity = str(uuid.uuid4())
        if item.get("cloud_init"):
            seed = self.cloud_seed(directory, identity)
        elif spec["os"] == "windows":
            seed = self.windows_seed(directory, identity, target)
        else:
            seed = None
        state = {"schema_version": 1, "target": target, "uuid": identity, "created_at": now(),
                 "temporary": True, "disk": str(disk), "base": str(base),
                 "source_sha256": item["sha256"], "seed": str(seed) if seed else None,
                 "status": "created", "qmp_port": None, "pid": None, "snapshots": []}
        if spec.get("firmware"):
            import firmware
            state["firmware"] = firmware.prepare(self, target)
        self.save(target, state)
        return state

    def command(self, target: str, state: dict, acceleration: str, provisioning: bool) -> list[str]:
        spec = self.matrix["targets"][target]
        self.require_windows_backend(target)
        arm = spec["arch"] in ("aarch64", "arm64")
        loong = spec["arch"] == "loongarch64"
        if spec["arch"] not in {"aarch64", "arm64", "loongarch64", "x86_64", "i686"}:
            raise RuntimeError("UNSUPPORTED_GUEST_ARCHITECTURE")
        if arm and acceleration != "tcg":
            raise RuntimeError("ARM_FULL_SYSTEM_REQUIRES_TCG_ON_X86_HOST")
        if loong and acceleration != "tcg":
            raise RuntimeError("LOONGARCH_FULL_SYSTEM_REQUIRES_TCG_ON_X86_HOST")
        name = "qemu-system-loongarch64" if loong else ("qemu-system-aarch64" if arm else ("qemu-system-i386" if spec["arch"] == "i686" else "qemu-system-x86_64"))
        # Windows 版 i386 仿真器仅提供 TCG；x86_64 仿真器可通过 WHPX 运行 32 位 Guest。
        if spec["arch"] == "i686" and acceleration == "whpx":
            name = "qemu-system-x86_64"
        windows = spec["os"] == "windows"
        accelerator = "whpx,hyperv=off,kernel-irqchip=off" if acceleration == "whpx" and spec.get("whpx_legacy_irq") else acceleration
        command = [self.binary(name), "-name", "partyops-lab-" + target, "-uuid", state["uuid"],
                   "-machine", spec["machine"] if loong else ("virt" if arm else "q35"), "-accel", accelerator,
                   "-cpu", spec["cpu"] if loong else "max", "-smp", str(spec.get("cpus", self.defaults["cpus"])),
                   "-m", str(state.get("memory_mib", spec.get("memory_mib", self.defaults["memory_mib"]))),
                   "-drive", f"file={state['disk']},if={'none,id=osdisk' if loong else ('ide' if windows else 'virtio')},format=qcow2",
                   "-qmp", f"tcp:127.0.0.1:{state['qmp_port']},server=on,wait=off",
                   "-display", "none", "-vnc", f"127.0.0.1:{state['vnc_display']}",
                   "-serial", "file:" + str(self.vm_dir(target) / "serial.log"),
                   "-netdev", f"user,id=net0,restrict={'off' if provisioning else 'on'},hostfwd=tcp:127.0.0.1:{spec['ssh_port']}-:22",
                   "-device", ("e1000" if windows else "virtio-net-pci") + ",netdev=net0"]
        if windows:
            # Windows 按本地时区解释硬件时钟；否则首次启动会比宿主慢八小时。
            command += ["-rtc", "base=localtime,clock=host"]
        if spec.get("winrm_port"):
            index = command.index("-netdev") + 1
            command[index] += f",hostfwd=tcp:127.0.0.1:{spec['winrm_port']}-:5985"
        if loong:
            import firmware
            uefi = firmware.validate(self, target, state)
            command += ["-drive", f"if=pflash,unit=0,format=raw,readonly=on,file={uefi['code']}",
                        "-drive", f"if=pflash,unit=1,format=raw,file={uefi['vars']}",
                        "-device", "virtio-blk-pci,drive=osdisk,bootindex=1"]
        if loong or arm:
            command += ["-device", "virtio-gpu-pci", "-device", "qemu-xhci",
                        "-device", "usb-kbd", "-device", "usb-tablet"]
        if target == "win10-x86":
            # noVNC 鼠标定位需要 USB absolute tablet；仅此遗留目标显式挂接 xHCI。
            command += ["-device", "qemu-xhci,id=qa-input-usb",
                        "-device", "usb-tablet,bus=qa-input-usb.0"]
        if arm:
            firmware = next(iter(self.qemu_home.rglob("edk2-aarch64-code.fd")), None)
            if firmware is None:
                raise RuntimeError("MISSING_AARCH64_UEFI")
            command += ["-bios", str(firmware)]
        optical = []
        if self.media[spec["media"]]["format"] == "iso" and not state.get("installation_media_detached"):
            # 安装 ISO 必须先成为可启动光驱；无人值守介质作为第二光驱供
            # Windows Setup 扫描，不能抢占 BIOS 的 CD 启动顺序。
            optical.append(state["base"])
            if state.get("seed"):
                optical.append(state["seed"])
            command += ["-boot", "order=d"]
        elif state.get("seed"):
            optical.append(state["seed"])
        if (arm or loong) and optical:
            command += ["-device", "virtio-scsi-pci,id=scsi0"]
        for index, path in enumerate(optical):
            if arm or loong:
                cd_device = f"scsi-cd,drive=cd{index},bus=scsi0.0"
                if loong:
                    cd_device += f",bootindex={0 if index == 0 else index + 2}"
                command += ["-drive", f"file={path},if=none,id=cd{index},format=raw,readonly=on",
                            "-device", cd_device]
            else:
                command += ["-drive", f"file={path},media=cdrom,readonly=on"]
        return command

    def start(self, target: str, acceleration: str = "tcg", provisioning: bool = False) -> dict:
        state = self.state(target)
        if self.live(state):
            return state
        if self.process_active(state):
            raise RuntimeError("VM_PROCESS_ACTIVE_QMP_UNREACHABLE")
        spec = self.matrix["targets"][target]
        if spec.get("firmware"):
            import firmware
            firmware.validate(self, target, state, checkpoint=True)
        if spec["os"] == "windows" and not state.get("seed") and not state.get("installation_media_detached"):
            state["seed"] = str(self.windows_seed(self.vm_dir(target), state["uuid"], target))
            self.save(target, state)
        roots = {self.root, Path(self.defaults["fallback_root"]).resolve()}
        for path in [p for root in roots for p in (root / "vms").glob("*/vm.json")]:
            other = json.loads(path.read_text(encoding="utf-8"))
            if self.live(other) or self.process_active(other):
                raise RuntimeError(f"CONCURRENCY_LIMIT: {other['target']} is running")
        if (self.root / "state/macos-x64-vm.json").is_file():
            from macos import VMwareMacLab
            if VMwareMacLab(self).running():
                raise RuntimeError("CONCURRENCY_LIMIT: macos-x64 is running")
        if os.name == "nt":
            free = int(run(["powershell.exe", "-NoProfile", "-Command", "(Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory"]).strip()) * 1024
            needed = self.matrix["targets"][target].get("memory_mib", self.defaults["memory_mib"]) * 1024**2
            # Guest RAM 之外仍有 QEMU/TCG 翻译缓存；本机实测不能只扣 Guest 内存。
            overhead_mib = 1536 if acceleration == "tcg" else 512
            available = free - self.defaults["host_free_memory_gib"] * 1024**3 - overhead_mib * 1024**2
            allocated_mib = min(needed // 1024**2, (available // 1024**3) * 1024)
            minimum = (
                3072
                if target.startswith("win7") or (spec["os"] == "windows" and spec["arch"] == "i686")
                else 4096
            )
            if allocated_mib < minimum:
                raise RuntimeError("HOST_MEMORY_HEADROOM")
            state["memory_mib"] = allocated_mib
            state["host_overhead_reserved_mib"] = overhead_mib
        growth = 2 if state.get("clean_baseline") else spec.get("installation_growth_gib", 20)
        self.check_space(self.vm_dir(target), growth)
        state["qmp_port"] = free_port()
        vnc_port = free_port(5900)
        state["vnc_display"] = vnc_port - 5900
        if state["vnc_display"] < 0:
            raise RuntimeError("INVALID_VNC_PORT")
        command = self.command(target, state, acceleration, provisioning)
        directory = self.vm_dir(target)
        with (directory / "qemu-stdout.log").open("ab") as output, (directory / "qemu-stderr.log").open("ab") as errors:
            process = subprocess.Popen(command, stdin=subprocess.DEVNULL, stdout=output, stderr=errors,
                                       creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
        state.update(pid=process.pid, status="starting", acceleration=acceleration,
                     environment_type="full-system-emulated" if acceleration == "tcg" else "hardware-virtualized",
                     provisioning_network=provisioning, started_at=now())
        self.save(target, state)
        for _ in range(20):
            if self.live(state):
                state["status"] = "running"
                self.save(target, state)
                return state
            if process.poll() is not None:
                state["status"] = "failed"
                self.save(target, state)
                raise RuntimeError("QEMU_START_FAILED: " + (directory / "qemu-stderr.log").read_text(errors="replace")[-2000:])
            time.sleep(0.5)
        raise RuntimeError("QEMU_START_TIMEOUT; process retained for diagnosis")

    def stop(self, target: str, force: bool = False) -> dict:
        state = self.state(target)
        if self.live(state):
            qmp(state["qmp_port"], "quit" if force else "system_powerdown")
            state["status"] = "stop_requested"
        else:
            if self.matrix["targets"][target].get("firmware"):
                import firmware
                firmware.checkpoint_stopped(self, target, state)
                state["pid"] = None
            state["status"] = "stopped"
        self.save(target, state)
        return state

    def snapshot(self, target: str, name: str, restore: bool = False) -> dict:
        checked_id(name)
        state = self.state(target)
        if self.live(state) or self.process_active(state):
            raise RuntimeError("SNAPSHOT_REQUIRES_STOPPED_VM")
        if restore and name not in state["snapshots"]:
            raise RuntimeError("SNAPSHOT_NOT_FOUND")
        uefi = self.matrix["targets"][target].get("firmware")
        if uefi:
            import firmware
            if not restore:
                firmware.checkpoint_stopped(self, target, state)
                state["pid"] = None
            if restore or name in state["snapshots"]:
                firmware.snapshot_record(self, target, state, name)
        if not restore and name in state["snapshots"]:
            return state
        disk = safe_child(self.vm_dir(target), Path(state["disk"]))
        if uefi:
            if restore:
                state["firmware_restore_pending"] = name
                self.save(target, state)
            else:
                uefi_record = firmware.save_snapshot(self, target, state, name)
        run([self.binary("qemu-img"), "snapshot", "-a" if restore else "-c", name, str(disk)], timeout=300)
        if uefi:
            if restore:
                firmware.restore_snapshot(self, target, state, name)
            else:
                state.setdefault("firmware_snapshots", {})[name] = uefi_record
        if not restore:
            state["snapshots"].append(name)
        state["status"] = "snapshot_restored" if restore else "snapshot_created"
        if restore:
            state["restore_generation"] = str(uuid.uuid4())
        self.save(target, state)
        return state

    def snapshot_record(self, target: str, name: str) -> dict:
        state = self.state(target)
        disk = safe_child(self.vm_dir(target), Path(state["disk"]))
        info = json.loads(run([self.binary("qemu-img"), "info", "--force-share", "--output=json", str(disk)]))
        matches = [row for row in info.get("snapshots", []) if row.get("name") == name]
        if len(matches) != 1:
            raise RuntimeError("BASELINE_SNAPSHOT_NOT_ON_DISK")
        row = matches[0]
        record = {key: row.get(key) for key in ("id", "name", "date-sec", "date-nsec", "vm-state-size")}
        if self.matrix["targets"][target].get("firmware"):
            import firmware
            record["firmware"] = firmware.snapshot_record(self, target, state, name)
        return record

    def ssh(self, target: str, command: str, timeout: int = 60, log: Path | None = None) -> str:
        state = self.state(target)
        if not self.live(state):
            raise RuntimeError("VM_NOT_RUNNING")
        if self.matrix["targets"][target].get("winrm_port"):
            from windows_remote import execute
            return execute(self, target, command, timeout, log)
        input_text = None
        prefix = "powershell.exe -NoProfile -EncodedCommand "
        if self.matrix["targets"][target].get("os") == "windows" and command.startswith(prefix) and len(command) > 7000:
            # Windows OpenSSH 默认经 cmd.exe 执行，长身份探针超过其命令行上限。
            # 原始编码脚本改走标准输入，检查内容不变，也不在 Guest 留临时脚本。
            input_text = command[len(prefix):] + "\n"
            command = ('powershell.exe -NoProfile -NonInteractive -Command '
                       '"$s=[Console]::In.ReadLine(); '
                       '& ([ScriptBlock]::Create([Text.Encoding]::Unicode.GetString('
                       '[Convert]::FromBase64String($s))))"')
        return run(["ssh", "-i", str(self.root / "keys" / "guest_ed25519"),
                    "-p", str(self.matrix["targets"][target]["ssh_port"]), "-o", "BatchMode=yes",
                    "-o", "ConnectTimeout=8", "-o", "StrictHostKeyChecking=accept-new",
                    "-o", "UserKnownHostsFile=" + str(self.root / "keys" / "known_hosts"),
                    "partyopsqa@127.0.0.1", command], timeout=timeout, log=log, input_text=input_text)

    def cleanup(self, target: str, force: bool) -> dict:
        state = self.state(target)
        if self.live(state) or self.process_active(state) or not state.get("temporary"):
            raise RuntimeError("CLEANUP_REQUIRES_STOPPED_TEMPORARY_VM")
        vm_root = Path(self.defaults["fallback_root"]).resolve() if self.matrix["targets"][target].get("vm_storage") == "fallback" else self.root
        directory = safe_child(vm_root / "vms", self.vm_dir(target))
        if not (self.root / ".partyops-lab.json").is_file():
            raise RuntimeError("MISSING_LAB_OWNERSHIP")
        for path in directory.rglob("*"):
            safe_child(directory, path)
        record = {"target": target, "path": str(directory), "deleted": force,
                  "generated_at": now(), "preserves": ["bases", "downloads", "reports", "user_data"]}
        if force:
            write_json(self.root / "state" / f"cleanup-{target}-{uuid.uuid4().hex}.json", record)
            shutil.rmtree(directory)
        return record
