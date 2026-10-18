"""恢复后独立host请求和restore证据路由离线契约。"""
import importlib.util
from pathlib import Path
import unittest
from tempfile import TemporaryDirectory
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("host_after_journal", Path(__file__).with_name("host-after-journal.py"))
r = importlib.util.module_from_spec(spec); spec.loader.exec_module(r)


class IndependentHostTests(unittest.TestCase):
    def test_independent_names_do_not_reuse_failed_attempt(self):
        h = r.configure()
        for action in ("host-prepare", "host-submit", "restore-prepare", "restore-submit"):
            self.assertTrue(h.operation_name(action).startswith(r.PREFIX))
        self.assertIn("After-Journal", h.HOST_TASK)
        self.assertIn("After-Journal", h.RESTORE_TASK)

    def test_restore_routes_only_action_receipts_and_keeps_install_evidence(self):
        h = r.configure()
        first, host, form, transition = h.helpers()
        restore = h.configured_restore(first, host, form, transition, object())
        with patch.object(h.contract, "read", return_value={}) as reader:
            restore.contract.read(h.OUTPUT / "first-host-prepare.result.json")
            reader.assert_called_once_with(h.OUTPUT / "after-journal-first-host-prepare.result.json")
        with patch.object(h.contract, "read", return_value={}) as reader:
            restore.contract.read(h.OUTPUT / "overlay-install/completed.json")
            reader.assert_called_once_with(h.OUTPUT / "overlay-install/completed.json")
        with patch.object(h.contract, "exclusive") as writer:
            restore.contract.exclusive(h.OUTPUT / "restore-personal-submit.result.json", {"status": "real"})
            writer.assert_called_once_with(h.OUTPUT / "after-journal-restore-personal-submit.result.json", {"status": "real"})

    def test_retry_prepare_closes_only_bound_recovery_wizard_with_tick_checks(self):
        h = r.configure()
        first, host, form, transition = h.helpers()
        wizard = {"name": "PartyOpsWizard.exe", "pid": 4000, "created": "2026-10-18T10:00:00Z",
            "installed_path_matches": True, "owner_is_original_user": True, "session_id": 1}
        runtime = {"boot_id": "2026-10-18T09:43:26Z", "processes": [{"pid": 6208, "path": r.c.contract.INSTALL + r"\PartyOps.exe",
                    "created": "2026-10-18T09:44:41.9347Z"}]}
        snapshot = {"user_mode": "personal", "system_mode": "personal", "pending_mode_switch": False,
                    "user_journal_present": False, "processes": [wizard]}
        with patch.object(r, "recovery", return_value={"snapshot": snapshot, "runtime": runtime}), \
             patch.object(r.c.contract, "runtime_matches", return_value={}):
            script = h.HOST_PREPARE_HOOK(first, host, transition, {"manifest_sha256": "m", "wizard_sha256": "w"},
                                       "2026-10-18T10:01:00Z", snapshot, runtime)
        self.assertIn("Test-CimCreationInstant", script)
        self.assertNotIn(".created -cne $target.personal.created", script)
        self.assertNotIn("ToString('o') -cne $target.created", script)
        self.assertIn("CONSENT_STILL_ACTIVE", script)
        self.assertIn("WIZARD_HELPER_OR_CHILD_ACTIVE", script)
        self.assertIn(h.HOST_TASK, script)
        self.assertIn("--initial-role host", script)

    def test_default_check_requires_verified_recovery(self):
        h = r.configure()
        with patch.object(h, "current_inputs", side_effect=RuntimeError("CONFIRMED_FAILED_TRANSACTION_RECOVERY_REQUIRED")), \
             patch.object(h.c, "environment", return_value=(object(), object(), {})), \
             patch.object(h.contract, "candidate", return_value={}):
            with self.assertRaisesRegex(RuntimeError, "CONFIRMED_FAILED_TRANSACTION_RECOVERY_REQUIRED"):
                h.main(["--package", "unused", "--sha256", "new"])

    def test_confirmed_recovery_gate_requires_rollback_and_all_source_hashes(self):
        binding = {"package": "new"}; inputs = {"baseline_sha256": "baseline"}
        baseline = {"retained": {"user_id": "original"}, "calendar_id": "original", "calendar_sha256": "calendar"}
        metadata = {"transaction_id": "same", "outcome": "rollback", "journal_present": False, "pending": False,
                    "consent_count": 0, "wizard_count": 1, "other_powershell_count": 0, "wizard_children": [],
                    "services": [{"name": "PartyOpsHost", "state": "Stopped"}, {"name": "PartyOpsUpdateService", "state": "Stopped"}]}
        runtime = {"boot_id": "boot", "processes": [{"pid": 6208, "path": r.contract.INSTALL + r"\PartyOps.exe", "created": "2026-10-18T09:44:41Z"}]}
        values = {
            "host-journal-recovery-before.json": {"candidate": binding, "runtime": runtime, "metadata": {"transaction_id": "same"},
                "failed_host_attempt_sha256": "first-host-approved-existing-root-submit.attempt.json",
                "failed_host_result_sha256": "first-host-approved-existing-root-submit.result.json"},
            "host-journal-reopen.attempt.json": {"candidate": binding, "transaction_id": "same", "before_sha256": "host-journal-recovery-before.json"},
            "host-journal-reopen.result.json": {"candidate": binding, "attempt_sha256": "host-journal-reopen.attempt.json", "operation": {"launch_requested": True}},
            "first-host-approved-existing-root-submit.result.json": {"candidate": binding, "status": "failed", "attempt_sha256": "first-host-approved-existing-root-submit.attempt.json"},
            "retention-baseline.json": baseline}
        verified = {"candidate": binding, "status": "rollback-personal-retained", "inputs": inputs, "runtime": runtime,
            "launch_source": "host-journal-reopen", "transaction_id": "same", "metadata": metadata,
            "snapshot": {"user_mode": "personal", "system_mode": "personal"}, **baseline,
            "failed_host_attempt_sha256": "first-host-approved-existing-root-submit.attempt.json",
            "failed_host_result_sha256": "first-host-approved-existing-root-submit.result.json",
            "before_sha256": "host-journal-recovery-before.json", "reopen_attempt_sha256": "host-journal-reopen.attempt.json",
            "reopen_result_sha256": "host-journal-reopen.result.json"}
        values["host-journal-recovery-verified.json"] = verified
        with patch.object(r.contract, "read", side_effect=lambda path: values[Path(path).name]), \
             patch.object(r.contract, "digest", side_effect=lambda path: Path(path).name), \
             patch.object(r.c, "preserved_inputs", return_value=inputs), patch.object(r.contract, "runtime_matches"):
            r.recovery(binding)
            for field, bad in (("outcome", ""), ("journal_present", True), ("pending", True), ("consent_count", 1),
                               ("transaction_id", "another"), ("other_powershell_count", 1)):
                values["host-journal-recovery-verified.json"] = {**verified, "metadata": {**metadata, field: bad}}
                with self.subTest(field=field), self.assertRaisesRegex(RuntimeError, "CONFIRMED_FAILED_TRANSACTION_RECOVERY_REQUIRED"):
                    r.recovery(binding)
            values["host-journal-recovery-verified.json"] = {**verified, "reopen_result_sha256": "wrong"}
            with self.assertRaisesRegex(RuntimeError, "CONFIRMED_FAILED_TRANSACTION_RECOVERY_REQUIRED"):
                r.recovery(binding)
            values["host-journal-recovery-verified.json"] = {**verified, "runtime": {**runtime,
                "processes": [{**runtime["processes"][0], "pid": 9999}]}}
            with self.assertRaisesRegex(RuntimeError, "RESTORED_PERSONAL_RUNTIME_CHANGED"):
                r.recovery(binding)
            values["host-journal-recovery-verified.json"] = {**verified, "metadata": {**metadata,
                "services": [{"name": "PartyOpsHost", "state": "Running"}, {"name": "PartyOpsUpdateService", "state": "Stopped"}]}}
            with self.assertRaisesRegex(RuntimeError, "CONFIRMED_FAILED_TRANSACTION_RECOVERY_REQUIRED"):
                r.recovery(binding)


if __name__ == "__main__": unittest.main()
