"""Win7 Guest HTTP适配保留错误、独立Cookie以及目标边界。"""
import base64
import http.cookiejar
import importlib.util
import json
import re
import urllib.error
import urllib.request
from pathlib import Path

import pytest


@pytest.fixture
def driver():
    path = Path(__file__).resolve().parents[1] / 'scripts/exercise-win7-business.py'
    spec = importlib.util.spec_from_file_location('win7_business_test', path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class Client:
    def __init__(self, status=200):
        self.status = status
        self.requests = []

    def powershell(self, script, timeout):
        value = re.search(r"FromBase64String\('([^']+)'\)", script)[1]
        self.requests.append(json.loads(base64.b64decode(value)))
        return json.dumps({'status': self.status, 'reason': 'test',
                           'headers': ['Content-Type: application/json', 'Set-Cookie: qa=session; Path=/'],
                           'body': base64.b64encode(b'{"verified":true}').decode()})


def test_cookie_and_unicode_request_stay_in_guest(driver):
    client = Client()
    cookies = http.cookiejar.CookieJar()
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), driver.GuestHTTP(client),
                                        urllib.request.HTTPCookieProcessor(cookies))
    with opener.open('http://127.0.0.1:18775/start') as response:
        assert json.load(response) == {'verified': True}
    payload = '中文 空格'.encode()
    with opener.open(urllib.request.Request('http://127.0.0.1:18775/configure', data=payload)):
        pass
    assert base64.b64decode(client.requests[1]['body']) == payload
    assert client.requests[1]['headers']['Cookie'] == 'qa=session'
    assert client.requests[1]['method'] == 'POST'


def test_http_failure_remains_http_failure(driver):
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), driver.GuestHTTP(Client(404)))
    with pytest.raises(urllib.error.HTTPError) as captured:
        opener.open('http://127.0.0.1:18775/missing')
    assert captured.value.code == 404
    assert json.loads(captured.value.read()) == {'verified': True}


@pytest.mark.parametrize('method,path,expected', [
    ('GET', '/api/v1/health', urllib.error.URLError),
    ('POST', '/api/v1/health', RuntimeError),
    ('POST', '/api/v1/admin/backups/restore', RuntimeError),
])
def test_only_health_connection_failure_is_pollable(driver, method, path, expected):
    class Unconnected:
        calls = 0
        def powershell(self, script, timeout):
            self.calls += 1
            raise RuntimeError('WIN7_NATIVE_COMMAND_FAILED:1; WIN7_PS_FAILURE type=RuntimeException id=GUEST_HTTP_CONNECTION_FAILED line=39')
    client = Unconnected()
    with pytest.raises(expected):
        driver.GuestHTTP(client).http_open(urllib.request.Request('http://127.0.0.1:18775' + path, method=method))
    assert client.calls == 1


def test_business_resume_keeps_product_identity_when_only_qa_changes(driver):
    before = {'package': {'sha256': 'fixed-package'}, 'source_fingerprint': 'fixed-source',
              'environment': {'vm_uuid': 'original'}, 'restore_generation': 'same-snapshot',
              'scripts': {'desktop': 'old'}, 'controller_sha256': 'old-qa'}
    after = {**before, 'scripts': {'desktop': 'ps2-fixed'}, 'controller_sha256': 'new-qa'}
    assert driver.product_binding(before) == driver.product_binding(after)
    for key, value in [('package', {'sha256': 'changed'}), ('source_fingerprint', 'changed'),
                       ('environment', {'vm_uuid': 'another'}), ('restore_generation', 'restored')]:
        assert driver.product_binding({**after, key: value}) != driver.product_binding(before)


@pytest.mark.parametrize('url', ['http://example.org/', 'http://user:password@127.0.0.1/',
                                 'http://127.0.0.1/#fragment'])
def test_non_guest_requests_never_reach_transport(driver, url):
    client = Client()
    with pytest.raises(RuntimeError, match='ONLY_GUEST_LOOPBACK'):
        driver.GuestHTTP(client).http_open(urllib.request.Request(url))
    assert client.requests == []


def test_uncertain_backup_marker_is_reused_without_post(driver):
    run = object.__new__(driver.Win7Run)
    marker = {'id': 'existing', 'title': '备份之后新增：恢复时必须消失 test'}
    run.request = lambda path: {'total': 1, 'items': [marker]}
    run.task = lambda title: pytest.fail('回包失败不能重发创建请求')
    assert run.backup_extra_task() == marker


def test_native_command_preserves_unicode_and_does_not_use_cmd_or_pipe_eof(driver, monkeypatch):
    from types import SimpleNamespace
    active = set()
    class Protocol:
        def open_shell(self, **kwargs):
            assert not active, '上一条命令必须释放shell，避免Win7操作额度累积'
            active.add('shell')
            return 'shell'
        def run_command(self, shell, path, args, **kwargs):
            assert kwargs == {'console_mode_stdin': True, 'skip_cmd_shell': True}
            assert '中文' in base64.b64decode(args[-1]).decode('utf-16le')
            return 'command'
        def get_command_output(self, shell, command): return '中文'.encode(), b'', 0
        def cleanup_command(self, shell, command): pass
        def close_shell(self, shell): active.remove(shell)
    monkeypatch.setattr(driver, 'session', lambda *a: SimpleNamespace(protocol=Protocol(), partyops_powershell='verified-powershell'))
    client = driver.Win7Commands(None, 'win7-x64')
    for _ in range(20):
        assert client.powershell('中文') == '中文'
        assert not active


def test_staged_upload_uses_only_registered_guest_input(driver):
    client = Client()
    handler = driver.GuestHTTP(client)
    body = driver.GuestUpload(r'C:\PartyOps-QA\owned\image.png', 50000, b'header', b'trailer')
    request = urllib.request.Request('http://127.0.0.1:18775/upload', data=body,
                                     headers={'Content-Length': '50013'})
    handler.http_open(request)
    payload = client.requests[0]
    assert payload['body'] == ''
    assert payload['upload']['size'] == 50000
    assert base64.b64decode(payload['upload']['header']) == b'header'
    body.path = r'C:\Users\partyopsuser\private.txt'
    with pytest.raises(RuntimeError, match='TRANSFER_OUTSIDE'):
        handler.http_open(request)
