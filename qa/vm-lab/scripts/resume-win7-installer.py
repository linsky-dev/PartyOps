"""原 Win7 构建在 Inno 压缩期间中断时，只续封装已完整校验的冻结目录。"""
import argparse
import datetime
import json
import os
import subprocess
import sys
import uuid
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from evidence import now, safe_child, sha256, write_json
from lab import REPO, fingerprint, inventory, load_configuration
from providers import QemuLab
from windows_build_payload import capture_payload, manifest_entries


def require(value, message):
    if not value:
        raise RuntimeError(message)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--build-report', type=Path, required=True)
    args = parser.parse_args()
    matrix, media = load_configuration()
    lab = QemuLab(matrix, Path(matrix['defaults']['primary_root']), media)
    original = safe_child(lab.root / 'reports', args.build_report)
    previous = json.loads((original / 'build.json').read_text(encoding='utf-8'))
    require(previous['status'] == 'running' and previous['package_ids'] == ['windows7_amd64'], 'ONLY_INTERRUPTED_WIN7_BUILD_ALLOWED')
    require(previous['source_before'] == fingerprint(), 'SOURCE_CHANGED_SINCE_BUILD')
    import psutil
    require(not any(p.info['name'].lower() == 'iscc.exe' for p in psutil.process_iter(['name']) if p.info['name']), 'INNO_STILL_RUNNING')
    stage = (REPO / 'artifacts/PartyOps-1.4.5-rc.6-windows7-amd64').resolve()
    manifest = json.loads((stage / 'release-manifest.json').read_text(encoding='utf-8'))
    entries = manifest_entries(manifest, {'id': 'windows7_amd64', 'version': '1.4.5-rc.6'})
    started_ns = int(datetime.datetime.fromisoformat(previous['started_at']).timestamp() * 1_000_000_000)
    require((stage / 'release-manifest.json').stat().st_mtime_ns > started_ns, 'STAGING_MANIFEST_PREDATES_BUILD')
    for item in entries.values():
        file = safe_child(stage, stage / item['path'])
        require(file.stat().st_size == item['size'] and sha256(file) == item['sha256'], 'FROZEN_PAYLOAD_CHANGED:' + item['path'])
    report = lab.root / 'reports' / ('build-resume-' + uuid.uuid4().hex[:12])
    report.mkdir()
    record = {'started_at': previous['started_at'], 'resumed_at': now(), 'source_before': fingerprint(),
              'package_ids': ['windows7_amd64'], 'status': 'running',
              'resumed_from': {'path': str(original), 'record_sha256': sha256(original / 'build.json'),
                               'log_sha256': sha256(original / 'build.log'), 'original_exit_code': None},
              'frozen_manifest_sha256': sha256(stage / 'release-manifest.json'),
              'verified_frozen_files': len(entries), 'recompiled_program': False}
    write_json(report / 'build.json', record)
    # 复用正式构建脚本的完整 Inno/候选元数据段，不复制编译流程。
    script = r'''
$ErrorActionPreference='Stop'
$repoRoot=(Get-Location).Path
$installerScripts=Join-Path $repoRoot 'packaging\windows'
$outputRoot=Join-Path $repoRoot 'artifacts'
$bundleRoot=Join-Path $outputRoot 'PartyOps-1.4.5-rc.6-windows7-amd64'
$buildRoot=Join-Path $outputRoot 'windows-runtime-windows7-amd64'
$env:TEMP='E:\codex\PartyOps\.partyops-vm-lab\builds\windows-local\tmp';$env:TMP=$env:TEMP
$InnoCompiler=Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'
$isLegacy=$true;$targetArchitecture='amd64';$releaseVersion='1.4.5-rc.6'
$releaseTag='v1.4.5-rc.6';$runtimeProfile='legacy-smart';$platformFamily='windows7';$officialSqliteVersion='3.53.4'
$sourceCommit=(Get-Content -LiteralPath (Join-Path $bundleRoot 'release-manifest.json') -Raw|ConvertFrom-Json).source_commit
. (Join-Path $installerScripts 'prepare-dotnet-runtime.ps1')
Add-VerifiedPartyOpsDotNet48Prerequisite -RepoRoot $repoRoot -Destination (Join-Path $bundleRoot 'prerequisites')
$source=[IO.File]::ReadAllText((Join-Path $installerScripts 'build-windows.ps1'))
$index=$source.IndexOf('if (-not (Test-Path -LiteralPath $InnoCompiler))')
if($index -lt 0){throw 'INSTALLER_BUILD_ENTRY_MISSING'}
& ([scriptblock]::Create($source.Substring($index).Replace('$PSScriptRoot','$installerScripts')))
if($LASTEXITCODE -ne 0){throw 'RESUMED_INSTALLER_FAILED'}
'''
    # 实际源文件名从已存在的定义文件确定，不能猜测或跳过前置包校验。
    helpers = [p for p in (REPO / 'packaging/windows').glob('*.ps1')
               if 'function Add-VerifiedPartyOpsDotNet48Prerequisite' in p.read_text(encoding='utf-8-sig')]
    require(len(helpers) == 1, 'DOTNET_PREREQUISITE_HELPER_AMBIGUOUS')
    script = script.replace('prepare-dotnet-runtime.ps1', helpers[0].name)
    entry = report / 'resume-installer.ps1'
    entry.write_text(script, encoding='utf-8')
    gate = [sys.executable, str(REPO / 'scripts/verify-full-function-gate.py'), 'verify', '--root', str(REPO), '--scope', 'package']
    log = report / 'build.log'
    with log.open('xb') as output:
        commands = [gate, ['pwsh.exe', '-NoProfile', '-File', str(entry)],
                    [sys.executable, str(REPO / 'scripts/validate-win7-pe.py'), '--root', str(stage), '--architecture', 'amd64'], gate]
        code = 1
        for command in commands:
            code = subprocess.run(command, cwd=REPO, stdout=output, stderr=subprocess.STDOUT,
                                  creationflags=subprocess.CREATE_NO_WINDOW, check=False).returncode
            if code: break
    record.update(exit_code=code, finished_at=now(), source_after=fingerprint(),
                  log={'path': str(log), 'sha256': sha256(log)})
    record['status'] = 'failed'
    write_json(report / 'build.json', record)
    require(code == 0 and record['source_after'] == record['source_before'], 'INSTALLER_RESUME_FAILED')
    packages, errors = inventory(matrix, REPO / 'artifacts')
    require('windows7_amd64' not in errors, 'RESUMED_PACKAGE_METADATA_INVALID')
    package = packages['windows7_amd64']
    payload = capture_payload(lab, REPO / 'artifacts', package, report, started_ns)
    record.update(status='completed', windows_payloads={'windows7_amd64': payload})
    write_json(report / 'build.json', record)
    write_json(lab.root / 'state/builds' / (package['sha256'] + '.json'),
               {**record, 'package': package, 'output_created_during_build': True, 'windows_payload': payload})
    print(json.dumps({'status': 'completed', 'report': str(report), 'sha256': package['sha256']}))


if __name__ == '__main__':
    main()
