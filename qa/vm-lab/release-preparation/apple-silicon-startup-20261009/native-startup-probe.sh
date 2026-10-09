#!/bin/bash
# 仅审批后的 ARM 原生诊断；不安装、不发布，不执行业务入口或 WPS。
# 已核对 RC4 tag b5c910677f8d28a324b18d22d7ac1b8100c1c776 的 launcher.py
# blob 3a56f6020a36e7556f2ca9e3c1060835d367f220：--self-test 只检查入口并打印。
set -euo pipefail
[[ "$(uname -s)" == Darwin && "$(uname -m)" == arm64 ]] || { echo '仅允许 Darwin arm64'; exit 2; }
[[ "${PARTYOPS_PROBE_CONFIRM:-}" == 'RC4_ARM_ISOLATED_PROBE' ]] || { echo '缺少实验确认词'; exit 2; }
[[ -n "${RUNNER_TEMP:-}" && -d "$RUNNER_TEMP" ]] || { echo '缺少 RUNNER_TEMP'; exit 2; }
[[ -n "${PROBE_SOURCE_ROOT:-}" && -d "$PROBE_SOURCE_ROOT/.git" ]] || { echo '缺少固定源码 checkout'; exit 2; }
python3 - <<'PY'
"""所有文件仅留在本次 RUNNER_TEMP；日志只保留脱敏、有界摘要。"""
import hashlib
import json
import os
import platform
import plistlib
import signal
import subprocess
import tempfile
from datetime import datetime, timezone
from pathlib import Path

COMMIT = 'b9b469fa5f146198c92db32ccce30996f672f12e'
SHA = '5434f1811e73b826e29958a4d5b2add23de83e41f8bcf0eb7b80fa5a58a55c7f'
ENTITLEMENTS_SHA = 'da68b708e3fc30709e82683a8d1cb794ae6666778ed7257decb98ece88ee3ead'
URL = 'https://github.com/linsky-dev/PartyOps/releases/download/v1.4.5-rc.4/PartyOps_1.4.5-rc.4_macos_arm64.pkg'
ENTRIES = ('partyops-desktop-bin', 'partyops', 'partyops-client', 'partyops-wizard', 'partyops-launch-agent', 'partyops-updater')
source = Path(os.environ['PROBE_SOURCE_ROOT']).resolve()
temp_root = Path(os.environ['RUNNER_TEMP']).resolve()
work = Path(tempfile.mkdtemp(prefix='partyops-rc4-arm-', dir=temp_root)).resolve()
assert work.is_relative_to(temp_root)
reports = temp_root / 'partyops-rc4-arm-reports'
reports.mkdir(exist_ok=False)
qa_home = work / 'qa-home'
qa_home.mkdir()
receipt = {'utc': datetime.now(timezone.utc).isoformat(), 'experiment': '旧 RC4 ARM 签名最小对照',
           'package_sha256_expected': SHA, 'source_commit_expected': COMMIT,
           'platform': {'system': platform.system(), 'machine': platform.machine()},
           'installed': False, 'published': False, 'latest_version_validation': False,
           'child_home_isolated': True,
           'commands': [], 'baseline': None, 'after': None, 'status': 'preparing',
           'source_self_test_review': {'tag_commit': 'b5c910677f8d28a324b18d22d7ac1b8100c1c776',
                                     'launcher_blob': '3a56f6020a36e7556f2ca9e3c1060835d367f220',
                                     'finding': '--self-test 提前返回，不创建Config/日志，不启动业务。'},
           'limitations': ['只比较旧公开 RC4 副本；不是最新版本构建或发布验收。',
                           '仅六个冻结入口改权限并重签外App；失败后停止，不扩大签名范围。',
                           '未验证冻结包与已审查tag源码的逐字节可复现关系。']}
env = {k: v for k, v in os.environ.items() if k in ('PATH', 'LANG', 'LC_ALL', 'SYSTEMROOT')}
# 只调整子进程环境；不改宿主 HOME，任何意外业务写入仍落到隔离副本。
env.update({'HOME': str(qa_home), 'TMPDIR': str(work), 'PYTHONDONTWRITEBYTECODE': '1'})


def clean(value):
    for original, replacement in ((str(work), '<PROBE>'), (str(temp_root), '<RUNNER_TEMP>'),
                                  (str(source), '<FIXED_SOURCE>'), (str(Path.home()), '<HOME>')):
        value = value.replace(original, replacement)
    return value[:65536]


def run(args, label, timeout=120, required=True):
    # 重定向到隔离临时文件，防止未知启动失败日志无限占用内存。
    raw = work / (label + '.raw')
    with raw.open('wb') as output:
        process = subprocess.Popen([str(a) for a in args], cwd=work, env=env,
                                   stdin=subprocess.DEVNULL, stdout=output, stderr=subprocess.STDOUT,
                                   start_new_session=True)
        timed_out = False
        try:
            code = process.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            timed_out = True
            os.killpg(process.pid, signal.SIGKILL)
            process.wait(timeout=10)
            code = 124
    with raw.open('rb') as f:
        text = clean(f.read(65536).decode('utf-8', 'replace'))
    item = {'label': label, 'exit_code': code, 'timed_out': timed_out,
            'output': text, 'output_truncated': raw.stat().st_size > 65536}
    receipt['commands'].append(item)
    if label in ('baseline', 'after'):
        receipt[label] = item
        (reports / (label + '.log')).write_text(text, encoding='utf-8')
    if required and code:
        raise RuntimeError(label + ' failed, exit=' + str(code))
    return item


try:
    assert platform.system() == 'Darwin' and platform.machine() == 'arm64'
    head = run(['/usr/bin/git', '-C', source, 'rev-parse', 'HEAD'], 'source-head')['output'].strip()
    receipt['source_commit_actual'] = head
    assert head == COMMIT, '固定 checkout 不符'
    pkg = work / 'rc4-arm.pkg'
    # 公开匿名下载，无凭据；不使用自动重试，状态码按约定退避一次。
    import time
    for attempt in range(2):
        download = run(['/usr/bin/curl', '--location', '--silent', '--show-error', '--fail',
                        '--connect-timeout', '30', '--max-time', '240', '--output', pkg,
                        '--write-out', '%{http_code}', URL], 'download-' + str(attempt), 250, False)
        if download['exit_code'] == 0:
            break
        if attempt == 0 and ('429' in download['output'] or '5' == download['output'][-3:-2]
                             or download['exit_code'] == 28):
            time.sleep(20 if '429' in download['output'] else 2)
        else:
            raise RuntimeError('公开包下载失败')
    if download['exit_code']:
        raise RuntimeError('公开包下载重试失败')
    digest = hashlib.sha256()
    with pkg.open('rb') as f:
        for block in iter(lambda: f.read(1048576), b''):
            digest.update(block)
    receipt['package_sha256_actual'] = digest.hexdigest()
    assert digest.hexdigest() == SHA, '旧包 SHA 不符'
    expanded = work / 'expanded'
    run(['/usr/sbin/pkgutil', '--expand-full', pkg, expanded], 'expand-full', 180)
    archives = list(expanded.rglob('PartyOps.app.zip'))
    assert len(archives) == 1 and archives[0].is_file() and not archives[0].is_symlink()
    # ditto 只展开固定SHA包中的 ZIP 到临时目录，不执行安装脚本。
    isolated = work / 'isolated'
    isolated.mkdir()
    run(['/usr/bin/ditto', '-x', '-k', archives[0], isolated], 'extract-app', 180)
    app = isolated / 'PartyOps.app'
    assert app.is_dir() and not app.is_symlink() and app.resolve().is_relative_to(work)
    for name in ENTRIES:
        entry = app / 'Contents' / 'MacOS' / name
        assert entry.is_file() and not entry.is_symlink() and entry.resolve().is_relative_to(app)
        architecture = run(['/usr/bin/lipo', '-archs', entry], 'arch-' + name)['output'].strip()
        assert architecture == 'arm64', '入口非纯 arm64'
    desktop = app / 'Contents' / 'MacOS' / 'partyops-desktop-bin'
    run([desktop, '--self-test'], 'baseline', 60, False)
    candidate = source / 'packaging' / 'macos' / 'pyinstaller-candidate-entitlements.plist'
    assert candidate.is_file() and not candidate.is_symlink(), '固定commit缺少候选权限；停止，不自造权限'
    candidate_data = candidate.read_bytes()
    assert hashlib.sha256(candidate_data).hexdigest() == ENTITLEMENTS_SHA, '候选权限文件SHA不符'
    assert plistlib.loads(candidate_data) == {'com.apple.security.cs.disable-library-validation': True}
    receipt['candidate_entitlements_sha256'] = hashlib.sha256(candidate_data).hexdigest()
    # 外App原始 XML 权限在任何重签前读取，只接受有效 plist。
    original = run(['/usr/bin/codesign', '-d', '--entitlements', ':-', app], 'root-entitlements')
    raw_root = (work / 'root-entitlements.raw').read_bytes()
    start, end = raw_root.find(b'<?xml'), raw_root.find(b'</plist>')
    assert start >= 0 and end > start, '无法保留原App权限；停止'
    root_bytes = raw_root[start:end + len(b'</plist>')]
    receipt['original_root_entitlements'] = plistlib.loads(root_bytes)
    root_entitlements = work / 'original-root-entitlements.plist'
    root_entitlements.write_bytes(root_bytes)
    for name in ENTRIES:
        run(['/usr/bin/codesign', '--force', '--sign', '-', '--options', 'runtime',
             '--timestamp=none', '--entitlements', candidate,
             app / 'Contents' / 'MacOS' / name], 'resign-' + name)
    run(['/usr/bin/codesign', '--force', '--sign', '-', '--options', 'runtime', '--timestamp=none',
         '--entitlements', root_entitlements, app], 'resign-root')
    verify = run(['/usr/bin/codesign', '--verify', '--deep', '--strict', '--verbose=2', app], 'verify-after', 120, False)
    # --deep 只用于验证；绝不递归强签 Python 或其他动态库。
    if verify['exit_code']:
        receipt['status'] = 'minimal-resign-verification-failed'
    else:
        run([desktop, '--self-test'], 'after', 60, False)
        receipt['status'] = 'comparison-complete'
        receipt['signature_hypothesis_supported'] = (receipt['baseline']['exit_code'] != 0 and receipt['after']['exit_code'] == 0)
except Exception as exc:
    receipt['status'] = 'stopped'
    receipt['error'] = clean(type(exc).__name__ + ': ' + str(exc))
finally:
    (reports / 'native-receipt.json').write_text(json.dumps(receipt, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    for label in ('baseline', 'after'):
        path = reports / (label + '.log')
        if not path.exists():
            path.write_text('未执行；原因见 native-receipt.json\n', encoding='utf-8')
    print(json.dumps({'status': receipt['status'], 'reports': '<RUNNER_TEMP>/partyops-rc4-arm-reports'}, ensure_ascii=False))
if receipt['status'] != 'comparison-complete' or receipt['after']['exit_code'] != 0:
    raise SystemExit(1)
PY
