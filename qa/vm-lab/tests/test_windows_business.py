"""Windows 验收 HTTP 连接不能落到宿主或外部网站。"""
import importlib.util
import json
from pathlib import Path
from types import SimpleNamespace

import pytest


@pytest.fixture
def driver():
    path = Path(__file__).resolve().parents[1] / "scripts/exercise-windows-business.py"
    spec = importlib.util.spec_from_file_location("windows_business_test", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def test_install_preparation_reboot_requires_bound_install_and_separate_pointer(driver, monkeypatch, tmp_path):
    import windows_win7_standard

    calls = []
    bound = {"package": {"sha256": "current"}, "environment": {"uuid": "win7"}}
    monkeypatch.setattr(driver.restart, "fingerprint", lambda: "current-source")
    monkeypatch.setattr(windows_win7_standard, "installation_binding",
                        lambda *args: calls.append(args) or bound)
    lab = SimpleNamespace(root=tmp_path)
    report = tmp_path / "install"
    context, scope, pointer = driver.restart.restart_context(lab, "win7-x64", report)
    assert context == bound and calls == [(lab, "win7-x64", report, "current-source")]
    assert scope == "guest-shutdown-cold-start-for-standard-user-preparation"
    assert pointer == "standard-user-reboot-"
    assert not (tmp_path / "state/business-win7-x64.json").exists()


def test_business_reboot_keeps_its_original_binding_and_pointer(driver, tmp_path):
    (tmp_path / "state").mkdir()
    (tmp_path / "state/business-win10-x64.json").write_text(json.dumps({"context": {"business": "current"}}))
    context, scope, pointer = driver.restart.restart_context(SimpleNamespace(root=tmp_path), "win10-x64")
    assert context == {"business": "current"}
    assert scope == "guest-shutdown-cold-start-not-full-lifecycle" and pointer == "reboot-"


def test_original_win7_prerequisite_reboot_has_no_product_pass_binding(driver, monkeypatch, tmp_path):
    lab = SimpleNamespace(root=tmp_path, state=lambda target: {"restore_generation": "current"})
    monkeypatch.setattr(driver.restart, "runtime_binding", lambda *args: {"uuid": "original-win7"})
    context, scope, pointer = driver.restart.restart_context(lab, "win7-x64", prerequisites=True)
    assert context == {"target": "win7-x64", "environment": {"uuid": "original-win7"}, "restore_generation": "current"}
    assert scope == "original-win7-prerequisite-cold-start" and pointer == "prerequisite-reboot-"
    with pytest.raises(RuntimeError, match="PREREQUISITE_REBOOT_REQUIRED"):
        driver.restart.restart_context(lab, "win11-x64-native", prerequisites=True)
    with pytest.raises(RuntimeError, match="PREREQUISITE_REBOOT_REQUIRED"):
        driver.restart.restart_context(lab, "win7-x64", tmp_path, prerequisites=True)


def test_guest_http_uses_verified_ssh_transport(driver):
    calls = []
    channel = SimpleNamespace(settimeout=lambda value: calls.append(("timeout", value)))
    transport = SimpleNamespace(open_channel=lambda *args, **kwargs: calls.append((args, kwargs)) or channel)
    connection = driver.GuestHTTPConnection("127.0.0.1:18775", transport=transport, timeout=240)
    connection.connect()
    assert connection.sock.channel is channel
    assert calls[0][0] == ("direct-tcpip", ("127.0.0.1", 18775), ("127.0.0.1", 0))


@pytest.mark.parametrize("host", ["example.com", "10.0.2.2", "localhost"])
def test_guest_http_rejects_other_hosts_before_connecting(driver, host):
    transport = SimpleNamespace(open_channel=lambda *args, **kwargs: pytest.fail("不能连接错误目标"))
    connection = driver.GuestHTTPConnection(host, transport=transport)
    with pytest.raises(RuntimeError, match="ONLY_GUEST_LOOPBACK_HTTP_ALLOWED"):
        connection.connect()


def test_http_close_does_not_discard_unread_guest_response(driver):
    import io
    closed = []
    channel = SimpleNamespace(makefile=lambda *args: io.BytesIO(b"actual guest response body"),
                              close=lambda: closed.append(True))
    sock = driver.TunnelSocket(channel)
    stream = sock.makefile("rb")
    sock.close()
    assert closed == []
    assert stream.read() == b"actual guest response body"
    stream.close()
    assert closed == [True]


@pytest.mark.parametrize("owner,session", [("Administrator", 1), ("partyopsuser", 0)])
def test_partial_configuration_rejects_other_process_owner(driver, owner, session):
    info = {"administrator": False, "username": "partyopsuser", "desktop": [{"session_id": 1}],
            "processes": [{"name": "PartyOps.exe", "owner": owner, "session_id": session}]}
    with pytest.raises(RuntimeError, match="PROCESS_OWNER_CHANGED"):
        driver.WindowsRun.require_configuration_owner(info)


def reboot_receipt():
    return {"context": {"package": "current"}, "status": "passed", "actual": True,
            "qemu_pid_before": 11, "qemu_pid_after": 22,
            "identity_before": {"boot_id": "before"}, "identity_after": {"boot_id": "after"},
            "shutdown_event": {"event": "SHUTDOWN", "data": {"guest": True, "reason": "guest-shutdown"}}}


def test_windows_reboot_requires_normal_power_cycle_beyond_changed_clock(driver):
    receipt = reboot_receipt()
    driver.restart.validate_restart(receipt, {"package": "current"}, {"pid": 22})
    receipt.pop("shutdown_event")
    with pytest.raises(RuntimeError, match="NORMAL_GUEST_SHUTDOWN_NOT_PROVEN"):
        driver.restart.validate_restart(receipt, {"package": "current"}, {"pid": 22})


@pytest.mark.parametrize("acknowledged", [True, False])
def test_win7_normal_shutdown_does_not_force_or_retry_on_disconnect(driver, monkeypatch, acknowledged):
    calls = []

    def run_ps(script):
        calls.append(script)
        assert "& $shutdown /s /t 0" in script
        assert "& $shutdown /s /f" not in script
        if not acknowledged:
            raise RuntimeError("WINRM_TRANSPORT_FAILED:connection closed")
        return SimpleNamespace(status_code=0, std_out=b"shutdown-requested")

    result = driver.restart.request_win7_shutdown(SimpleNamespace(run_ps=run_ps), "win7-x64",
                                                 "406ef8ee-e342-405f-933f-7ddce0a55f1b")
    assert len(calls) == 1
    assert result["command_acknowledged"] is acknowledged
    assert result["arguments"] == ["/s", "/t", "0"]
    assert "shutdown_event" not in result


def test_win7_shutdown_prepares_transport_before_exclusive_qmp_listener(driver, monkeypatch, tmp_path):
    sequence = []
    state = {"pid": 11, "qmp_port": 12345, "uuid": "406ef8ee-e342-405f-933f-7ddce0a55f1b",
             "restore_generation": "same", "acceleration": "tcg"}
    environment = {"vm_uuid": state["uuid"]}
    context = {"environment": environment, "restore_generation": "same"}
    (tmp_path / "reports/win7-x64").mkdir(parents=True)
    lab = SimpleNamespace(root=tmp_path, matrix={"targets": {"win7-x64": {"os": "windows"}}},
                          state=lambda target: state.copy(), start=lambda *args: state.update(pid=22))
    responses = iter([{"QMP": {}}, {"return": {}},
                      {"event": "SHUTDOWN", "data": {"guest": True, "reason": "guest-shutdown"}}])
    stream = SimpleNamespace(readline=lambda: (json.dumps(next(responses)) + "\n").encode(),
                             write=lambda data: sequence.append(json.loads(data)["execute"]), flush=lambda: None)

    class Socket:
        def __enter__(self):
            sequence.append("qmp-exclusive")
            return self

        def __exit__(self, *args):
            pass

        def makefile(self, mode):
            return stream

        def settimeout(self, value):
            pass

    def connect(*args):
        assert "qmp-exclusive" not in sequence
        sequence.append("verified-winrm")

        def run_ps(script):
            assert sequence[-1] == "qmp_capabilities"
            sequence.append("normal-guest-shutdown")
            return SimpleNamespace(status_code=0, std_out=b"shutdown-requested")

        return SimpleNamespace(run_ps=run_ps)

    monkeypatch.setattr(driver.restart, "winrm_session", connect)
    monkeypatch.setattr(driver.restart.socket, "create_connection", lambda *args, **kwargs: Socket())
    monkeypatch.setattr(driver.restart, "probe", lambda *args: {"boot_id": "before"})
    monkeypatch.setattr(driver.restart, "runtime_binding", lambda *args: environment)
    monkeypatch.setattr(driver.restart, "restart_context", lambda *args: (context, "unit", "standard-user-reboot-"))
    monkeypatch.setattr(driver.restart, "wait_stopped", lambda *args: None)
    monkeypatch.setattr(driver.restart, "wait_probe", lambda *args: {"boot_id": "after"})
    result = driver.restart.reboot("win7-x64", lab=lab)
    assert sequence == ["verified-winrm", "qmp-exclusive", "qmp_capabilities", "normal-guest-shutdown"]
    assert result["status"] == "passed" and result["qemu_pid_after"] == 22


@pytest.mark.parametrize("field,value,message", [
    ("qemu_pid_before", 22, "NEW_OWNED_QEMU_PROCESS_REQUIRED"),
    ("qemu_pid_after", 33, "NEW_OWNED_QEMU_PROCESS_REQUIRED"),
    ("context", {"package": "obsolete"}, "REBOOT_CONTEXT_CHANGED"),
    ("actual", False, "GUEST_REBOOT_NOT_COMPLETED"),
    ("identity_after", {"boot_id": "before"}, "GUEST_BOOT_ID_CHANGE_REQUIRED"),
])
def test_windows_reboot_rejects_process_restart_or_foreign_receipt(driver, field, value, message):
    receipt = reboot_receipt()
    receipt[field] = value
    with pytest.raises(RuntimeError, match=message):
        driver.restart.validate_restart(receipt, {"package": "current"}, {"pid": 22})


@pytest.mark.parametrize("field,message", [
    ("embedding_loaded", "EMBEDDING_UNAVAILABLE_AFTER_INFERENCE"),
    ("embedding_available", "EMBEDDING_UNAVAILABLE_AFTER_INFERENCE"),
    ("llm_running", "LOCAL_LLM_UNAVAILABLE_AFTER_INFERENCE"),
    ("llm_available", "LOCAL_LLM_UNAVAILABLE_AFTER_INFERENCE"),
])
def test_single_answer_does_not_pass_models_if_runtime_is_paused(driver, field, message):
    status = {"embedding_loaded": True, "embedding_available": True,
              "llm_running": True, "llm_available": True}
    driver.WindowsRun.require_models_available(status)
    status[field] = False
    with pytest.raises(RuntimeError, match=message):
        driver.WindowsRun.require_models_available(status)


@pytest.mark.parametrize("remote,path", [
    ("lifecycle-012345abcdef", r"C:\Users\partyopsuser\Documents\PartyOps QA\lifecycle-012345abcdef\..\other"),
    ("lifecycle-012345abcdef", r"C:\Users\partyopsuser\Documents"),
    ("lifecycle-ffffffffffff", r"C:\Users\partyopsuser\Documents\PartyOps QA\lifecycle-012345abcdef\中文 空格业务数据"),
    ("..", r"C:\Users\partyopsuser\Documents\PartyOps QA\..\中文 空格业务数据"),
])
def test_windows_cleanup_rejects_unowned_or_foreign_lifecycle_directory(driver, remote, path):
    with pytest.raises(RuntimeError, match="UNREGISTERED_LIFECYCLE_DIRECTORY|TEST_DATA_CLEANUP_OUTSIDE_OWNED_WORK"):
        driver.validate_test_data_path(remote, path)


def test_windows_cleanup_accepts_only_registered_exact_chinese_path(driver):
    path = r"C:\Users\partyopsuser\Documents\PartyOps QA\lifecycle-012345abcdef\中文 空格业务数据"
    assert driver.validate_test_data_path("lifecycle-012345abcdef", path) == path


@pytest.mark.parametrize("field,value,message", [
    ("exit_code", 1, "WINDOWS_UNINSTALLER_FAILED"),
    ("registered", True, "UNINSTALL_REGISTRATION_REMAINS"),
    ("program_exists", True, "UNINSTALL_LEFT_EXECUTABLE"),
    ("services", ["PartyOpsHost"], "UNINSTALL_SERVICES_REMAIN"),
    ("processes", [{"Name": "PartyOpsWizard.exe"}], "UNINSTALL_LEFT_INSTALLED_PROGRAM_RUNNING"),
])
def test_uninstall_exit_zero_does_not_override_real_residue(driver, field, value, message):
    result = {"exit_code": 0, "registered": False, "program_exists": False, "services": [], "processes": []}
    driver.verify_uninstalled(result)
    result[field] = value
    with pytest.raises(RuntimeError, match=message):
        driver.verify_uninstalled(result)


def test_reinstall_rejects_changed_package_before_starting_desktop(driver, monkeypatch):
    run = driver.WindowsRun.__new__(driver.WindowsRun)
    run.state = {"uninstall_preserved": {"package_sha256": "old", "data": {}}}
    run.package = {"sha256": "new"}
    monkeypatch.setattr(driver.windows, "exercise", lambda *args, **kwargs: pytest.fail("不得安装不同哈希的包"))
    with pytest.raises(RuntimeError, match="SAME_PACKAGE_REINSTALL_BINDING_MISSING"):
        run.same_package_reinstall()


def test_test_data_removal_requires_verified_reinstall(driver):
    run = driver.WindowsRun.__new__(driver.WindowsRun)
    run.state = {"business_verified": True}
    with pytest.raises(RuntimeError, match="REINSTALL_BUSINESS_PRECONDITION_MISSING"):
        run.uninstall_remove_test_data()


def test_windows_lifecycle_powershell_scripts_parse_without_guest_execution(driver, tmp_path):
    import ast
    import base64
    import os
    import subprocess

    if os.name != "nt":
        pytest.skip("只在 Windows 宿主使用官方 PowerShell 语法解析器")
    tree = ast.parse(Path(driver.__file__).read_text(encoding="utf-8"))
    scripts = [node.args[0].value for node in ast.walk(tree)
               if isinstance(node, ast.Call) and isinstance(node.func, ast.Attribute)
               and node.func.attr == "lifecycle_command" and node.args
               and isinstance(node.args[0], ast.Constant)]
    for index, script in enumerate(scripts):
        path = tmp_path / f"lifecycle-{index}.ps1"
        path.write_text(driver.GUEST_OWNERSHIP + script, encoding="utf-8-sig")
    directory = str(tmp_path).replace("'", "''")
    command = "$ErrorActionPreference='Stop'; $failures=@(); Get-ChildItem -LiteralPath '" + directory + "' -Filter '*.ps1' | ForEach-Object { $tokens=$null; $errors=$null; [void][Management.Automation.Language.Parser]::ParseFile($_.FullName,[ref]$tokens,[ref]$errors); $failures+=@($errors) }; if($failures.Count) { $failures | Out-String | Write-Output; exit 1 }"
    result = subprocess.run(["powershell.exe", "-NoProfile", "-NonInteractive", "-EncodedCommand",
                             base64.b64encode(command.encode("utf-16le")).decode("ascii")],
                            capture_output=True, text=True, timeout=30, check=False)
    assert result.returncode == 0, result.stdout + result.stderr
