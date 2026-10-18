"""本次journal恢复入口离线契约；不连接Guest。"""
import importlib.util
from pathlib import Path
import unittest
import subprocess
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("recover_host_journal", Path(__file__).with_name("recover-host-journal.py"))
r = importlib.util.module_from_spec(spec); spec.loader.exec_module(r)


class JournalRecoveryTests(unittest.TestCase):
    def setUp(self):
        self.before = {"metadata": {"journal_present": True, "pending": False, "outcome": "", "transaction_id": "a" * 43,
            "journal_sha256": "b" * 64, "wizard_count": 1, "other_powershell_count": 0, "consent_count": 0, "wizard_children": [],
            "services": [{"name": "PartyOpsHost", "state": "Stopped"}, {"name": "PartyOpsUpdateService", "state": "Stopped"}]},
            "snapshot": {"user_mode": "personal", "system_mode": "personal", "processes": [
                {"name": "PartyOpsWizard.exe", "pid": 1192, "created": "2026-10-18T09:54:00Z"}]}}

    def test_no_writer_preconditions_fail_closed(self):
        r.can_reopen(self.before)
        for field, value in (("journal_present", False), ("pending", True), ("outcome", "commit"),
                ("wizard_count", 2), ("other_powershell_count", 1), ("consent_count", 1), ("wizard_children", [{"pid": 8}]),
                ("services", [{"state": "Running"}])):
            with self.subTest(field=field), self.assertRaisesRegex(RuntimeError, "NO_WRITER"):
                r.can_reopen({**self.before, "metadata": {**self.before["metadata"], field: value}})

    def test_reopen_reuses_precise_process_recycle_and_preserves_journal(self):
        h, first, host, transition, restore = r.helpers()
        script = r.reopen_script(host, first, transition, restore,
            {"manifest_sha256": "m", "wizard_sha256": "w"}, "2026-10-18T10:00:00Z", self.before)
        self.assertIn("Test-CimCreationInstant", script)
        self.assertIn("$p.SessionId -ne $desktop[0].session_id", script)
        self.assertIn("$owner.User -ne 'partyopsuser'", script)
        self.assertIn("PRODUCT_PROCESS_CHANGED_DURING_PREPARE", script)
        self.assertIn("$handle.Kill()", script)
        self.assertIn("--reconfigure --initial-role personal", script)
        self.assertIn(r.TASK, script)
        self.assertIn("a" * 43, script)
        self.assertIn("b" * 64, script)
        self.assertNotIn("Remove-Item", script)
        self.assertNotIn("Stop-Service", script)

    def test_actions_require_explicit_execute(self):
        for action in ("inspect", "reopen", "resume-launch", "cancelled-inspect", "cancelled-relaunch", "verify"):
            with self.subTest(action=action), patch.object(r.c, "environment", side_effect=AssertionError("Guest forbidden")):
                with self.assertRaisesRegex(RuntimeError, "EXPLICIT_RECOVERY_EXECUTE_REQUIRED"):
                    r.main([action, "--package", "unused", "--sha256", "new"])

    def test_resume_requires_absent_task_writer_and_same_journal(self):
        value = {**self.before, "metadata": {**self.before["metadata"], "wizard_count": 0, "recovery_task_count": 0}}
        r.can_resume(value, self.before)
        for field, bad in (("wizard_count", 1), ("recovery_task_count", 1), ("consent_count", 1),
                ("other_powershell_count", 1), ("journal_sha256", "changed"), ("transaction_id", "changed")):
            with self.subTest(field=field), self.assertRaisesRegex(RuntimeError, "RESUME_REQUIRES"):
                r.can_resume({**value, "metadata": {**value["metadata"], field: bad}}, self.before)

    def test_resume_only_launches_tail_and_captures_public_failure_phase(self):
        h, first, host, transition, restore = r.helpers()
        before = {**self.before, "runtime": {"processes": [{"pid": 6208, "created": "2026-10-18T09:44:41Z"}]}}
        script = r.resume_script(host, {"manifest_sha256": "m", "wizard_sha256": "w"}, "2026-10-18T10:00:00Z", before)
        self.assertNotIn(".Kill()", script)
        self.assertNotIn("CloseMainWindow", script)
        self.assertIn("RegisterTaskDefinition", script)
        self.assertIn("--reconfigure --initial-role personal", script)
        self.assertIn("$phase='register'", script)
        self.assertIn("Test-CimCreationInstant $p[0].CreationDate $expected.personal.created", script)
        self.assertIn("script_line=", script)
        self.assertNotIn("Remove-Item", script)

    def test_real_powershell_creation_precision_equivalence(self):
        h, first, host, transition, restore = r.helpers()
        source = transition.START_TIME_MATCH + r"""
$t=[DateTimeOffset]::Parse('2026-10-18T09:44:41.9347000Z').UtcDateTime
if(-not (Test-CimCreationInstant $t '2026-10-18T09:44:41.9347Z')) {exit 1}
if(-not (Test-CimCreationInstant $t '2026-10-18T09:44:41.934700Z')) {exit 2}
if(Test-CimCreationInstant $t '2026-10-18T09:44:41.934701Z') {exit 3}
"""
        result = subprocess.run(["powershell.exe", "-NoProfile", "-NonInteractive", "-Command", source], capture_output=True, timeout=20)
        self.assertEqual(result.returncode, 0)

    def test_cancelled_relaunch_requires_bound_lua_task_and_no_writer(self):
        value = {**self.before, "metadata": {**self.before["metadata"], "recovery_task_count": 1, "cancel_relaunch_task_count": 0,
            "resume_task": {"state": 4, "last_run": "2026-10-18T09:53:59Z", "original_user_matches": True,
                "run_level": 0, "logon_type": 3, "actions": [{"path_matches": True, "arguments": "--reconfigure --initial-role personal"}]}},
            "snapshot": {**self.before["snapshot"], "session_id": 1, "processes": [{**self.before["snapshot"]["processes"][0],
                "installed_path_matches": True, "owner_is_original_user": True, "session_id": 1}]}}
        r.can_cancelled_relaunch(value, self.before)
        for field, bad in (("consent_count", 1), ("other_powershell_count", 1), ("cancel_relaunch_task_count", 1)):
            with self.subTest(field=field), self.assertRaises(RuntimeError):
                r.can_cancelled_relaunch({**value, "metadata": {**value["metadata"], field: bad}}, self.before)
        with self.assertRaisesRegex(RuntimeError, "BOUND_CANCELLED"):
            r.can_cancelled_relaunch({**value, "metadata": {**value["metadata"], "resume_task": {**value["metadata"]["resume_task"], "run_level": 1}}}, self.before)

    def test_cancelled_script_has_unique_task_tick_and_public_phase(self):
        h, first, host, transition, restore = r.helpers()
        script = r.cancelled_script(host, first, transition, restore,
            {"manifest_sha256": "m", "wizard_sha256": "w"}, "2026-10-18T10:00:00Z", self.before)
        self.assertIn(r.CANCEL_TASK, script)
        self.assertNotIn("RECOVERY_PartyOps", script)
        self.assertIn("Test-CimCreationInstant", script)
        self.assertIn("OTHER_LIVE_WIZARD_APPEARED", script)
        self.assertIn("$phase='register'", script)
        self.assertIn("--reconfigure --initial-role personal", script)
        self.assertNotIn("Remove-Item", script)
        parser = "$s=[Console]::In.ReadToEnd();$t=$null;$e=$null;$null=[Management.Automation.Language.Parser]::ParseInput($s,[ref]$t,[ref]$e);if($e.Count){exit 1}"
        result = subprocess.run(["powershell.exe", "-NoProfile", "-NonInteractive", "-Command", parser], input=script.encode("utf-8"), capture_output=True, timeout=20)
        self.assertEqual(result.returncode, 0)


if __name__ == "__main__": unittest.main()
