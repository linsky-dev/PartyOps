"""恢复已生成Linux制品的校验回执；保留原控制器退出未知，不重新编译。"""
import json
import subprocess
import sys
from datetime import datetime
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE))
from evidence import now, sha256, write_json
from lab import REPO, fingerprint, inventory, load_configuration
from providers import QemuLab

matrix, media = load_configuration()
lab = QemuLab(matrix, Path(matrix['defaults']['primary_root']), media)
original = lab.root / 'reports/build-f8b92ae22008'
before = json.loads((original / 'build.json').read_text(encoding='utf-8'))
assert before['status'] == 'running'
assert before['source_before'] == fingerprint(), 'RECOVERY_SOURCE_CHANGED'
packages, errors = inventory(matrix, REPO / 'artifacts')
ids = before['package_ids']
assert all(key in packages and key not in errors for key in ids)
started = datetime.fromisoformat(before['started_at']).timestamp()
for key in ids:
    package = packages[key]
    path = Path(package['path'])
    sidecar = Path(str(path) + '.sha256')
    assert path.stat().st_mtime > started and sidecar.stat().st_mtime > started
    assert sidecar.read_text().split()[0] == package['sha256'], 'FINAL_SIDECAR_MISMATCH'
    assert package['sha256'] not in {p['sha256'] for p in before['previous_candidates']}
    assert not (lab.root / 'state/builds' / (package['sha256'] + '.json')).exists()
directory = lab.root / 'reports/build-recovery-20260913'
directory.mkdir(exist_ok=False)
log = directory / 'recovery.log'
command = ['wsl.exe', '-d', 'OracleLinux_7_9', '--exec', 'bash', '-lc', r'''
set -euo pipefail
cd /mnt/e/codex/PartyOps/artifacts
sha256sum -c PartyOps_1.4.5-rc.6_linux_amd64.deb.sha256
sha256sum -c PartyOps-1.4.5-0.rc.6.1.x86_64.rpm.sha256
test "$(dpkg-deb -f PartyOps_1.4.5-rc.6_linux_amd64.deb Package Version Architecture | tr '\n' '|')" = 'Package: partyops|Version: 1.4.5~rc.6|Architecture: amd64|'
dpkg-deb --fsys-tarfile PartyOps_1.4.5-rc.6_linux_amd64.deb | tar -tf - >/dev/null
test "$(rpm -qp --queryformat '%{NAME}|%{VERSION}|%{RELEASE}|%{ARCH}' PartyOps-1.4.5-0.rc.6.1.x86_64.rpm)" = 'partyops|1.4.5|0.rc.6.1|x86_64'
rpm -K --nosignature PartyOps-1.4.5-0.rc.6.1.x86_64.rpm
echo RECOVERED_PACKAGE_INTEGRITY_OK
''']
with log.open('wb') as stream:
    result = subprocess.run(command, stdout=stream, stderr=subprocess.STDOUT, check=False)
    if result.returncode == 0:
        result = subprocess.run([sys.executable, str(REPO / 'scripts/verify-full-function-gate.py'),
            'verify', '--root', str(REPO), '--scope', 'package'], stdout=stream, stderr=subprocess.STDOUT, check=False)
record = {'status': 'failed', 'kind': 'completed-artifact-recovery', 'command': command,
    'original_build': str(original), 'original_build_record_sha256': sha256(original / 'build.json'),
    'original_build_log_sha256': sha256(original / 'build.log'), 'original_exit_code': None,
    'source_before': before['source_before'], 'source_after': fingerprint(), 'exit_code': result.returncode,
    'completed_at': now(), 'output_created_during_build': True,
    'log': {'path': str(log), 'sha256': sha256(log)}}
assert record['source_after'] == record['source_before'], 'RECOVERY_SOURCE_CHANGED'
for key in ids:
    assert sha256(Path(packages[key]['path'])) == packages[key]['sha256']
if result.returncode == 0:
    record['status'] = 'completed'
    for key in ids:
        write_json(lab.root / 'state/builds' / (packages[key]['sha256'] + '.json'), {**record, 'package': packages[key]})
write_json(directory / 'recovery.json', record)
print(json.dumps({'status': record['status'], 'exit_code': result.returncode, 'packages': packages}, ensure_ascii=False))
raise SystemExit(result.returncode)
