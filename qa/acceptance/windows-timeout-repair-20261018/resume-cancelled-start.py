"""本次受控回收已完成后仅补正常启动；独占新请求，不再次回收向导。"""
import argparse
import json
from pathlib import Path
import controller as c

OUT = Path(__file__).resolve().parent
r = c.load('cancelled_start_recovery', OUT / 'recover-host-journal.py')

def ready(value, original):
    m = value['metadata']
    task = m.get('resume_task') or {}
    c.require(m.get('wizard_count') == m.get('other_powershell_count') == m.get('consent_count') == m.get('cancel_relaunch_task_count') == 0
        and m.get('wizard_children') == [] and m.get('journal_present') is True and m.get('pending') is False and m.get('outcome') == ''
        and m.get('transaction_id') == original['metadata']['transaction_id'] and m.get('journal_sha256') == original['metadata']['journal_sha256']
        and value['snapshot'].get('user_mode') == value['snapshot'].get('system_mode') == 'personal'
        and {x['name']: x['state'] for x in m.get('services', [])} == {'PartyOpsHost': 'Stopped', 'PartyOpsUpdateService': 'Stopped'}
        and m.get('recovery_task_count') == 1 and task.get('state') == 3 and task.get('original_user_matches') is True
        and task.get('run_level') == 0 and task.get('logon_type') == 3
        and task.get('actions') == [{'path_matches': True, 'arguments': '--reconfigure --initial-role personal'}],
        'ONLY_MISSING_START_WITH_UNCHANGED_JOURNAL_ALLOWED')
    c.require(not any(x['name'] == 'PartyOpsWizard.exe' for x in value['snapshot']['processes']), 'NO_WIZARD_REQUIRED')

def launch_script(host, binding, since, original):
    # 复用已成功的正常任务启动；只换独立任务名，不包含回收动作。
    script = r.resume_script(host, binding, since, original).replace(r.TASK, r.CANCEL_TASK)
    c.require('CloseMainWindow' not in script and '.Kill(' not in script and 'Stop-Process' not in script, 'START_ONLY_REQUIRED')
    return script

def main():
    p = argparse.ArgumentParser()
    p.add_argument('--execute', action='store_true')
    args = p.parse_args()
    original_path = OUT / 'host-journal-cancelled-before.json'
    diagnosis_path = OUT / 'host-journal-cancelled-relaunch-readonly-diagnosis.json'
    failed_path = OUT / 'host-journal-cancelled-relaunch.result.json'
    original, diagnosis, failed = [c.contract.read(x) for x in (original_path, diagnosis_path, failed_path)]
    binding = original['candidate']
    c.require(diagnosis['candidate'] == failed['candidate'] == binding
        and diagnosis['failed_result_sha256'] == c.contract.digest(failed_path)
        and diagnosis['cancelled_before_sha256'] == c.contract.digest(original_path)
        and failed['operation'].get('phase') == 'recycle' and failed['operation'].get('error_id') == 'OTHER_LIVE_WIZARD_APPEARED'
        and failed['operation'].get('launch_requested') is False and diagnosis['details']['mutation_executed'] is False,
        'BOUND_FAILED_RECYCLE_DIAGNOSIS_REQUIRED')
    ready(diagnosis, original)
    if not args.execute:
        print('本地来源检查通过；--execute仅独占一次正常注册启动，不回收。'); return
    attempt = OUT / 'host-journal-cancelled-start.attempt.json'
    result = OUT / 'host-journal-cancelled-start.result.json'
    c.no_attempt(attempt); c.no_attempt(result)
    module, lab, target = c.environment()
    c.contract.bind_guest(lab, module.qmp)
    h, first, host, transition, restore = r.helpers()
    h.current_inputs(binding)
    client = module.windows.connect(lab, target)
    try:
        since = json.loads(module.windows.execute(client, restore.GUEST_CLOCK))['guest_utc']
        import uuid
        def probe(script):
            return host.transfer(client, module.windows.execute, script, 'host-wizard-' + uuid.uuid4().hex[:12])
        current = {'snapshot': probe(first.inspection_script(host, binding, since)),
                   'metadata': probe(r.metadata_script(host, binding, since, original['metadata']['transaction_id'])),
                   'runtime': c.contract.probe(client, module.windows.execute)}
        ready(current, original)
        c.contract.runtime_matches(current['runtime'], binding)
        verify = c.load('cancelled_start_personal', c.OLD / 'verify-retained-business.py')
        verify.same_restored_runtime(current['runtime'], original['runtime'], binding)
        c.contract.exclusive(attempt, {'candidate': binding, 'time_utc': c.contract.now(), 'before': current,
            'cancelled_before_sha256': c.contract.digest(original_path), 'failed_result_sha256': c.contract.digest(failed_path),
            'diagnosis_sha256': c.contract.digest(diagnosis_path), 'action': 'normal-task-register-start-only-no-recycle'})
        operation = host.transfer(client, lambda cli, script, timeout=120: r.captured_execute(cli, module.windows, script, timeout),
                                  launch_script(host, binding, since, original), 'host-wizard-' + uuid.uuid4().hex[:12])
        c.contract.exclusive(result, {'candidate': binding, 'attempt_sha256': c.contract.digest(attempt),
                                     'time_utc': c.contract.now(), 'operation': operation})
        c.require(operation.get('launch_requested') is True, 'START_FAILED_INSPECT_ONLY_NO_REPLAY')
        print('独立正常恢复启动已请求；由主控立即处理GUI UAC，尚未证明恢复完成。')
    finally:
        client.close()

if __name__ == '__main__':
    main()
