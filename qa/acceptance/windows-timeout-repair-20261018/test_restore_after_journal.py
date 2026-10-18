"""既有恢复准备薄适配的本地来源与生成脚本测试。"""
import subprocess
import unittest
from types import SimpleNamespace
from pathlib import Path
from unittest.mock import patch
import controller as c

OUT = Path(__file__).resolve().parent
r = c.load('test_restore_after_journal', OUT / 'restore-after-journal.py')

class RestoreAdapterTests(unittest.TestCase):
    def test_actual_observed_completion_and_tampered_sources(self):
        path = OUT / 'after-journal-restore-personal-observed-completion.json'
        value = c.contract.read(path)
        self.assertEqual(r.validate_completion(value['candidate'])['processes'][0]['pid'], 3380)
        read = c.contract.read
        for field, bad in [('http_terminal_observed', True), ('original_data_directory_verified', False),
                           ('submit_attempt_sha256', 'changed'), ('user_journal_present', True)]:
            def changed(p):
                row = read(p)
                return {**row, field: bad} if Path(p) == path else row
            with self.subTest(field=field), patch.object(c.contract, 'read', side_effect=changed), self.assertRaises(RuntimeError):
                r.validate_completion(value['candidate'])

    def test_publication_requires_real_recovered_success_and_submit_links(self):
        binding = {'package': 'candidate'}
        links = {'failed_prepare_sha256': 'failed'}
        values = {
            'after-journal-restore-personal-recover-prepare.attempt.json': {'candidate': binding, 'action': 'recover-prepare', 'prepared_source': 'recover-prepare', 'recovery_links': links, 'original_personal_instance': None},
            'after-journal-restore-personal-recover-prepare.result.json': {'candidate': binding, 'status': 'prepared', 'attempt_sha256': 'after-journal-restore-personal-recover-prepare.attempt.json', 'prepared_source': 'recover-prepare', 'recovery_links': links, 'operation': {'launch_requested': True}, 'original_personal_instance': None},
            'after-journal-restore-personal-submit.attempt.json': {'candidate': binding, 'prepared_source': 'recover-prepare', 'recovery_links': links, 'original_personal_instance': None}}
        with patch.object(r, 'configure', return_value=(None, SimpleNamespace(recovery_links=lambda b: links))), \
             patch.object(c.contract, 'read', side_effect=lambda p: values[Path(p).name]), \
             patch.object(c.contract, 'digest', side_effect=lambda p: Path(p).name):
            names, diagnostics = r.validate_recovered(binding)
            self.assertIn('after-journal-restore-personal-prepare.result.json', names)
            for field, bad in [('status', 'failed'), ('attempt_sha256', 'changed'), ('recovery_links', {}), ('original_personal_instance', {'pid': 999})]:
                name = 'after-journal-restore-personal-recover-prepare.result.json'
                original = values[name]
                values[name] = {**original, field: bad}
                with self.subTest(field=field), self.assertRaises(RuntimeError): r.validate_recovered(binding)
                values[name] = original

    def test_actual_failed_sources_and_tamper_rejected(self):
        h, restore = r.configure()
        value = c.contract.read(OUT / 'after-journal-restore-personal-after-prepare-failure-inspect.json')
        restore.recovery_links(value['candidate'])
        read = c.contract.read
        def changed(path):
            row = read(path)
            if Path(path).name.endswith('after-prepare-failure-inspect.json'):
                return {**row, 'failed_result_sha256': 'changed'}
            return row
        with patch.object(c.contract, 'read', side_effect=changed), self.assertRaises(RuntimeError):
            restore.recovery_links(value['candidate'])

    def test_existing_recovery_script_uses_new_task_and_never_recycles(self):
        h, restore = r.configure()
        first, host, form, transition = h.helpers()
        value = c.contract.read(OUT / 'after-journal-restore-personal-after-prepare-failure-inspect.json')
        script = restore.recovery_script(host, transition, value['candidate'], value['time_utc'], value['runtime'], value['services'])
        self.assertIn(h.RESTORE_TASK, script)
        self.assertNotIn('PartyOps-QA-Restore-Personal-20261017', script)
        for forbidden in ['CloseMainWindow', '.Kill(', 'Remove-Item', 'Stop-Service']:
            self.assertNotIn(forbidden, script)
        parser = "$s=[Console]::In.ReadToEnd();$t=$null;$e=$null;$null=[Management.Automation.Language.Parser]::ParseInput($s,[ref]$t,[ref]$e);if($e.Count){exit 1}"
        result = subprocess.run(['powershell.exe','-NoProfile','-NonInteractive','-Command',parser],
                                input=script.encode('utf-8'),capture_output=True,timeout=20)
        self.assertEqual(result.returncode, 0)

if __name__ == '__main__': unittest.main()
