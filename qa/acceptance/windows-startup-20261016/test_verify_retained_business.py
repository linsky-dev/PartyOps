"""恢复个人后的独立保留阶段契约；不连接Guest或读取合成凭据。"""
import copy
import importlib.util
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location("restored_business_contract", Path(__file__).with_name("verify-retained-business.py"))
adapter = importlib.util.module_from_spec(spec); spec.loader.exec_module(adapter)


class RestoredBusinessContractTests(unittest.TestCase):
    def setUp(self):
        self.binding = {"manifest_sha256": "a" * 64, "executable_sha256": "b" * 64, "wizard_sha256": "c" * 64}
        self.runtime = {**self.binding, "boot_id": "2026-10-17T05:00:00Z", "http_owners": [2448],
            "processes": [{"pid": 2448, "path": adapter.contract.INSTALL + r"\PartyOps.exe", "created": "2026-10-17T06:00:00.123456Z"}]}
        self.receipt = {"status": "restored-ready", "candidate": self.binding, "attempt_sha256": "submit-attempt",
            "user_mode": "personal", "pending_mode_switch": False, "user_journal_present": False,
            "original_personal_data_directory": adapter.PERSONAL_DATA, "runtime": self.runtime,
            "services": [{"name": name, "state": "Stopped", "start_mode": "Manual"}
                         for name in ("PartyOpsHost", "PartyOpsUpdateService")]}

    def test_successful_restore_and_same_runtime_allow_existing_personal_instance(self):
        previous = adapter.restored_personal(self.receipt, self.binding, "submit-attempt")
        current = copy.deepcopy(self.runtime); current["processes"][0]["created"] = "2026-10-17T06:00:00.1234560Z"
        adapter.same_restored_runtime(current, previous, self.binding)

    def test_rollback_failed_wrong_candidate_or_directory_cannot_authorize_stage(self):
        for key, value in (("status", "host-first-run-failed"), ("status", "passed"), ("candidate", {}),
                           ("attempt_sha256", "old-attempt"), ("original_personal_data_directory", r"C:\Other"),
                           ("pending_mode_switch", True), ("user_journal_present", True), ("user_mode", "host")):
            with self.subTest(key=key, value=value), self.assertRaises(RuntimeError):
                adapter.restored_personal({**self.receipt, key: value}, self.binding, "submit-attempt")
        for field, value in (("state", "Running"), ("start_mode", "Auto")):
            receipt = copy.deepcopy(self.receipt); receipt["services"][0][field] = value
            with self.assertRaises(RuntimeError): adapter.restored_personal(receipt, self.binding, "submit-attempt")

    def test_changed_runtime_or_http_owner_is_rejected(self):
        for field, value in (("pid", 999), ("created", "2026-10-17T06:01:00Z"), ("path", r"C:\Other\PartyOps.exe")):
            current = copy.deepcopy(self.runtime); current["processes"][0][field] = value
            if field == "pid": current["http_owners"] = [value]
            with self.subTest(field=field), self.assertRaises(RuntimeError):
                adapter.same_restored_runtime(current, self.runtime, self.binding)
        for key, value in (("http_owners", [999]), ("boot_id", "2026-10-17T07:00:00Z"), ("manifest_sha256", "d" * 64)):
            with self.subTest(key=key), self.assertRaises(RuntimeError):
                adapter.same_restored_runtime({**self.runtime, key: value}, self.runtime, self.binding)

    def test_generic_observed_completion_cannot_replace_bound_live_completion(self):
        forged = {**self.receipt, "completion_kind": "read-only-observed-after-submit", "http_terminal_observed": False,
                  "original_data_directory_verified": True, "system_mode": "personal", "submit_attempt_sha256": "submit-attempt"}
        with self.assertRaises(RuntimeError): adapter.restored_personal(forged, self.binding, "submit-attempt")


if __name__ == "__main__": unittest.main()
