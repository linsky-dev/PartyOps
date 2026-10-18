"""仅签本轮已核输入；显式原私钥路径，不搜索、不复制、不输出密钥。"""
import argparse
import base64
import copy
import json
from pathlib import Path
from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey, Ed25519PublicKey
import release_driver as release

def canonical(value):
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(',', ':')).encode('utf-8')

def sign(key_path):
    target = release.HERE / release.MANIFEST
    release.require(not target.exists(), 'SIGNED_MANIFEST_EXISTS_INSPECT_ONLY')
    checked = release.checked_preparation()
    original = release.baseline()
    source = release.HERE / 'manifest-signing-input.json'
    payload = release.read(source)
    release.require(payload == release.compose(original, checked, payload['generated_at']), 'EXACT_VERIFIED_SIGNING_INPUT_REQUIRED')
    release.require(payload.get('formal_release_ready') is True and payload.get('formal_gate') == 'FULL_FUNCTION_GATE_PASSED', 'ACCEPTANCE_REQUIRED')
    historical = release.history().p
    trusted = historical.trusted_key()
    verifier = Ed25519PublicKey.from_public_bytes(trusted)
    release.require(payload['public_key'] == original['public_key'], 'TRUSTED_PUBLIC_KEY_CHANGED')
    historical.verify_signature(original, verifier)
    retained = [copy.deepcopy(a) for a in payload['assets'] if a['filename'] != release.NAME]
    release.require(len(retained) == 9 and sum(a['filename'] == release.NAME for a in payload['assets']) == 1, 'NINE_RETAINED_AND_ONE_NEW_ASSET_REQUIRED')
    for asset in retained:
        historical.verify_signature(asset, verifier)
    # 通过全部来源门禁后才读取调用者显式指定的私钥一次。
    key = serialization.load_pem_private_key(Path(key_path).read_bytes(), password=None)
    release.require(isinstance(key, Ed25519PrivateKey) and key.public_key().public_bytes(serialization.Encoding.Raw, serialization.PublicFormat.Raw) == trusted,
                    'ORIGINAL_ED25519_SIGNING_KEY_REQUIRED')
    for asset in payload['assets']:
        if asset['filename'] == release.NAME:
            unsigned = {k: v for k, v in asset.items() if k != 'signature'}
            asset['signature'] = base64.b64encode(key.sign(canonical(unsigned))).decode('ascii')
    release.require([a for a in payload['assets'] if a['filename'] != release.NAME] == retained, 'RETAINED_NINE_SIGNATURES_CHANGED')
    payload.pop('signature', None)
    payload['signature'] = base64.b64encode(key.sign(canonical(payload))).decode('ascii')
    historical.verify_signature(payload, verifier)
    for asset in payload['assets']:
        historical.verify_signature(asset, verifier)
    release.exclusive(target, payload)
    release.signed_manifest(target, checked)
    return {'manifest': str(target), 'sha256': release.digest(target), 'private_key_output': False, 'private_key_copied': False, 'network_write_performed': False}

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--key', type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(sign(args.key), ensure_ascii=False))

if __name__ == '__main__': main()
