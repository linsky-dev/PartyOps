"""主机/个人薄适配离线契约；不读取 Guest 凭据，不连接 Guest。"""
import importlib.util
import unittest
from pathlib import Path
from tempfile import TemporaryDirectory
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("host_personal", Path(__file__).with_name("host-personal.py"))
h = importlib.util.module_from_spec(spec)
spec.loader.exec_module(h)


class HostPersonalTests(unittest.TestCase):
    def setUp(self):
        self.first, self.host, self.form, self.transition = h.helpers()
        self.binding = {"manifest_sha256": "manifest", "wizard_sha256": "wizard", "package": {"sha256": "new"}}
        self.success = {"candidate": self.binding, "attempt_sha256": "attempt", "case": "retained-existing-root",
            "status": "bootstrap-page-observed", "http_status": 200, "bootstrap_admin_page": True, "public_error_codes": [],
            "host_status": {"present": True, "stage": "ready", "code": "", "pid": 200},
            "user_mode": "host", "system_mode": "host", "qa_host_data_directory": self.first.ROOT_HOST_DATA,
            "original_personal_restore_directory": self.first.PERSONAL_DATA}

    def test_default_check_does_not_connect(self):
        with patch.object(h.c, "environment", return_value=(object(), object(), {})), \
             patch.object(h.contract, "candidate", return_value=self.binding), \
             patch.object(h, "current_inputs", return_value={}), patch.object(h, "directory_provenance", return_value={}), \
             patch.object(h.contract, "bind_guest", side_effect=AssertionError("Guest forbidden")):
            h.main(["--package", "not-opened.exe", "--sha256", "new"])

    def test_all_guest_actions_require_execute(self):
        for action in ("inspect", "host-prepare", "host-submit", "restore-prepare", "restore-submit", "verify-retention"):
            with self.subTest(action=action), patch.object(h.c, "environment", side_effect=AssertionError("environment forbidden")):
                with self.assertRaisesRegex(RuntimeError, "EXPLICIT_EXECUTE_REQUIRED"):
                    h.main([action, "--package", "not-opened.exe", "--sha256", "new"])

    def test_existing_attempt_stops_before_environment(self):
        with TemporaryDirectory(dir=h.OUTPUT) as folder, patch.object(h, "OUTPUT", Path(folder)):
            for action in ("host-prepare", "host-submit", "restore-prepare", "restore-submit"):
                h.contract.exclusive(Path(folder) / (h.operation_name(action) + ".attempt.json"), {"status": "unknown"})
                with self.subTest(action=action), patch.object(h.c, "environment", side_effect=AssertionError("environment forbidden")):
                    with self.assertRaisesRegex(RuntimeError, "READ_ONLY_INSPECT_REQUIRED"):
                        h.main([action, "--package", "not-opened.exe", "--sha256", "new", "--execute"])

    def test_success_requires_new_binding_and_host_ready(self):
        h.host_success(self.success, self.binding, "attempt", self.first)
        for field, value in (("candidate", "old"), ("case", "approved-existing-root"), ("attempt_sha256", "old"),
                ("user_mode", "personal"), ("system_mode", "missing"), ("bootstrap_admin_page", False),
                ("host_status", {"present": True, "stage": "starting", "code": "", "pid": 200})):
            with self.subTest(field=field), self.assertRaisesRegex(RuntimeError, "SUCCESSFUL_NEW_BOUND_HOST_REQUIRED"):
                h.host_success({**self.success, field: value}, self.binding, "attempt", self.first)

    def test_modules_write_only_new_evidence(self):
        for module in (self.first, self.host, self.form, self.transition):
            self.assertEqual(module.OUTPUT, h.OUTPUT)
        self.assertIs(self.transition.preserved_inputs, h.c.boot_inputs)
        self.assertEqual(self.host.TASK, h.HOST_TASK)

    def test_host_launch_reuses_user_and_blocks_existing_task(self):
        script = h.launch_script(self.host, self.binding, "2026-10-18T01:00:00Z")
        self.assertIn("--initial-role host", script)
        self.assertNotIn("--reconfigure --initial-role host", script)
        self.assertIn("HOST_TASK_EXISTS_INSPECT_ONLY", script)
        self.assertIn("RunLevel=0", script)
        self.assertNotIn("PartyOps-QA-Host-Wizard-20261017", script)
        self.assertIn("PartyOps-QA-Host-20261017", script)
        self.assertNotIn("Prepare", script)

    def test_restore_injection_reuses_original_flow(self):
        windows = object()
        restore = h.configured_restore(self.first, self.host, self.form, self.transition, windows)
        self.assertEqual(restore.OUTPUT, h.OUTPUT)
        self.assertIs(restore.host_success, h.host_success)
        self.assertIs(restore.load(h.OUTPUT / "first-host-qa.py", "test"), self.first)
        self.assertIs(restore.load(h.c.ROOT / "qa/vm-lab/scripts/exercise-windows-business.py", "test"), windows)
        script = restore.prepare_script(self.host, self.first, self.transition, self.binding, "2026-10-18T01:00:00Z",
                                        {"pid": 1, "created": "2026-10-18T01:00:00Z"})
        self.assertIn(h.RESTORE_TASK, script)
        self.assertIn("RESTORE_TASK_EXISTS_INSPECT_ONLY", script)
        self.assertNotIn("PartyOps-QA-Restore-Personal-20261017", script)
        self.assertIn("--reconfigure --initial-role personal", script)
        self.assertIn(h.RESTORE_TASK, restore.TASK_ABSENT)

    def test_old_directory_provenance_is_real_and_readonly(self):
        value = h.directory_provenance(self.first)
        self.assertEqual(len(value["ca_sha256"]), 64)
        self.assertIn("created_after", value)

    def test_start_preserves_actual_restored_system_personal(self):
        observed = {"user_mode": "personal", "system_mode": "personal", "wizard_exists": False}
        h.require_personal_start(observed)
        for field, value in (("user_mode", "host"), ("system_mode", "host"),
                             ("system_mode", "missing"), ("wizard_exists", True)):
            with self.subTest(field=field, value=value):
                with self.assertRaisesRegex(RuntimeError, "RESTORED_PERSONAL_WITHOUT_WIZARD_REQUIRED"):
                    h.require_personal_start({**observed, field: value})


if __name__ == "__main__":
    unittest.main()
