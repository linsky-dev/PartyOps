"""复用已成功上传协议；默认本地核对，未知写入只读恢复，不重发。"""
import argparse
import json
import re
from pathlib import Path
from types import SimpleNamespace
import release_driver as release

BASE_URL = 'https://partyops-rc6-win7-x86.app.workbuddy.host'

def checked_batch(_unused=None):
    checked = release.checked_preparation()
    folder = release.HERE / 'batch'
    release.signed_manifest(folder / release.MANIFEST, checked)
    binding = release.read(folder / 'binding.json')
    expected = {'filename': release.NAME, 'bytes': checked['package']['bytes'],
        'package_sha256': checked['candidate']['package']['sha256'], 'manifest_sha256': release.digest(folder / release.MANIFEST),
        'baseline_manifest_sha256': release.BASELINE_SHA, 'receiver': release.PROFILE, 'publication_authorized': True,
        'formal_release_ready': True, 'evidence_sha256': checked['evidence_sha256']}
    release.require(binding == expected and release.digest(folder / release.NAME) == binding['package_sha256'], 'NEW_BATCH_BINDING_CHANGED')
    receiver = release.load('domestic_new_receiver', release.HERE / 'windows_receiver_factory.py')
    retained = release.read(release.PRIOR / 'batch/post-switch-receiver/upload-metadata.json')['assets']
    release.require((folder / 'receiver/_serve.js').read_text(encoding='utf-8') == receiver.build_receiver(retained, binding['manifest_sha256'], binding), 'NEW_RECEIVER_SOURCE_CHANGED')
    metadata = release.read(folder / 'receiver/upload-metadata.json')
    release.require(metadata == {'receiver': release.PROFILE, 'version': '1.4.5-rc.6', 'manifest_sha256': binding['manifest_sha256'],
        'baseline_manifest_sha256': release.BASELINE_SHA, 'assets': retained + [receiver.candidate(binding)]}, 'NEW_RECEIVER_METADATA_CHANGED')
    return folder, binding

def configured():
    folder, binding = checked_batch()
    old = release.history()
    old.HERE = release.HERE
    old.BASE_URL = BASE_URL
    old.receiver = SimpleNamespace(NAME=release.NAME, SIZE=binding['bytes'], SHA=binding['package_sha256'], PROFILE=release.PROFILE)
    old.checked_batch = checked_batch
    old.inputs = lambda: (release.baseline(), release.read(release.PRIOR / 'batch/post-switch-receiver/upload-metadata.json')['assets'])
    return old, folder, binding

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['verify', 'upload', 'recover', 'finalize'], nargs='?', default='verify')
    parser.add_argument('--execute-network', action='store_true')
    parser.add_argument('--execute-read-only', action='store_true')
    args = parser.parse_args()
    release.require(args.execute_network == (args.action == 'upload'), 'EXPLICIT_UPLOAD_FLAG_REQUIRED')
    release.require(args.execute_read_only == (args.action in {'recover', 'finalize'}), 'EXPLICIT_READ_ONLY_FLAG_REQUIRED')
    old, folder, binding = configured()
    if args.action == 'verify':
        result = {'status': 'verified-local', 'binding': binding, 'token_file_present': (folder / 'receiver/.upload-token').is_file(), 'network_write_performed': False}
    elif args.action == 'upload':
        tokens = release.read(folder / 'receiver/.upload-token')
        release.require(isinstance(tokens, dict) and set(tokens) == {release.NAME}
            and isinstance(tokens[release.NAME], str) and re.fullmatch('[a-f0-9]{64}', tokens[release.NAME]), 'CURRENT_CANDIDATE_TOKEN_MAP_REQUIRED')
        del tokens
        # 旧入口独占intent；upload_one自动完成分块、单次finalize及匿名全量回读。
        result = old.upload(None, execute=True)
    else:
        if args.action == 'finalize':
            release.require(not (folder / 'windows-domestic-readback.json').exists(), 'DOMESTIC_RECEIPT_EXISTS_VERIFY_ONLY')
            release.require(release.read(folder / 'windows-upload.intent.json').get('binding') == binding, 'BOUND_UPLOAD_INTENT_REQUIRED')
            deployment = release.read(folder / 'deployment-receipt.json')
            release.require(deployment.get('status') == 'received' and deployment.get('binding') == binding and deployment.get('base_url') == BASE_URL
                and deployment.get('current_backend_preflight_verified') is True, 'BOUND_DEPLOYMENT_REQUIRED')
        result = old.read_only_recover(execute=True)
        if args.action == 'finalize':
            observed = release.read(Path(result['observation']))
            release.require(observed.get('status') == 'read-only-full-get-matched', 'FINALIZE_UNKNOWN_READ_ONLY_ONLY')
            release.exclusive(folder / 'windows-domestic-readback.json', {'binding': binding, 'result': observed['readback'],
                'completed_at': old.now(), 'other_nine_preserved': True, 'old_windows_preserved': True,
                'formal_release_ready': True, 'publication_completed': False, 'read_only_recovery_sha256': release.digest(Path(result['observation']))})
            result['status'] = 'domestic-readback-recorded-read-only'
    print(json.dumps(result, ensure_ascii=False))

if __name__ == '__main__': main()
