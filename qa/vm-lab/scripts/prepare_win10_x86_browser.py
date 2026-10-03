"""为原版 Win10 x86 QA Guest 安装官方 Firefox；不构成 PartyOps 验收。"""
from __future__ import annotations

import argparse
import base64
import json
import sys
import time
import uuid
from pathlib import Path

import paramiko

LAB_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(LAB_DIR))
from evidence import now, sha256, write_json
from identity import probe, runtime_binding
from lab import load_configuration
from providers import QemuLab

TARGET = "win10-x86"
EXPECTED_UUID = "03461f3b-f67c-4871-93a3-1711d8dc5329"
EXPECTED_SHA256 = "ab895b98fcc78217639b3fe61add19c272e8f632b5afb6ec77a237dfdb0df08f"
EXPECTED_BYTES = 57603448
DOWNLOAD_URL = (
    "https://archive.mozilla.org/pub/firefox/releases/115.40.0esr/win32/"
    "zh-CN/Firefox%20Setup%20115.40.0esr.exe"
)
CHECKSUM_URL = "https://archive.mozilla.org/pub/firefox/releases/115.40.0esr/SHA256SUMS"
FILENAME = "Firefox-ESR-115.40.0esr-win32-zh-CN.exe"


def verify_source(source: Path, metadata_path: Path, *, expected_sha256: str = EXPECTED_SHA256,
                  expected_bytes: int = EXPECTED_BYTES) -> dict:
    """要求本地文件、邻接来源记录及固定官方归档身份一致。"""
    metadata = json.loads(metadata_path.read_text(encoding="utf-8"))
    before = source.stat()
    actual = sha256(source)
    after = source.stat()
    if (before.st_size, before.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
        raise RuntimeError("BROWSER_SOURCE_CHANGED_DURING_HASH")
    if metadata.get("download_url") != DOWNLOAD_URL or metadata.get("official_sha256sum_url") != CHECKSUM_URL:
        raise RuntimeError("BROWSER_OFFICIAL_SOURCE_EVIDENCE_MISMATCH")
    if metadata.get("sha256", "").lower() != expected_sha256.lower() or actual.lower() != expected_sha256.lower():
        raise RuntimeError("BROWSER_SOURCE_SHA256_MISMATCH")
    if metadata.get("bytes") != expected_bytes or after.st_size != expected_bytes:
        raise RuntimeError("BROWSER_SOURCE_SIZE_MISMATCH")
    if source.name != FILENAME:
        raise RuntimeError("BROWSER_SOURCE_FILENAME_MISMATCH")
    return {"path": str(source.resolve()), "sha256": actual, "bytes": after.st_size,
            "download_url": metadata["download_url"],
            "official_sha256sum_url": metadata["official_sha256sum_url"]}


def validate_guest_baseline(state: dict, spec: dict, baseline_identity: dict) -> None:
    """本地确认当前目标仍绑定到已登记的 Win10 x86 干净冷启动 baseline。"""
    if state.get("uuid", "").casefold() != EXPECTED_UUID.casefold():
        raise RuntimeError("WIN10_X86_GUEST_UUID_MISMATCH")
    if spec.get("os") != "windows" or spec.get("os_release") != "10" or spec.get("arch") != "i686":
        raise RuntimeError("WIN10_X86_TARGET_SPEC_MISMATCH")
    baseline = state.get("clean_baseline", {})
    if (not baseline or baseline.get("name") not in state.get("snapshots", [])
            or baseline.get("cold_boot_verified") is not True
            or not baseline.get("identity_sha256")):
        raise RuntimeError("WIN10_X86_CLEAN_BASELINE_REQUIRED")
    if (baseline_identity.get("uuid", baseline_identity.get("hardware_uuid", "")).casefold()
            != EXPECTED_UUID.casefold()):
        raise RuntimeError("WIN10_X86_BASELINE_UUID_MISMATCH")
    if (baseline_identity.get("os") != "windows" or baseline_identity.get("os_build") != "19045"
            or baseline_identity.get("arch") != "i686"
            or baseline_identity.get("installed_package") is not False):
        raise RuntimeError("WIN10_X86_BASELINE_IDENTITY_MISMATCH")


def encoded_powershell(script: str) -> str:
    script = "[Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)\n" + script
    payload = base64.b64encode(script.encode("utf-16le")).decode("ascii")
    return f"powershell.exe -NoProfile -NonInteractive -EncodedCommand {payload}"


def validate_install_result(result: dict) -> None:
    """核对安装退出码、文件版本和 PE32 x86 标记；不验证浏览器业务流程。"""
    version = str(result.get("version", "")).split(".")[:3]
    if (result.get("status") != "complete" or result.get("exit_code") != 0
            or result.get("installed") is not True or version != ["115", "40", "0"]
            or result.get("pe_machine") != 0x14C
            or str(result.get("installer_sha256", "")).lower() != EXPECTED_SHA256):
        raise RuntimeError("BROWSER_INSTALL_OR_PE32_VERSION_VERIFICATION_FAILED")


def execute(client: paramiko.SSHClient, script: str, timeout: int = 660) -> str:
    _stdin, stdout, _stderr = client.exec_command(encoded_powershell(script), timeout=timeout)
    channel = stdout.channel
    output_parts: list[bytes] = []
    error_parts: list[bytes] = []
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
            raise RuntimeError("WIN10_X86_BROWSER_REMOTE_TIMEOUT_CHECK_GUEST_BEFORE_RETRY")
        time.sleep(0.05)
    status = channel.recv_exit_status()
    output = b"".join(output_parts).decode("utf-8", errors="replace").strip()
    error = b"".join(error_parts).decode("utf-8", errors="replace").strip()
    if status:
        raise RuntimeError(f"WIN10_X86_BROWSER_REMOTE_FAILED ({status}): {error[-2000:]}")
    return output


def connect(lab: QemuLab, spec: dict) -> paramiko.SSHClient:
    client = paramiko.SSHClient()
    client.load_system_host_keys()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    client.connect("127.0.0.1", port=spec["ssh_port"], username="partyopsqa",
                   key_filename=str(lab.root / "keys" / "guest_ed25519"),
                   allow_agent=False, look_for_keys=False, timeout=10,
                   banner_timeout=10, auth_timeout=10)
    return client


def prepare(source: Path, metadata_path: Path, lab: QemuLab) -> dict:
    source_facts = verify_source(source, metadata_path)
    spec = lab.matrix["targets"].get(TARGET)
    if not spec or spec.get("backend") != "qemu":
        raise RuntimeError("WIN10_X86_QEMU_TARGET_REQUIRED")
    state = lab.state(TARGET)
    runtime_binding(lab, TARGET)
    baseline_path = lab.vm_dir(TARGET) / "baseline-identity.json"
    baseline_identity = json.loads(baseline_path.read_text(encoding="utf-8"))
    validate_guest_baseline(state, spec, baseline_identity)

    # probe() 在真实 SSH 连接上读取 Guest UUID、系统 build、ISA 及安装状态。
    live_identity = probe(lab, TARGET)
    if (live_identity.get("hardware_uuid", "").casefold() != EXPECTED_UUID.casefold()
            or live_identity.get("os") != "windows" or live_identity.get("os_release") != "10"
            or live_identity.get("os_build") != "19045" or live_identity.get("arch") != "i686"
            or live_identity.get("installed_package") is not False):
        raise RuntimeError("WIN10_X86_LIVE_GUEST_IDENTITY_OR_CLEAN_STATE_MISMATCH")

    run_id = "firefox-esr-115.40.0esr-" + uuid.uuid4().hex[:12]
    report_dir = lab.root / "reports" / TARGET / run_id
    report_dir.mkdir(parents=True, exist_ok=False)
    write_json(report_dir / "source.json", source_facts)
    write_json(report_dir / "guest-identity.json", live_identity)
    remote = "C:\\PartyOps-QA\\incoming\\" + FILENAME
    remote_result = "C:\\PartyOps-QA\\" + run_id + ".json"
    client = connect(lab, spec)
    try:
        preflight = execute(client, rf"""
$ErrorActionPreference='Stop'
$exe='C:\Program Files\Mozilla Firefox\firefox.exe'
if(Test-Path -LiteralPath $exe){{throw 'BROWSER_ALREADY_INSTALLED_INSPECT_BEFORE_RETRY'}}
if(Test-Path -LiteralPath '{remote}'){{throw 'BROWSER_INSTALLER_ALREADY_STAGED_INSPECT_BEFORE_RETRY'}}
if(Test-Path -LiteralPath '{remote_result}'){{throw 'BROWSER_RESULT_ALREADY_EXISTS_INSPECT_BEFORE_RETRY'}}
if(-not (Test-Path -LiteralPath 'C:\PartyOps-QA\incoming')){{New-Item -ItemType Directory -Path 'C:\PartyOps-QA\incoming' -Force|Out-Null}}
if(Get-Process -Name 'Firefox Setup 115.40.0esr' -ErrorAction SilentlyContinue){{throw 'BROWSER_INSTALLER_PROCESS_ALREADY_RUNNING'}}
'preflight-ok'
""")
        if preflight != "preflight-ok":
            raise RuntimeError("WIN10_X86_BROWSER_PREFLIGHT_UNEXPECTED_OUTPUT")
        with client.open_sftp() as sftp:
            sftp.put(str(source), remote.replace("C:\\", "/C:/", 1))
        script = rf"""
$ErrorActionPreference='Stop'
$installer='{remote}'
$expected='{EXPECTED_SHA256}'
$actual=(Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
if($actual -ne $expected){{throw 'BROWSER_GUEST_SHA256_MISMATCH'}}
$exe='C:\Program Files\Mozilla Firefox\firefox.exe'
$process=Start-Process -FilePath $installer -ArgumentList '/S' -WindowStyle Hidden -PassThru
$pidStarted=[int]$process.Id
if(-not $process.WaitForExit(600000)){{
  $pending=@{{status='pending';pid=$pidStarted;exit_code=$null;installed=[IO.File]::Exists($exe);path=$exe;version=$null;pe_machine=$null;installer_sha256=$actual}}
  [Console]::WriteLine(($pending|ConvertTo-Json -Compress)); exit 0
}}
$process.Refresh()
$machine=$null
$version=$null
if([IO.File]::Exists($exe)){{
  $stream=[IO.File]::OpenRead($exe); $reader=New-Object IO.BinaryReader($stream)
  try {{
    if($reader.ReadUInt16() -ne 23117){{throw 'BROWSER_INSTALLED_MZ_HEADER_INVALID'}}
    $reader.BaseStream.Position=60; $offset=$reader.ReadUInt32(); $reader.BaseStream.Position=$offset
    if($reader.ReadUInt32() -ne 17744){{throw 'BROWSER_INSTALLED_PE_HEADER_INVALID'}}
    $machine=[int]$reader.ReadUInt16()
  }} finally {{$reader.Close()}}
  $version=[string][Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion
}}
$result=@{{status='complete';pid=$pidStarted;exit_code=[int]$process.ExitCode;installed=[IO.File]::Exists($exe);path=$exe;version=$version;pe_machine=$machine;installer_sha256=$actual;runtime_environment_passed=$false;browser_ui_opened=$false;ui_login_tested=$false;refresh_acceptance_claimed=$false}}
$body=$result|ConvertTo-Json -Compress
[IO.File]::WriteAllText('{remote_result}',$body,(New-Object Text.UTF8Encoding($false)))
[Console]::WriteLine($body)
"""
        result = json.loads(execute(client, script, timeout=660))
    finally:
        client.close()
    write_json(report_dir / "result.json", result)
    if result.get("status") != "complete":
        raise RuntimeError("BROWSER_INSTALL_PENDING_INSPECT_GUEST_BEFORE_RETRY")
    validate_install_result(result)
    report = {"status": "prepared", "scope": "browser-prerequisite-only",
              "target": TARGET, "guest_uuid": EXPECTED_UUID, "source": source_facts,
              "result": result, "browser_ui_opened": False, "ui_login_tested": False,
              "refresh_acceptance_claimed": False, "runtime_environment_passed": False,
              "report_dir": str(report_dir)}
    write_json(report_dir / "browser-preparation.json", report)
    return report


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path)
    parser.add_argument("--metadata", type=Path)
    args = parser.parse_args()
    try:
        matrix, media = load_configuration()
        lab = QemuLab(matrix, Path(matrix["defaults"]["primary_root"]), media)
        source = args.source or (lab.root / "downloads" / FILENAME)
        metadata = args.metadata or source.with_name(source.stem + ".source.json")
        print(json.dumps(prepare(source, metadata, lab), ensure_ascii=False, indent=2))
        return 0
    except (OSError, ValueError, RuntimeError, KeyError, json.JSONDecodeError) as exc:
        print(json.dumps({"status": "blocked", "error": str(exc)}, ensure_ascii=False, indent=2))
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
