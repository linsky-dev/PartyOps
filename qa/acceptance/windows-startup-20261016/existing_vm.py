"""复用已登记的 Win10 Guest；不创建磁盘、不恢复快照，不访问 E 盘。"""
from __future__ import annotations

import argparse
import base64
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "qa/vm-lab"))
from lab import load_configuration  # noqa: E402
from providers import QemuLab  # noqa: E402

parser = argparse.ArgumentParser()
parser.add_argument("action", choices=["start", "identity"])
parser.add_argument("--script", type=Path)
args = parser.parse_args()
matrix, media = load_configuration()
matrix["defaults"]["fallback_root"] = "D:/PartyOps-VM-Lab"
# 公开源码未包含历史 .local 介质登记；现有 Guest 已拆除安装介质，
# 这里只恢复格式识别，绝不再次挂载 ISO 或重新安装系统。
media.setdefault("win10-x64", {"format": "iso", "arch": "x86_64"})
lab = QemuLab(matrix, Path("D:/PartyOps-VM-Lab"), media)
state = lab.state("win10-x64")
expected_disk = Path("D:/PartyOps-VM-Lab/vms/win10-x64/system.qcow2").resolve()
if state["uuid"] != "f47cf265-72a2-4d51-9cf1-b1f5ae960cb2" or Path(state["disk"]).resolve() != expected_disk:
    raise SystemExit("现有 Guest 身份不匹配，停止")
if args.action == "start":
    result = lab.start("win10-x64", acceleration="whpx", provisioning=False)
    print(json.dumps({k: result.get(k) for k in ("uuid", "pid", "status", "disk", "qmp_port", "vnc_display")}, ensure_ascii=False))
else:
    # 仅核对测试环境身份，不读取业务数据库、凭据或用户文档。
    script = r"""
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=New-Object System.Text.UTF8Encoding($false)
$os=Get-CimInstance Win32_OperatingSystem
$apps=@(Get-ItemProperty 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*','HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue | Where-Object {$_.DisplayName -match 'PartyOps|党建智办'} | Select-Object DisplayName,DisplayVersion,InstallLocation)
$services=@(Get-Service -Name 'PartyOps*' -ErrorAction SilentlyContinue | Select-Object Name,Status)
$processes=@(Get-Process -Name 'PartyOps*' -ErrorAction SilentlyContinue | Select-Object Id,ProcessName,Path)
$identity=Get-Content -LiteralPath 'C:\ProgramData\PartyOps-VM-Lab\identity.json' -Raw | ConvertFrom-Json
if($identity.uuid -ne 'f47cf265-72a2-4d51-9cf1-b1f5ae960cb2' -or $identity.purpose -ne 'disposable-qa'){throw 'QA_GUEST_IDENTITY_MISMATCH'}
$installedFiles=@(); foreach($app in $apps){if($app.InstallLocation){$installedFiles+=@(Get-ChildItem -LiteralPath $app.InstallLocation -File | Select-Object Name,Length)}}
$listeners=@(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Where-Object {$_.OwningProcess -in $processes.Id} | Select-Object LocalAddress,LocalPort,OwningProcess)
$manifests=@(); foreach($app in $apps){if($app.InstallLocation){$mp=Join-Path $app.InstallLocation 'release-manifest.json';if(Test-Path -LiteralPath $mp){$m=Get-Content -LiteralPath $mp -Raw | ConvertFrom-Json;$exe=Join-Path $app.InstallLocation 'PartyOps.exe';$manifests+=@{product=$m.product;version=$m.version;architecture=$m.architecture;runtime_profile=$m.runtime_profile;manifest_sha256=(Get-FileHash -LiteralPath $mp -Algorithm SHA256).Hash;exe_sha256=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash;source_fingerprint=$m.source_fingerprint}}}}
@{os=$os.Caption;version=$os.Version;architecture=$os.OSArchitecture;apps=$apps;services=$services;processes=$processes;qa_identity=$identity;installed_files=$installedFiles;listeners=$listeners;manifests=$manifests} | ConvertTo-Json -Depth 5 -Compress
"""
    if args.script:
        checked = args.script.resolve(strict=True)
        if not checked.is_relative_to(Path(__file__).parent) or checked.suffix != ".ps1":
            raise SystemExit("只允许本次验收目录中的明确 QA 脚本")
        script = checked.read_text(encoding="utf-8")
    encoded = base64.b64encode(script.encode("utf-16-le")).decode("ascii")
    output = lab.ssh("win10-x64", "powershell.exe -NoProfile -EncodedCommand " + encoded)
    output_name = (args.script.stem + ".result.json") if args.script else "guest-before-identity.json"
    (Path(__file__).parent / output_name).write_text(output, encoding="utf-8")
    print(output)
