"""在原版 Win7 的安装版 HTTP 服务中执行真实 WPS 任务；每个任务只提交一次。"""
import importlib.util
import base64
import argparse
import json
import sys
import time
import uuid
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE))
from evidence import now, sha256, write_json
from identity import probe
from lab import fingerprint, load_configuration
from providers import QemuLab, qmp
from windows_win7_standard import installation_binding, installed_script, ps

spec = importlib.util.spec_from_file_location('wps_business', HERE / 'scripts/exercise-win7-business.py')
business = importlib.util.module_from_spec(spec)
spec.loader.exec_module(business)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--install-report', type=Path, required=True)
    parser.add_argument('--resume-upload', type=Path)
    parser.add_argument('--resume-jobs', type=Path, help='续收已提交任务，不重新上传或提交同一功能')
    parser.add_argument('--retry-missing-fonts', type=Path, help='安装字体后仅重试明确缺字体的排版任务')
    parser.add_argument('--renew-expired-session', action='store_true')
    args = parser.parse_args()
    matrix, media = load_configuration()
    lab = QemuLab(matrix, Path(matrix['defaults']['primary_root']), media)
    binding = business.product_binding(installation_binding(lab, 'win7-x64', args.install_report, fingerprint()))
    media_before = qmp(lab.state('win7-x64')['qmp_port'], 'query-block')
    business.require(not any(row.get('inserted') for row in media_before if row.get('removable')),
                     'WPS_REQUIRES_EMPTY_CD')
    pointer = json.loads((lab.root / 'state/win7-business-win7-x64.json').read_text(encoding='utf-8'))
    report = lab.root / 'reports/win7-x64' / ('wps-installed-' + uuid.uuid4().hex[:12])
    report.mkdir(exist_ok=False)
    write_json(report / 'context.json', {'binding': binding, 'controller_sha256': sha256(Path(__file__)),
        'prior_pass_results_imported': False, 'startup_workaround_cd_present': False, 'media_before': media_before,
        'workspace_source_fingerprint': fingerprint(), 'release_eligible': False})
    print(report, flush=True)
    run = business.Win7Run(lab, 'configure', binding, pointer['remote'], report, probe(lab, 'win7-x64'))
    run.phase = run.args.phase = 'wps-installed'
    run.report['phase'] = run.phase
    try:
        business.require(sum(bool(value) for value in (args.resume_upload, args.resume_jobs, args.retry_missing_fonts)) <= 1,
                         'RESUME_MODE_CONFLICT')
        existing_input = args.resume_jobs or args.retry_missing_fonts
        previous = None
        if args.resume_upload or existing_input:
            previous = (args.resume_upload or existing_input).resolve()
            business.require(previous.parent == (lab.root / 'reports/win7-x64').resolve()
                             and (existing_input or not (previous / 'input.json').exists()), 'RESUME_UPLOAD_ALREADY_SUBMITTED')
            prior = json.loads((previous / 'context.json').read_text(encoding='utf-8'))
            business.require(prior['binding'] == binding, 'RESUME_INSTALLED_PACKAGE_CHANGED')
            previous_report = json.loads((previous / 'wps-installed.json').read_text(encoding='utf-8'))
            if args.retry_missing_fonts:
                failed = [row for row in previous_report['checks'] if row['id'] == 'actual-wps-format']
                business.require(previous_report.get('completed_at') and len(failed) == 1
                    and failed[0]['result']['state'] == 'failed'
                    and all(item['error_code'] == 'REQUIRED_FONT_MISSING' for item in failed[0]['result']['items']),
                    'FONT_RETRY_REQUIRES_EXPLICIT_MISSING_FONT')
            if args.resume_jobs:
                business.require(previous_report.get('completed_at')
                                 and previous_report.get('error_type') == 'WSManFaultError',
                                 'RESUME_REQUIRES_TERMINAL_TRANSPORT_FAILURE')
            verified_report = previous_report
            if previous_report.get('resumed_from'):
                hash_report = Path(previous_report['resumed_from']).resolve()
                business.require(hash_report.parent == previous.parent, 'HASH_REPORT_OUTSIDE_TARGET')
                business.require(json.loads((hash_report / 'context.json').read_text(encoding='utf-8'))['binding'] == binding,
                                 'HASH_REPORT_BINDING_CHANGED')
                verified_report = json.loads((hash_report / 'wps-installed.json').read_text(encoding='utf-8'))
            business.require(any(row['id'] == 'installed-payload' and row['status'] == 'passed'
                                 for row in verified_report['checks']), 'PRIOR_INSTALLED_HASH_NOT_VERIFIED')
            run.report['resumed_from'] = str(previous)
            private_ref = previous_report.get('private_session_reference', previous.name)
            business.require(Path(private_ref).name == private_ref, 'INVALID_PRIVATE_SESSION_REFERENCE')
            private_path = 'C:\\ProgramData\\PartyOps-VM-Lab\\private\\' + private_ref + '.json'
            private = json.loads(run.client.powershell('[Console]::WriteLine([IO.File]::ReadAllText('
                + ps(private_path) + ',[Text.Encoding]::UTF8))'))
            base, session = private['base'], private['session']
            run.report['private_session_reference'] = private_ref
            if args.renew_expired_session:
                base, session = open_session(run, binding, report, pointer, verify_payload=False)
        else:
            base, session = open_session(run, binding, report, pointer)
        prefix = '/v1/sessions/' + session['session_id']
        headers = {'X-PartyOps-Local-Token': session['session_token']}
        source = HERE.parents[1] / 'backend/tests/fixtures/document-formatter-source/input-manual-break.docx'
        original_hash = sha256(source)
        boundary = 'PartyOpsQAWps' + uuid.uuid4().hex
        header = ('--' + boundary + '\r\nContent-Disposition: form-data; name="document"; '
                  'filename="sample.docx"\r\nContent-Type: application/octet-stream\r\n\r\n').encode()
        trailer = ('\r\n--' + boundary + '--\r\n').encode()
        remote_input = 'C:\\PartyOps-QA\\' + report.name + '\\sample.docx'
        if existing_input:
            prior_input = json.loads((previous / 'input.json').read_text(encoding='utf-8'))
            business.require(prior_input['sha256'] == original_hash, 'RESUME_INPUT_CHANGED')
            document = prior_input['document']
        else:
            run.client.put(source, remote_input, original_hash)
            upload = business.GuestUpload(remote_input, source.stat().st_size, header, trailer)
            document = run.request(prefix + '/documents', 'POST', upload, base=base,
                                   content_type='multipart/form-data; boundary=' + boundary,
                                   extra_headers={**headers, 'Content-Length': str(len(header) + upload.size + len(trailer))})
        write_json(report / 'input.json', {'source': str(source), 'sha256': original_hash, 'document': document})
        for feature, options in [('convert', {'compatibility_mode': 'wps', 'target_format': 'pdf'}),
                                 ('format', {'compatibility_mode': 'wps', 'template': 'GB/T 9704-2012'})]:
            if args.retry_missing_fonts and feature != 'format':
                continue
            if args.resume_jobs and (previous / (feature + '-submitted.json')).exists():
                submitted = json.loads((previous / (feature + '-submitted.json')).read_text(encoding='utf-8'))
                job = run.request(prefix + '/jobs/' + submitted['id'], base=base, extra_headers=headers)
            else:
                job = run.request(prefix + '/jobs', 'POST', {'feature_id': feature,
                    'document_ids': [document['document_id']], 'options': options}, base=base, extra_headers=headers)
            write_json(report / (feature + '-submitted.json'), job)
            deadline = time.monotonic() + 600
            while job['state'] not in ('completed', 'failed', 'completed_with_errors', 'cancelled'):
                business.require(time.monotonic() < deadline, 'WPS_JOB_PENDING_COLLECT_EXISTING_JOB')
                time.sleep(10)
                job = run.request(prefix + '/jobs/' + job['id'], base=base, extra_headers=headers)
                write_json(report / (feature + '-job.json'), job)
            for output in job['outputs']:
                data = run.request(prefix + '/jobs/' + job['id'] + '/outputs/' + output['id'],
                                   base=base, extra_headers=headers)
                destination = report / (feature + Path(output['filename']).suffix)
                destination.write_bytes(data)
                output['download_sha256'] = sha256(destination)
                if feature == 'convert':
                    import fitz
                    with fitz.open(destination) as pdf:
                        business.require(len(pdf) > 0, 'WPS_PDF_EMPTY')
                        output['pages'] = len(pdf)
                        output['text'] = '\n'.join(page.get_text() for page in pdf)
                else:
                    from docx import Document
                    output['paragraphs'] = len(Document(destination).paragraphs)
                    business.require(output['paragraphs'] == 8, 'WPS_MANUAL_BREAK_FORMAT_MISMATCH')
            run.report['checks'].append({'id': 'actual-wps-' + feature,
                'status': 'passed' if job['state'] == 'completed' else 'failed', 'result': job})
            write_json(report / 'wps-installed.json', run.report)
            print(feature + ': ' + job['state'], flush=True)
        business.require(sha256(source) == original_hash, 'SOURCE_FIXTURE_CHANGED')
        business.require(business.product_binding(installation_binding(lab, 'win7-x64', args.install_report,
                         fingerprint())) == binding, 'WPS_INSTALLED_BINDING_CHANGED')
        run.report['status'] = 'partial'
    except Exception as exc:
        run.report['error_type'] = type(exc).__name__
        raise
    finally:
        run.report['completed_at'] = now()
        write_json(report / 'wps-installed.json', run.report)
        run.client.close()


def open_session(run, binding, report, pointer, verify_payload=True):
    if verify_payload:
        print('核对安装包及实际程序哈希', flush=True)
        run.client.powershell('[Console]::WriteLine("WPS_CHECK_READY")')
        # TCG 下 467 MiB 的完整 SHA 校验可能超过 WinRM 默认 70 秒读超时。
        # 只延长这条只读核验的响应等待，不重发安装或业务写入。
        run.client._native_client.protocol.transport.read_timeout_sec = 250
        payload = json.loads(run.client.powershell(installed_script(binding), timeout=240))
        business.require(payload['verified'] is True, 'INSTALLED_PAYLOAD_CHANGED')
        run.checked('installed-payload', payload)
    fixture = json.loads(run.client.powershell('[Console]::WriteLine([IO.File]::ReadAllText('
            + ps(run.state_path) + ',[Text.Encoding]::UTF8))'))
    business.require(business.product_binding(fixture['context']) == business.product_binding(pointer['context']),
                         'FIXTURE_CONTEXT_CHANGED')
    run.state = {key: fixture[key] for key in ('username', 'password')}
    print('连接普通用户运行实例', flush=True)
    run.login()
    ticket = run.request('/api/v1/official-format/local-ticket', 'POST', {'origin': run.origin})
    base = ticket['local_base_url']
    run.checked('formatter-installed-capabilities', run.request('/v1/capabilities', base=base))
    session = run.request('/v1/sessions', 'POST', {}, base=base,
                              extra_headers={'Authorization': 'Bearer ' + ticket['ticket']})
        # 回包中断时续收同一任务；令牌只进入 Guest 原有私有目录。
    private_path = 'C:\\ProgramData\\PartyOps-VM-Lab\\private\\' + report.name + '.json'
    private = base64.b64encode(json.dumps({'base': base, 'session': session}).encode()).decode()
    run.client.powershell('[IO.File]::WriteAllBytes(' + ps(private_path)
                              + ",[Convert]::FromBase64String('" + private + "'))")
    return base, session


if __name__ == '__main__':
    main()
