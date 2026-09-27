"""卸载已登记的 Win7 故障包并保留业务数据，为修复包实装准备；不追认验收。"""
import argparse
import json
import sys
import uuid
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE))
from evidence import now, safe_child, sha256, write_json
from lab import load_configuration
from providers import QemuLab
from windows_interactive_install import run_install
from windows_remote import WinRMFiles
from windows_win7_standard import hash_assertions


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--previous-install', type=Path, required=True)
    args = parser.parse_args()
    matrix, media = load_configuration()
    lab = QemuLab(matrix, Path(matrix['defaults']['primary_root']), media)
    target = 'win7-x64'
    source = safe_child(lab.root / 'reports' / target, args.previous_install / 'installed-probe.json')
    previous = json.loads(source.read_text(encoding='utf-8'))
    # 修复包卸载绑定用户指定的实际安装回执，不依赖仍指向更早版本的业务夹具。
    # 旧源码仅用于识别待卸载载荷，不将旧包验收结果导入新包。
    package = previous['package']
    receipt_path = safe_child(lab.root / 'state/builds', Path(package['build_receipt']))
    if sha256(receipt_path) != package['build_receipt_sha256']:
        raise RuntimeError('KNOWN_BUILD_RECEIPT_CHANGED')
    receipt = json.loads(receipt_path.read_text(encoding='utf-8'))
    payload = receipt['windows_payload']
    if (receipt['status'] != 'completed' or receipt['exit_code'] != 0
            or receipt['source_before'] != package['source_fingerprint']
            or payload['package_sha256'] != package['sha256']):
        raise RuntimeError('KNOWN_BUILD_BINDING_CHANGED')
    manifest = safe_child(lab.root / 'reports', Path(payload['manifest']['path']))
    if sha256(manifest) != payload['manifest']['sha256']:
        raise RuntimeError('KNOWN_MANIFEST_CHANGED')
    log = safe_child(source.parent, source.parent / previous['evidence']['install_log']['path'])
    if sha256(log) != previous['evidence']['install_log']['sha256']:
        raise RuntimeError('KNOWN_INSTALL_LOG_CHANGED')
    # 保留策略已覆盖宿主旧安装器；卸载仅依赖封存回执和 Guest 现场程序哈希。
    binding = {'package': package, 'app_path': r'C:\PartyOps QA\中文 程序\PartyOps.exe',
               'app_sha256': payload['executables']['PartyOps.exe']['sha256'],
               'install_report': {'sha256': sha256(source)}}
    state = lab.state(target)
    if (previous['guest_uuid'] != state['uuid'] or previous['restore_generation'] != state['restore_generation']
            or previous['install']['exit_code'] != 0 or previous['package'] != binding['package']
            or binding['install_report']['sha256'] != sha256(source)):
        raise RuntimeError('KNOWN_INSTALL_EVIDENCE_CHANGED')
    report = lab.root / 'reports' / target / ('install-' + uuid.uuid4().hex[:12])
    report.mkdir(exist_ok=False)
    write_json(report / 'operation.json', {'at': now(), 'operation': 'remove-known-install-preserve-data',
        'previous_install': str(source), 'previous_install_sha256': sha256(source),
        'package_sha256': previous['package']['sha256'], 'runtime_environment_passed': False})
    print(str(report), flush=True)
    script = hash_assertions({binding['app_path']: binding['app_sha256']}) + r'''
$installDir='C:\PartyOps QA\中文 程序'
$keys=@('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{1C8EFC63-CAFC-46EF-A5E3-D3D119B5BB3A}_is1','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{1C8EFC63-CAFC-46EF-A5E3-D3D119B5BB3A}_is1')
$registrations=@($keys|Where-Object {Test-Path -LiteralPath $_})
if($registrations.Count -ne 1){throw 'KNOWN_INSTALL_REGISTRATION_NOT_UNIQUE'}
$registration=Get-ItemProperty -LiteralPath $registrations[0]
if($registration.DisplayVersion -ne '1.4.5-rc.6' -or $registration.InstallLocation.TrimEnd('\') -ne $installDir){throw 'KNOWN_INSTALL_REGISTRATION_CHANGED'}
$command=[string]$registration.UninstallString
if($command -match '^"([^"\r\n]+\\unins[0-9]{3}\.exe)"$'){$uninstaller=$matches[1]}
elseif($command -match '^([^"\r\n]+\\unins[0-9]{3}\.exe)$'){$uninstaller=$matches[1]}
else{throw 'REGISTERED_UNINSTALL_COMMAND_INVALID'}
if((Split-Path -Path $uninstaller -Parent) -ne $installDir){throw 'REGISTERED_UNINSTALLER_OUTSIDE_PROGRAM'}
$data=[IO.Path]::ChangeExtension($uninstaller,'.dat')
if(-not (Test-Path -LiteralPath $data) -or (Get-Item -LiteralPath $data).Length -eq 0){throw 'REGISTERED_UNINSTALL_DATA_INCOMPLETE'}
$info=New-Object Diagnostics.ProcessStartInfo
$info.FileName=$uninstaller
$info.Arguments='/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DATAACTION=preserve /LOG="C:\PartyOps-QA\known-install-uninstall-20260910.log"'
$info.UseShellExecute=$false;$info.CreateNoWindow=$true
$process=New-Object Diagnostics.Process;$process.StartInfo=$info
if(-not $process.Start()){throw 'KNOWN_INSTALL_UNINSTALLER_START_FAILED'}
$processHandle=$process.Handle
if(-not $process.WaitForExit(1800000)){throw 'UNINSTALL_STILL_RUNNING_NO_RETRY'}
if($null -eq $process.ExitCode){throw 'UNINSTALL_EXIT_CODE_UNAVAILABLE'}
$exitCode=[int]$process.ExitCode;$process.Dispose()
$result=@{operation='remove-known-install-preserve-data';exit_code=$exitCode;uninstaller_path=$uninstaller;app_exists=[bool](Test-Path -LiteralPath (Join-Path $installDir 'PartyOps.exe'));registration_exists=[bool](@($keys|Where-Object {Test-Path -LiteralPath $_}).Count);runtime_environment_passed=$false}
[Console]::WriteLine($json.Serialize($result))
'''
    result = run_install(lab, target, report, previous['package'], script)
    write_json(report / 'recovery-uninstall-result.json', result)
    WinRMFiles(lab, target).get(r'C:\PartyOps-QA\known-install-uninstall-20260910.log', report / 'uninstall.log')
    if result['exit_code'] != 0 or result['app_exists'] or result['registration_exists']:
        raise RuntimeError('KNOWN_INSTALL_UNINSTALL_INCOMPLETE')
    print(json.dumps({'status': 'completed', 'report': str(report), 'runtime_environment_passed': False}))


if __name__ == '__main__':
    main()
