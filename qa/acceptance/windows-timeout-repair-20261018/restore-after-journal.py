"""薄适配既有recover-prepare：只续接本轮已退出向导后的正常个人恢复。"""
import argparse
from pathlib import Path
from types import SimpleNamespace
import controller as c

OUT = Path(__file__).resolve().parent

def configure():
    h = c.load('restore_after_confirmed_host', OUT / 'host-after-journal.py').configure()
    module, lab, target = c.environment()
    first, host, form, transition = h.helpers()
    original_configuration = module.load_configuration
    def configuration():
        matrix, media = original_configuration()
        media.setdefault('win10-x64', {'format': 'iso', 'arch': 'x86_64'})
        return matrix, media
    module.load_configuration = configuration
    restore = h.configured_restore(first, host, form, transition, module)
    diagnosis_file = OUT / 'after-journal-restore-personal-after-prepare-failure-inspect.json'
    read = restore.contract.read
    def read_source(path):
        if Path(path) == OUT / 'restore-personal-preflight-inspect.json':
            # 既有recover-prepare读取此键；本轮实际来源是失败后只读诊断，不伪造历史preflight。
            return c.contract.read(diagnosis_file)
        return read(path)
    restore.contract.read = read_source
    def links(binding):
        failure = OUT / 'after-journal-restore-personal-prepare.result.json'
        attempt = OUT / 'after-journal-restore-personal-prepare.attempt.json'
        value, intent, observed = [c.contract.read(p) for p in (failure, attempt, diagnosis_file)]
        c.require(value.get('status') == 'failed' and value.get('candidate') == intent.get('candidate') == observed.get('candidate') == binding
            and value.get('attempt_sha256') == c.contract.digest(attempt)
            and observed.get('failed_result_sha256') == c.contract.digest(failure)
            and value.get('original_personal_instance', 'missing') is None and intent.get('original_personal_instance', 'missing') is None
            and observed['snapshot'].get('wizard_exists') is False and observed['snapshot'].get('user_mode') == observed['snapshot'].get('system_mode') == 'host'
            and observed['metadata'].get('consent_count') == observed['metadata'].get('other_powershell_count') == 0
            and observed['metadata'].get('journal_present') is False and observed['metadata'].get('pending') is False
            and observed['details'].get('mutation_script_executed') is False
            and not any(t['name'] == h.RESTORE_TASK for t in observed['details']['tasks'])
            and observed.get('failed_script_sha256') == c.contract.digest(OUT / observed['failed_script_local']),
            'BOUND_FAILED_RESTORE_AND_READONLY_HOST_REQUIRED')
        restore.services_match(observed['services'], 'Running')
        return {'failed_prepare_sha256': c.contract.digest(failure), 'failed_prepare_attempt_sha256': c.contract.digest(attempt),
                'partial_inspection_sha256': c.contract.digest(diagnosis_file),
                'failed_guest_script_sha256': observed['failed_script_sha256']}
    restore.recovery_links = links
    original_script = restore.recovery_script
    def script(*args):
        value = original_script(*args).replace('PartyOps-QA-Restore-Personal-20261017', h.RESTORE_TASK)
        c.require('CloseMainWindow' not in value and '.Kill(' not in value, 'RECOVER_PREPARE_START_ONLY_REQUIRED')
        return value
    restore.recovery_script = script
    def completion_sources(binding):
        validate_recovered(binding)
        files = {'submit_attempt': 'after-journal-restore-personal-submit.attempt.json',
                 'failed_submit': 'after-journal-restore-personal-submit.result.json',
                 'observed_prepare': 'after-journal-restore-personal-recover-prepare.result.json',
                 'host_success': 'after-journal-first-host-approved-existing-root-submit.result.json'}
        values = {key: c.contract.read(OUT / name) for key, name in files.items()}
        source_links = {(('recovered_prepare' if key == 'observed_prepare' else key) + '_sha256'): c.contract.digest(OUT / name)
                        for key, name in files.items()}
        failed, intent = values['failed_submit'], values['submit_attempt']
        c.require(all(v.get('candidate') == binding for v in values.values()) and failed.get('status') == 'failed'
            and failed.get('error_code') == 'SINGLE_WIZARD_REQUIRED'
            and failed.get('attempt_sha256') == source_links['submit_attempt_sha256']
            and failed.get('states') and failed['states'][-1] == {'status': 'running', 'code': 'RUNTIME_CONFIGURING'}
            and intent.get('action') == 'submit' and intent.get('prepared_source') == 'recover-prepare'
            and intent.get('host_success_sha256') == source_links['host_success_sha256']
            and failed.get('recovery_links') == intent.get('recovery_links')
            and failed.get('runtime_action_started_at') == intent.get('clock_observation', {}).get('guest_utc')
            and values['observed_prepare'].get('original_personal_data_directory') == intent.get('original_personal_data_directory') == failed.get('original_personal_data_directory'),
            'BOUND_RECOVERED_SUBMITTED_OBSERVATION_FAILURE_REQUIRED')
        return values, source_links
    restore.completion_sources = completion_sources
    return h, restore

def main():
    p = argparse.ArgumentParser()
    p.add_argument('action', choices=['check', 'recover-prepare', 'submit', 'verify-completion'])
    p.add_argument('--package', type=Path, required=True)
    p.add_argument('--sha256', required=True)
    p.add_argument('--execute', action='store_true')
    args = p.parse_args()
    h, restore = configure()
    diagnosis = c.contract.read(OUT / 'after-journal-restore-personal-after-prepare-failure-inspect.json')
    c.require(diagnosis['candidate']['package']['sha256'] == args.sha256, 'EXPLICIT_CANDIDATE_REQUIRED')
    h.current_inputs(diagnosis['candidate'])
    restore.recovery_links(diagnosis['candidate'])
    if args.action == 'check':
        print('本地失败/诊断链通过；recover-prepare复用既有仅启动路径。'); return
    c.require(args.execute, 'EXPLICIT_EXECUTE_REQUIRED')
    if args.action == 'verify-completion':
        c.no_attempt(OUT / 'after-journal-restore-personal-verify-completion.attempt.json')
        c.no_attempt(OUT / 'after-journal-restore-personal-observed-completion.json')
        restore.verify_completion(args)
        return
    for suffix in ('attempt', 'result'):
        c.no_attempt(OUT / ('after-journal-restore-personal-' + args.action + '.' + suffix + '.json'))
    restore.run(SimpleNamespace(action=args.action, package=args.package, sha256=args.sha256,
                                use_recovered_prepare=args.action == 'submit', use_observed_recovery=False))

def validate_recovered(binding):
    """发布离线核对真实恢复准备及submit链，保留失败prepare。"""
    h, restore = configure()
    links = restore.recovery_links(binding)
    names = ['after-journal-restore-personal-prepare.attempt.json', 'after-journal-restore-personal-prepare.result.json',
             'after-journal-restore-personal-after-prepare-failure-inspect.json',
             'after-journal-restore-personal-recover-prepare.attempt.json', 'after-journal-restore-personal-recover-prepare.result.json']
    intent, prepared = [c.contract.read(OUT / name) for name in names[-2:]]
    submit = c.contract.read(OUT / 'after-journal-restore-personal-submit.attempt.json')
    c.require(intent.get('candidate') == prepared.get('candidate') == submit.get('candidate') == binding
        and prepared.get('status') == 'prepared' and prepared.get('attempt_sha256') == c.contract.digest(OUT / names[-2])
        and intent.get('action') == 'recover-prepare' and intent.get('prepared_source') == prepared.get('prepared_source') == submit.get('prepared_source') == 'recover-prepare'
        and intent.get('recovery_links') == prepared.get('recovery_links') == submit.get('recovery_links') == links
        and prepared.get('operation', {}).get('launch_requested') is True
        and intent.get('original_personal_instance', 'missing') is None
        and prepared.get('original_personal_instance', 'missing') is None
        and submit.get('original_personal_instance', 'missing') is None,
        'RECOVERED_RESTORE_PREPARE_SUBMIT_CHAIN_REQUIRED')
    return names, ['after-journal-restore-personal-failed-prepare.ps1']

def validate_completion(binding):
    """消费只读结案的真实来源，不将失败submit或未知HTTP终态改写。"""
    h, restore = configure()
    _, links = restore.completion_sources(binding)
    attempt = OUT / 'after-journal-restore-personal-verify-completion.attempt.json'
    completed = OUT / 'after-journal-restore-personal-observed-completion.json'
    intent, value = c.contract.read(attempt), c.contract.read(completed)
    c.require(value.get('candidate') == intent.get('candidate') == binding
        and value.get('status') == 'restored-ready' and value.get('attempt_sha256') == c.contract.digest(attempt)
        and value.get('completion_kind') == 'read-only-observed-after-submit' and value.get('http_terminal_observed') is False
        and intent.get('operation') == 'read-only-verify-completion'
        and all(value.get(k) == intent.get(k) == v for k, v in links.items())
        and value.get('original_data_directory_verified') is True
        and value.get('original_personal_data_directory') == restore.completion_sources(binding)[0]['submit_attempt']['original_personal_data_directory']
        and value.get('user_mode') == value.get('system_mode') == 'personal'
        and value.get('pending_mode_switch') is False and value.get('user_journal_present') is False,
        'BOUND_OBSERVED_RESTORED_COMPLETION_REQUIRED')
    restore.services_match({'services': value.get('services', [])}, 'Stopped')
    c.contract.runtime_matches(value.get('runtime', {}), binding)
    return value['runtime']

if __name__ == '__main__': main()
