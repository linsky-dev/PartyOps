"""仅验证签名前门禁，不读取或使用任何私钥、不执行签名。"""
import copy
import importlib.util
from pathlib import Path
from types import SimpleNamespace
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('new_sign_helper', Path(__file__).with_name('sign-verified-manifest.py'))
s = importlib.util.module_from_spec(spec); spec.loader.exec_module(s)

class SigningGateTests(unittest.TestCase):
    def test_existing_manifest_blocks_before_evidence_or_key(self):
        with patch.object(Path, 'exists', return_value=True), patch.object(s.release, 'checked_preparation') as checked, patch.object(Path, 'read_bytes') as key:
            with self.assertRaisesRegex(RuntimeError, 'SIGNED_MANIFEST_EXISTS'): s.sign(Path('never-read'))
            checked.assert_not_called(); key.assert_not_called()

    def test_changed_input_blocks_before_key(self):
        with patch.object(Path, 'exists', return_value=False), patch.object(s.release, 'checked_preparation', return_value={}), \
             patch.object(s.release, 'baseline', return_value={}), patch.object(s.release, 'read', return_value={'generated_at': 'date', 'changed': True}), \
             patch.object(s.release, 'compose', return_value={'generated_at': 'date'}), patch.object(Path, 'read_bytes') as key:
            with self.assertRaisesRegex(RuntimeError, 'EXACT_VERIFIED'): s.sign(Path('never-read'))
            key.assert_not_called()

    def test_retained_assets_verified_before_explicit_key_read(self):
        payload = {'generated_at': 'date', 'public_key': 'same', 'formal_release_ready': True, 'formal_gate': 'FULL_FUNCTION_GATE_PASSED',
                   'assets': [{'filename': str(n), 'signature': 'retained'} for n in range(9)] + [{'filename': s.release.NAME}]}
        from unittest.mock import Mock
        historical = SimpleNamespace(trusted_key=lambda: b'\x00'*32, verify_signature=Mock())
        with patch.object(Path, 'exists', return_value=False), patch.object(s.release, 'checked_preparation', return_value={}), \
             patch.object(s.release, 'baseline', return_value={'public_key': 'same'}), patch.object(s.release, 'read', return_value=copy.deepcopy(payload)), \
             patch.object(s.release, 'compose', return_value=payload), patch.object(s.release, 'history', return_value=SimpleNamespace(p=historical)), \
             patch.object(Path, 'read_bytes', side_effect=RuntimeError('TEST_STOP_BEFORE_PRIVATE_KEY')) as key:
            with self.assertRaisesRegex(RuntimeError, 'TEST_STOP_BEFORE_PRIVATE_KEY'): s.sign(Path('explicit-test-only'))
            self.assertEqual(historical.verify_signature.call_count, 10)
            key.assert_called_once()

if __name__ == '__main__': unittest.main()
