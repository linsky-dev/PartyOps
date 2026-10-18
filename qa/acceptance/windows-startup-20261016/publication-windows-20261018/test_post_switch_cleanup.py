"""只做计划、生成脚本语法和拒绝测试；不运行删除/网络代码。"""
import copy
import importlib.util
from pathlib import Path
import subprocess
import unittest
from types import SimpleNamespace
from unittest.mock import patch
import post_switch_common as common

HERE = Path(__file__).resolve().parent
def load(name, filename):
    spec = importlib.util.spec_from_file_location(name, HERE / filename)
    value = importlib.util.module_from_spec(spec); spec.loader.exec_module(value); return value
p = load('test_prepare_post_switch', 'prepare-post-switch-cleanup.py')
g = load('test_withdraw_windows_old', 'withdraw-github-old.py')

class CleanupTests(unittest.TestCase):
    def test_real_prepare_entry_loads_hyphenated_domestic_filename(self):
        actual_load = common.release.load
        loaded = []
        def load_domestic(name, path):
            module = actual_load(name, path)
            self.assertEqual(Path(path), HERE / 'domestic-release.py')
            loaded.append(module)
            def stop_before_changes(): raise RuntimeError('ACTUAL_MODULE_LOADED_STOP_BEFORE_PREPARE')
            module.checked_batch = stop_before_changes
            return module
        with patch('sys.argv', ['helper', 'prepare', '--website-proof', 'site.json', '--github-readback', 'gh.json']), \
             patch.object(common, 'fixed_plan', return_value={}), patch.object(common, 'proofs', return_value=(None, None, None, {})), \
             patch.object(common.release, 'load', side_effect=load_domestic):
            with self.assertRaisesRegex(RuntimeError, 'ACTUAL_MODULE_LOADED_STOP_BEFORE_PREPARE'): p.main()
        self.assertEqual(len(loaded), 1)

    def test_production_requires_same_frozen_inventory_intent_result_and_receipt(self):
        inventory = {'edgeone.json': 'config', 'cloud-functions/example.js': 'backend', 'index.html': 'current-html'}
        values = {'website-frozen-files.json': inventory, 'website-production-result.json': {'success': True, 'returncode': 0, 'deployment_id': 'dpactual'},
                  'website-production.intent.json': {'inventory_sha256': 'website-frozen-files.json'}}
        web = {'inventory_sha256': 'website-frozen-files.json', 'deployment_result_sha256': 'website-production-result.json'}
        with patch.object(common.release, 'read', side_effect=lambda path: values[Path(path).name]), \
             patch.object(common.release, 'digest', side_effect=lambda path: Path(path).name), \
             patch.object(common.release, 'load', return_value=SimpleNamespace(inventory=lambda: inventory)):
            common.production_website(web)
            for field, bad in [('inventory_sha256', 'changed'), ('deployment_result_sha256', 'changed')]:
                with self.subTest(field=field), self.assertRaisesRegex(RuntimeError, 'SAME_BATCH_PRODUCTION'):
                    common.production_website({**web, field: bad})
            for name, field, bad in [('website-production-result.json', 'success', False), ('website-production.intent.json', 'inventory_sha256', 'changed')]:
                original = values[name]; values[name] = {**original, field: bad}
                with self.subTest(field=field), self.assertRaisesRegex(RuntimeError, 'SAME_BATCH_PRODUCTION'): common.production_website(web)
                values[name] = original
        with patch.object(common.release, 'read', side_effect=lambda path: values[Path(path).name]), \
             patch.object(common.release, 'digest', return_value='sha'), \
             patch.object(common.release, 'load', return_value=SimpleNamespace(inventory=lambda: {**inventory, 'index.html': 'changed'})):
            with self.assertRaisesRegex(RuntimeError, 'CURRENT_FROZEN'): common.production_website(web)

    def test_proofs_rejects_noncanonical_production_receipt_name(self):
        with patch.object(Path, 'resolve', lambda self, strict=False: self):
            with self.assertRaisesRegex(RuntimeError, 'CURRENT_BATCH_PROOF_PATHS'):
                common.proofs(HERE / 'preview-website-readback.json', HERE / 'github-windows-assets-state/readback-aaaaaaaaaaaa.json')

    def test_snapshot_exact_old_ids_sizes_digests(self):
        plan = common.fixed_plan()
        self.assertEqual([x['id'] for x in plan['github_old_assets']], [606128919, 606131252])
        before = common.release.read(HERE / 'github-current-before.json')
        before['release']['assets'] = [dict(x, size=1) if x['id'] == 606128919 else x for x in before['release']['assets']]
        with patch.object(common.release, 'read', return_value=before), self.assertRaisesRegex(RuntimeError, 'EXACT_OLD_WINDOWS'):
            common.fixed_plan()

    def test_mature_cleanup_generated_ten_routes_and_node_syntax_only(self):
        meta = common.release.read(HERE / 'batch/receiver/upload-metadata.json')
        source = (HERE / 'batch/receiver/_serve.js').read_text(encoding='utf-8')
        generated, output = p.receiver_source(source, meta, common.fixed_plan())
        self.assertEqual(len(output['assets']), 10)
        self.assertNotIn(common.OLD_WINDOWS['file_name'], {a['file_name'] for a in output['assets']})
        self.assertIn('path.resolve(ROOT) !== "/workspace"', generated)
        self.assertEqual(generated.count('fs.unlinkSync('), 1)
        self.assertIn('await withdrawSuperseded();', generated)
        self.assertIn('WITHDRAWAL_OLD_MISMATCH', generated)
        result = subprocess.run(['C:/Program Files/nodejs/node.exe', '--check'], input=generated.encode('utf-8'), capture_output=True, timeout=15)
        self.assertEqual(result.returncode, 0)

    def test_health_requires_all_ten_and_two_exact_withdrawals(self):
        plan = common.fixed_plan()
        meta = common.release.read(HERE / 'batch/receiver/upload-metadata.json')
        meta['assets'] = [x for x in meta['assets'] if x['file_name'] != common.OLD_WINDOWS['file_name']]
        health = {'ok': True, 'version': '1.4.5-rc.6', 'receiver': common.release.PROFILE, 'manifest_sha256': meta['manifest_sha256'],
            'files': {x['file_name']: {'file_ready': True, 'bytes': x['bytes'], 'sha256': x['sha256']} for x in meta['assets']},
            'withdrawal': [{'filename': x['file_name'], 'sha256': x['sha256'], 'bytes': x['bytes'], 'result': 'already-absent'} for x in plan['old_assets']]}
        p.validate_health(health, meta, plan)
        for field, value in [('manifest_sha256', 'changed'), ('withdrawal', []), ('files', {})]:
            with self.subTest(field=field), self.assertRaises(RuntimeError): p.validate_health({**health, field: value}, meta, plan)
        bad = copy.deepcopy(health); bad['withdrawal'][0]['bytes'] = 1
        with self.assertRaisesRegex(RuntimeError, 'EXACT_OLD_WINDOWS'): p.validate_health(bad, meta, plan)

    def test_missing_explicit_flag_blocks_before_any_proof_or_network(self):
        with patch('sys.argv', ['helper', 'withdraw']), patch.object(common, 'proofs') as proofs:
            with self.assertRaisesRegex(RuntimeError, 'EXPLICIT_WITHDRAW'): g.main()
            proofs.assert_not_called()

if __name__ == '__main__': unittest.main()
