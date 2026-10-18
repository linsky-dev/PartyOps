"""仅补启动入口的离线准入反例及PowerShell语法检查。"""
import copy
import subprocess
import unittest
from pathlib import Path
import controller as c

OUT = Path(__file__).resolve().parent
s = c.load('test_cancelled_start', OUT / 'resume-cancelled-start.py')

class StartOnlyTests(unittest.TestCase):
    def test_real_diagnosis_gate_and_reject_writer_or_changed_state(self):
        value = c.contract.read(OUT / 'host-journal-cancelled-relaunch-readonly-diagnosis.json')
        original = c.contract.read(OUT / 'host-journal-cancelled-before.json')
        s.ready(value, original)
        for field, bad in [('wizard_count', 1), ('consent_count', 1), ('other_powershell_count', 1),
                           ('cancel_relaunch_task_count', 1), ('journal_sha256', 'changed'), ('outcome', 'commit')]:
            v = copy.deepcopy(value); v['metadata'][field] = bad
            with self.subTest(field=field), self.assertRaises(RuntimeError): s.ready(v, original)

    def test_start_only_script_parses_and_uses_independent_task(self):
        original = c.contract.read(OUT / 'host-journal-cancelled-before.json')
        h, first, host, transition, restore = s.r.helpers()
        script = s.launch_script(host, original['candidate'], original['time_utc'], original)
        self.assertIn(s.r.CANCEL_TASK, script)
        self.assertNotIn(s.r.TASK, script)
        for forbidden in ['CloseMainWindow', '.Kill(', 'Stop-Process', 'Remove-Item']:
            self.assertNotIn(forbidden, script)
        self.assertIn('Test-CimCreationInstant', script)
        parser = "$s=[Console]::In.ReadToEnd();$t=$null;$e=$null;$null=[Management.Automation.Language.Parser]::ParseInput($s,[ref]$t,[ref]$e);if($e.Count){exit 1}"
        completed = subprocess.run(['powershell.exe', '-NoProfile', '-NonInteractive', '-Command', parser],
                                   input=script.encode('utf-8'), capture_output=True, timeout=20)
        self.assertEqual(completed.returncode, 0)

if __name__ == '__main__': unittest.main()
