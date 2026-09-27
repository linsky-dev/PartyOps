"""只在已登记、运行中的 Win7 Guest 执行不会进入安装阶段的 Inno 提取诊断。"""
from __future__ import annotations

import argparse
import json
import re
import sys
import uuid
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE))
from evidence import now, sha256, write_json
from lab import load_configuration
from providers import QemuLab
from windows_remote import WinRMFiles, transfer_path

ROOT = Path("D:/PartyOps-VM-Lab/reports/win7-x64/dotnet-extraction-20260908")
FULL_ROOT = Path("E:/codex/PartyOps/.partyops-vm-lab/reports/win7-dotnet-extraction-20260908")
EXPECTED_UUID = "406ef8ee-e342-405f-933f-7ddce0a55f1b"
CASES = ("sentinel-ultra64", "sentinel-none", "sentinel-prefix", "offline-ultra64", "offline-none", "full-layout")


def resource_script() -> str:
    """在独立只读远程调用内测量资源，避免抬高随后提取进程的 Shell 内存。"""
    return r'''
Add-Type -AssemblyName System.Web.Extensions
$j=New-Object Web.Script.Serialization.JavaScriptSerializer
$os=Get-WmiObject Win32_OperatingSystem
$memory=Get-WmiObject Win32_PerfFormattedData_PerfOS_Memory
$disk=Get-WmiObject Win32_LogicalDisk -Filter "DeviceID='C:'"
$quotas=@()
foreach($key in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WSMAN\WinRS','HKLM:\SOFTWARE\Policies\Microsoft\Windows\WinRM\Service\WinRS')){
  $value=Get-ItemProperty -LiteralPath $key -Name MaxMemoryPerShellMB -ErrorAction SilentlyContinue
  if($null -ne $value){$quotas+=@{key=$key;MaxMemoryPerShellMB=$value.MaxMemoryPerShellMB}}
}
[Console]::WriteLine($j.Serialize(@{generated_at=[DateTime]::UtcNow.ToString('o');free_physical_kib=$os.FreePhysicalMemory;total_visible_kib=$os.TotalVisibleMemorySize;free_virtual_kib=$os.FreeVirtualMemory;committed_bytes=$memory.CommittedBytes;commit_limit_bytes=$memory.CommitLimit;disk_c_free_bytes=$disk.FreeSpace;winrm_registry_quotas=$quotas}))
'''


def diagnostic_script(remote: str, expected_hash: str, log: str) -> str:
    return r'''
Add-Type -AssemblyName System.Web.Extensions
$j=New-Object Web.Script.Serialization.JavaScriptSerializer
$path='__REMOTE__'
$file=[IO.File]::OpenRead($path)
try{$hash=[Security.Cryptography.SHA256]::Create();$actual=[BitConverter]::ToString($hash.ComputeHash($file)).Replace('-','').ToLowerInvariant()}finally{$file.Dispose();$hash.Clear()}
if($actual -ne '__HASH__'){throw 'EXTRACTION_DIAGNOSTIC_HASH_CHANGED'}
$info=New-Object Diagnostics.ProcessStartInfo
$info.FileName=$path
$info.Arguments='/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /LOG="__LOG__"'
$info.UseShellExecute=$false
$info.CreateNoWindow=$true
$p=New-Object Diagnostics.Process
$p.StartInfo=$info
if(-not $p.Start()){throw 'EXTRACTION_DIAGNOSTIC_START_FAILED'}
$handle=$p.Handle
if($handle -eq [IntPtr]::Zero){throw 'EXTRACTION_DIAGNOSTIC_HANDLE_UNAVAILABLE'}
if(-not $p.WaitForExit(600000)){throw 'EXTRACTION_DIAGNOSTIC_TIMEOUT_CHECK_BEFORE_RETRY'}
if($null -eq $p.ExitCode){throw 'EXTRACTION_DIAGNOSTIC_EXIT_UNAVAILABLE'}
$result=@{exit_code=[int]$p.ExitCode;diagnostic_sha256=$actual;no_install_expected=$true;log_path='__LOG__';runtime_environment_passed=$false}
$p.Dispose()
[Console]::WriteLine($j.Serialize($result))
'''.replace("__REMOTE__", remote).replace("__HASH__", expected_hash).replace("__LOG__", log)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("case", choices=CASES)
    parser.add_argument("--reuse-upload", type=Path,
                        help="仅复用同一诊断已登记的 Guest 载荷，启动前仍核验实机 SHA-256")
    args = parser.parse_args()
    root = FULL_ROOT if args.case == "full-layout" else ROOT
    local = root / (args.case + ".exe")
    source = root / "full-layout.iss" if args.case == "full-layout" else HERE / "guest/dotnet-extraction-diagnostic.iss"
    matrix, media = load_configuration()
    lab = QemuLab(matrix, Path(matrix["defaults"]["primary_root"]), media)
    state = lab.state("win7-x64")
    if state["uuid"] != EXPECTED_UUID or not lab.live(state):
        raise RuntimeError("EXTRACTION_DIAGNOSTIC_REQUIRES_REGISTERED_RUNNING_WIN7")
    run_id = args.case + "-" + uuid.uuid4().hex[:10]
    directory = root / run_id
    directory.mkdir()
    remote = "C:\\PartyOps-QA\\" + run_id + ".exe"
    log = "C:\\PartyOps-QA\\" + run_id + ".log"
    record = {"generated_at": now(), "scope": "inno-extraction-only-no-install", "uuid": EXPECTED_UUID,
              "source": str(source), "source_sha256": sha256(source),
              "case": args.case, "diagnostic_sha256": sha256(local), "diagnostic_bytes": local.stat().st_size,
              "remote": remote, "runtime_environment_passed": False}
    payload = ROOT / "sentinel.txt" if args.case.startswith("sentinel-") else HERE.parents[1] / "vendor/windows/dotnet-framework-4.8/ndp48-x86-x64-allos-enu.exe"
    record["payload_sha256"] = sha256(payload)
    record["payload_bytes"] = payload.stat().st_size
    if args.reuse_upload:
        previous_dir = args.reuse_upload.resolve()
        if previous_dir.parent != root.resolve() or not previous_dir.name.startswith(args.case + "-"):
            raise RuntimeError("EXTRACTION_DIAGNOSTIC_REUSE_OUTSIDE_SAME_CASE")
        previous = json.loads((previous_dir / "request.json").read_text(encoding="utf-8"))
        for key in ("uuid", "case", "diagnostic_sha256", "payload_sha256", "source_sha256"):
            if previous.get(key) != record[key]:
                raise RuntimeError("EXTRACTION_DIAGNOSTIC_REUSE_BINDING_MISMATCH:" + key)
        remote = transfer_path(previous["remote"])
        if remote != "C:\\PartyOps-QA\\" + previous_dir.name + ".exe":
            raise RuntimeError("EXTRACTION_DIAGNOSTIC_REUSE_REMOTE_MISMATCH")
        record["remote"] = remote
        record["reused_upload_report"] = str(previous_dir)
    (directory / "diagnostic-source.iss").write_bytes(source.read_bytes())
    write_json(directory / "request.json", record)
    print(json.dumps({"stage": "reuse-upload-with-rehash" if args.reuse_upload else "upload",
                      "directory": str(directory), "bytes": local.stat().st_size}), flush=True)
    files = WinRMFiles(lab, "win7-x64")
    if not args.reuse_upload:
        files.put(local, remote, record["diagnostic_sha256"], timeout=900)
    write_json(directory / "guest-resource-before.json", json.loads(files.powershell(resource_script(), timeout=90)))
    print(json.dumps({"stage": "extract-diagnostic", "case": args.case}), flush=True)
    result = json.loads(files.powershell(diagnostic_script(remote, record["diagnostic_sha256"], log), timeout=660))
    write_json(directory / "result.json", result)
    files.get(log, directory / "diagnostic.log", timeout=120)
    lines = (directory / "diagnostic.log").read_text(encoding="utf-8-sig", errors="replace").splitlines()
    observed = [value.group(1).lower() for line in lines if (value := re.search(r"DIAGNOSTIC_EXTRACT_SUCCEEDED_SHA256=([0-9a-fA-F]{64})", line))]
    result["payload_hash_verified"] = observed == [record["payload_sha256"]]
    result["extraction_errors"] = [line for line in lines if "DIAGNOSTIC_EXTRACT_EXCEPTION=" in line]
    result["diagnostic_log_sha256"] = sha256(directory / "diagnostic.log")
    write_json(directory / "result.json", result)
    print(json.dumps({"directory": str(directory), "result": result,
                      "extraction": [line for line in lines if "DIAGNOSTIC_EXTRACT_" in line]}, ensure_ascii=False))


if __name__ == "__main__":
    main()
