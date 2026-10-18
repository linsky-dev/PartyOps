"""首次主机 QA 的离线契约验证，不连接 Guest、不提交任何表单。"""
import importlib.util
import copy
import io
import json
import subprocess
import unittest
from contextlib import ExitStack
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import MagicMock, patch


def load(name, filename):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(filename))
    result = importlib.util.module_from_spec(spec); spec.loader.exec_module(result); return result


adapter = load("first_host_qa", "first-host-qa.py")
host = load("first_host_test_identity", "win10-host-wizard-qa.py")
transition = load("first_host_test_transition", "runtime-transition.py")
form = load("first_host_test_form", "win10-host-form-qa.py")


class FirstHostTests(unittest.TestCase):
    def setUp(self):
        self.binding = {"manifest_sha256": "a" * 64, "wizard_sha256": "b" * 64}
        self.parser = adapter.first_form_parser(form.HostForm)

    def page(self, title="首次配置", selected="10.0.2.15"):
        return '<title>党建智办 · ' + title + '</title><form id="host-form"><input name="csrf" value="test_csrf_1234567890"><input name="mode" value="host"><select name="host">' + \
            '<option value="" disabled>请选择</option><option value="10.0.2.15" ' + ('selected' if selected == "10.0.2.15" else '') + '>实际地址</option>' + \
            '<option value="127.0.0.1" ' + ('selected' if selected == "127.0.0.1" else '') + '>本机</option></select></form>'

    def test_actual_address_and_page_default_are_used_without_wildcard_rewrite(self):
        self.assertEqual(adapter.choose_first_form(self.page(), "10.0.2.15", self.parser)[1], "10.0.2.15")
        self.assertEqual(adapter.choose_first_form(self.page(selected="127.0.0.1"), "default", self.parser)[1], "127.0.0.1")
        for page, address in ((self.page("重新配置运行角色"), "10.0.2.15"),
                              (self.page().replace('value="10.0.2.15"', 'value="10.0.2.16"'), "10.0.2.15"),
                              (self.page(), "0.0.0.0")):
            with self.assertRaises(RuntimeError): adapter.choose_first_form(page, address, self.parser)

    def test_admin_page_detection_does_not_read_admin_csrf_or_account(self):
        parsed = self.parser()
        parsed.feed('<form id="admin-form"><input name="mode" value="bootstrap_admin"><input name="csrf" value="private"></form>')
        self.assertTrue(parsed.bootstrap_admin)
        self.assertIsNone(parsed.csrf)
        parsed.feed('<span id="diagnostic-text">[BIND_ADDRESS_UNAVAILABLE] 私有诊断正文</span>')
        self.assertEqual(parsed.codes, ["BIND_ADDRESS_UNAVAILABLE"])
        self.assertNotIn("私有诊断正文", str(parsed.__dict__))

    def test_negative_case_binding_is_exact(self):
        attempt = {"candidate": self.binding, "wizard_pid": 612, "wizard_created": adapter.OLD_CREATED}
        result = {"attempt_sha256": "digest", "status": "recorded", "unexpected_ready": False,
                  "states": [{"status": "failed", "code": "RUNTIME_CONFIGURATION_INVALID"}]}
        adapter.negative_binding(attempt, result, self.binding, "digest")
        for change in ({"states": [{"status": "ready", "code": "RUNTIME_READY"}]}, {"attempt_sha256": "other"}):
            with self.assertRaises(RuntimeError): adapter.negative_binding(attempt, {**result, **change}, self.binding, "digest")
        with self.assertRaises(RuntimeError): adapter.negative_binding({**attempt, "wizard_pid": 613}, result, self.binding, "digest")

    def test_prepare_only_targets_bound_qa_wizard_and_launches_lua_first_run(self):
        script = adapter.prepare_script(host, transition, self.binding, "2026-10-17T06:00:00Z",
                                        {"pid": 100, "created": "2026-10-17T05:00:00Z"})
        self.assertIn("--initial-role host", script)
        self.assertNotIn("--reconfigure", script)
        self.assertIn(".RunLevel=0", script)
        self.assertIn(".LogonType=3", script)
        self.assertIn("Test-CimCreationInstant $handle.StartTime $target.created", script)
        self.assertIn("PERSONAL_PROCESS_CHANGED_DURING_PREPARE", script)
        self.assertNotIn("Stop-Process", script)
        self.assertNotIn("--privileged-", script)
        parser = "$s=[Console]::In.ReadToEnd();$e=$null;$t=$null;[void][Management.Automation.Language.Parser]::ParseInput($s,[ref]$t,[ref]$e);if($e.Count){$e|ForEach-Object {$_.Message};exit 1}"
        result = subprocess.run(["powershell", "-NoProfile", "-NonInteractive", "-Command", parser],
                                input=script, encoding="utf-8", capture_output=True)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def run_prepare_failure(self, *, final_preservation_failure=False, close_failure=False):
        """模拟完整 prepare 流程直到收尾；所有传输/文件回执均为内存替身。"""
        client = MagicMock()
        client.open_sftp.return_value.__enter__.return_value.file.return_value.__enter__.return_value = io.StringIO(
            json.dumps({"business_verified": True, "data_dir": adapter.PERSONAL_DATA}))
        if close_failure: client.close.side_effect = OSError("synthetic close failure")
        personal = {"pid": 100, "created": "2026-10-17T05:00:00Z"}
        original = {"name": "PartyOpsWizard.exe", "pid": adapter.OLD_PID, "created": adapter.OLD_CREATED,
                    "installed_path_matches": True, "owner_is_original_user": True, "session_id": 1}
        snapshot = {"pending_mode_switch": False, "user_journal_present": False, "session_id": 1,
                    "processes": [original, {"name": "PartyOps.exe", "pid": 100, "owner_is_original_user": True, "session_id": 1}]}
        fresh = {"processes": [{**original, "pid": 700, "created": "2026-10-17T06:00:01Z"}]}
        identity = SimpleNamespace(LAB_ROOT=Path("D:/PartyOps-VM-Lab"), HOST_DATA=host.HOST_DATA,
            transfer=MagicMock(side_effect=[snapshot, {"launch_requested": True}]), validate_wizard=MagicMock(),
            inspect_script=MagicMock(return_value="# synthetic read-only probe"))
        preserved = {"baseline_sha256": "same"}
        values = [(preserved, {}), (preserved, {}), ({"baseline_sha256": "changed"} if final_preservation_failure else preserved, {})]
        retention = SimpleNamespace(preserved_inputs=MagicMock(side_effect=values))
        windows = SimpleNamespace(load_configuration=lambda: ({"defaults": {}, "targets": {"win10-x64": {}}}, {}),
            QemuLab=lambda *_: object(), qmp=MagicMock(), windows=SimpleNamespace(connect=MagicMock(return_value=client),
            execute=MagicMock(return_value=json.dumps(fresh))))
        modules = {"first_host_identity": identity, "first_host_form_helpers": SimpleNamespace(),
                   "first_host_retention": retention, "first_host_windows": windows}
        negative_attempt = {"candidate": self.binding, "wizard_pid": adapter.OLD_PID, "wizard_created": adapter.OLD_CREATED}
        negative_result = {"attempt_sha256": "digest", "status": "recorded", "unexpected_ready": False,
                           "states": [{"status": "failed", "code": "RUNTIME_CONFIGURATION_INVALID"}]}
        def read(path):
            return {"host-form-invalid-address.attempt.json": negative_attempt,
                    "host-form-invalid-address.result.json": negative_result,
                    "attempt.json": {"time_utc": "2026-10-17T04:00:00Z"}}[path.name]
        saved = {}
        def exclusive(path, value): saved[path.name] = copy.deepcopy(value)
        args = SimpleNamespace(action="prepare", package=Path("candidate.exe"), sha256="sha", address="10.0.2.15")
        with ExitStack() as patches:
            patches.enter_context(patch.object(adapter, "load_module", side_effect=lambda _path, name: modules[name]))
            patches.enter_context(patch.object(adapter, "prepare_script", return_value="# synthetic prepare"))
            patches.enter_context(patch.object(adapter, "inspection_script", return_value="# synthetic read-only probe"))
            patches.enter_context(patch.object(adapter.Path, "exists", return_value=False))
            for name, replacement in {"candidate": MagicMock(return_value=self.binding), "completed_install": MagicMock(),
                    "read": MagicMock(side_effect=read), "digest": MagicMock(return_value="digest"),
                    "bind_guest": MagicMock(), "probe": MagicMock(return_value={"processes": [personal]}),
                    "runtime_matches": MagicMock(side_effect=lambda actual, *_: actual),
                    "exclusive": MagicMock(side_effect=exclusive), "now": MagicMock(return_value="2026-10-17T06:00:00Z")}.items():
                patches.enter_context(patch.object(adapter.contract, name, replacement))
            expected = "PRESERVED_BASELINE_CHANGED_AFTER_ACTION" if final_preservation_failure else "SSH_CLOSE_FAILED"
            with self.assertRaisesRegex(RuntimeError, expected): adapter.main_run(args)
        client.close.assert_called_once()
        self.assertEqual(identity.transfer.call_count, 2)
        return saved["first-host-prepare.result.json"]

    def test_final_preservation_failure_persists_failed_and_never_returns_prepared(self):
        record = self.run_prepare_failure(final_preservation_failure=True)
        self.assertEqual(record["status"], "failed")
        self.assertEqual(record["error_type"], "RuntimeError")
        self.assertIn("completed_at", record)

    def test_ssh_close_failure_persists_failed_and_never_returns_prepared(self):
        record = self.run_prepare_failure(close_failure=True)
        self.assertEqual(record["status"], "failed")
        self.assertEqual(record["cleanup_error"], "SSH_CLOSE_FAILED")
        self.assertIn("completed_at", record)

    def test_same_wizard_accepts_only_equivalent_cim_instants(self):
        expected = {"name": "PartyOpsWizard.exe", "pid": 5872, "created": "2026-10-17T05:25:05.4063860Z",
                    "installed_path_matches": True, "owner_is_original_user": True, "session_id": 1}
        self.assertTrue(adapter.same_wizard({**expected, "created": "2026-10-17T05:25:05.406386Z"}, expected))
        for key, value in (("created", "2026-10-17T05:25:05.406387Z"), ("pid", 5873), ("session_id", 0),
                           ("owner_is_original_user", False), ("created", "2026-10-17T05:25:05.406386")):
            self.assertFalse(adapter.same_wizard({**expected, key: value}, expected))

    def test_root_recovery_requires_exact_failed_profile_case(self):
        value = {"candidate": self.binding, "attempt_sha256": "digest", "status": "host-first-run-failed",
                 "http_status": 400, "bootstrap_admin_page": False, "public_error_codes": ["ADMIN_CHILD_FAILED"],
                 "host_status": {"present": False}, "user_mode": "personal", "system_mode": "missing"}
        adapter.root_recovery_binding(value, self.binding, "digest")
        for key, other in (("status", "bootstrap-page-observed"), ("bootstrap_admin_page", True),
                           ("public_error_codes", []), ("attempt_sha256", "other"), ("system_mode", "host")):
            with self.assertRaises(RuntimeError): adapter.root_recovery_binding({**value, key: other}, self.binding, "digest")

    def test_root_path_rejects_profiles_and_requires_new_fixed_disk_without_reparse(self):
        for path in (adapter.PERSONAL_DATA, host.HOST_DATA, r"C:\Users\partyopsuser\host", r"D:\host", r"\\server\share"):
            with self.assertRaises(RuntimeError): adapter.root_path_script(path)
        script = adapter.root_path_script(adapter.ROOT_HOST_DATA)
        self.assertIn("$disk.DriveType -ne 3", script)
        self.assertIn("Test-Path -LiteralPath $full", script)
        self.assertIn("ReparsePoint", script)
        self.assertNotIn("New-Item", script)
        parser = "$s=[Console]::In.ReadToEnd();$e=$null;$t=$null;[void][Management.Automation.Language.Parser]::ParseInput($s,[ref]$t,[ref]$e);if($e.Count){$e|ForEach-Object {$_.Message};exit 1}"
        result = subprocess.run(["powershell", "-NoProfile", "-NonInteractive", "-Command", parser],
                                input=script, encoding="utf-8", capture_output=True)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_inspector_uses_new_root_data_without_changing_original_module(self):
        script = adapter.inspection_script(host, self.binding, "2026-10-17T06:00:00Z")
        self.assertIn(json.dumps(adapter.ROOT_HOST_DATA, ensure_ascii=False), script)
        self.assertNotIn(json.dumps(host.HOST_DATA, ensure_ascii=False), script)
        self.assertIn("Users", host.HOST_DATA)

    def approved_sources(self):
        preserved = {"baseline_sha256": "bound"}
        attempt = {"candidate": self.binding, "case": "root-host-directory", "qa_host_data_directory": adapter.ROOT_HOST_DATA,
                   "preserved_inputs": preserved, "time_utc": "2026-10-17T05:29:49Z"}
        result = {"candidate": self.binding, "case": "root-host-directory", "qa_host_data_directory": adapter.ROOT_HOST_DATA,
                  "attempt_sha256": "digest", "status": "host-first-run-failed", "http_status": 500,
                  "bootstrap_admin_page": False, "completed_at": "2026-10-17T05:32:58Z",
                  "directory_contract": {"fixed_disk": True, "directory_exists": False, "profile_path": False, "reparse_ancestor": False}}
        rollback = {"candidate": self.binding, "snapshot": {"pending_mode_switch": False, "user_journal_present": False,
                    "user_mode": "personal", "system_mode": "missing", "public_outcomes_since_launch": ["rollback"]},
                    "test_ca_sha256": "c" * 64, "host_configuration": "blocked-awaiting-ca-consent",
                    "live": {"services": [{"name": "PartyOpsHost", "state": "Stopped"}]}}
        baseline = {"retained": {"user_id": "synthetic", "attachment_sha256": "same"}, "calendar_id": "calendar", "calendar_sha256": "same"}
        retained = {**baseline, "candidate": self.binding, "status": "passed", "phase": "after-host-rollback",
                    "guest_uuid": adapter.contract.UUID, "disk": str(adapter.contract.DISK.resolve())}
        return attempt, result, rollback, retained, baseline, preserved

    def test_approved_existing_root_requires_complete_attempt_ca_rollback_retention_binding(self):
        attempt, result, rollback, retained, baseline, preserved = self.approved_sources()
        def validate(a=attempt, r=result, b=rollback, t=retained):
            return adapter.approved_existing_binding(a, r, b, t, baseline, self.binding, "digest", preserved)
        self.assertEqual(validate()["ca_sha256"], "c" * 64)
        for source, key, value in (("attempt", "qa_host_data_directory", adapter.PERSONAL_DATA),
                                   ("result", "attempt_sha256", "other"), ("result", "http_status", 200),
                                   ("rollback", "test_ca_sha256", ""), ("retained", "status", "failed"),
                                   ("retained", "retained", {"user_id": "other"})):
            values = {"a": attempt, "r": result, "b": rollback, "t": retained}
            target = {"attempt": "a", "result": "r", "rollback": "b", "retained": "t"}[source]
            values[target] = {**values[target], key: value}
            with self.assertRaises(RuntimeError): validate(**values)
        bad = copy.deepcopy(rollback); bad["snapshot"]["pending_mode_switch"] = True
        with self.assertRaises(RuntimeError): validate(b=bad)

    def test_existing_root_metadata_proof_fails_closed(self):
        provenance = {"ca_sha256": "c" * 64, "created_after": "2026-10-17T05:29:49Z", "created_before": "2026-10-17T05:32:58Z"}
        proof = {"ca_sha256": "c" * 64, "database_sha256": "d" * 64, "directory_exists": True,
                 "database_hardlinks": 1, **{key: True for key in ("fixed_disk", "single_qa_database", "database_header_valid",
                  "database_creation_bound", "host_services_stopped")}, "profile_path": False, "reparse_ancestor": False, "tree_reparse": False}
        adapter.validate_existing_root(proof, provenance)
        for key, value in (("ca_sha256", "e" * 64), ("database_hardlinks", 2), ("database_creation_bound", False),
                           ("tree_reparse", True), ("host_services_stopped", False), ("single_qa_database", False)):
            with self.assertRaises(RuntimeError): adapter.validate_existing_root({**proof, key: value}, provenance)
        script = adapter.existing_root_script(provenance)
        for required in ("fsutil.exe hardlink list", "CreationTimeUtc", "QA_PUBLIC_CA_PROVENANCE_MISMATCH", "SQLite format 3"):
            self.assertIn(required, script)
        for forbidden in ("Remove-Item", "New-Item", "sqlite3", "SELECT ", "Stop-Service", "Start-Service", "ca.key"):
            self.assertNotIn(forbidden, script)
        parser = "$s=[Console]::In.ReadToEnd();$e=$null;$t=$null;[void][Management.Automation.Language.Parser]::ParseInput($s,[ref]$t,[ref]$e);if($e.Count){$e|ForEach-Object {$_.Message};exit 1}"
        parsed = subprocess.run(["powershell", "-NoProfile", "-NonInteractive", "-Command", parser],
                                 input=script, encoding="utf-8", capture_output=True)
        self.assertEqual(parsed.returncode, 0, parsed.stdout + parsed.stderr)


if __name__ == "__main__": unittest.main()
