"""修复包实装后的定向回归：复用业务夹具，不复用旧包通过结论或修改旧记录。"""
import argparse
import importlib.util
import json
import sys
import uuid
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE))
from evidence import now, safe_child, sha256, write_json
from identity import probe
from lab import fingerprint, load_configuration
from providers import QemuLab, qmp
from windows_win7_standard import installation_binding, installed_script, ps

spec = importlib.util.spec_from_file_location('win7_ocr_business', HERE / 'scripts/exercise-win7-business.py')
business = importlib.util.module_from_spec(spec)
spec.loader.exec_module(business)
require = business.require


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--install-report', type=Path, required=True)
    parser.add_argument('--resume-retention', type=Path, help='仅续做同包同环境中被管理通道中断的数据保留核对')
    args = parser.parse_args()
    matrix, media = load_configuration()
    lab = QemuLab(matrix, Path(matrix['defaults']['primary_root']), media)
    binding = business.product_binding(installation_binding(lab, 'win7-x64', args.install_report, fingerprint()))
    pointer_path = lab.root / 'state/win7-business-win7-x64.json'
    pointer = json.loads(pointer_path.read_text(encoding='utf-8'))
    prior = business.product_binding(pointer['context'])
    require(all(prior[key] == binding[key] for key in ('target', 'uuid', 'environment', 'restore_generation')),
            'REPAIR_FIXTURE_ENVIRONMENT_CHANGED')
    require(prior['package']['sha256'] != binding['package']['sha256'], 'REPAIR_EXPECTS_CHANGED_PACKAGE')
    resumed = None
    if args.resume_retention:
        previous_dir = safe_child(lab.root / 'reports/win7-x64', args.resume_retention)
        previous_context = json.loads((previous_dir / 'context.json').read_text(encoding='utf-8'))
        previous_result = json.loads((previous_dir / 'repair-regression.json').read_text(encoding='utf-8'))
        completed = {row['id'] for row in previous_result.get('checks', []) if row.get('status') == 'passed'}
        require(previous_context['binding'] == binding
                and previous_context['fixture_pointer_sha256'] == sha256(pointer_path),
                'RETENTION_RESUME_BINDING_CHANGED')
        require(previous_result.get('error_type') == 'WSManFaultError'
                and previous_result.get('completed_at')
                and {'repaired-installed-payload', 'repaired-package-health',
                     'repaired-standard-user-runtime', 'empty-cd-runtime', 'actual-image-ocr'} <= completed
                and 'retained-account-task-attachment' not in completed,
                'RETENTION_RESUME_REQUIRES_COMPLETED_RUNTIME_AND_OCR')
        resumed = {'path': str(previous_dir), 'context_sha256': sha256(previous_dir / 'context.json'),
                   'result_sha256': sha256(previous_dir / 'repair-regression.json'),
                   'completed_checks': sorted(completed)}
    # 本轮必须按现场光驱状态记录，不能沿用旧诊断中“插入测试光盘”的固定值。
    media_before = qmp(lab.state('win7-x64')['qmp_port'], 'query-block')
    require(not any(row.get('inserted') for row in media_before if row.get('removable')),
            'REPAIR_REQUIRES_EMPTY_CD')
    report = lab.root / 'reports/win7-x64' / ('ocr-repaired-' + uuid.uuid4().hex[:12])
    report.mkdir(exist_ok=False)
    write_json(report / 'context.json', {'binding': binding, 'fixture_pointer': str(pointer_path),
        'fixture_pointer_sha256': sha256(pointer_path), 'prior_package_sha256': prior['package']['sha256'],
        'controller_sha256': sha256(Path(__file__)), 'prior_pass_results_imported': False,
        'same_package_continuation': resumed,
        'historical_startup_blocker': str(lab.root / 'reports/win7-x64/empty-cd-startup-block-20260910.json'),
        'media_before': media_before,
        'qa_input_cd_present_for_diagnosis': False, 'empty_cd_startup_passed': False})
    print(str(report), flush=True)
    # configure 参数只初始化空控制器；这里不调用 configure，不改动已保存的账号或配置。
    run = business.Win7Run(lab, 'configure', binding, pointer['remote'], report, probe(lab, 'win7-x64'))
    run.phase = 'repair-retention' if resumed else 'repair-regression'
    run.args.phase = run.phase
    run.report['phase'] = run.phase
    try:
        verified = json.loads(run.client.powershell(installed_script(binding), timeout=240))
        require(verified['verified'] is True, 'REPAIRED_INSTALLED_PAYLOAD_NOT_VERIFIED')
        fixture = json.loads(run.client.powershell('[Console]::WriteLine([IO.File]::ReadAllText('
            + ps(run.state_path) + ',[Text.Encoding]::UTF8))'))
        require(business.product_binding(fixture['context']) == prior, 'ORIGINAL_FIXTURE_CHANGED')
        # 仅提取测试输入与账号秘密；business_verified 等旧包通过标志不进入新验证。
        run.state = {key: fixture[key] for key in ('username', 'password', 'user_id', 'task_id',
                    'task_title', 'attachment_id', 'attachment_sha256', 'data_dir')}
        run.checked('repaired-installed-payload', verified)
        if resumed:
            # 已通过的启动/OCR引用原证据，不重发图片请求，不改写原中断报告。
            run.checked('retained-account-task-attachment', run.check_business_data())
            require(business.product_binding(installation_binding(lab, 'win7-x64', args.install_report, fingerprint())) == binding,
                    'REPAIR_BINDING_CHANGED')
            run.report.update(status='passed', completed_at=now(), same_package_continuation=resumed)
            print(json.dumps({'status': 'partial', 'retention': 'passed', 'report': str(report),
                              'runtime_environment_passed': False}))
            return
        # 原普通用户启动任务已经发出；只等待原进程，不重复创建启动任务。
        desktop = run.desktop('Inspect')
        run.require_configuration_owner(desktop)
        require(any(row['name'] in ('PartyOpsLauncher.exe', 'PartyOps.exe')
                    for row in desktop['processes']), 'ORIGINAL_STANDARD_START_PROCESS_MISSING')
        run.checked('repaired-package-health', run.wait_health())
        desktop = run.desktop('Inspect')
        run.require_configuration_owner(desktop)
        require(any(row['name'] == 'PartyOps.exe' and row['path'] == binding['app_path']
                    for row in desktop['processes']), 'REPAIRED_INSTALLED_RUNTIME_MISSING')
        run.checked('repaired-standard-user-runtime', desktop)
        media_after = qmp(lab.state('win7-x64')['qmp_port'], 'query-block')
        require(not any(row.get('inserted') for row in media_after if row.get('removable')),
                'REPAIR_CD_CHANGED_DURING_RUNTIME_CHECK')
        # 此项证明当前普通用户运行时可在空光驱下提供health，不代替真正重启场景。
        run.checked('empty-cd-runtime', {'media_after': media_after, 'cold_boot_verified': False})
        run.ocr()
        run.checked('retained-account-task-attachment', run.check_business_data())
        require(business.product_binding(installation_binding(lab, 'win7-x64', args.install_report, fingerprint())) == binding,
                'REPAIR_BINDING_CHANGED')
        run.report.update(status='passed', completed_at=now())
    except Exception as exc:
        run.report.update(error_type=type(exc).__name__, completed_at=now())
        raise
    finally:
        write_json(report / 'repair-regression.json', run.report)
        run.client.close()
    print(json.dumps({'status': 'partial', 'repair_regression': 'passed', 'report': str(report),
                      'runtime_environment_passed': False}))


if __name__ == '__main__':
    main()
