"""本机验收驱动的隔离边界与共享 HTTP 业务；不读取真实账号、不连接真实产品。"""
import copy
import hashlib
import importlib.util
import io
import json
import ntpath
import urllib.error
import urllib.parse
import zipfile
from pathlib import Path
from types import SimpleNamespace

import pytest


@pytest.fixture
def driver():
    path = Path(__file__).resolve().parents[1] / "scripts/exercise-windows-native-business.py"
    spec = importlib.util.spec_from_file_location("native_business_test", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


@pytest.mark.parametrize("run,work", [
    (r"C:\native-test", r"E:\test"),
    (r"D:\PartyOps-VM-Lab\reports\win11-x64-native\native-one", r"E:\PartyOps1\PartyOps"),
    (r"D:\PartyOps-VM-Lab\reports\win11-x64-native\native-one\..\native-two", r"E:\test"),
    (r"D:\PartyOps-VM-Lab\reports\win11-x64-native\native-one", r"\\server\share\test"),
])
def test_foreign_or_ambiguous_workspace_is_rejected(driver, run, work):
    with pytest.raises(RuntimeError, match="NATIVE_"):
        driver.validate_paths(run, work)


def test_data_requires_exact_registered_run_and_random_chinese_leaf(driver):
    run = ntpath.join(driver.REPORT_ROOT, "native-one")
    work = ntpath.join(driver.WORK_ROOT, "native-one", driver.ACCOUNT)
    assert driver.validate_paths(run, work) == work
    data = ntpath.join(work, "中文 空格业务数据-" + "a" * 32)
    assert driver.validate_data_path(work, data) == data
    for wrong in [ntpath.join(work, "中文 空格业务数据"), ntpath.join(work, "真实业务"),
                  data.replace("native-one", "native-two")]:
        with pytest.raises(RuntimeError, match="NATIVE_DATA_DIRECTORY_MISMATCH"):
            driver.validate_data_path(work, wrong)


def listener():
    return {"username": "PartyOpsNativeQA", "sid": "S-1-5-21-1-2-3-1001", "enabled": True, "administrator": False,
            "listeners": [{"address": "127.0.0.1", "port": 18825, "pid": 42, "owner_sid": "S-1-5-21-1-2-3-1001",
                           "session_id": 2, "executable_path": r"E:\PartyOps1\PartyOps\PartyOps.exe",
                           "sha256": "a" * 64, "created_at": "2026-09-08T09:00:00.0000000Z"}]}


@pytest.mark.parametrize("field,value,message", [
    ("owner_sid", "S-1-5-21-1-2-3-500", "OWNER_MISMATCH"),
    ("session_id", 0, "OWNER_MISMATCH"),
    ("executable_path", r"E:\old\PartyOps.exe", "INSTALLED_PROCESS_MISMATCH"),
    ("sha256", "b" * 64, "INSTALLED_PROCESS_MISMATCH"),
    ("address", "0.0.0.0", "NOT_LOOPBACK"),
    ("pid", 99, "PID_CHANGED"),
    ("created_at", "later", "PROCESS_REPLACED"),
])
def test_loopback_health_does_not_override_process_identity(driver, field, value, message):
    snapshot = listener()
    snapshot["listeners"][0][field] = value
    with pytest.raises(RuntimeError, match=message):
        driver.validate_listener(snapshot, {"sid": snapshot["sid"], "session_id": 2},
                                 r"E:\PartyOps1\PartyOps\PartyOps.exe", "a" * 64, 18825, 42,
                                 "2026-09-08T09:00:00.0000000Z")


def test_administrator_or_duplicate_listener_is_rejected(driver):
    snapshot = listener()
    account = {"sid": snapshot["sid"], "session_id": 2}
    for fault in ("administrator", "duplicate"):
        bad = copy.deepcopy(snapshot)
        if fault == "administrator":
            bad["administrator"] = True
        else:
            bad["listeners"] *= 2
        with pytest.raises(RuntimeError, match="STANDARD_ACCOUNT_CHANGED|LISTENER_NOT_UNIQUE"):
            driver.validate_listener(bad, account, snapshot["listeners"][0]["executable_path"], "a" * 64, 18825)


@pytest.mark.parametrize("url", ["https://127.0.0.1:18825", "http://localhost:18825", "http://user:pw@127.0.0.1:18825",
                                "http://127.0.0.1:18825/?token=secret", "http://example.com:18825"])
def test_noncanonical_origins_fail_closed(driver, url):
    with pytest.raises(RuntimeError, match="NATIVE_HTTP_ORIGIN_REJECTED"):
        driver.loopback_origin(url)


def bare_run(driver, tmp_path, phase="business"):
    run = driver.NativeRun.__new__(driver.NativeRun)
    run.args = SimpleNamespace(phase=phase, port=18825)
    run.reports = tmp_path / "reports"
    run.reports.mkdir()
    run.state_path = tmp_path / "private.json"
    run.state = {"username": "nativeqa_synthetic", "password": "synthetic-secret-never-public",
                 "user_id": "admin", "data_dir": str(tmp_path / ("中文 空格业务数据-" + "a" * 32))}
    run.report = {"status": "failed", "phase": phase, "runtime_environment_passed": False, "checks": []}
    run.origin = "http://127.0.0.1:18825"
    run.wizard_origin = "http://127.0.0.1:18826"
    run.package = {"version": "1.4.5-rc.6"}
    run.cookies = driver.http.cookiejar.CookieJar()
    run.persist = lambda: driver.write_json(run.state_path, run.state)
    return run


def retained_fixture():
    context = {"target": "win11-x64-native", "host_id": "host", "account_sid": "ordinary-sid",
               "data_dir": r"E:\owned\data", "package_sha256": "new", "source_fingerprint": "new-source",
               "executable_sha256": "new-exe"}
    package = {"sha256": "old", "source_fingerprint": "old-source"}
    install = {"package": package, "installer_exit_code": 0, "installed_executable_sha256": "old-exe"}
    previous = {"context": {**context, "package_sha256": "old", "source_fingerprint": "old-source", "executable_sha256": "old-exe"},
                "username": "nativeqa_fixture", "password": "synthetic-only", "user_id": "admin", "data_dir": context["data_dir"],
                "task_id": "old-task", "task_title": "保留旧合成事项", "attachment_id": "old-attachment", "attachment_sha256": "a" * 64,
                "runtime_process": {"pid": 999}, "cookies": "old-cookie", "ticket": "old-ticket", "session_token": "old-token",
                "business_verified": True, "revalidation_verified": True, "ocr": "passed", "models": "passed"}
    return previous, context, package, install


def test_retained_import_preserves_objects_but_never_reuses_old_process_session_or_passes(driver):
    previous, context, package, install = retained_fixture()
    before = copy.deepcopy(previous)
    imported = driver.import_retained_state(previous, context, package, install, "ordinary-sid", context["data_dir"])
    assert imported["username"] == previous["username"] and imported["password"] == previous["password"]
    assert imported["retained_business"]["task_id"] == "old-task"
    assert imported["context"] == context and previous == before
    assert not {"runtime_process", "cookies", "ticket", "session_token", "business_verified", "revalidation_verified",
                "task_id", "ocr", "models"}.intersection(imported)


@pytest.mark.parametrize("field", ["host_id", "account_sid", "data_dir", "package_sha256", "source_fingerprint", "executable_sha256"])
def test_retained_import_rejects_unrelated_old_run_binding(driver, field):
    previous, context, package, install = retained_fixture()
    previous["context"][field] = "foreign"
    with pytest.raises(RuntimeError, match="NATIVE_RETAINED_STATE_BINDING_MISMATCH"):
        driver.import_retained_state(previous, context, package, install, "ordinary-sid", context["data_dir"])


@pytest.mark.parametrize("fault", ["none", "pid", "created_at", "owner_sid", "sha256"])
def test_revalidation_guard_requires_fresh_runtime_receipt_before_http(driver, tmp_path, monkeypatch, fault):
    run = bare_run(driver, tmp_path)
    snapshot = listener()
    actual = snapshot["listeners"][0]
    run.account = {"sid": snapshot["sid"], "session_id": actual["session_id"]}
    run.runtime_receipt = copy.deepcopy(actual)
    run.executable = actual["executable_path"]
    run.install = {"installed_executable_sha256": actual["sha256"]}
    run.data_dir = str(tmp_path)
    run.configuration_values = dict
    if fault == "pid":
        actual["pid"] += 1
    elif fault == "created_at":
        actual["created_at"] = "2026-09-09T01:00:00Z"
    elif fault == "owner_sid":
        actual["owner_sid"] = "other-account"
    elif fault == "sha256":
        actual["sha256"] = "old-package"
    monkeypatch.setattr(driver, "read_json", lambda _: {"format_version": 1, "pid": actual["pid"], "executable": run.executable})
    run.inspect_listener = lambda _: snapshot
    if fault == "none":
        assert run.guard_url(run.origin + "/api/v1/health") == actual
    else:
        with pytest.raises(RuntimeError, match="NATIVE_"):
            run.guard_url(run.origin + "/api/v1/health")
        assert "runtime_process" not in run.state


def test_identity_guard_runs_before_any_http_or_login_secret(driver, tmp_path):
    run = bare_run(driver, tmp_path)
    run.guard_url = lambda url: (_ for _ in ()).throw(RuntimeError("NATIVE_LISTENER_OWNER_MISMATCH"))
    run.opener = SimpleNamespace(open=lambda *args, **kwargs: pytest.fail("不得向错误实例发送登录密码"))
    with pytest.raises(RuntimeError, match="OWNER_MISMATCH"):
        run.login()


def test_redirect_cannot_escape_verified_instance(driver, tmp_path):
    run = bare_run(driver, tmp_path)
    run.inspect_listener = lambda port: pytest.fail("其他地址在进程检查前就应拒绝")
    redirect = driver.GuardedRedirect(run)
    request = driver.urllib.request.Request(run.origin + "/")
    with pytest.raises(RuntimeError, match="DESTINATION_REJECTED"):
        redirect.redirect_request(request, None, 302, "Found", {}, "http://127.0.0.1:18775/")


class FakeHTTP:
    """模拟安装版的 HTTP 边界，备份真复制合成状态并回滚，断言仍来自共享业务实现。"""
    def __init__(self, run, *, broken_restore=False):
        self.run = run
        self.calls = []
        self.tasks, self.users, self.attachment = {}, {}, b""
        self.session = "admin"
        self.backup = None
        self.broken_restore = broken_restore
        self.configured = False
        self.backup_bytes = b"synthetic-complete-backup"

    def open(self, request, timeout):
        parsed = urllib.parse.urlsplit(request.full_url)
        path = parsed.path + (("?" + parsed.query) if parsed.query else "")
        method = request.get_method()
        raw = request.data
        data = json.loads(raw) if raw and request.headers.get("Content-type") == "application/json" else raw
        self.calls.append((parsed.port, path, method, data))
        content_type = "application/json"
        if parsed.port == 18826:
            content_type = "text/html"
            if method == "GET":
                value = b'<input name="csrf" value="synthetic-csrf">'
            else:
                values = urllib.parse.parse_qs(raw.decode())
                assert values["csrf"] == ["synthetic-csrf"]
                if values["mode"] == ["personal"]:
                    assert values["data_dir"] == [self.run.data_dir]
                    assert values["port"] == ["18825"]
                    self.run.state["runtime_process"] = {"pid": 42, "owner_sid": "ordinary"}
                    value = b'<input name="mode" value="bootstrap_admin">'
                else:
                    assert values["username"] == [self.run.state["username"]]
                    assert values["password"] == [self.run.state["password"]]
                    self.configured = True
                    value = b"configured"
        elif path == "/api/v1/health":
            value = {"status": "ok", "app_version": "1.4.5-rc.6"}
        elif path == "/api/v1/bootstrap/status":
            value = {"configured": self.configured}
        elif path == "/api/v1/auth/login":
            self.session = next((key for key, user in self.users.items() if user["username"] == data["username"]), "admin")
            value = {"ok": True}
        elif path == "/api/v1/auth/me":
            value = self.users.get(self.session, {"id": "admin", "username": self.run.state["username"]})
        elif path == "/api/v1/admin/users":
            value = {"id": "staff", "username": data["username"]}
            self.users["staff"] = value
        elif path == "/api/v1/tasks" and method == "POST":
            value = {"id": "task-" + str(len(self.tasks) + 1), "version": 1, **data}
            self.tasks[value["id"]] = value
        elif path.endswith("/materials/quick-upload"):
            self.attachment = raw.split(b"\r\n\r\n")[-1].rsplit(b"\r\n--", 1)[0]
            value = {"materials": [{"versions": [{"id": "attachment"}]}]}
        elif path == "/api/v1/attachments/attachment/download":
            value, content_type = self.attachment, "application/octet-stream"
        elif path.startswith("/api/v1/exports/"):
            buffer = io.BytesIO()
            with zipfile.ZipFile(buffer, "w") as archive:
                archive.writestr("synthetic.xml", "合成导出")
            value, content_type = buffer.getvalue(), "application/octet-stream"
        elif path == "/api/v1/backups" and method == "POST":
            self.backup = copy.deepcopy(self.tasks)
            value = {"id": "backup", "status": "completed"}
        elif path == "/api/v1/admin/backups/backup/verify":
            value = {"valid": True, "sha256": hashlib.sha256(self.backup_bytes).hexdigest()}
        elif path == "/api/v1/backups/backup/download":
            value, content_type = self.backup_bytes, "application/octet-stream"
        elif path == "/api/v1/admin/backups/restore?backup_id=backup":
            if not self.broken_restore:
                self.tasks = copy.deepcopy(self.backup)
            value = {"restored": True}
        elif path.endswith("/comments"):
            value = {"id": "comment", "mentioned_user_ids": data["mentioned_user_ids"]}
        elif path.endswith("/actions"):
            value = self.tasks[path.split("/")[-2]]
        elif path == "/api/v1/tasks/my-work-summary":
            value = {"collaborating": 1, "reviewing": 1, "step_assigned": 1}
        elif path == "/api/v1/tasks?scope=reviewing":
            value = {"items": list(self.tasks.values())}
        elif path == "/api/v1/notifications?unread_only=true":
            value = [{"entity_id": list(self.tasks)[-1], "notification_type": "mention"}]
        elif path.startswith("/api/v1/tasks/"):
            identifier = path.rsplit("/", 1)[-1]
            if identifier not in self.tasks:
                raise urllib.error.HTTPError(request.full_url, 404, "missing", {}, io.BytesIO(b"not found"))
            value = self.tasks[identifier]
        else:
            pytest.fail("未预期的 HTTP 接口: " + path)
        body = json.dumps(value).encode() if content_type == "application/json" else value
        response = io.BytesIO(body)
        response.headers = {"Content-Type": content_type}
        return response


def attach_fake(run, fake):
    run.guarded = []
    run.guard_url = lambda url: run.guarded.append(url)
    run.opener = fake
    run.make_opener = lambda cookies: fake


def test_shared_business_and_collaboration_execute_http_assertions(driver, tmp_path):
    run = bare_run(driver, tmp_path)
    fake = FakeHTTP(run)
    attach_fake(run, fake)
    run.run()
    assert run.state["business_verified"] is True
    assert run.report["runtime_environment_passed"] is False
    assert len(run.guarded) == len(fake.calls)
    assert len(fake.tasks) == 1  # 恢复后新增事项必须消失。
    run.args.phase = "collaboration-business"
    run.run()
    assert run.state["collaboration_account"]["id"] == "staff"
    checks = {item["id"]: item for item in run.report["checks"]}
    assert checks["collaboration-independent-staff-session"]["result"]["separate_machine_enrollment_verified"] is False
    public = "".join(path.read_text(encoding="utf-8") for path in run.reports.glob("*.json"))
    assert run.state["password"] not in public
    assert run.state["collaboration_account"]["password"] not in public


def test_successful_restore_response_without_actual_rollback_fails(driver, tmp_path):
    run = bare_run(driver, tmp_path)
    attach_fake(run, FakeHTTP(run, broken_restore=True))
    with pytest.raises(RuntimeError, match="BACKUP_RESTORE_DID_NOT_ROLL_BACK_CHANGE"):
        run.run()
    assert run.report["status"] == "failed"
    assert run.report["runtime_environment_passed"] is False


def test_first_configuration_uses_real_form_contract_and_private_synthetic_admin(driver, tmp_path):
    run = bare_run(driver, tmp_path, "configure")
    run.state = {}
    run.context = {"package_sha256": "current"}
    run.config = tmp_path / "new-user-config"
    run.config.mkdir()
    run.data_dir = str(tmp_path / ("中文 空格业务数据-" + "a" * 32))
    run.assert_no_system_role_change = lambda: None
    run.require_unused_personal_ports = lambda: None
    fake = FakeHTTP(run)
    attach_fake(run, fake)
    run.run()
    assert fake.configured and run.state["user_id"] == "admin"
    assert json.loads(run.state_path.read_text(encoding="utf-8"))["password"] == run.state["password"]
    assert run.state["password"] not in (run.reports / "configure.json").read_text(encoding="utf-8")
    assert [call[2] for call in fake.calls if call[0] == 18826] == ["GET", "POST", "POST"]


def test_existing_user_configuration_cannot_be_overwritten(driver, tmp_path):
    run = bare_run(driver, tmp_path, "configure")
    run.state = {}
    run.config = tmp_path / "existing-config"
    run.config.mkdir()
    (run.config / "personal.env").write_text("PARTYOPS_DATA_DIR=E:/real-business", encoding="utf-8")
    run.request = lambda *args, **kwargs: pytest.fail("已有配置时不能提交向导")
    with pytest.raises(RuntimeError, match="FIRST_CONFIGURATION_REQUIRES_CLEAN_USER"):
        run.configure()


def test_guest_lifecycle_methods_are_not_exposed(driver):
    for name in ("desktop_start", "after_reboot", "uninstall_keep", "uninstall_remove_test_data", "after_reinstall"):
        assert not hasattr(driver.NativeRun, name)
    assert "after-reboot" not in driver.PHASES


@pytest.mark.parametrize("field,value,message", [
    ("is_admin", True, "STANDARD_PROBE_NOT_PASSED"),
    ("sid", "S-1-5-18", "LOCAL_SID_REQUIRED"),
    ("app_sha256", "obsolete", "BINDING_MISMATCH"),
    ("install_result_sha256", "obsolete", "BINDING_MISMATCH"),
    ("data_directory", r"E:\existing-business", "BINDING_MISMATCH"),
    ("hardware_uuid", "other-host", "HOST_MISMATCH"),
    ("local_appdata", r"C:\Users\Administrator\AppData\Local", "PROFILE_MISMATCH"),
])
def test_receipts_cannot_import_admin_old_package_or_other_host(driver, monkeypatch, field, value, message):
    run = driver.NativeRun.__new__(driver.NativeRun)
    run.run_directory = Path(ntpath.join(driver.REPORT_ROOT, "native-one"))
    run.work = Path(ntpath.join(driver.WORK_ROOT, "native-one", driver.ACCOUNT))
    run.data_dir = ntpath.join(str(run.work), "中文 空格业务数据-" + "a" * 32)
    run.install = {"installed_executable_sha256": "a" * 64}
    run.machine = {"hardware_uuid": "same-host", "machine_name": "same-name"}
    monkeypatch.setattr(driver, "sha256", lambda path: "receipt-digest")
    receipt = {"status": "passed", "username": driver.ACCOUNT, "sid": "S-1-5-21-1-2-3-1001", "is_admin": False,
               "session_id": 2, "native_workspace": str(run.work), "run_directory": str(run.run_directory),
               "data_directory": run.data_dir, "app_sha256": "a" * 64, "install_result_sha256": "receipt-digest",
               "hardware_uuid": "same-host", "machine_name": "same-name",
               "user_profile": r"C:\Users\PartyOpsNativeQA", "local_appdata": r"C:\Users\PartyOpsNativeQA\AppData\Local"}
    run.validate_identity(receipt)
    receipt[field] = value
    with pytest.raises(RuntimeError, match=message):
        run.validate_identity(receipt)


def test_http_error_body_is_not_copied_to_public_report(driver, tmp_path):
    run = bare_run(driver, tmp_path)
    run.guard_url = lambda url: None
    def fail(*args, **kwargs):
        raise urllib.error.HTTPError(run.origin, 400, "failed", {}, io.BytesIO(run.state["password"].encode()))
    run.opener = SimpleNamespace(open=fail)
    with pytest.raises(RuntimeError, match="HTTP_STATUS_400"):
        run.run()
    public = (run.reports / "business.json").read_text(encoding="utf-8")
    assert run.state["password"] not in public
    assert not list(run.reports.glob("*-http-error.txt"))


def test_listener_powershell_parses_without_host_execution(driver, tmp_path):
    import base64
    import os
    import subprocess
    if os.name != "nt":
        pytest.skip("仅使用 Windows PowerShell 的语法解析器")
    script = tmp_path / "readonly-listener.ps1"
    script.write_text(driver.LISTENER_PROBE, encoding="utf-8-sig")
    path = str(script).replace("'", "''")
    command = "$tokens=$null;$errors=$null;[void][Management.Automation.Language.Parser]::ParseFile('" + path + "',[ref]$tokens,[ref]$errors);if($errors.Count){exit 1}"
    result = subprocess.run(["powershell.exe", "-NoProfile", "-NonInteractive", "-EncodedCommand",
                             base64.b64encode(command.encode("utf-16le")).decode()], capture_output=True, timeout=30, check=False)
    assert result.returncode == 0


def test_windows_powershell_uses_its_own_acl_module(driver, tmp_path, monkeypatch):
    import os
    if os.name != "nt":
        pytest.skip("需要真实 Windows PowerShell 模块加载器")
    # 模拟宿主 PowerShell 7 传入无效模块路径，子进程仍必须从自己的系统模块加载 Get-Acl。
    monkeypatch.setenv("PSModulePath", str(tmp_path / "foreign-module-path"))
    value = driver.powershell(
        "$acl=Get-Acl -LiteralPath $request.path;"
        "[ordered]@{exists=($null -ne $acl);major=$PSVersionTable.PSVersion.Major;"
        "module=(Get-Command Get-Acl).Module.ModuleBase}|ConvertTo-Json -Compress",
        {"path": str(tmp_path)},
    )
    assert value["exists"] is True
    assert value["major"] == 5
    assert "windows\\system32\\windowspowershell\\v1.0" in value["module"].casefold()


def test_process_timestamp_accepts_json_trailing_zero_normalization_only(driver):
    snapshot = listener()
    snapshot["listeners"][0]["created_at"] = "2026-09-08T02:52:59.9983800Z"
    args = (snapshot, {"sid": snapshot["sid"], "session_id": 2},
            r"E:\PartyOps1\PartyOps\PartyOps.exe", "a" * 64, 18825, 42)
    driver.validate_listener(*args, created_at="2026-09-08T02:52:59.99838Z")
    for changed in ("2026-09-08T02:52:59.9983801Z", "2026-09-08T02:52:58.99838Z", "invalid"):
        with pytest.raises(RuntimeError, match="PROCESS_REPLACED"):
            driver.validate_listener(*args, created_at=changed)


def test_native_models_respect_host_memory_before_loading(driver, monkeypatch):
    run = driver.NativeRun.__new__(driver.NativeRun)
    monkeypatch.setattr(driver, "powershell", lambda *args: {"free_bytes": 10 * 1024**3})
    monkeypatch.setattr(driver.windows_runtime.WindowsRun, "models", lambda self: pytest.fail("内存不足仍启动模型"))
    with pytest.raises(RuntimeError, match="NATIVE_MODELS_HOST_MEMORY_RESERVE"):
        run.models()


@pytest.mark.parametrize("foreign", [False, True])
def test_formatter_probe_binds_same_user_and_never_exports_ticket(driver, tmp_path, foreign):
    run = bare_run(driver, tmp_path, "formatter-probe")
    snapshot = listener()
    actual = snapshot["listeners"][0]
    actual["port"] = 18768
    run.account = {"sid": snapshot["sid"], "session_id": 2}
    run.executable = actual["executable_path"]
    run.install = {"installed_executable_sha256": actual["sha256"]}
    run.state["runtime_process"] = copy.deepcopy(actual)
    if foreign:
        actual["owner_sid"] = "S-1-5-21-1-2-3-500"
    run.wait_health = lambda: None
    run.login = lambda: None
    calls = []
    def request(path, method, payload):
        calls.append(path)
        return {"local_base_url": "http://127.0.0.1:18768", "ticket": "private-ticket-never-public"}
    run.request = request
    run.inspect_listener = lambda port: snapshot
    if foreign:
        with pytest.raises(RuntimeError, match="NATIVE_LISTENER_OWNER_MISMATCH"):
            run.run()
    else:
        run.run()
    assert calls == ["/api/v1/official-format/local-ticket"]
    public = "".join(path.read_text(encoding="utf-8") for path in run.reports.glob("*.json"))
    assert "private-ticket-never-public" not in public
    assert run.report["runtime_environment_passed"] is False


@pytest.mark.parametrize("fails", [False, True])
def test_native_model_report_never_claims_guest_network_isolation(driver, monkeypatch, fails):
    run = driver.NativeRun.__new__(driver.NativeRun)
    run.report = {}
    monkeypatch.setattr(driver, "powershell", lambda *args: {"free_bytes": 12 * 1024**3})
    def shared(instance):
        instance.report["network"] = "Guest QEMU restrict=on"
        if fails:
            raise RuntimeError("MODEL_NOT_READY")
    monkeypatch.setattr(driver.windows_runtime.WindowsRun, "models", shared)
    if fails:
        with pytest.raises(RuntimeError, match="MODEL_NOT_READY"):
            run.models()
    else:
        run.models()
    assert run.report["network"] == {"environment": "native-host", "host_outbound_isolation_verified": False,
                                     "external_provider_consent": False}
