"""受保护主机配置事务：普通父进程禁止触碰数据，真正执行助手写入与恢复。"""
from __future__ import annotations

import json
import os
import shlex
import subprocess
from pathlib import Path

import pytest

from app import setup_wizard as wizard


@pytest.fixture
def transaction_lab(monkeypatch, tmp_path):
    class WindowsOS:
        name = "nt"
        def __getattr__(self, key):
            return getattr(os, key)
    monkeypatch.setattr(wizard, "os", WindowsOS())
    monkeypatch.setenv("PARTYOPS_ENVIRONMENT", "production")
    monkeypatch.setenv("PROGRAMDATA", str(tmp_path / "ProgramData"))
    config = tmp_path / "user"
    config.mkdir()
    data = tmp_path / "host-data"
    data.mkdir()
    personal = tmp_path / "personal-data"
    personal.mkdir()
    (personal / "business.txt").write_text("保留个人数据", encoding="utf-8")
    monkeypatch.setattr(wizard, "config_root", lambda: config)
    monkeypatch.setattr(wizard, "discover_lan_addresses", lambda: ["192.168.1.8", "192.168.1.9"])
    monkeypatch.setattr(wizard, "_assert_path_components_have_no_reparse_points", lambda *_a: None)
    monkeypatch.setattr(wizard, "_protect_windows_control_config", lambda *_a: None)
    state = {"admin": False, "deny_restore": False, "calls": [], "events": [], "restarts": [], "running": {"PartyOpsHost": True, "PartyOpsUpdateService": False}}
    monkeypatch.setattr(wizard, "windows_is_admin", lambda: state["admin"])
    def validate(path, **kwargs):
        assert state["admin"], "普通父进程不得验证受保护目录读写权限"
        if kwargs.get("check_access", True):
            path.mkdir(parents=True, exist_ok=True)
        return path.resolve()
    monkeypatch.setattr(wizard, "_validate_windows_data_dir", validate)
    def privileged_event(name):
        assert state["admin"]
        state["events"].append(name)
    monkeypatch.setattr(wizard, "_grant_windows_service_access", lambda *_a: privileged_event("acl"))
    monkeypatch.setattr(wizard, "_windows_service_start_config", lambda service: (2, service == "PartyOpsHost"))
    monkeypatch.setattr(wizard, "_windows_service_running", lambda service: state["running"][service])
    def stop():
        privileged_event("stop")
        previous = dict(state["running"])
        state["running"] = {service: False for service in previous}
        return previous
    monkeypatch.setattr(wizard, "_stop_windows_service_for_data_migration", stop)
    monkeypatch.setattr(wizard, "_enable_windows_host_service_autostart", lambda: privileged_event("enable"))
    monkeypatch.setattr(wizard, "_restore_windows_service_start_config", lambda *_a: privileged_event("restore-start"))
    def restore_services(states):
        privileged_event("restore-running")
        state["running"] = states
    monkeypatch.setattr(wizard, "_restore_windows_services_after_mode_switch", restore_services)
    monkeypatch.setattr(wizard, "_executable", lambda *_a: tmp_path / "PartyOpsWizard.exe")
    monkeypatch.setattr(wizard, "clear_windows_client_autostart", lambda: None)
    monkeypatch.setattr(wizard, "clear_windows_personal_autostart", lambda: None)
    monkeypatch.setattr(wizard, "_restore_previous_windows_autostart", lambda *_a: None)
    monkeypatch.setattr(wizard, "_stop_personal_process_for_data_migration", lambda *_a: True)
    def restart(environment):
        assert not state["admin"]
        assert not wizard._windows_host_switch_pending_path().exists()
        state["restarts"].append(environment)
    monkeypatch.setattr(wizard, "_restart_previous_personal_process", restart)
    (config / "personal.env").write_text(f"PARTYOPS_MODE=personal\nPARTYOPS_PORT=18775\nPARTYOPS_DATA_DIR={shlex.quote(str(personal))}\n", encoding="utf-8")
    (config / "mode.json").write_text('{"format_version":1,"mode":"personal"}', encoding="utf-8")
    paths = wizard._host_configuration_paths(data)
    paths["host_env"].parent.mkdir(parents=True)
    paths["host_env"].write_text(f"PARTYOPS_MODE=host\nPARTYOPS_HOST=192.168.1.8\nPARTYOPS_BIND_HOST=192.168.1.8\nPARTYOPS_DATA_DIR={shlex.quote(str(data))}\n", encoding="utf-8")
    paths["system_mode"].write_text('{"format_version":1,"mode":"host"}', encoding="utf-8")
    wizard._write_data_root_marker(data, "host")
    paths["network"].write_text('{"bind_host":"192.168.1.8","port":18765,"audit":"保留"}', encoding="utf-8")
    originals = {path: path.read_text(encoding="utf-8") for path in paths.values()}
    protected_roots = (data, tmp_path / "ProgramData" / "PartyOps-System")
    original_read, original_unlink, original_write = Path.read_text, Path.unlink, wizard._write_private
    def protected(path):
        return any(path == root or root in path.parents for root in protected_roots)
    def guarded_read(path, *args, **kwargs):
        if protected(path):
            assert state["admin"], f"普通父进程读取受保护文件：{path}"
        return original_read(path, *args, **kwargs)
    def guarded_unlink(path, *args, **kwargs):
        if protected(path):
            assert state["admin"], f"普通父进程删除受保护文件：{path}"
        return original_unlink(path, *args, **kwargs)
    def guarded_write(path, *args, **kwargs):
        if protected(path):
            assert state["admin"], f"普通父进程写入受保护文件：{path}"
        return original_write(path, *args, **kwargs)
    monkeypatch.setattr(Path, "read_text", guarded_read)
    monkeypatch.setattr(Path, "unlink", guarded_unlink)
    monkeypatch.setattr(wizard, "_write_private", guarded_write)
    def elevated(executable, arguments):
        state["calls"].append(arguments)
        if arguments[0] == "--privileged-restore-host" and state["deny_restore"]:
            return subprocess.CompletedProcess([], 1223, "", "PARTYOPS_ELEVATION_START_FAILED")
        state["admin"] = True
        try:
            transaction_id = arguments[arguments.index("--mode-switch-transaction") + 1] if "--mode-switch-transaction" in arguments else ""
            if arguments[0] == "--privileged-host-config":
                wizard._configure_windows_host_privileged(arguments[arguments.index("--host") + 1], int(arguments[arguments.index("--port") + 1]), Path(arguments[arguments.index("--data-dir") + 1]), transaction_id, Path(arguments[arguments.index("--personal-data-dir") + 1]) if "--personal-data-dir" in arguments else None)
            elif arguments[0] == "--privileged-restore-host":
                wizard._restore_windows_host_switch_privileged(transaction_id)
            elif arguments[0] == "--privileged-abort-host-config":
                wizard._abort_unprepared_host_configuration_privileged(transaction_id)
            else:
                wizard._finalize_windows_host_switch_privileged(transaction_id)
            return subprocess.CompletedProcess([], 0, "PARTYOPS_ELEVATION_CHILD_EXIT", "")
        except Exception as exc:
            # 对齐真实main对已确认前置拒绝的固定退出码，不把它伪装成普通子进程失败。
            code = wizard.PERSONAL_HOST_REJECTED_EXIT if "[PERSONAL_DATA_HOST_IN_PLACE_UNSUPPORTED]" in str(exc) else 1
            return subprocess.CompletedProcess([], code, "PARTYOPS_ELEVATION_CHILD_EXIT", str(exc))
        finally:
            state["admin"] = False
    monkeypatch.setattr(wizard, "run_elevated", elevated)
    return state, data, personal, config, paths, originals, original_read


def assert_original_files(lab):
    state, _data, _personal, _config, _paths, originals, original_read = lab
    assert {path: original_read(path, encoding="utf-8") for path in originals} == originals
    assert state["running"] == {"PartyOpsHost": True, "PartyOpsUpdateService": False}


def test_standard_parent_reconfigures_existing_host_without_protected_io(transaction_lab):
    state, data, _personal, config, paths, _originals, original_read = transaction_lab
    wizard.configure_host_config("192.168.1.9", 18765, data)
    network = json.loads(original_read(paths["network"], encoding="utf-8"))
    assert network == {"bind_host": "192.168.1.9", "advertise_host": "192.168.1.9", "port": 18765, "audit": "保留"}
    assert json.loads((config / "mode.json").read_text(encoding="utf-8"))["mode"] == "host"
    assert [call[0] for call in state["calls"]] == ["--privileged-host-config", "--privileged-finalize-host-switch"]
    ids = [call[call.index("--mode-switch-transaction") + 1] for call in state["calls"]]
    assert ids[0] == ids[1]
    assert not wizard._windows_host_switch_pending_path().exists()


def test_helper_service_failure_restores_exact_files_and_services(transaction_lab, monkeypatch):
    state, data, _personal, _config, paths, _originals, original_read = transaction_lab
    def fail_enable():
        assert state["admin"]
        assert json.loads(original_read(paths["network"], encoding="utf-8"))["bind_host"] == "192.168.1.9"
        raise ValueError("synthetic enable failure")
    monkeypatch.setattr(wizard, "_enable_windows_host_service_autostart", fail_enable)
    with pytest.raises(wizard.HostStartupError, match="ADMIN_CHILD_FAILED"):
        wizard.configure_host_config("192.168.1.9", 18765, data)
    assert_original_files(transaction_lab)
    assert state["restarts"] == []  # 助手失败时旧个人进程从未停止。
    assert not wizard._windows_host_switch_pending_path().exists()


def test_standard_parent_commit_failure_restores_via_same_privileged_id(transaction_lab, monkeypatch):
    state, data, _personal, config, _paths, _originals, _original_read = transaction_lab
    previous_user = (config / "mode.json").read_text(encoding="utf-8")
    monkeypatch.setattr(wizard, "write_mode_config", lambda *_a, **_k: (_ for _ in ()).throw(ValueError("synthetic user commit failure")))
    with pytest.raises(ValueError, match="synthetic user commit"):
        wizard.configure_host_config("192.168.1.9", 18765, data)
    assert_original_files(transaction_lab)
    assert (config / "mode.json").read_text(encoding="utf-8") == previous_user
    assert [call[0] for call in state["calls"]] == ["--privileged-host-config", "--privileged-restore-host"]
    assert state["calls"][0][state["calls"][0].index("--mode-switch-transaction") + 1] == state["calls"][1][-1]
    assert state["restarts"]


def test_cancelled_privileged_rollback_keeps_pending_and_never_restarts_personal(transaction_lab, monkeypatch):
    state, data, _personal, _config, _paths, _originals, _original_read = transaction_lab
    state["deny_restore"] = True
    monkeypatch.setattr(wizard, "write_mode_config", lambda *_a, **_k: (_ for _ in ()).throw(ValueError("synthetic user commit failure")))
    with pytest.raises(ValueError, match="MODE_SWITCH_ROLLBACK_FAILED"):
        wizard.configure_host_config("192.168.1.9", 18765, data)
    assert wizard._windows_host_switch_pending_path().exists()
    assert state["restarts"] == []
    state["deny_restore"] = False
    wizard.recover_pending_windows_host_switch()
    assert_original_files(transaction_lab)
    assert not wizard._windows_host_switch_pending_path().exists()


def test_fresh_empty_host_directory_supported(transaction_lab):
    state, data, _personal, _config, paths, _originals, _original_read = transaction_lab
    state["admin"] = True
    for path in paths.values():
        path.unlink()
    state["admin"] = False
    wizard.configure_host_config("127.0.0.1", 18765, data)
    assert paths["host_env"].is_file()
    assert paths["data_marker"].is_file()
    assert not paths["network"].exists()
    assert not wizard._windows_host_switch_pending_path().exists()


def test_nonempty_personal_same_directory_rejected_before_any_changes(transaction_lab, monkeypatch):
    state, _data, personal, _config, _paths, _originals, _original_read = transaction_lab
    monkeypatch.setattr(wizard, "_stop_personal_process_for_data_migration", lambda *_a: pytest.fail("拒绝前不能停个人进程"))
    before = (personal / "business.txt").read_bytes()
    with pytest.raises(wizard.HostStartupError, match="PERSONAL_DATA_HOST_IN_PLACE_UNSUPPORTED"):
        wizard.configure_host_config("127.0.0.1", 18765, personal)
    assert state["events"] == []
    assert [call[0] for call in state["calls"]] == ["--privileged-host-config"]
    assert (personal / "business.txt").read_bytes() == before
    assert not wizard._windows_host_switch_pending_path().exists()
    assert not wizard._host_configuration_journal_path().exists()


def test_elevation_timeout_before_pending_aborts_same_id_and_blocks_late_helper(transaction_lab, monkeypatch):
    state, data, _personal, _config, _paths, _originals, _original_read = transaction_lab
    elevated = wizard.run_elevated
    seen = []
    def timeout_before_prepare(executable, arguments):
        if arguments[0] == "--privileged-host-config":
            state["calls"].append(arguments)
            seen.append(arguments[arguments.index("--mode-switch-transaction") + 1])
            assert not wizard._windows_host_switch_pending_path().exists()
            raise subprocess.TimeoutExpired("synthetic-elevation-wrapper", 180)
        assert arguments == ["--privileged-abort-host-config", "--mode-switch-transaction", seen[0]]
        assert wizard._host_configuration_journal_path().exists()
        return elevated(executable, arguments)
    monkeypatch.setattr(wizard, "run_elevated", timeout_before_prepare)
    with pytest.raises(subprocess.TimeoutExpired):
        wizard.configure_host_config("192.168.1.9", 18765, data)
    assert wizard._read_host_configuration_outcome(seen[0]) == "rollback"
    assert not wizard._host_configuration_journal_path().exists()
    assert state["restarts"] == [] and state["events"] == []
    state["admin"] = True
    with pytest.raises(ValueError, match="MODE_SWITCH_TRANSACTION_FINISHED"):
        wizard._configure_windows_host_privileged("192.168.1.9", 18765, data, seen[0])
    assert_original_files(transaction_lab)
    assert state["events"] == []


def test_elevation_timeout_after_privileged_prepare_aborts_and_restores(transaction_lab, monkeypatch):
    state, data, _personal, config, _paths, _originals, _original_read = transaction_lab
    elevated = wizard.run_elevated
    previous = (config / "mode.json").read_text(encoding="utf-8")
    def timeout_after_prepare(executable, arguments):
        result = elevated(executable, arguments)
        if arguments[0] == "--privileged-host-config":
            assert result.returncode == 0 and wizard._windows_host_switch_pending_path().exists()
            raise subprocess.TimeoutExpired("synthetic-elevation-wrapper", 180)
        return result
    monkeypatch.setattr(wizard, "run_elevated", timeout_after_prepare)
    with pytest.raises(subprocess.TimeoutExpired):
        wizard.configure_host_config("192.168.1.9", 18765, data)
    transaction_id = state["calls"][0][state["calls"][0].index("--mode-switch-transaction") + 1]
    assert state["calls"][-1] == ["--privileged-abort-host-config", "--mode-switch-transaction", transaction_id]
    assert wizard._read_host_configuration_outcome(transaction_id) == "rollback"
    assert not wizard._windows_host_switch_pending_path().exists()
    assert not wizard._host_configuration_journal_path().exists()
    assert (config / "mode.json").read_text(encoding="utf-8") == previous
    assert state["restarts"] == []
    assert_original_files(transaction_lab)


def test_timed_out_parent_aborts_prepare_holding_mutex_before_pending(transaction_lab, monkeypatch):
    import threading
    from contextlib import contextmanager
    state, data, _personal, _config, _paths, _originals, _original_read = transaction_lab
    elevated, mutex = wizard.run_elevated, wizard._host_configuration_mutex
    acquired, abort_waiting, release = threading.Event(), threading.Event(), threading.Event()
    identity = threading.local()
    errors, threads = [], []
    parent = threading.get_ident()
    monkeypatch.setattr(wizard, "windows_is_admin", lambda: getattr(identity, "admin", state["admin"]))
    @contextmanager
    def observed_mutex():
        if threading.get_ident() == parent:
            abort_waiting.set()
        with mutex():
            yield
    monkeypatch.setattr(wizard, "_host_configuration_mutex", observed_mutex)
    def late_prepare(transaction_id):
        identity.admin = True
        try:
            with wizard._host_configuration_mutex():
                acquired.set()
                assert release.wait(timeout=5)
                wizard._configure_windows_host_privileged("192.168.1.9", 18765, data, transaction_id)
        except Exception as exc:
            errors.append(exc)
    def release_locked_prepare():
        try:
            assert abort_waiting.wait(timeout=5)
            assert not wizard._windows_host_switch_pending_path().exists()
        except Exception as exc:
            errors.append(exc)
        finally:
            release.set()
    def timeout_with_mutex_owned(executable, arguments):
        if arguments[0] != "--privileged-host-config":
            return elevated(executable, arguments)
        state["calls"].append(arguments)
        transaction_id = arguments[arguments.index("--mode-switch-transaction") + 1]
        threads.extend([threading.Thread(target=late_prepare, args=(transaction_id,)), threading.Thread(target=release_locked_prepare)])
        for thread in threads: thread.start()
        assert acquired.wait(timeout=5)
        assert not wizard._windows_host_switch_pending_path().exists()
        raise subprocess.TimeoutExpired("synthetic-wrapper", 180)
    monkeypatch.setattr(wizard, "run_elevated", timeout_with_mutex_owned)
    try:
        with pytest.raises(subprocess.TimeoutExpired):
            wizard.configure_host_config("192.168.1.9", 18765, data)
    finally:
        release.set()
        for thread in threads: thread.join(timeout=5)
    assert not errors and all(not thread.is_alive() for thread in threads)
    transaction_id = state["calls"][-1][-1]
    assert state["calls"][-1][0] == "--privileged-abort-host-config"
    assert wizard._read_host_configuration_outcome(transaction_id) == "rollback"
    assert not wizard._host_configuration_journal_path().exists()
    assert not wizard._windows_host_switch_pending_path().exists()
    assert state["restarts"] == []
    assert_original_files(transaction_lab)


def test_privileged_cli_personal_directory_refusal_is_fixed_preparation_free_exit(transaction_lab, monkeypatch):
    import sys
    state, _data, personal, _config, _paths, _originals, _original_read = transaction_lab
    state["admin"] = True
    monkeypatch.setattr(sys, "argv", ["PartyOpsWizard", "--privileged-host-config", "--host", "127.0.0.1", "--port", "18765",
                                     "--data-dir", str(personal), "--personal-data-dir", str(personal), "--mode-switch-transaction", "Q" * 32])
    with pytest.raises(SystemExit) as result:
        wizard.main()
    assert result.value.code == wizard.PERSONAL_HOST_REJECTED_EXIT
    assert not wizard._windows_host_switch_pending_path().exists()
    assert not wizard._read_host_configuration_outcome("Q" * 32)
    assert state["events"] == []


@pytest.mark.parametrize("abort_failure", ["cancelled", "timeout", "busy", "no_receipt"])
def test_unknown_elevation_abort_failure_preserves_journal_until_reopen(transaction_lab, monkeypatch, abort_failure):
    state, data, _personal, _config, _paths, _originals, _original_read = transaction_lab
    elevated = wizard.run_elevated
    def unknown(executable, arguments):
        state["calls"].append(arguments)
        if arguments[0] == "--privileged-host-config":
            raise subprocess.TimeoutExpired("synthetic-elevation-wrapper", 180)
        if abort_failure == "timeout":
            raise subprocess.TimeoutExpired("synthetic-abort-wrapper", 180)
        if abort_failure == "cancelled":
            return subprocess.CompletedProcess([], 1223, "", "PARTYOPS_ELEVATION_START_FAILED")
        if abort_failure == "busy":
            return subprocess.CompletedProcess([], 1, "PARTYOPS_ELEVATION_CHILD_EXIT", "[MODE_SWITCH_BUSY] synthetic")
        return subprocess.CompletedProcess([], 0, "PARTYOPS_ELEVATION_CHILD_EXIT", "")
    monkeypatch.setattr(wizard, "run_elevated", unknown)
    with pytest.raises(ValueError, match="MODE_SWITCH_ROLLBACK_FAILED"):
        wizard.configure_host_config("192.168.1.9", 18765, data)
    journal = wizard._host_configuration_journal_path()
    transaction_id = json.loads(journal.read_text(encoding="utf-8"))["transaction_id"]
    before = journal.read_bytes()
    assert not wizard._read_host_configuration_outcome(transaction_id)
    assert not wizard._windows_host_switch_pending_path().exists()
    with pytest.raises(ValueError, match="MODE_SWITCH_RECOVERY_REQUIRED"):
        wizard.configure_host_config("192.168.1.9", 18765, data)
    assert journal.read_bytes() == before and len(state["calls"]) == 2
    assert state["restarts"] == []
    assert_original_files(transaction_lab)
    # 重开向导执行现有同ID abort恢复；不是重发原prepare。
    monkeypatch.setattr(wizard, "run_elevated", elevated)
    wizard.recover_pending_windows_host_switch()
    assert state["calls"][-1] == ["--privileged-abort-host-config", "--mode-switch-transaction", transaction_id]
    assert wizard._read_host_configuration_outcome(transaction_id) == "rollback"
    assert not journal.exists() and state["restarts"] == []


@pytest.mark.parametrize("code", [1223, 786, 1260])
def test_confirmed_start_cancel_or_policy_refusal_never_requests_second_uac(transaction_lab, monkeypatch, code):
    state, data, _personal, _config, _paths, _originals, _original_read = transaction_lab
    def declined(executable, arguments):
        state["calls"].append(arguments)
        return subprocess.CompletedProcess([], code, "", "PARTYOPS_ELEVATION_START_FAILED")
    monkeypatch.setattr(wizard, "run_elevated", declined)
    with pytest.raises(wizard.HostStartupError) as failure:
        wizard.configure_host_config("192.168.1.9", 18765, data)
    assert failure.value.code == ("ADMIN_CANCELLED" if code == 1223 else wizard.ADMIN_POLICY_BLOCKED)
    assert [arguments[0] for arguments in state["calls"]] == ["--privileged-host-config"]
    assert not wizard._host_configuration_journal_path().exists()
    assert not wizard._windows_host_switch_pending_path().exists()
    assert state["events"] == [] and state["restarts"] == []
    assert_original_files(transaction_lab)


@pytest.mark.parametrize("code", [1223, wizard.PERSONAL_HOST_REJECTED_EXIT])
def test_unmarked_exit_does_not_claim_no_helper_started(transaction_lab, monkeypatch, code):
    state, data, _personal, _config, _paths, _originals, _original_read = transaction_lab
    elevated = wizard.run_elevated
    def unmarked(executable, arguments):
        if arguments[0] == "--privileged-host-config":
            state["calls"].append(arguments)
            return subprocess.CompletedProcess([], code, "", "synthetic unmarked failure")
        return elevated(executable, arguments)
    monkeypatch.setattr(wizard, "run_elevated", unmarked)
    with pytest.raises(wizard.HostStartupError, match="ADMIN_START_FAILED"):
        wizard.configure_host_config("192.168.1.9", 18765, data)
    assert [arguments[0] for arguments in state["calls"]] == ["--privileged-host-config", "--privileged-abort-host-config"]
    assert wizard._read_host_configuration_outcome(state["calls"][-1][-1]) == "rollback"
    assert not wizard._host_configuration_journal_path().exists()


def test_helper_rejects_real_personal_marker_before_access_validation(transaction_lab, monkeypatch):
    state, _data, personal, _config, _paths, _originals, _original_read = transaction_lab
    wizard._write_data_root_marker(personal, "personal")
    state["admin"] = True
    monkeypatch.setattr(wizard, "_validate_windows_data_dir", lambda *_a, **_k: pytest.fail("个人原地转换不得进入目录接管"))
    with pytest.raises(ValueError, match="PERSONAL_DATA_HOST_IN_PLACE_UNSUPPORTED"):
        wizard._configure_windows_host_privileged("127.0.0.1", 18765, personal, "A" * 32)
    assert state["events"] == []
    assert not wizard._windows_host_switch_pending_path().exists()


def test_existing_pending_not_overwritten_and_malformed_file_targets_rejected(transaction_lab, monkeypatch):
    state, data, _personal, _config, _paths, _originals, original_read = transaction_lab
    state["admin"] = True
    wizard._configure_windows_host_privileged("192.168.1.9", 18765, data, "A" * 32)
    snapshot = wizard._windows_host_switch_snapshot_path()
    before = original_read(snapshot, encoding="utf-8")
    with pytest.raises(ValueError, match="MODE_SWITCH_PENDING"):
        wizard._configure_windows_host_privileged("192.168.1.8", 18765, data, "B" * 32)
    assert original_read(snapshot, encoding="utf-8") == before
    payload = json.loads(before)
    payload["configuration"]["files"]["arbitrary_path"] = "untrusted"
    wizard._write_private(snapshot, json.dumps(payload))
    monkeypatch.setattr(wizard, "_stop_windows_service_for_data_migration", lambda: pytest.fail("完整验证前不能停服务"))
    with pytest.raises(ValueError, match="文件清单无效"):
        wizard._restore_windows_host_switch_privileged("A" * 32)
    assert wizard._windows_host_switch_pending_path().exists()


@pytest.mark.parametrize("failure_point", ["finalize_windows_host_switch"])
def test_post_user_commit_failure_restores_system_and_user_files(transaction_lab, monkeypatch, failure_point):
    state, data, _personal, config, _paths, _originals, _original_read = transaction_lab
    previous_user = (config / "mode.json").read_text(encoding="utf-8")
    def fail(*_a):
        assert json.loads((config / "mode.json").read_text(encoding="utf-8"))["mode"] == "host"
        raise ValueError("synthetic post-commit failure")
    monkeypatch.setattr(wizard, failure_point, fail)
    with pytest.raises(ValueError, match="synthetic post-commit"):
        wizard.configure_host_config("192.168.1.9", 18765, data)
    assert_original_files(transaction_lab)
    assert (config / "mode.json").read_text(encoding="utf-8") == previous_user
    assert state["restarts"]
    assert state["calls"][-1][0] == "--privileged-restore-host"


def test_failed_service_restore_keeps_recoverable_snapshot(transaction_lab, monkeypatch):
    state, data, _personal, _config, _paths, _originals, _original_read = transaction_lab
    def fail(*_a):
        raise ValueError("synthetic persistent restore failure")
    monkeypatch.setattr(wizard, "_enable_windows_host_service_autostart", fail)
    monkeypatch.setattr(wizard, "_restore_windows_service_start_config", fail)
    with pytest.raises(ValueError, match="MODE_SWITCH_ROLLBACK_FAILED"):
        wizard.configure_host_config("192.168.1.9", 18765, data)
    assert wizard._windows_host_switch_pending_path().exists()
    assert wizard._windows_host_switch_snapshot_path().exists()
    assert state["restarts"] == []


@pytest.mark.parametrize("cleanup_name", ["clear_windows_client_autostart", "clear_windows_personal_autostart"])
def test_autostart_cleanup_failure_keeps_committed_host(transaction_lab, monkeypatch, cleanup_name):
    state, data, _personal, config, _paths, _originals, _original_read = transaction_lab
    monkeypatch.setattr(wizard, cleanup_name, lambda: (_ for _ in ()).throw(OSError("synthetic cleanup denied")))
    wizard.configure_host_config("192.168.1.9", 18765, data)
    assert json.loads((config / "mode.json").read_text(encoding="utf-8"))["mode"] == "host"
    assert "AUTOSTART_CLEANUP_DEFERRED" in (config / "autostart-warning.log").read_text(encoding="utf-8")
    assert not wizard._host_configuration_journal_path().exists()
    assert state["restarts"] == []


@pytest.mark.parametrize("failure", ["launch", "ready"])
def test_launch_or_ready_failure_remains_inside_privileged_transaction(transaction_lab, monkeypatch, failure):
    state, data, _personal, config, _paths, _originals, _original_read = transaction_lab
    monkeypatch.setattr(wizard, "_restore_previous_windows_autostart", lambda *_a: pytest.fail("commit 前没有修改自启动，无需写回"))
    with pytest.raises(ValueError, match=f"synthetic {failure}"):
        with wizard.host_configuration_transaction("192.168.1.9", 18765, data):
            assert wizard._windows_host_switch_pending_path().exists()
            assert wizard._host_configuration_journal_path().exists()
            assert json.loads((config / "mode.json").read_text(encoding="utf-8"))["mode"] == "personal"
            raise ValueError(f"synthetic {failure} failure")
    assert_original_files(transaction_lab)
    assert not wizard._host_configuration_journal_path().exists()
    assert state["restarts"]


def prepare_crashed_transaction(lab, transaction_id="C" * 32):
    state, data, _personal, config, _paths, _originals, _original_read = lab
    previous_mode = (config / "mode.json").read_text(encoding="utf-8")
    wizard._write_private(wizard._host_configuration_journal_path(), json.dumps({"format_version": 1, "transaction_id": transaction_id, "previous_mode": previous_mode, "previous_role": "personal"}))
    state["admin"] = True
    wizard._configure_windows_host_privileged("192.168.1.9", 18765, data, transaction_id)
    state["admin"] = False
    wizard.write_mode_config("host", config_path=wizard._host_configuration_paths(data)["host_env"])
    return transaction_id, previous_mode


def test_crash_after_user_mode_write_recovers_privileged_then_user_state(transaction_lab):
    _state, _data, _personal, config, _paths, _originals, _original_read = transaction_lab
    transaction_id, previous_mode = prepare_crashed_transaction(transaction_lab)
    wizard.recover_pending_windows_host_switch()
    assert_original_files(transaction_lab)
    assert (config / "mode.json").read_text(encoding="utf-8") == previous_mode
    assert wizard._read_host_configuration_outcome(transaction_id) == "rollback"
    assert not wizard._host_configuration_journal_path().exists()


def test_crash_after_commit_receipt_never_rolls_back_committed_host(transaction_lab, monkeypatch):
    state, _data, _personal, config, paths, _originals, original_read = transaction_lab
    transaction_id, _previous = prepare_crashed_transaction(transaction_lab)
    cleanup = wizard._cleanup_completed_host_configuration
    monkeypatch.setattr(wizard, "_cleanup_completed_host_configuration", lambda *_a: None)
    state["admin"] = True
    wizard._finalize_windows_host_switch_privileged(transaction_id)
    state["admin"] = False
    monkeypatch.setattr(wizard, "_cleanup_completed_host_configuration", cleanup)
    wizard.recover_pending_windows_host_switch()
    assert wizard._read_host_configuration_outcome(transaction_id) == "commit"
    assert json.loads((config / "mode.json").read_text(encoding="utf-8"))["mode"] == "host"
    assert json.loads(original_read(paths["network"], encoding="utf-8"))["bind_host"] == "192.168.1.9"
    assert not wizard._host_configuration_journal_path().exists()


def test_unprepared_user_journal_recovers_and_late_helper_is_rejected(transaction_lab):
    state, data, _personal, _config, _paths, _originals, _original_read = transaction_lab
    transaction_id = "C" * 32
    wizard._write_private(wizard._host_configuration_journal_path(), json.dumps({"format_version": 1, "transaction_id": transaction_id, "previous_mode": None, "previous_role": "personal"}))
    wizard.recover_pending_windows_host_switch()
    assert wizard._read_host_configuration_outcome(transaction_id) == "rollback"
    assert not wizard._host_configuration_journal_path().exists()
    state["admin"] = True
    with pytest.raises(ValueError, match="TRANSACTION_FINISHED"):
        wizard._configure_windows_host_privileged("192.168.1.9", 18765, data, transaction_id)
    assert state["events"] == []
    assert_original_files(transaction_lab)


def test_gui_business_rejection_survives_missing_stderr():
    result = subprocess.CompletedProcess([], wizard.PERSONAL_HOST_REJECTED_EXIT, "PARTYOPS_ELEVATION_CHILD_EXIT", "")
    with pytest.raises(wizard.HostStartupError, match="请选择独立的主机数据目录") as stopped:
        wizard._check_windows_elevation_result(result, "主机配置")
    assert stopped.value.code == "PERSONAL_DATA_HOST_IN_PLACE_UNSUPPORTED"


def test_receipt_write_failure_keeps_rollback_available(transaction_lab, monkeypatch):
    state, data, _personal, _config, _paths, _originals, _original_read = transaction_lab
    original = wizard._write_host_configuration_outcome
    def write(transaction_id, outcome):
        if outcome == "commit":
            raise OSError("synthetic receipt write failure")
        return original(transaction_id, outcome)
    monkeypatch.setattr(wizard, "_write_host_configuration_outcome", write)
    with pytest.raises(wizard.HostStartupError, match="ADMIN_CHILD_FAILED"):
        wizard.configure_host_config("192.168.1.9", 18765, data)
    assert_original_files(transaction_lab)
    assert state["restarts"]


def test_rollback_receipt_survives_missing_pending_until_user_recovery(transaction_lab):
    state, _data, _personal, config, _paths, _originals, _original_read = transaction_lab
    transaction_id, previous_mode = prepare_crashed_transaction(transaction_lab)
    state["admin"] = True
    wizard._restore_windows_host_switch_privileged(transaction_id)
    state["admin"] = False
    assert not wizard._windows_host_switch_pending_path().exists()
    assert wizard._host_configuration_journal_path().exists()
    wizard.recover_pending_windows_host_switch()
    assert (config / "mode.json").read_text(encoding="utf-8") == previous_mode
    assert not wizard._host_configuration_journal_path().exists()


def test_user_mode_restore_failure_preserves_journal(transaction_lab, monkeypatch):
    state, _data, _personal, config, _paths, _originals, _original_read = transaction_lab
    transaction_id, _previous_mode = prepare_crashed_transaction(transaction_lab)
    state["admin"] = True
    wizard._restore_windows_host_switch_privileged(transaction_id)
    state["admin"] = False
    original = wizard._write_private
    def write(path, *args, **kwargs):
        if path == config / "mode.json":
            raise OSError("synthetic user mode permission failure")
        return original(path, *args, **kwargs)
    monkeypatch.setattr(wizard, "_write_private", write)
    with pytest.raises(OSError, match="synthetic user mode"):
        wizard.recover_pending_windows_host_switch()
    assert wizard._host_configuration_journal_path().exists()


def test_abort_after_prepared_transaction_restores_instead_of_claiming_unprepared(transaction_lab):
    state, _data, _personal, _config, _paths, _originals, _original_read = transaction_lab
    transaction_id, _previous = prepare_crashed_transaction(transaction_lab)
    state["admin"] = True
    wizard._abort_unprepared_host_configuration_privileged(transaction_id)
    assert wizard._read_host_configuration_outcome(transaction_id) == "rollback"
    assert_original_files(transaction_lab)


def test_abort_refuses_ambiguous_privileged_record(transaction_lab):
    state, _data, _personal, _config, _paths, _originals, _original_read = transaction_lab
    state["admin"] = True
    wizard._write_private(wizard._windows_host_switch_pending_path(), "{}")
    with pytest.raises(ValueError, match="SNAPSHOT_INVALID"):
        wizard._abort_unprepared_host_configuration_privileged("C" * 32)
    assert not wizard._read_host_configuration_outcome("C" * 32)


def test_abort_waits_for_active_privileged_prepare_mutex(transaction_lab, monkeypatch):
    import threading
    state, data, _personal, _config, _paths, _originals, _original_read = transaction_lab
    state["admin"] = True
    prepared, release, aborted = threading.Event(), threading.Event(), threading.Event()
    errors = []
    def hold_enable():
        prepared.set()
        assert release.wait(timeout=5)
    monkeypatch.setattr(wizard, "_enable_windows_host_service_autostart", hold_enable)
    def prepare():
        try:
            wizard._configure_windows_host_privileged("192.168.1.9", 18765, data, "M" * 32)
        except Exception as exc:
            errors.append(exc)
    def abort():
        try:
            wizard._abort_unprepared_host_configuration_privileged("M" * 32)
            aborted.set()
        except Exception as exc:
            errors.append(exc)
    preparer, aborter = threading.Thread(target=prepare), threading.Thread(target=abort)
    preparer.start()
    try:
        assert prepared.wait(timeout=5)
        aborter.start()
        assert not aborted.wait(timeout=0.1)
    finally:
        release.set()
        preparer.join(timeout=5)
        if aborter.ident is not None:
            aborter.join(timeout=5)
    assert not errors
    assert aborted.is_set()
    assert wizard._read_host_configuration_outcome("M" * 32) == "rollback"
    assert_original_files(transaction_lab)


def test_windows_mutex_recovers_abandoned_privileged_owner(transaction_lab, monkeypatch):
    import ctypes
    import sys
    from ctypes import wintypes
    state, _data, _personal, _config, _paths, _originals, _original_read = transaction_lab
    state["admin"] = True
    child_script = """import ctypes,sys
from ctypes import wintypes
k=ctypes.WinDLL('kernel32',use_last_error=True)
k.CreateMutexW.argtypes=[wintypes.LPVOID,wintypes.BOOL,wintypes.LPCWSTR]
k.CreateMutexW.restype=wintypes.HANDLE
h=k.CreateMutexW(None,True,'Global\\\\PartyOps.HostConfiguration')
assert h and ctypes.get_last_error()!=183
print('owned',flush=True)
sys.stdin.read(1)
"""
    process = subprocess.Popen([sys.executable, "-c", child_script], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.OpenMutexW.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.LPCWSTR]
    kernel.OpenMutexW.restype = wintypes.HANDLE
    kernel.CloseHandle.argtypes = [wintypes.HANDLE]
    kernel.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
    kernel.WaitForSingleObject.restype = wintypes.DWORD
    handle = None
    try:
        assert process.stdout.readline().strip() == "owned"
        handle = kernel.OpenMutexW(0x1F0001, False, "Global\\PartyOps.HostConfiguration")
        assert handle
        process.stdin.write("\n")
        process.stdin.flush()
        assert process.wait(timeout=5) == 0
        original_dll = ctypes.WinDLL
        observed = []
        def wait(handle, timeout):
            result = kernel.WaitForSingleObject(handle, timeout)
            observed.append(result)
            return result
        class KernelProxy:
            WaitForSingleObject = staticmethod(wait)
            def __getattr__(self, name):
                return getattr(kernel, name)
        monkeypatch.setattr(ctypes, "WinDLL", lambda name, **kwargs: KernelProxy() if name == "kernel32" else original_dll(name, **kwargs))
        with wizard._host_configuration_mutex():
            pass
        assert observed == [0x80]
    finally:
        if process.stdin:
            process.stdin.close()
        process.wait(timeout=5)
        if handle:
            kernel.CloseHandle(handle)
