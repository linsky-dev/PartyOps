"""个人恢复控制器离线测试；不连接Guest、不关闭进程、不提交配置。"""
import copy
import importlib.util
import json
import subprocess
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import MagicMock, patch


def load(name, file):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(file))
    result = importlib.util.module_from_spec(spec); spec.loader.exec_module(result); return result


adapter = load("restore_personal_test_adapter", "restore-personal-qa.py")
first = load("restore_personal_test_first", "first-host-qa.py")
host = load("restore_personal_test_host", "win10-host-wizard-qa.py")
form = load("restore_personal_test_form", "win10-host-form-qa.py")
transition = load("restore_personal_test_transition", "runtime-transition.py")


class PersonalRestoreTests(unittest.TestCase):
    def setUp(self):
        self.binding = {"manifest_sha256": "a" * 64, "executable_sha256": "b" * 64, "wizard_sha256": "c" * 64}

    def test_host_receipt_must_be_successful_and_bound(self):
        value = {"candidate": self.binding, "attempt_sha256": "digest", "case": "approved-existing-root",
            "status": "bootstrap-page-observed", "http_status": 200, "bootstrap_admin_page": True,
            "public_error_codes": [], "host_status": {"present": True, "stage": "ready", "code": "", "pid": 3724},
            "user_mode": "host", "system_mode": "host", "qa_host_data_directory": first.ROOT_HOST_DATA,
            "original_personal_restore_directory": first.PERSONAL_DATA}
        adapter.host_success(value, self.binding, "digest", first)
        for key, other in (("http_status", 500), ("bootstrap_admin_page", False), ("status", "host-first-run-failed"),
                           ("attempt_sha256", "other"), ("user_mode", "personal"), ("public_error_codes", ["ADMIN_CHILD_FAILED"])):
            with self.assertRaises(RuntimeError): adapter.host_success({**value, key: other}, self.binding, "digest", first)

    def test_personal_form_only_no_admin_or_host_csrf(self):
        page = '<title>党建智办 · 重新配置运行角色</title><form id="personal-form"><input name="csrf" value="personal_test_1234567890"><input name="mode" value="personal"></form><form id="host-form"><input name="csrf" value="ignored_host_secret"><input name="mode" value="host"></form>'
        self.assertEqual(adapter.personal_csrf(page, form.HostForm), "personal_test_1234567890")
        for invalid in (page.replace("重新配置运行角色", "首次配置"), page.replace('value="personal"', 'value="bootstrap_admin"'),
                        '<title>党建智办 · 创建首位管理员</title><form id="admin-form"><input name="csrf" value="secret"></form>'):
            with self.assertRaises(RuntimeError): adapter.personal_csrf(invalid, form.HostForm)

    def test_prepare_only_closes_bound_wizard_and_uses_lua_reconfigure_personal(self):
        script = adapter.prepare_script(host, first, transition, self.binding, "2026-10-17T07:00:00Z",
            {"name": "PartyOpsWizard.exe", "pid": 5872, "created": "2026-10-17T05:25:05.406386Z"})
        for expected in ("--reconfigure --initial-role personal", ".RunLevel=0", ".LogonType=3",
                         "Test-CimCreationInstant $handle.StartTime $target.created", "PRODUCT_PROCESS_CHANGED_DURING_PREPARE"):
            self.assertIn(expected, script)
        for forbidden in ("Stop-Service", "Stop-Process", "--privileged-", "Start-Service", "Remove-Item"):
            self.assertNotIn(forbidden, script)
        parser = "$s=[Console]::In.ReadToEnd();$e=$null;$t=$null;[void][Management.Automation.Language.Parser]::ParseInput($s,[ref]$t,[ref]$e);if($e.Count){$e|ForEach-Object {$_.Message};exit 1}"
        for source in (script, adapter.SERVICES):
            result = subprocess.run(["powershell", "-NoProfile", "-NonInteractive", "-Command", parser],
                                    input=source, encoding="utf-8", capture_output=True)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_personal_ready_requires_cleanup_services_and_same_http_owner(self):
        service = {"services": [{"name": name, "state": "Stopped", "start_mode": "Manual"}
                                 for name in ("PartyOpsHost", "PartyOpsUpdateService")]}
        snapshot = {"user_mode": "personal", "system_mode": "personal", "pending_mode_switch": False,
            "user_journal_present": False, "session_id": 1, "processes": [{"name": "PartyOps.exe", "pid": 100,
                "created": "2026-10-17T07:01:00Z", "installed_path_matches": True, "owner_is_original_user": True, "session_id": 1}]}
        actual = {**self.binding, "processes": [{"pid": 100, "path": adapter.contract.INSTALL + r"\PartyOps.exe",
            "created": "2026-10-17T07:01:00Z"}], "http_owners": [100], "boot_id": "2026-10-17T05:00:00Z"}
        adapter.runtime_ready(snapshot, service, actual, self.binding, "2026-10-17T07:00:00Z")
        for key, value in (("pending_mode_switch", True), ("user_journal_present", True), ("system_mode", "host")):
            with self.assertRaises(RuntimeError): adapter.runtime_ready({**snapshot, key: value}, service, actual, self.binding, "2026-10-17T07:00:00Z")
        wrong = copy.deepcopy(service); wrong["services"][0]["state"] = "Running"
        with self.assertRaises(RuntimeError): adapter.runtime_ready(snapshot, wrong, actual, self.binding, "2026-10-17T07:00:00Z")
        with self.assertRaises(RuntimeError): adapter.runtime_ready(snapshot, service, {**actual, "http_owners": [99]}, self.binding, "2026-10-17T07:00:00Z")

    def original_runtime(self):
        service = {"services": [{"name": name, "state": "Stopped", "start_mode": "Manual"}
                                 for name in ("PartyOpsHost", "PartyOpsUpdateService")]}
        snapshot = {"user_mode": "personal", "system_mode": "personal", "pending_mode_switch": False,
            "user_journal_present": False, "session_id": 1, "processes": [{"name": "PartyOps.exe", "pid": 2448,
                "created": "2026-10-17T06:00:00.1234560Z", "installed_path_matches": True,
                "owner_is_original_user": True, "session_id": 1}]}
        actual = {**self.binding, "processes": [{"pid": 2448, "path": adapter.contract.INSTALL + r"\PartyOps.exe",
            "created": "2026-10-17T06:00:00.123456Z"}], "http_owners": [2448], "boot_id": "2026-10-17T05:00:00Z"}
        return snapshot, service, actual

    def test_bound_original_instance_can_be_reused_before_prepare(self):
        snapshot, service, actual = self.original_runtime()
        bound = adapter.personal_instance(snapshot, actual, self.binding)
        adapter.runtime_ready(snapshot, service, actual, self.binding, "2026-10-17T07:00:00Z", bound)

    def test_personal_and_receipted_host_service_child_can_coexist_before_action(self):
        snapshot, services, actual = self.original_runtime()
        host_process = {"pid": 3724, "path": adapter.contract.INSTALL + r"\PartyOps.exe", "created": "2026-10-17T06:10:00Z"}
        actual["processes"].append(host_process)
        snapshot["processes"].append({"name": "PartyOps.exe", "pid": 3724, "created": host_process["created"],
            "installed_path_matches": True, "owner_is_original_user": False, "session_id": 0})
        for row in services["services"]: row.update(state="Running", pid=3596 if row["name"] == "PartyOpsHost" else 300)
        services["host_process_tree"] = [
            {"pid": 3596, "parent_pid": 500, "path": adapter.contract.INSTALL + r"\PartyOpsService.exe", "created": "2026-10-17T06:09:59Z"},
            {**host_process, "parent_pid": 3596}]
        bound = adapter.personal_instance(snapshot, actual, self.binding, services, 3724)
        self.assertEqual(bound["pid"], 2448)
        # 恢复后不准继续忽略Host实例，即使它有合法服务祖先。
        for row in services["services"]: row.update(state="Stopped", start_mode="Manual")
        with self.assertRaises(RuntimeError):
            adapter.runtime_ready(snapshot, services, actual, self.binding, "2026-10-17T07:00:00Z", bound)
        for row in services["services"]: row.update(state="Running")
        for mutation in ("wrong-parent", "wrong-host-pid", "foreign-wrapper", "recycled-parent", "stopped-service", "missing-tree"):
            bad = copy.deepcopy(services); host_pid = 3724
            if mutation == "wrong-parent": bad["host_process_tree"][1]["parent_pid"] = 999
            if mutation == "wrong-host-pid": host_pid = 999
            if mutation == "foreign-wrapper": bad["host_process_tree"][0]["path"] = r"C:\Other\PartyOpsService.exe"
            if mutation == "recycled-parent": bad["host_process_tree"][0]["created"] = "2026-10-17T06:11:00Z"
            if mutation == "stopped-service": bad["services"][0]["state"] = "Stopped"
            if mutation == "missing-tree": bad["host_process_tree"] = []
            with self.subTest(mutation=mutation), self.assertRaises(RuntimeError):
                adapter.personal_instance(snapshot, actual, self.binding, bad, host_pid)

    def test_old_instance_without_binding_or_changed_identity_is_rejected(self):
        snapshot, service, actual = self.original_runtime()
        bound = adapter.personal_instance(snapshot, actual, self.binding)
        with self.assertRaises(RuntimeError):
            adapter.runtime_ready(snapshot, service, actual, self.binding, "2026-10-17T07:00:00Z")
        for field, value in (("pid", 999), ("created", "2026-10-17T06:01:00+00:00"),
                             ("session_id", 2), ("owner_is_original_user", False), ("http_owners", [999]),
                             ("candidate", {**self.binding, "manifest_sha256": "d" * 64})):
            with self.subTest(field=field), self.assertRaises(RuntimeError):
                adapter.runtime_ready(snapshot, service, actual, self.binding, "2026-10-17T07:00:00Z", {**bound, field: value})
        changed = copy.deepcopy(snapshot); changed["processes"][0]["owner_is_original_user"] = False
        with self.assertRaisesRegex(RuntimeError, "PERSONAL_HTTP_OWNER_REQUIRED"):
            adapter.runtime_ready(changed, service, actual, self.binding, "2026-10-17T07:00:00Z", bound)

    def test_real_host_only_preflight_allows_null_original_binding(self):
        # 只读实际采集回执：Host PID3724、服务3596、18775无监听；不会连接Guest。
        fixture = json.loads(Path(__file__).with_name("restore-personal-preflight-inspect.json").read_text(encoding="utf-8"))
        actual, snapshot, services = fixture["runtime"], fixture["snapshot"], fixture["services"]
        binding = {key: actual[key] for key in ("manifest_sha256", "executable_sha256", "wizard_sha256")}
        self.assertIsNone(adapter.personal_instance(snapshot, actual, binding, services, 3724))
        for mutation in ("host-pid", "candidate", "owner", "parent", "service", "extra-process"):
            runtime = copy.deepcopy(actual); state = copy.deepcopy(services)
            if mutation == "host-pid": runtime["processes"][0]["pid"] = 999
            if mutation == "candidate": runtime["manifest_sha256"] = "d" * 64
            if mutation == "owner": runtime["http_owners"] = [999]
            if mutation == "parent": state["host_process_tree"][1]["parent_pid"] = 999
            if mutation == "service": state["services"][0]["state"] = "Stopped"
            if mutation == "extra-process": runtime["processes"].append({**runtime["processes"][0], "pid": 999})
            with self.subTest(mutation=mutation), self.assertRaises(RuntimeError):
                adapter.personal_instance(snapshot, runtime, binding, state, 3724)
        # null不授予旧实例复用例外，提交后必须是动作之后的新个人实例。
        personal, stopped, runtime = self.original_runtime()
        with self.assertRaises(RuntimeError):
            adapter.runtime_ready(personal, stopped, runtime, self.binding, "2026-10-17T07:00:00Z", None)
        runtime["processes"][0]["created"] = personal["processes"][0]["created"] = "2026-10-17T07:01:00Z"
        adapter.runtime_ready(personal, stopped, runtime, self.binding, "2026-10-17T07:00:00Z", None)

    def test_replacement_instance_must_start_after_action(self):
        snapshot, service, actual = self.original_runtime()
        bound = adapter.personal_instance(snapshot, actual, self.binding)
        for created, passes in (("2026-10-17T06:59:59Z", False), ("2026-10-17T07:01:00Z", True)):
            current = copy.deepcopy(actual); current["processes"][0].update(pid=3000, created=created); current["http_owners"] = [3000]
            observed = copy.deepcopy(snapshot); observed["processes"][0].update(pid=3000, created=created)
            if passes: adapter.runtime_ready(observed, service, current, self.binding, "2026-10-17T07:00:00Z", bound)
            else:
                with self.assertRaises(RuntimeError):
                    adapter.runtime_ready(observed, service, current, self.binding, "2026-10-17T07:00:00Z", bound)

    def test_ssh_cleanup_failure_is_failed_receipt_and_exception(self):
        client = MagicMock(); client.close.side_effect = OSError("synthetic failure")
        record = {"status": "restored-ready"}; saved = []
        with patch.object(adapter.contract, "exclusive", side_effect=lambda _, value: saved.append(copy.deepcopy(value))):
            with self.assertRaisesRegex(RuntimeError, "SSH_CLOSE_FAILED"): adapter.finish(client, record, Path("unused.json"))
        self.assertEqual(saved[0]["status"], "failed")
        self.assertEqual(saved[0]["cleanup_error"], "SSH_CLOSE_FAILED")

    def test_recovery_only_launches_missing_task_and_reports_safe_stage_codes(self):
        fixture = json.loads(Path(__file__).with_name("restore-personal-preflight-inspect.json").read_text(encoding="utf-8"))
        script = adapter.recovery_script(host, transition, self.binding, "2026-10-17T07:00:00Z", fixture["runtime"], fixture["services"])
        for expected in ("RECOVERY_REQUIRES_NO_WIZARD", "RESTORE_TASK_ALREADY_EXISTS", "RECOVERY_HOST_PROCESS_CHANGED",
                         "--reconfigure --initial-role personal", ".RunLevel=0", ".LogonType=3",
                         "RESTORE_QA_TASK_REGISTER_FAILED", "hresult=[int]$_.Exception.HResult"):
            self.assertIn(expected, script)
        for forbidden in (".Kill(", "CloseMainWindow", "Stop-Service", "Stop-Process", "Remove-Item", "--privileged-", "csrf"):
            self.assertNotIn(forbidden, script)
        parser = "$s=[Console]::In.ReadToEnd();$e=$null;$t=$null;[void][Management.Automation.Language.Parser]::ParseInput($s,[ref]$t,[ref]$e);if($e.Count){$e|ForEach-Object {$_.Message};exit 1}"
        result = subprocess.run(["powershell", "-NoProfile", "-NonInteractive", "-Command", parser],
                                input=script, encoding="utf-8", capture_output=True)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(adapter.public_error(RuntimeError("RESTORE_TASK_ALREADY_EXISTS")), "RESTORE_TASK_ALREADY_EXISTS")
        self.assertEqual(adapter.public_error(RuntimeError("synthetic sensitive body csrf=hidden")), "RESTORE_QA_OPERATION_FAILED")

    def test_recovery_requires_failed_prepare_bound_to_original_attempt(self):
        failure = {"status": "failed", "candidate": self.binding, "attempt_sha256": "restore-personal-prepare.attempt.json",
                   "original_personal_instance": None}
        intent = {"candidate": self.binding, "original_personal_instance": None}
        def read(path): return failure if path.name.endswith("result.json") else intent
        with patch.object(adapter.contract, "read", side_effect=read), patch.object(adapter.contract, "digest", side_effect=lambda p: p.name):
            links = adapter.recovery_links(self.binding)
            self.assertEqual(links["failed_prepare_sha256"], "restore-personal-prepare.result.json")
            for field, value in (("status", "prepared"), ("candidate", {}), ("attempt_sha256", "other"),
                                 ("original_personal_instance", {"pid": 2448})):
                saved = copy.deepcopy(failure); failure[field] = value
                with self.subTest(field=field), self.assertRaises(RuntimeError): adapter.recovery_links(self.binding)
                failure.clear(); failure.update(saved)

    def test_observation_uses_guest_task_time_and_existing_wizard_despite_host_skew(self):
        fixture = json.loads(Path(__file__).with_name("restore-personal-task-after-timeout.json").read_text(encoding="utf-8"))
        expected = next(p for p in fixture["processes"] if p["Name"] == "PartyOpsWizard.exe")
        snapshot = {"username": "partyopsuser", "administrator": False, "session_id": 1,
            "processes": [{"name": "PartyOpsWizard.exe", "pid": expected["ProcessId"], "created": expected["created"],
                "session_id": 1, "installed_path_matches": True, "owner_is_original_user": True}],
            "listeners": [{"pid": expected["ProcessId"], "port": 49733, "loopback": True, "owned_by_wizard": True}]}
        task = {"name": "PartyOps-QA-Restore-Personal-20261017", "state": 4, "run_level": 0, "logon_type": 3,
            "user": r"QA-WIN10\partyopsuser", "original_user_matches": True, "last_run": fixture["tasks"][0]["last_run"],
            "actions": [{"path": adapter.contract.INSTALL + r"\PartyOpsWizard.exe", "working_directory": adapter.contract.INSTALL,
                         "arguments": "--reconfigure --initial-role personal"}]}
        self.assertEqual(adapter.observe_recovery(snapshot, task, expected, host)["pid"], 340)
        # 官方COM可返回本地短用户名，身份由SID解析确认；实际开始可晚于调度请求。
        actual_task = {**task, "user": "partyopsuser", "last_run": "2026-10-17T06:28:45Z"}
        self.assertEqual(adapter.observe_recovery(snapshot, actual_task, expected, host)["pid"], 340)
        with self.assertRaises(RuntimeError): host.validate_wizard(snapshot, "2026-10-17T06:30:08Z")
        for field, value in (("run_level", 1), ("original_user_matches", False), ("last_run", "2026-10-17T06:29:00Z")):
            with self.subTest(field=field), self.assertRaises(RuntimeError):
                adapter.observe_recovery(snapshot, {**task, field: value}, expected, host)
        parser = "$s=[Console]::In.ReadToEnd();$e=$null;$t=$null;[void][Management.Automation.Language.Parser]::ParseInput($s,[ref]$t,[ref]$e);if($e.Count){exit 1}"
        for script in (adapter.GUEST_CLOCK, adapter.TASK_OBSERVE):
            result = subprocess.run(["powershell", "-NoProfile", "-NonInteractive", "-Command", parser], input=script, encoding="utf-8", capture_output=True)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            for forbidden in (".Run(", "Set-Date", "RegisterTaskDefinition", "Stop-Process", ".Kill("):
                self.assertNotIn(forbidden, script)

    def test_completion_sources_bind_actual_submitted_failure_without_resubmitting(self):
        # 只读本地实际中断回执；并不据此声明Guest当前完成或生成成功证据。
        path = Path(__file__).with_name("restore-personal-submit.result.json")
        failed = json.loads(path.read_text(encoding="utf-8")); binding = failed["candidate"]
        values, links = adapter.completion_sources(binding)
        self.assertEqual(values["failed_submit"]["status"], "failed")
        self.assertEqual(links["failed_submit_sha256"], adapter.contract.digest(path))
        original = adapter.contract.read
        for field, value in (("error_code", "OTHER_FAILURE"), ("attempt_sha256", "unbound"),
                             ("status", "restored-ready"), ("states", [{"status": "failed", "code": "RUNTIME_CONFIGURATION_INVALID"}])):
            def changed(file):
                row = original(file)
                return {**row, field: value} if Path(file) == path else row
            with self.subTest(field=field), patch.object(adapter.contract, "read", side_effect=changed), self.assertRaises(RuntimeError):
                adapter.completion_sources(binding)


if __name__ == "__main__": unittest.main()
