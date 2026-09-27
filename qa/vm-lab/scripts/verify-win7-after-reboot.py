"""真实重启后只验证普通用户运行与原业务数据，不重跑OCR、公文或安装。"""
import importlib.util
import json
import sys
import uuid
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE))
from evidence import now, sha256, write_json
from identity import probe
from lab import fingerprint, load_configuration
from providers import QemuLab, qmp
from windows_win7_standard import installation_binding, ps

def module(name, filename):
    spec = importlib.util.spec_from_file_location(name, HERE / 'scripts' / filename)
    value = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(value)
    return value

business = module('postboot_business', 'exercise-win7-business.py')
reboot = module('postboot_reboot', 'reboot-windows.py')
matrix, media = load_configuration()
lab = QemuLab(matrix, Path(matrix['defaults']['primary_root']), media)
receipt_path = lab.root / 'reports/win7-x64/reboot-eb636ca323db.json'
receipt = json.loads(receipt_path.read_text(encoding='utf-8'))
install = lab.root / 'reports/win7-x64/install-550416415de0'
context = installation_binding(lab, 'win7-x64', install, fingerprint())
reboot.validate_restart(receipt, context, lab.state('win7-x64'))
system = probe(lab, 'win7-x64')
business.require(system['boot_id'] == receipt['identity_after']['boot_id'], 'POSTBOOT_ID_CHANGED')
pointer = json.loads((lab.root / 'state/win7-business-win7-x64.json').read_text(encoding='utf-8'))
binding = business.product_binding(context)
report = lab.root / 'reports/win7-x64' / ('postboot-' + uuid.uuid4().hex[:12])
report.mkdir()
write_json(report / 'context.json', {'binding': binding, 'reboot_receipt': str(receipt_path),
    'reboot_receipt_sha256': sha256(receipt_path), 'controller_sha256': sha256(Path(__file__))})
print(report, flush=True)
run = business.Win7Run(lab, 'configure', binding, pointer['remote'], report, system)
run.report['phase'] = 'postboot'
try:
    fixture = json.loads(run.client.powershell('[Console]::WriteLine([IO.File]::ReadAllText('
        + ps(run.state_path) + ',[Text.Encoding]::UTF8))'))
    business.require(business.product_binding(fixture['context']) == business.product_binding(pointer['context']),
                     'POSTBOOT_FIXTURE_CHANGED')
    run.state = {key: fixture[key] for key in ('username', 'password', 'user_id', 'task_id',
        'task_title', 'attachment_id', 'attachment_sha256', 'data_dir')}
    desktop = run.desktop('Inspect')
    run.require_configuration_owner(desktop)
    run.checked('postboot-health', run.wait_health())
    desktop = run.desktop('Inspect')
    run.require_configuration_owner(desktop)
    business.require(any(p['name'] == 'PartyOps.exe' and p['path'] == binding['app_path']
                         for p in desktop['processes']), 'POSTBOOT_INSTALLED_PROCESS_MISSING')
    run.checked('postboot-ordinary-runtime', desktop)
    run.checked('postboot-retained-account-task-attachment', run.check_business_data())
    blocks = qmp(lab.state('win7-x64')['qmp_port'], 'query-block')
    business.require(not any(p.get('inserted') for p in blocks if p.get('removable')), 'POSTBOOT_CD_PRESENT')
    run.report['status'] = 'passed'
except Exception as exc:
    run.report['error_type'] = type(exc).__name__
    raise
finally:
    run.report['completed_at'] = now()
    write_json(report / 'postboot.json', run.report)
    run.client.close()
