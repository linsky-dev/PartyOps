"""新批次薄适配的局部检查，不执行网络或读取令牌。"""
import importlib.util
from pathlib import Path
from types import SimpleNamespace
import unittest
from unittest.mock import patch

HERE = Path(__file__).resolve().parent
def load(name, filename):
    spec = importlib.util.spec_from_file_location(name, HERE / filename)
    value = importlib.util.module_from_spec(spec); spec.loader.exec_module(value)
    return value
d = load('test_new_domestic_transport', 'domestic-release.py')

class TransportTests(unittest.TestCase):
    def test_network_writes_block_before_loading_clients(self):
        for module, action in [(d, 'upload')]:
            with self.subTest(action=action), patch('sys.argv', ['helper', action]), patch.object(module, 'configured') as factory:
                with self.assertRaisesRegex(RuntimeError, 'EXPLICIT_'): module.main()
                factory.assert_not_called()

    def test_domestic_uses_new_binding_with_mature_old_upload(self):
        binding = {'bytes': 449988807, 'package_sha256': 'candidate'}
        old = SimpleNamespace()
        with patch.object(d, 'checked_batch', return_value=(HERE / 'batch', binding)), patch.object(d.release, 'history', return_value=old):
            actual, folder, actual_binding = d.configured()
        self.assertIs(actual, old)
        self.assertEqual(actual.HERE, HERE)
        self.assertEqual(actual.receiver.NAME, d.release.NAME)
        self.assertEqual(actual.receiver.PROFILE, 'windows-startup-20261018-v1')
        self.assertEqual(actual.receiver.SIZE, 449988807)

if __name__ == '__main__': unittest.main()
