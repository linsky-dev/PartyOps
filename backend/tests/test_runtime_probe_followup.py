"""本轮进程日志与已保存配置拒读反例；只使用假进程和临时文件。"""
from __future__ import annotations

import errno
import importlib.util
import json
from pathlib import Path
from types import SimpleNamespace

import pytest

from app import setup_wizard as wizard
from app import windows_host_status as status

ROOT = next(parent for parent in Path(__file__).resolve().parents if (parent / "packaging/windows/windows_launcher.py").is_file())


def fake_spawn(monkeypatch, root, *, code=1, output=b""):
    process = SimpleNamespace(returncode=code, pid=934, poll=lambda: code)

    def popen(_command, **options):
        # 模拟子进程在 Popen 返回前已经写出异常并退出；起点在 wait 内采集会漏报。
        options["stdout"].write(output)
        options["stdout"].flush()
        return process

    monkeypatch.setattr(wizard.subprocess, "Popen", popen)
    monkeypatch.setattr(wizard.urllib.request, "urlopen", lambda *_args, **_kwargs: pytest.fail("假进程已退出，不得发 HTTP"))
    return wizard._spawn(["fixture-never-executed"], root / "launcher.log")


def failure(root, process):
    with pytest.raises(wizard.HostStartupError) as caught:
        wizard.wait_for_host_health("127.0.0.1", 19875, data_dir=root, service_managed=False, process=process)
    return caught.value


@pytest.mark.parametrize("code,output,expected", [
    (0xC0000135, b"", status.RUNTIME_DEPENDENCY_MISSING),
    (-1073741819, b"", status.RUNTIME_NATIVE_CRASH),
    (0xC000007B, b"", status.RUNTIME_BINARY_INCOMPATIBLE),
    (1, b"", status.CHILD_EXITED),
    (1, b"[RUNTIME_PERMISSION_DENIED] new access failure\n", status.RUNTIME_PERMISSION_DENIED),
    (1, b"PermissionError: [WinError 5] Access is denied\n", status.RUNTIME_PERMISSION_DENIED),
    (1, b"[DATABASE_IO_FAILED] new storage failure\n", status.DATABASE_IO_FAILED),
    (0xC0000135, b"[RUNTIME_PERMISSION_DENIED] misleading text\n", status.RUNTIME_DEPENDENCY_MISSING),
])
def test_current_exit_and_early_output_cannot_be_overridden_by_history(tmp_path, monkeypatch, code, output, expected):
    history = b"[RUNTIME_PERMISSION_DENIED] historical access failure\n"
    log = tmp_path / "launcher.log"
    log.write_bytes(history)
    process = fake_spawn(monkeypatch, tmp_path, code=code, output=output)
    error = failure(tmp_path, process)
    assert error.code == expected
    assert str(code) in error.detail
    assert "historical access failure" not in error.detail
    if output:
        assert output.decode().strip() in error.detail
    assert log.read_bytes() == history + output


@pytest.mark.parametrize("mutation", ["replace", "truncate", "truncate-regrow", "missing-boundary"])
def test_replaced_truncated_or_unbound_log_is_not_current_process_evidence(tmp_path, monkeypatch, mutation):
    log = tmp_path / "launcher.log"
    history = b"x" * 512
    log.write_bytes(history)
    process = fake_spawn(monkeypatch, tmp_path)
    if mutation == "replace":
        log.replace(tmp_path / "preserved-history.log")
        log.write_bytes(b"[RUNTIME_PERMISSION_DENIED] other file")
        assert (tmp_path / "preserved-history.log").read_bytes() == history
    elif mutation == "truncate":
        log.write_bytes(b"[RUNTIME_PERMISSION_DENIED] truncated")
    elif mutation == "truncate-regrow":
        log.write_bytes(b"y" * 512 + b"[RUNTIME_PERMISSION_DENIED] unrelated re-grown file")
    else:
        delattr(process, "_partyops_log_boundary")
    error = failure(tmp_path, process)
    assert error.code == status.CHILD_EXITED
    assert "无本轮日志输出" in error.detail


def test_boundary_is_captured_after_archiving_old_large_log(tmp_path, monkeypatch):
    log = tmp_path / "launcher.log"
    history = b"old history\n" * 10
    log.write_bytes(history)
    rotate = wizard._rotate_bounded_log
    monkeypatch.setattr(wizard, "_rotate_bounded_log", lambda path: rotate(path, max_bytes=32))
    process = fake_spawn(monkeypatch, tmp_path, output=b"[DATABASE_IO_FAILED] current\n")
    assert failure(tmp_path, process).code == status.DATABASE_IO_FAILED
    assert log.with_name("launcher.log.1").read_bytes() == history


def test_failed_popen_closes_parent_log_handle(tmp_path, monkeypatch):
    handles = []

    def denied(_command, **options):
        handles.append(options["stdout"])
        raise PermissionError(errno.EACCES, "synthetic process denied")

    monkeypatch.setattr(wizard.subprocess, "Popen", denied)
    with pytest.raises(PermissionError):
        wizard._spawn(["fixture-never-executed"], tmp_path / "launcher.log")
    assert handles[0].closed


@pytest.mark.parametrize("step", ["fstat", "resolve"])
def test_failed_boundary_capture_closes_parent_log_handle(tmp_path, monkeypatch, step):
    handles = []
    original = Path.open

    def tracked(path, mode="r", *args, **kwargs):
        handle = original(path, mode, *args, **kwargs)
        if mode == "ab":
            handles.append(handle)
        return handle

    def denied(*_args, **_kwargs):
        raise OSError(errno.EIO, "synthetic metadata failure")

    monkeypatch.setattr(Path, "open", tracked)
    monkeypatch.setattr(wizard.os if step == "fstat" else Path, step, denied)
    monkeypatch.setattr(wizard.subprocess, "Popen", lambda *_args, **_kwargs: pytest.fail("起点未捕获，不得创建进程"))
    with pytest.raises(OSError, match="synthetic metadata failure"):
        wizard._spawn(["fixture-never-executed"], tmp_path / "launcher.log")
    assert len(handles) == 1 and handles[0].closed


@pytest.mark.parametrize("step", ["open", "digest"])
def test_unreadable_historical_prefix_does_not_block_append_only_launch(tmp_path, monkeypatch, step):
    log = tmp_path / "launcher.log"
    history = b"[RUNTIME_PERMISSION_DENIED] historical\n"
    log.write_bytes(history)
    original = Path.open

    def denied_read(path, mode="r", *args, **kwargs):
        if path == log and mode == "rb":
            raise PermissionError(errno.EACCES, "synthetic read denied")
        return original(path, mode, *args, **kwargs)

    def denied_digest(*_args):
        raise OSError(errno.EIO, "synthetic historical read failure")

    if step == "open":
        monkeypatch.setattr(Path, "open", denied_read)
    else:
        monkeypatch.setattr(wizard, "_log_prefix_digest", denied_digest)
    process = fake_spawn(monkeypatch, tmp_path, code=0xC0000135, output=b"new output\n")
    error = failure(tmp_path, process)
    assert error.code == status.RUNTIME_DEPENDENCY_MISSING
    assert "3221225781" in error.detail and "无本轮日志输出" in error.detail
    with original(log, "rb") as stream:
        assert stream.read() == history + b"new output\n"


@pytest.mark.parametrize("step", ["config", "executable"])
@pytest.mark.parametrize("number,expected", [(errno.EACCES, status.RUNTIME_PERMISSION_DENIED),
                                            (errno.EIO, status.DATABASE_IO_FAILED), (errno.ENOSPC, status.DATA_DIR_FULL)])
def test_preflight_read_failures_keep_actual_io_category(tmp_path, monkeypatch, step, number, expected):
    config, executable = tmp_path / "personal.env", tmp_path / "PartyOps.exe"
    config.write_text("configured", encoding="utf-8")
    executable.write_bytes(b"MZ")
    monkeypatch.setattr(wizard, "_executable", lambda _: executable)
    original = Path.open
    target = config if step == "config" else executable

    def denied(path, *args, **kwargs):
        if path == target:
            raise OSError(number, "synthetic storage read failure")
        return original(path, *args, **kwargs)

    monkeypatch.setattr(Path, "open", denied)
    with pytest.raises(wizard.HostStartupError) as caught:
        wizard._preflight_personal_runtime_access(config, tmp_path / "untouched-data")
    assert caught.value.code == expected
    assert f"errno={number}" in caught.value.detail and str(target) in caught.value.detail
    assert not (tmp_path / "untouched-data").exists()


@pytest.mark.parametrize("content", ["PARTYOPS_PORT=19875\n", "PARTYOPS_PORT=19875\nPARTYOPS_DATA_DIR=\n",
                                    "PARTYOPS_PORT=19875\nPARTYOPS_DATA_DIR=relative\n", "PARTYOPS_DATA_DIR=relative\n"])
def test_daily_launch_does_not_fill_missing_saved_paths_or_port_from_inherited_environment(tmp_path, monkeypatch, content):
    config = tmp_path / "personal.env"
    config.write_text(content, encoding="utf-8")
    monkeypatch.setenv("PARTYOPS_PORT", "19999")
    monkeypatch.setenv("PARTYOPS_DATA_DIR", str(tmp_path / "foreign-data"))
    monkeypatch.setattr(wizard, "_preflight_personal_runtime_access", lambda *_: pytest.fail("不得触碰继承目录"))
    monkeypatch.setattr(wizard, "_spawn", lambda *_: pytest.fail("缺失保存配置不得启动"))
    with pytest.raises((ValueError, KeyError)):
        wizard.launch_personal(config)
    assert config.read_text(encoding="utf-8") == content
    assert not (tmp_path / "foreign-data").exists()


def test_daily_launch_keeps_saved_directory_and_normal_system_environment(tmp_path, monkeypatch):
    config, data = tmp_path / "personal.env", tmp_path / "保存 业务数据"
    config.write_text(f"PARTYOPS_PORT=19875\nPARTYOPS_DATA_DIR={wizard.shlex.quote(str(data))}\nPARTYOPS_OFFICIAL_FORMAT_PORT=19877\n", encoding="utf-8")
    before = config.read_bytes()
    monkeypatch.setenv("PARTYOPS_PORT", "19999")
    monkeypatch.setenv("PARTYOPS_DATA_DIR", str(tmp_path / "foreign-data"))
    monkeypatch.setenv("PATH", "fixture-system-path")
    monkeypatch.setattr(wizard, "_preflight_personal_runtime_access", lambda path, directory: (tmp_path / "PartyOps.exe") if path == config and directory == data else pytest.fail("目录不符"))
    monkeypatch.setattr(wizard, "_windows_data_lock_owner_pids", lambda _: set())
    monkeypatch.setattr(wizard, "_discover_running_windows_personal", lambda *_: None)
    monkeypatch.setattr(wizard.socket, "create_connection", lambda *_args, **_kwargs: (_ for _ in ()).throw(OSError("fixture closed port")))
    captured = []
    process = SimpleNamespace(pid=945)
    monkeypatch.setattr(wizard, "_spawn", lambda _cmd, log, env: captured.append((log, env)) or process)
    monkeypatch.setattr(wizard, "wait_for_host_health", lambda host, port, **_kwargs: f"http://{host}:{port}")
    monkeypatch.setattr(wizard, "_record_personal_process", lambda *_: None)
    assert wizard.launch_personal(config) == "http://127.0.0.1:19875"
    assert captured[0][0] == data / "launcher.log"
    assert captured[0][1]["PARTYOPS_DATA_DIR"] == str(data)
    assert captured[0][1]["PARTYOPS_PORT"] == "19875"
    assert captured[0][1]["PATH"] == "fixture-system-path"
    assert config.read_bytes() == before and not (tmp_path / "foreign-data").exists()


def test_service_status_keeps_its_own_freshness_contract(tmp_path, monkeypatch):
    rows = iter([
        {"updated_at": "old", "stage": "failed", "code": status.RUNTIME_PERMISSION_DENIED},
        {"updated_at": "new", "stage": "failed", "code": status.DATABASE_IO_FAILED, "detail": "new service failure"},
    ])
    monkeypatch.setattr(wizard, "read_service_status", lambda _: next(rows))
    monkeypatch.setattr(wizard.urllib.request, "urlopen", lambda *_args, **_kwargs: pytest.fail("明确服务故障不应继续 HTTP"))
    with pytest.raises(wizard.HostStartupError) as caught:
        wizard.wait_for_host_health("127.0.0.1", 19875, data_dir=tmp_path, service_managed=True)
    assert caught.value.code == status.DATABASE_IO_FAILED and caught.value.detail == "new service failure"


@pytest.mark.parametrize("number,expected", [(2, status.RUNTIME_EXECUTABLE_MISSING), (5, status.RUNTIME_PERMISSION_DENIED),
                                            (126, status.RUNTIME_DEPENDENCY_MISSING), (193, status.RUNTIME_BINARY_INCOMPATIBLE)])
def test_structured_windows_error_precedes_text(number, expected):
    assert status.classify_runtime_failure("[DATABASE_LOCKED] unrelated text", winerror=number) == expected


@pytest.fixture
def launcher(tmp_path, monkeypatch):
    spec = importlib.util.spec_from_file_location("runtime_followup_launcher", ROOT / "packaging/windows/windows_launcher.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    local = tmp_path / "local" / "PartyOps"
    local.mkdir(parents=True)
    config = local / "personal.env"
    config.write_text(f"PARTYOPS_DATA_DIR={wizard.shlex.quote(str(tmp_path / '业务数据'))}\nPARTYOPS_PORT=19875\n", encoding="utf-8")
    mode = local / "mode.json"
    mode.write_text(json.dumps({"mode": "personal", "config_path": str(config)}), encoding="utf-8")
    monkeypatch.setenv("LOCALAPPDATA", str(local.parent))
    monkeypatch.setenv("PROGRAMDATA", str(tmp_path / "programdata"))
    monkeypatch.setattr(module, "_persist_user_runtime_root", lambda *_: None)
    monkeypatch.setattr(module, "_preflight_windows_runtime_dependencies", lambda *_: None)
    monkeypatch.setattr(module, "ensure_user_protocols", lambda *_: None)
    monkeypatch.setattr(module, "launch_wizard_and_wait", lambda *_: pytest.fail("拒读不能重开首次向导"))
    monkeypatch.setattr(module, "launch_personal", lambda *_: pytest.fail("拒读不能启动业务进程"))
    diagnostics, messages = [], []
    monkeypatch.setattr(module, "_append_launcher_diagnostic", lambda path, message: diagnostics.append((path, message)))
    monkeypatch.setattr(module, "show_launch_failure", messages.append)
    return SimpleNamespace(module=module, local=local, mode=mode, config=config, diagnostics=diagnostics, messages=messages)


@pytest.mark.parametrize("background", [False, True])
@pytest.mark.parametrize("step", ["mode-read", "personal-stat", "personal-read"])
@pytest.mark.parametrize("number,expected", [(errno.EACCES, status.RUNTIME_PERMISSION_DENIED),
                                            (errno.EIO, status.DATABASE_IO_FAILED), (errno.ENOSPC, status.DATA_DIR_FULL)])
def test_existing_mode_or_personal_config_denied_keeps_files_and_fails(launcher, monkeypatch, background, step, number, expected):
    before = {path: path.read_bytes() for path in (launcher.mode, launcher.config)}
    target = launcher.mode if step == "mode-read" else launcher.config
    operation = "stat" if step == "personal-stat" else "read_text"
    original = getattr(Path, operation)

    def denied(path, *args, **kwargs):
        if path == target:
            raise OSError(number, "synthetic config denied")
        return original(path, *args, **kwargs)

    monkeypatch.setattr(Path, operation, denied)
    monkeypatch.setattr(launcher.module.sys, "argv", ["launcher", *(["--background"] if background else [])])
    assert launcher.module.main() == 1
    assert len(launcher.diagnostics) == 1
    assert expected in launcher.diagnostics[0][1]
    assert str(target) in launcher.diagnostics[0][1]
    assert bool(launcher.messages) is not background
    assert all(path.read_bytes() == content for path, content in before.items())


def test_missing_mode_still_opens_first_run_but_corrupt_mode_needs_explicit_repair(launcher, monkeypatch):
    calls = []
    monkeypatch.setattr(launcher.module, "launch_wizard_and_wait", lambda *args: calls.append(args) or True)
    monkeypatch.setattr(launcher.module.sys, "argv", ["launcher"])
    launcher.mode.unlink()
    assert launcher.module.main() == 0 and len(calls) == 1
    launcher.mode.write_text("{broken", encoding="utf-8")
    assert launcher.module.main() == 0 and len(calls) == 2
    assert launcher.mode.read_text(encoding="utf-8") == "{broken"


@pytest.mark.parametrize("background", [False, True])
def test_executable_permission_failure_is_not_relabelled_as_configuration_read_failure(launcher, monkeypatch, background):
    before = {path: path.read_bytes() for path in (launcher.mode, launcher.config)}
    wizard_calls = []

    def executable_denied(_config):
        raise PermissionError(errno.EACCES, "Permission denied", str(launcher.local / "PartyOps.exe"))

    monkeypatch.setattr(launcher.module, "launch_personal", executable_denied)
    monkeypatch.setattr(launcher.module, "launch_wizard_and_wait", lambda *args: wizard_calls.append(args) or True)
    monkeypatch.setattr(launcher.module.sys, "argv", ["launcher", *(["--background"] if background else [])])
    assert launcher.module.main() == 1
    assert not launcher.diagnostics  # 不得误入配置读取失败分支。
    assert all(path.read_bytes() == content for path, content in before.items())
    if background:
        assert not launcher.messages and not wizard_calls
    else:
        assert len(launcher.messages) == 1
        assert "RUNTIME_PERMISSION_DENIED" in launcher.messages[0]
        assert "运行文件" in launcher.messages[0] and "已保存的模式配置" not in launcher.messages[0]
