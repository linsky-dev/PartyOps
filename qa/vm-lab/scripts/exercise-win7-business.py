"""Win7原版安装包业务验收：复用现有业务断言，通过WinRM访问Guest回环HTTP。"""
from __future__ import annotations

import argparse
import base64
import email.parser
import http.cookiejar
import io
import json
import secrets
import sys
import urllib.parse
import urllib.request
import urllib.response
import uuid
import warnings
from pathlib import Path
from types import SimpleNamespace

HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE))
from evidence import now, safe_child, sha256, write_json
from identity import probe
from lab import fingerprint, load_configuration
from providers import QemuLab
from windows_remote import WinRMFiles, session, transfer_path
from windows_win7_standard import JSON_PS, installation_binding, ps, validate_account

import importlib.util
spec = importlib.util.spec_from_file_location('win7_shared_windows_business', HERE / 'scripts/exercise-windows-business.py')
shared = importlib.util.module_from_spec(spec)
spec.loader.exec_module(shared)
require = shared.require


def product_binding(binding):
    """产品/环境严格绑定；QA实现另记每轮报告，修复QA不重做已通过的产品步骤。"""
    return {key: value for key, value in binding.items() if key not in {'scripts', 'controller_sha256'}}


class GuestHTTP(urllib.request.HTTPHandler):
    """保留标准urllib Cookie/CSRF/HTTP错误处理，HTTP仅在已验证Guest内发起。"""
    def __init__(self, client):
        super().__init__()
        self.client = client

    def http_open(self, request):
        parsed = urllib.parse.urlsplit(request.full_url)
        require(parsed.scheme == 'http' and parsed.hostname == '127.0.0.1'
                and not parsed.username and not parsed.password and not parsed.fragment,
                'ONLY_GUEST_LOOPBACK_HTTP_ALLOWED')
        data = request.data or b''
        require(isinstance(data, GuestUpload) or (isinstance(data, bytes) and len(data) <= 8 * 1024 * 1024),
                'WIN7_HTTP_LARGE_UPLOAD_REQUIRES_STREAMING')
        payload = {'url': request.full_url, 'method': request.get_method(),
                   'headers': dict(request.header_items()), 'body': base64.b64encode(data if isinstance(data, bytes) else b'').decode()}
        if isinstance(data, GuestUpload):
            payload['upload'] = {'path': transfer_path(data.path), 'size': data.size,
                                 'header': base64.b64encode(data.header).decode(),
                                 'trailer': base64.b64encode(data.trailer).decode()}
        encoded = base64.b64encode(json.dumps(payload).encode()).decode()
        script = JSON_PS + "$payload=$json.DeserializeObject([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + encoded + "')))\n" + r'''
$request=[Net.HttpWebRequest]::Create([string]$payload['url'])
$request.Proxy=$null;$request.AllowAutoRedirect=$false;$request.Timeout=240000;$request.ReadWriteTimeout=240000
$request.Method=[string]$payload['method']
foreach($key in $payload['headers'].Keys){
 $value=[string]$payload['headers'][$key]
 switch($key.ToLowerInvariant()){
  'content-type' {$request.ContentType=$value}
  'user-agent' {$request.UserAgent=$value}
  'accept' {$request.Accept=$value}
  'host' {}
  'content-length' {}
  'connection' {}
  # PS2 对部分扩展请求头的默认索引器绑定失败；使用 .NET 的标准方法。
  default {$request.Headers.Set([string]$key,[string]$value)}
 }
}
$body=[Convert]::FromBase64String([string]$payload['body'])
if($payload.ContainsKey('upload')){
 $upload=$payload['upload'];$file=[IO.File]::OpenRead([string]$upload['path'])
 try{
  if($file.Length -ne [long]$upload['size']){throw 'UPLOAD_SIZE_CHANGED'}
  $header=[Convert]::FromBase64String([string]$upload['header']);$trailer=[Convert]::FromBase64String([string]$upload['trailer'])
  $request.AllowWriteStreamBuffering=$false;$request.ContentLength=$header.Length+$file.Length+$trailer.Length
  $stream=$request.GetRequestStream()
  try{
   $stream.Write($header,0,$header.Length);$buffer=New-Object byte[] 65536
   while(($count=$file.Read($buffer,0,$buffer.Length)) -gt 0){$stream.Write($buffer,0,$count)}
   $stream.Write($trailer,0,$trailer.Length)
  }finally{$stream.Close()}
 }finally{$file.Close()}
}elseif($body.Length -gt 0 -or $request.Method -eq 'POST' -or $request.Method -eq 'PUT' -or $request.Method -eq 'PATCH'){
 $request.ContentLength=$body.Length;$stream=$request.GetRequestStream()
 try{$stream.Write($body,0,$body.Length)}finally{$stream.Close()}
}
try{$response=$request.GetResponse()}catch [Net.WebException]{
 if(-not $_.Exception.Response){throw 'GUEST_HTTP_CONNECTION_FAILED'}
 $response=$_.Exception.Response
}
try{
 $memory=New-Object IO.MemoryStream;$stream=$response.GetResponseStream();$buffer=New-Object byte[] 65536
 try{while(($count=$stream.Read($buffer,0,$buffer.Length)) -gt 0){
  $memory.Write($buffer,0,$count)
  if($memory.Length -gt 8MB){throw 'GUEST_HTTP_RESPONSE_REQUIRES_STREAMING'}
 }}finally{$stream.Close()}
 $headers=@()
 foreach($key in $response.Headers.AllKeys){foreach($value in $response.Headers.GetValues($key)){$headers+=([string]$key+': '+[string]$value)}}
 $json.MaxJsonLength=16777216
 [Console]::WriteLine($json.Serialize(@{status=[int]$response.StatusCode;reason=[string]$response.StatusDescription;headers=$headers;body=[Convert]::ToBase64String($memory.ToArray())}))
 $memory.Close()
}finally{$response.Close()}
'''
        try:
            result = json.loads(self.client.powershell(script, timeout=300))
        except RuntimeError as exc:
            # 只读健康轮询沿用 urllib 的未连接语义；业务写请求不重发。
            if request.get_method() == 'GET' and parsed.path == '/api/v1/health' and 'id=GUEST_HTTP_CONNECTION_FAILED ' in str(exc):
                raise urllib.error.URLError('GUEST_HTTP_CONNECTION_FAILED') from None
            raise
        headers = email.parser.Parser().parsestr('\r\n'.join(result['headers']) + '\r\n\r\n')
        response = urllib.response.addinfourl(io.BytesIO(base64.b64decode(result['body'])), headers,
                                             request.full_url, result['status'])
        response.msg = result['reason']
        return response


class GuestUpload:
    """已通过既有文件传输校验的输入；HTTP在Guest内流式读文件，不膨胀控制命令。"""
    def __init__(self, path, size, header, trailer):
        self.path, self.size, self.header, self.trailer = path, size, header, trailer


class Win7Commands(WinRMFiles):
    """小命令直接启动 PowerShell，避开 cmd 长度限制及 PS2 管道 EOF；大文件仍用流接口。"""
    def close(self):
        client = getattr(self, '_native_client', None)
        if client is not None and getattr(self, '_native_shell', None) is not None:
            try:
                client.protocol.close_shell(self._native_shell)
            except Exception as exc:
                warnings.warn('WIN7_SHELL_CLEANUP_FAILED:' + type(exc).__name__, RuntimeWarning)
            self._native_client = None

    def powershell(self, script, timeout=600):
        client = getattr(self, '_native_client', None)
        if client is None:
            client = session(self.lab, self.target, min(timeout, 60))
            self._native_client = client
            self._native_state = self.lab.state(self.target) if self.lab is not None else None
        if self.lab is not None:
            current = self.lab.state(self.target)
            require(current['pid'] == self._native_state['pid'] and current['uuid'] == self._native_state['uuid']
                    and self.lab.live(current), 'WIN7_NATIVE_SESSION_GUEST_CHANGED')
        self._native_shell = client.protocol.open_shell(codepage=65001)
        payload = "$ErrorActionPreference='Stop'; [Console]::OutputEncoding=New-Object Text.UTF8Encoding($false); try {\n" + script + r'''
}catch{
 [Console]::Error.WriteLine('WIN7_PS_FAILURE type='+$_.Exception.GetType().FullName+' id='+$_.FullyQualifiedErrorId+' line='+$_.InvocationInfo.ScriptLineNumber)
 exit 1
}
'''
        encoded = base64.b64encode(payload.encode('utf-16le')).decode('ascii')
        protocol = client.protocol
        shell = self._native_shell
        command = None
        try:
            command = protocol.run_command(shell, client.partyops_powershell,
                ['-NoProfile', '-NonInteractive', '-EncodedCommand', encoded],
                console_mode_stdin=True, skip_cmd_shell=True)
            output, errors, status = protocol.get_command_output(shell, command)
            if status:
                import re
                failure = re.search(r'WIN7_PS_FAILURE type=[\w.]+ id=[\w.,]+ line=\d+', errors.decode('utf-8', errors='replace'))
                raise RuntimeError('WIN7_NATIVE_COMMAND_FAILED:' + str(status) + ('; ' + failure[0] if failure else ''))
            return output.decode('utf-8-sig', errors='replace').strip()
        finally:
            # 清理失败不能覆盖已取得的业务回包，也不能触发业务请求重发。
            for cleanup in ([lambda: protocol.cleanup_command(shell, command)] if command is not None else []):
                try:
                    cleanup()
                except Exception as exc:
                    warnings.warn('WIN7_COMMAND_CLEANUP_FAILED:' + type(exc).__name__, RuntimeWarning)
            # Win7单一长寿命shell会积累操作额度；每条命令后关闭shell，保留NTLM传输。
            # 不重发业务请求，也不修改Guest的WinRM配额。
            try:
                protocol.close_shell(shell)
            except Exception as exc:
                warnings.warn('WIN7_SHELL_CLEANUP_FAILED:' + type(exc).__name__, RuntimeWarning)
            finally:
                self._native_shell = None


class Win7Run(shared.WindowsRun):
    def __init__(self, lab, phase, binding, remote, reports, system):
        self.lab, self.target, self.phase = lab, 'win7-x64', phase
        self.client = Win7Commands(lab, self.target)
        self.package, self.context, self.remote, self.reports = binding['package'], binding, remote, reports
        self.args = SimpleNamespace(phase=phase, uuid=binding['uuid'], package_sha256=self.package['sha256'])
        self.boot = system['boot_id']
        self.state_path = 'C:\\ProgramData\\PartyOps-VM-Lab\\private\\' + remote + '.json'
        self.state = {} if phase == 'configure' else json.loads(self.client.powershell(
            '[Console]::WriteLine([IO.File]::ReadAllText(' + ps(self.state_path) + ',[Text.Encoding]::UTF8))'))
        if self.state:
            require(product_binding(self.state['context']) == binding, 'WIN7_BUSINESS_CONTEXT_CHANGED')
            self.state['context'] = binding
        self.report = {'scope': 'win7-installed-standard-user-business', 'phase': phase,
                       'generated_at': now(), 'guest_uuid': binding['uuid'], 'boot_id': self.boot,
                       'package_sha256': self.package['sha256'], 'status': 'failed',
                       'runtime_environment_passed': False, 'checks': []}
        self.cookies = http.cookiejar.CookieJar()
        self.opener = self.make_opener(self.cookies)
        self.origin = 'http://127.0.0.1:18775'

    def make_opener(self, cookies):
        return urllib.request.build_opener(urllib.request.ProxyHandler({}), GuestHTTP(self.client),
                                           urllib.request.HTTPCookieProcessor(cookies))

    def persist(self):
        # 测试账号秘密仅保存到既有Guest管理员私有目录，不进入宿主报告。
        encoded = base64.b64encode(json.dumps(self.state, ensure_ascii=False).encode()).decode()
        self.client.powershell('[IO.File]::WriteAllBytes(' + ps(self.state_path)
                               + ",[Convert]::FromBase64String('" + encoded + "'))")

    def upload_input(self, endpoint, path, checksum):
        require(path.is_file() and sha256(path) == checksum, 'PINNED_RUNTIME_INPUT_MISSING_OR_CHANGED')
        destination = 'C:\\PartyOps-QA\\' + self.remote + '\\input-' + checksum + path.suffix
        self.client.put(path, destination, checksum)
        boundary = 'PartyOpsQA' + secrets.token_hex(12)
        header = (f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="{path.name}"\r\n'
                  'Content-Type: application/octet-stream\r\n\r\n').encode()
        trailer = f'\r\n--{boundary}--\r\n'.encode()
        return self.request(endpoint, 'POST', GuestUpload(destination, path.stat().st_size, header, trailer),
                            content_type='multipart/form-data; boundary=' + boundary,
                            extra_headers={'Content-Length': str(len(header) + path.stat().st_size + len(trailer))})

    def desktop(self, action):
        path = 'C:\\PartyOps-QA\\' + self.remote + r'\windows-standard-user.ps1'
        script = '& ([scriptblock]::Create([IO.File]::ReadAllText(' + ps(path) + ',[Text.Encoding]::UTF8)))'
        return json.loads(self.client.powershell(script + ' -ExpectedUuid ' + self.args.uuid + ' -Action ' + action))

    def checked(self, name, value):
        def public(item):
            if isinstance(item, dict):
                return {key: public(value) for key, value in item.items() if key not in {'url', 'wizard_url', 'password'}}
            if isinstance(item, list):
                return [public(value) for value in item]
            return item
        super().checked(name, public(value))

    def configure(self):
        info = self.desktop('Inspect')
        validate_account(info, self.context, desktop=True)
        require(info['config_dir'], 'STANDARD_USER_PROFILE_MISSING')
        clean = self.client.powershell('[Console]::WriteLine([IO.File]::Exists('
                                       + ps(info['config_dir'] + r'\mode.json') + '))').strip()
        require(clean.lower() == 'false', 'FIRST_CONFIGURATION_REQUIRES_CLEAN_USER')
        self.state = {'context': self.context, 'uuid': self.args.uuid,
                      'package_sha256': self.package['sha256'], 'username': 'lifecycleqa',
                      'password': secrets.token_urlsafe(32),
                      'data_dir': 'C:\\Users\\partyopsuser\\Documents\\PartyOps QA\\' + self.remote + r'\中文 空格业务数据',
                      'boot_id_before': self.boot}
        self.persist()
        if not info.get('wizard_url') or not any(row['name'] == 'PartyOpsWizard.exe' for row in info['processes']):
            self.desktop_start()
        self.finish_configuration()

    def resume_configure(self):
        # 只续接已保存状态且尚未写入mode.json的首次配置，不重建账号/业务目录。
        info = self.desktop('Inspect')
        validate_account(info, self.context, desktop=True)
        clean = self.client.powershell('[Console]::WriteLine([IO.File]::Exists('
                                       + ps(info['config_dir'] + r'\mode.json') + '))').strip()
        require(clean.lower() == 'false', 'PARTIAL_CONFIGURATION_EXISTS_INSPECT_BEFORE_RESUME')
        require(self.state.get('data_dir') and self.state.get('password'), 'ORIGINAL_CONFIGURATION_STATE_REQUIRED')
        if not info.get('wizard_url') or not any(row['name'] == 'PartyOpsWizard.exe' for row in info['processes']):
            # 宿主中断留下的URL不是仍在监听的向导；无产品进程时仅清理此临时标记。
            if not info['processes'] and info.get('wizard_url'):
                self.client.powershell(r'''
$active=@(Get-WmiObject Win32_Process -Filter "Name LIKE 'PartyOps%'" )
if($active.Count -ne 0){throw 'PRODUCT_STARTED_DURING_STALE_MARKER_CHECK'}
Remove-Item -LiteralPath 'C:\Users\partyopsuser\AppData\Local\PartyOps\wizard.url'
''')
                self.checked('stale-interrupted-wizard-marker-removed', {'product_processes': []})
            self.desktop_start()
        self.finish_configuration()

    def resume_business(self):
        self.wait_health()
        self.login()
        self.finish_business()

    def resume_backup(self):
        # 仅恢复中断轮次生成的唯一备份，不重新导出或生成备份。
        self.wait_health()
        self.login()
        backups = self.request('/api/v1/backups')
        previous = self.original_report
        from datetime import datetime, timezone
        start = datetime.fromisoformat(previous['generated_at'])
        end = datetime.fromisoformat(previous['completed_at'])
        def timestamp(value):
            result = datetime.fromisoformat(value.replace('Z', '+00:00'))
            # SQLite DateTime(timezone=True) 的读取可能省略时区；产品 utcnow 存储 UTC。
            return result if result.tzinfo else result.replace(tzinfo=timezone.utc)
        candidates = [item for item in backups if item['status'] == 'completed'
                      and start <= timestamp(item['created_at']) <= end]
        require(len(candidates) == 1, 'UNIQUE_INTERRUPTED_BACKUP_REQUIRED')
        backup = candidates[0]
        self.checked('existing-backup-selected', {'backup_id': backup['id'], 'created_at': backup['created_at']})
        self.finish_backup(backup)

    def backup_extra_task(self):
        # 上次 POST 的回包失败不能据此重发：先读取隔离数据目录内的唯一恢复标记。
        tasks = self.request('/api/v1/tasks')
        require(tasks['total'] <= len(tasks['items']), 'BACKUP_MARKER_SCAN_INCOMPLETE')
        matches = [item for item in tasks['items'] if item['title'].startswith('备份之后新增：恢复时必须消失 ')]
        require(len(matches) <= 1, 'BACKUP_MARKER_AMBIGUOUS')
        return matches[0] if matches else super().backup_extra_task()

    def verify_restored_backup(self):
        # 上次已发送恢复请求，仅检查实际数据库和审计，不重发恢复。
        self.wait_health()
        self.login()
        previous = {row['id']: row['result'] for row in self.original_report['checks']}
        backup = previous['backup-download-integrity']
        extra = previous['post-backup-extra-task']['task_id']
        retained = self.check_business_data()
        try:
            self.request('/api/v1/tasks/' + extra)
        except urllib.error.HTTPError as exc:
            require(exc.code == 404, 'RESTORE_DELETION_UNEXPECTED_RESPONSE')
        else:
            raise RuntimeError('BACKUP_RESTORE_DID_NOT_ROLL_BACK_CHANGE')
        audit = self.request('/api/v1/admin/audit')
        matching = [row for row in audit if row['action'] == 'backup.restore' and row['entity_id'] == backup['backup_id']]
        require(len(matching) == 1, 'EXACT_BACKUP_RESTORE_AUDIT_REQUIRED')
        self.checked('business-backup-restore', {'backup_id': backup['backup_id'],
            'backup_sha256': backup['sha256'], 'post_backup_change_removed': True,
            'preserved': retained, 'audit': matching[0], 'restore_request_repeated': False})
        self.state['business_verified'] = True
        self.persist()

    def finish_collaboration(self):
        """双账号提交及回复已有证据时，仅续收最后的原始业务保留检查。"""
        require(self.state.get('business_verified'), 'BUSINESS_PRECONDITION_MISSING')
        self.checked('collaboration-preserves-original-business', self.check_business_data())


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('phase', choices=('configure', 'resume-configure', 'business', 'resume-business', 'resume-backup', 'verify-restored-backup', 'collaboration-business', 'finish-collaboration', 'ocr', 'models'))
    parser.add_argument('--install-report', type=Path, required=True)
    parser.add_argument('--resume-report', type=Path)
    args = parser.parse_args()
    matrix, media = load_configuration()
    lab = QemuLab(matrix, Path(matrix['defaults']['primary_root']), media)
    binding = product_binding(installation_binding(lab, 'win7-x64', args.install_report, fingerprint()))
    system = probe(lab, 'win7-x64')
    pointer = lab.root / 'state/win7-business-win7-x64.json'
    if args.phase == 'configure':
        require(not pointer.exists(), 'CONFIGURATION_ALREADY_REQUESTED_INSPECT_EXISTING_RUN')
        remote = 'lifecycle-' + uuid.uuid4().hex[:12]
        write_json(pointer, {'context': binding, 'remote': remote, 'at': now()})
    else:
        previous = json.loads(pointer.read_text(encoding='utf-8'))
        require(product_binding(previous['context']) == binding, 'WIN7_BUSINESS_CONTEXT_CHANGED')
        remote = previous['remote']
    reports = lab.root / 'reports/win7-x64' / ('business-' + args.phase + '-' + uuid.uuid4().hex[:12])
    reports.mkdir(exist_ok=False)
    write_json(reports / 'context.json', {'binding': binding, 'identity': system,
                                        'controller_sha256': sha256(Path(__file__)),
                                        'shared_business_sha256': sha256(HERE / 'guest/linux-business-lifecycle.py'),
                                        'desktop_script_sha256': sha256(HERE / 'guest/windows-standard-user.ps1')})
    run = Win7Run(lab, args.phase, binding, remote, reports, system)
    if args.phase == 'finish-collaboration':
        require(args.resume_report is not None, 'ORIGINAL_COLLABORATION_REPORT_REQUIRED')
        original = safe_child(lab.root / 'reports/win7-x64', args.resume_report)
        previous = json.loads((original / 'collaboration-business.json').read_text(encoding='utf-8'))
        original_context = json.loads((original / 'context.json').read_text(encoding='utf-8'))
        require(previous['phase'] == 'collaboration-business' and previous['status'] == 'failed'
                and previous['boot_id'] == run.boot and product_binding(original_context['binding']) == binding
                and [item['id'] for item in previous['checks']] == ['collaboration-admin-submission', 'collaboration-independent-staff-session']
                and all(item['status'] == 'passed' for item in previous['checks']), 'ONLY_COMPLETED_COLLABORATION_RESUME_ALLOWED')
        run.report['resumed_from'] = {'path': str(original), 'sha256': sha256(original / 'collaboration-business.json')}
    if args.phase == 'verify-restored-backup':
        require(args.resume_report is not None, 'ORIGINAL_RESTORE_REPORT_REQUIRED')
        original = safe_child(lab.root / 'reports/win7-x64', args.resume_report)
        previous = json.loads((original / 'resume-backup.json').read_text(encoding='utf-8'))
        original_context = json.loads((original / 'context.json').read_text(encoding='utf-8'))
        require(previous['phase'] == 'resume-backup' and previous['status'] == 'failed'
                and previous['boot_id'] == run.boot and product_binding(original_context['binding']) == binding
                and [item['id'] for item in previous['checks']] == ['existing-backup-selected', 'backup-download-integrity', 'post-backup-extra-task']
                and all(item['status'] == 'passed' for item in previous['checks'])
                and not run.state.get('business_verified'), 'ONLY_POST_RESTORE_VERIFICATION_ALLOWED')
        run.original_report = previous
        run.report['resumed_from'] = {'path': str(original), 'sha256': sha256(original / 'resume-backup.json')}
    if args.phase == 'resume-backup':
        require(args.resume_report is not None, 'ORIGINAL_BACKUP_REPORT_REQUIRED')
        original = safe_child(lab.root / 'reports/win7-x64', args.resume_report)
        previous = json.loads((original / 'resume-business.json').read_text(encoding='utf-8'))
        original_context = json.loads((original / 'context.json').read_text(encoding='utf-8'))
        require(previous['status'] == 'failed' and previous['phase'] == 'resume-business'
                and previous['boot_id'] == run.boot
                and product_binding(original_context['binding']) == binding
                and [item['id'] for item in previous['checks']] == ['task-attachment-account', 'export-xlsx', 'export-docx']
                and all(item['status'] == 'passed' for item in previous['checks'])
                and not run.state.get('business_verified'), 'ONLY_POST_EXPORT_BACKUP_RESUME_ALLOWED')
        for extension in ('xlsx', 'docx'):
            item = next(row for row in previous['checks'] if row['id'] == 'export-' + extension)
            require(sha256(original / item['result']['filename']) == item['result']['sha256'], 'PRIOR_EXPORT_CHANGED')
        run.original_report = previous
        run.report['resumed_from'] = {'path': str(original), 'sha256': sha256(original / 'resume-business.json')}
    if args.phase == 'resume-business':
        require(args.resume_report is not None, 'ORIGINAL_BUSINESS_REPORT_REQUIRED')
        original = safe_child(lab.root / 'reports/win7-x64', args.resume_report)
        previous = json.loads((original / 'business.json').read_text(encoding='utf-8'))
        original_context = json.loads((original / 'context.json').read_text(encoding='utf-8'))
        require(previous.get('phase') == 'business' and previous.get('status') == 'failed'
                and previous.get('completed_at') and previous.get('checks') == []
                and product_binding(original_context['binding']) == binding
                and run.state.get('task_id') and run.state.get('attachment_id')
                and not run.state.get('business_verified'), 'ONLY_PRE_EXPORT_BUSINESS_RESUME_ALLOWED')
        run.report['resumed_from'] = {'path': str(original), 'sha256': sha256(original / 'business.json')}
    if args.phase in {'configure', 'resume-configure'}:
        script = HERE / 'guest/windows-standard-user.ps1'
        run.client.put(script, 'C:\\PartyOps-QA\\' + remote + r'\windows-standard-user.ps1', sha256(script))
    try:
        getattr(run, args.phase.replace('-', '_'))()
        require(product_binding(installation_binding(lab, 'win7-x64', args.install_report, fingerprint())) == binding,
                'WIN7_BUSINESS_BINDING_CHANGED')
        run.report.update(status='passed', completed_at=now())
    except Exception as exc:
        # 原始网络命令可能含临时凭据，错误仅留类别，业务HTTP响应另由既有断言记录。
        run.report.update(error_type=type(exc).__name__, completed_at=now())
        raise
    finally:
        write_json(reports / (args.phase + '.json'), run.report)
        run.client.close()
    print(json.dumps({'status': 'partial', 'phase': args.phase, 'report_path': str(reports),
                      'runtime_environment_passed': False}))


if __name__ == '__main__':
    main()
