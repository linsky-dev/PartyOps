"""提取实际packaging刷新代码，模拟签名字节/工具结果验证ARM来源与随包绑定。"""
import copy
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
from types import SimpleNamespace

import pytest

ROOT = Path(__file__).resolve().parents[4]
spec = importlib.util.spec_from_file_location('arm_signing_validator', ROOT / 'scripts/validate-source-formatter-runtime.py')
validator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(validator)
NAMES = ('libmono-native-compat.dylib', 'libMonoPosixHelper.dylib')


def fixture(tmp_path):
    libraries, originals = [], {}
    for name in NAMES:
        path = tmp_path / name
        path.write_bytes(('原SDK:' + name).encode())
        digest = validator.sha256(path)
        originals[name] = ('original-' + name, digest)
        libraries.append({'registered_name': name, 'source_name': 'original-' + name,
                          'source_sha256': digest, 'packaged_sha256': digest, 'architectures': ['arm64']})
    host = tmp_path / 'partyops-document-formatter-host'
    host.write_bytes(b'original-host')
    record = {'adapter': validator.MAC_OBJECT_ADAPTER, 'native_bundle_mode': 'custom-static',
              'runtime_input_profile': 'winehq-mono-6.14.1-homebrew-arm64-sequoia',
              'host_sha256': validator.sha256(host), 'native_sidecars_sha256': {name: digest for name, (_, digest) in originals.items()}}
    native = {'libraries': libraries}
    return record, native, SimpleNamespace(LIBRARIES=originals)


def codesign(args, **kwargs):
    assert Path(args[0]).name == 'codesign'
    identifier = Path(args[-1]).name
    details = 'CDHash=' + 'a' * 40 + '\nIdentifier=' + identifier + '\n'
    return subprocess.CompletedProcess(args, 0, stdout='', stderr=details)


def refresh(tmp_path, record, native, monkeypatch):
    (tmp_path / 'source-host.json').write_text(json.dumps(record), encoding='utf-8')
    (tmp_path / 'mono-native-source.json').write_text(json.dumps(native), encoding='utf-8')
    source = (ROOT / 'packaging/macos/build-pkg.sh').read_text(encoding='utf-8')
    code = source.split('refresh_formatter_manifest_hash() {', 1)[1].split("<<'PY'\n", 1)[1].split('\nPY', 1)[0]
    monkeypatch.setattr(sys, 'argv', ['refresh', str(tmp_path / 'partyops-document-formatter-host'), str(tmp_path / 'source-host.json')])
    exec(compile(code, 'ACTUAL-PACKAGING-REFRESH', 'exec'), {'__name__': 'qa_refresh'})
    return json.loads((tmp_path / 'source-host.json').read_text()), json.loads((tmp_path / 'mono-native-source.json').read_text())


def signed_fixture(tmp_path, monkeypatch):
    record, native, profile = fixture(tmp_path)
    for name in NAMES:
        with (tmp_path / name).open('ab') as stream:
            stream.write(b'FAKE-CODESIGN-BYTES')
    monkeypatch.setattr(subprocess, 'run', codesign)
    record, native = refresh(tmp_path, record, native, monkeypatch)
    return record, native, profile


def test_raw_official_inputs_need_no_signing_override(tmp_path):
    record, native, profile = fixture(tmp_path)
    validator.validate_arm_sidecars(tmp_path, record, native, profile)


def test_actual_refresh_then_arm_validator(tmp_path, monkeypatch):
    record, native, profile = signed_fixture(tmp_path, monkeypatch)
    validator.validate_arm_sidecars(tmp_path, record, native, profile)
    assert record['pre_sign_sidecars_sha256'] == {name: digest for name, (_, digest) in profile.LIBRARIES.items()}
    for item in native['libraries']:
        assert item['source_sha256'] == profile.LIBRARIES[item['registered_name']][1]
        assert item['packaged_sha256'] != item['source_sha256']
        assert item['codesign']['pre_sign_sha256'] == item['source_sha256']


@pytest.mark.parametrize('broken', ['source', 'presign', 'packaged', 'current', 'missing-signing', 'signing-presign', 'signing-packaged', 'cdhash', 'identifier'])
def test_reject_broken_signed_binding(tmp_path, monkeypatch, broken):
    record, native, profile = signed_fixture(tmp_path, monkeypatch)
    item = native['libraries'][0]
    name = item['registered_name']
    if broken == 'source': item['source_sha256'] = '0' * 64
    elif broken == 'presign': record['pre_sign_sidecars_sha256'][name] = '0' * 64
    elif broken == 'packaged': item['packaged_sha256'] = '0' * 64
    elif broken == 'current': record['native_sidecars_sha256'][name] = '0' * 64
    elif broken == 'missing-signing': del item['codesign']
    elif broken == 'signing-presign': item['codesign']['pre_sign_sha256'] = '0' * 64
    elif broken == 'signing-packaged': item['codesign']['packaged_sha256'] = '0' * 64
    elif broken == 'cdhash': item['codesign']['cdhash'] = 'b' * 40
    else: item['codesign']['identifier'] = 'foreign'
    with pytest.raises(RuntimeError):
        validator.validate_arm_sidecars(tmp_path, record, native, profile)


def test_reject_actual_signature_failure(tmp_path, monkeypatch):
    record, native, profile = signed_fixture(tmp_path, monkeypatch)
    def invalid(args, **kwargs):
        return subprocess.CompletedProcess(args, 1, stdout='', stderr='invalid signature')
    monkeypatch.setattr(subprocess, 'run', invalid)
    with pytest.raises(RuntimeError, match='SIGNATURE_VERIFY_FAILED'):
        validator.validate_arm_sidecars(tmp_path, record, native, profile)


def test_intel_refresh_preserves_existing_metadata_contract(tmp_path, monkeypatch):
    record, native, _ = fixture(tmp_path)
    record.pop('runtime_input_profile')
    def must_not_run(*args, **kwargs):
        raise AssertionError('Intel刷新不得新增签名证据调用')
    monkeypatch.setattr(subprocess, 'run', must_not_run)
    _, refreshed = refresh(tmp_path, record, native, monkeypatch)
    assert all('codesign' not in item for item in refreshed['libraries'])
