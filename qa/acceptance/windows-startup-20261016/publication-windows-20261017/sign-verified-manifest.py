"""复用既有发布密钥，只签署证据齐全的 Windows 清单；不复制或输出私钥。"""
import argparse
import base64
import json
from pathlib import Path

from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PublicKey

import publication as release


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--key', type=Path, required=True)
    parser.add_argument('--evidence', type=Path, required=True)
    args = parser.parse_args()
    source = release.HERE / 'manifest-signing-input.json'
    target = release.HERE / release.MANIFEST_NAME
    release.p.require(not target.exists(), 'SIGNED_MANIFEST_EXISTS_INSPECT_ONLY')
    approved = release.acceptance_evidence(args.evidence)
    baseline, _ = release.inputs()
    payload = release.p.read(source)
    release.validate_manifest(payload, baseline, approved)
    release.p.require(payload['formal_release_ready'] is True
                      and payload['formal_gate'] == 'FULL_FUNCTION_GATE_PASSED', 'ACCEPTANCE_REQUIRED')
    trusted = release.p.trusted_key()
    verifier = Ed25519PublicKey.from_public_bytes(trusted)
    release.p.verify_signature(baseline, verifier)
    release.p.require(payload['public_key'] == baseline['public_key'], 'TRUSTED_PUBLIC_KEY_CHANGED')
    canonical = lambda value: json.dumps(value, ensure_ascii=False, sort_keys=True,
                                         separators=(',', ':')).encode('utf-8')
    for asset in payload['assets']:
        if asset['filename'] != release.receiver.NAME:
            release.p.verify_signature(asset, verifier)
    # 只读取显式路径一次；I/O 失败立即退出，不扫描坏盘、不创建替代密钥。
    key = serialization.load_pem_private_key(args.key.read_bytes(), password=None)
    release.p.require(key.public_key().public_bytes(serialization.Encoding.Raw,
                      serialization.PublicFormat.Raw) == trusted, 'ORIGINAL_SIGNING_KEY_REQUIRED')
    for asset in payload['assets']:
        unsigned = {k: v for k, v in asset.items() if k != 'signature'}
        if asset['filename'] == release.receiver.NAME:
            asset['signature'] = base64.b64encode(key.sign(canonical(unsigned))).decode('ascii')
        else:
            verifier.verify(base64.b64decode(asset['signature']), canonical(unsigned))
    payload.pop('signature', None)
    payload['signature'] = base64.b64encode(key.sign(canonical(payload))).decode('ascii')
    release.validate_manifest(payload, baseline, approved)
    release.p.verify_signature(payload, verifier)
    with target.open('x', encoding='utf-8', newline='\n') as stream:
        stream.write(json.dumps(payload, ensure_ascii=False, indent=2) + '\n')
    release.signed_manifest(target, approved)
    print(json.dumps({'manifest': str(target), 'sha256': release.p.digest(target),
                      'private_key_output': False, 'private_key_copied': False}))


if __name__ == '__main__':
    main()
