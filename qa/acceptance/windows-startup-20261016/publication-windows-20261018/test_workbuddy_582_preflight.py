"""只读版本适配及操作范围测试；不连接实际官方listener。"""
import importlib.util
import json
from pathlib import Path
from types import SimpleNamespace
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('workbuddy582_readonly_test', Path(__file__).with_name('workbuddy-582-preflight.py'))
w = importlib.util.module_from_spec(spec); spec.loader.exec_module(w)

class ReadonlyBridgeTests(unittest.TestCase):
    def test_exact_new_version_and_signature_still_required(self):
        module = w.bridge(w.HERE)
        for version, signer, status, accepted in [('5.8.2.0', 'O=Tencent Technology (Shenzhen) Company Limited', 'Valid', True),
                ('5.7.7.0', 'O=Tencent Technology (Shenzhen) Company Limited', 'Valid', False),
                ('5.8.2.0', 'Untrusted', 'Valid', False), ('5.8.2.0', 'O=Tencent Technology (Shenzhen) Company Limited', 'NotSigned', False)]:
            completed = SimpleNamespace(returncode=0, stdout=json.dumps({'status': status, 'signer': signer, 'version': version}).encode())
            with self.subTest(version=version, signer=signer, status=status), patch.object(module.subprocess, 'run', return_value=completed):
                if accepted: self.assertEqual(module.official_identity(), version)
                else:
                    with self.assertRaises(module.BridgeError): module.official_identity()

    def test_only_initialize_and_list_never_call_or_expose_headers(self):
        calls = []
        class Client:
            def __init__(self, uri, headers): pass
            def request(self, method, *args):
                calls.append(method)
                return {'tools': [{'name': 'workbuddy_sites_deploy'}]} if method == 'tools/list' else {}
        module = SimpleNamespace(discover=lambda: ('http://127.0.0.1:1/mcp', {'Authorization': 'DO_NOT_OUTPUT'}), Client=Client,
                                 NEW_TOOL='workbuddy_sites_deploy', OLD_TOOL='workbuddy_cloudstudio_deploy',
                                 require=lambda value, code: None if value else (_ for _ in ()).throw(RuntimeError(code)))
        with patch.object(w, 'bridge', return_value=module): result = w.run(w.HERE)
        self.assertEqual(calls, ['initialize', 'notifications/initialized', 'tools/list'])
        self.assertNotIn('DO_NOT_OUTPUT', json.dumps(result))
        self.assertFalse(result['deploy_called'])

    def test_missing_connector_is_not_retried(self):
        from unittest.mock import Mock
        discover = Mock(side_effect=RuntimeError('CONNECTOR_MISSING_OR_AMBIGUOUS'))
        with patch.object(w, 'bridge', return_value=SimpleNamespace(discover=discover)):
            with self.assertRaisesRegex(RuntimeError, 'CONNECTOR_MISSING'): w.run(w.HERE)
        discover.assert_called_once()

if __name__ == '__main__': unittest.main()
