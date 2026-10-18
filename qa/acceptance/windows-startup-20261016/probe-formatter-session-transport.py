"""对原QA实例单次创建空排版会话，记录响应长度，成功后立即删除；不读取业务文档。"""
import http.client
import http.cookiejar
import importlib.util
import json
import sys
import urllib.request
from pathlib import Path
from types import SimpleNamespace

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / 'qa/vm-lab'))
spec = importlib.util.spec_from_file_location('transport_probe_windows', ROOT / 'qa/vm-lab/scripts/exercise-windows-business.py')
windows = importlib.util.module_from_spec(spec)
spec.loader.exec_module(windows)
client = windows.windows.connect(SimpleNamespace(root=Path('D:/PartyOps-VM-Lab')), {'ssh_port': 22110})
rows = []
base = prefix = token = None
origin = 'http://127.0.0.1:18775'
cookies = http.cookiejar.CookieJar()
opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), windows.GuestHTTPHandler(client.get_transport()), urllib.request.HTTPCookieProcessor(cookies))


def request(path, method='GET', body=None, base=origin, headers=None):
    headers = {'Origin': origin, **(headers or {})}
    for cookie in cookies:
        if cookie.name == 'partyops_csrf':
            headers['X-PartyOps-CSRF'] = cookie.value
    if body is not None:
        body = json.dumps(body).encode()
        headers['Content-Type'] = 'application/json'
    row = {'step': len(rows), 'method': method}
    rows.append(row)
    try:
        with opener.open(urllib.request.Request(base + path, data=body, method=method, headers=headers), timeout=30) as response:
            row.update(status=response.status, content_length=response.headers.get('Content-Length'), protocol=response.version)
            payload = response.read()
            row['actual_bytes'] = len(payload)
            return json.loads(payload) if payload else None
    except http.client.IncompleteRead as error:
        row.update(error='IncompleteRead', partial_bytes=len(error.partial), missing_bytes=error.expected)
        raise RuntimeError('INCOMPLETE_RESPONSE_NO_RETRY') from None


try:
    with client.open_sftp() as fs, fs.file('C:/ProgramData/PartyOps-VM-Lab/private/lifecycle-82dbfdf1894f.json') as stream:
        state = json.load(stream)
    request('/api/v1/auth/login', 'POST', {'username': state['username'], 'password': state['password']})
    ticket = request('/api/v1/official-format/local-ticket', 'POST', {'origin': origin})
    base = ticket['local_base_url']
    assert base == 'http://127.0.0.1:18777'
    session = request('/v1/sessions', 'POST', {}, base, {'Authorization': 'Bearer ' + ticket['ticket']})
    prefix, token = '/v1/sessions/' + session['session_id'], session['session_token']
    request('/health', base=base)
except Exception as error:
    rows.append({'terminal_error': type(error).__name__})
finally:
    if prefix and token:
        try:
            request(prefix, 'DELETE', base=base, headers={'X-PartyOps-Local-Token': token})
        except Exception as error:
            rows.append({'cleanup_error': type(error).__name__})
    client.close()
    target = Path(__file__).with_name('session-transport-probe-20261018.json')
    with target.open('x', encoding='utf-8') as stream:
        json.dump(rows, stream, ensure_ascii=False, indent=2)
    print(json.dumps(rows, ensure_ascii=False))
