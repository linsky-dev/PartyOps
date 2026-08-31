"""rc.9 个人模式端口与进程归属的跨平台分支门禁。"""

from __future__ import annotations

import json
import os
from pathlib import Path
from types import SimpleNamespace

import pytest

from app import setup_wizard
from app.setup_wizard import HostStartupError


def run_result(stdout: str = "") -> SimpleNamespace:
    return SimpleNamespace(stdout=stdout, stderr="", returncode=0)


def test_listener_pid_windows_filters_every_untrusted_shape(
    monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.setattr(setup_wizard.os, "name", "nt")
    monkeypatch.setattr(setup_wizard.sys, "platform", "win32")
    output = "\n".join(
        [
            "short",
            "UDP 127.0.0.1:18775 x x 1",
            "TCP 127.0.0.1:18775 x ESTABLISHED 2",
            "TCP 127.0.0.1:19999 x LISTENING 3",
            "TCP 192.168.1.2:18775 x LISTENING 4",
            "TCP 127.0.0.1:18775 x LISTENING 5",
        ]
    )
    monkeypatch.setattr(setup_wizard.subprocess, "run", lambda *_a, **_k: run_result(output))
    assert setup_wizard._listener_pid_for_loopback_port(18775) == 5

    output += "\nTCP 0.0.0.0:18775 x LISTENING 6"
    monkeypatch.setattr(setup_wizard.subprocess, "run", lambda *_a, **_k: run_result(output))
    assert setup_wizard._listener_pid_for_loopback_port(18775) is None

    monkeypatch.setattr(
        setup_wizard.subprocess,
        "run",
        lambda *_a, **_k: run_result("TCP 127.0.0.1:18775 x LISTENING bad"),
    )
    assert setup_wizard._listener_pid_for_loopback_port(18775) is None


def test_listener_pid_darwin_linux_and_command_failure(
    monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.setattr(setup_wizard.os, "name", "posix")
    monkeypatch.setattr(setup_wizard.sys, "platform", "darwin")
    monkeypatch.setattr(
        setup_wizard.subprocess, "run", lambda *_a, **_k: run_result("17 noise")
    )
    assert setup_wizard._listener_pid_for_loopback_port(18775) == 17
    monkeypatch.setattr(
        setup_wizard.subprocess, "run", lambda *_a, **_k: run_result("17 18")
    )
    assert setup_wizard._listener_pid_for_loopback_port(18775) is None

    monkeypatch.setattr(setup_wizard.sys, "platform", "linux")
    monkeypatch.setattr(
        setup_wizard.subprocess,
        "run",
        lambda *_a, **_k: run_result('users:(("partyops",pid=21,fd=7))'),
    )
    assert setup_wizard._listener_pid_for_loopback_port(18775) == 21
    monkeypatch.setattr(
        setup_wizard.subprocess,
        "run",
        lambda *_a, **_k: run_result("pid=21 pid=22"),
    )
    assert setup_wizard._listener_pid_for_loopback_port(18775) is None
    monkeypatch.setattr(
        setup_wizard.subprocess,
        "run",
        lambda *_a, **_k: (_ for _ in ()).throw(OSError("missing")),
    )
    assert setup_wizard._listener_pid_for_loopback_port(18775) is None


def test_windows_listener_port_inventory_filters_untrusted_rows(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(setup_wizard.os, "name", "nt")
    output = "\n".join(
        [
            "short",
            "UDP 127.0.0.1:18775 *:* 42",
            "TCP 127.0.0.1:18770 0.0.0.0:0 ESTABLISHED 42",
            "TCP 127.0.0.1:18771 0.0.0.0:0 LISTENING bad",
            "TCP 127.0.0.1:18772 0.0.0.0:0 LISTENING 41",
            "TCP broken 0.0.0.0:0 LISTENING 42",
            "TCP 192.168.1.2:18773 0.0.0.0:0 LISTENING 42",
            "TCP 127.0.0.1:80 0.0.0.0:0 LISTENING 42",
            "TCP 127.0.0.1:18775 0.0.0.0:0 LISTENING 42",
            "TCP [::1]:18776 [::]:0 LISTENING 42",
        ]
    )
    monkeypatch.setattr(
        setup_wizard.subprocess, "run", lambda *_args, **_kwargs: run_result(output)
    )
    assert setup_wizard._loopback_listener_ports_for_pid(42) == {18775, 18776}
    assert setup_wizard._loopback_listener_ports_for_pid(0) == set()
    monkeypatch.setattr(setup_wizard.os, "name", "posix")
    assert setup_wizard._loopback_listener_ports_for_pid(42) == set()


def test_windows_listener_port_inventory_tolerates_command_failure(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(setup_wizard.os, "name", "nt")
    monkeypatch.setattr(
        setup_wizard.subprocess,
        "run",
        lambda *_args, **_kwargs: (_ for _ in ()).throw(OSError("netstat missing")),
    )
    assert setup_wizard._loopback_listener_ports_for_pid(42) == set()


def test_process_executable_matches_darwin_and_linux_matrix(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    expected = tmp_path / "partyops"
    expected.write_bytes(b"exe")
    assert not setup_wizard._process_executable_matches(0, expected)

    import ctypes

    monkeypatch.setattr(setup_wizard.sys, "platform", "darwin")
    monkeypatch.setattr(ctypes, "CDLL", lambda *_a, **_k: (_ for _ in ()).throw(OSError()))
    assert not setup_wizard._process_executable_matches(10, expected)

    class ProcPidPath:
        argtypes = None
        restype = None

        def __init__(self, value: bytes, length: int):
            self.value = value
            self.length = length

        def __call__(self, _pid, buffer, _size):
            if self.length > 0:
                buffer.value = self.value
            return self.length

    class LibProc:
        def __init__(self, proc):
            self.proc_pidpath = proc

    monkeypatch.setattr(ctypes, "CDLL", lambda *_a, **_k: LibProc(ProcPidPath(b"", 0)))
    assert not setup_wizard._process_executable_matches(10, expected)
    encoded = os.fsencode(str(expected.resolve()))
    monkeypatch.setattr(
        ctypes, "CDLL", lambda *_a, **_k: LibProc(ProcPidPath(encoded, len(encoded)))
    )
    assert setup_wizard._process_executable_matches(10, expected)

    native_path = type(tmp_path)
    monkeypatch.setattr(setup_wizard.sys, "platform", "linux")
    monkeypatch.setattr(setup_wizard.os, "name", "posix")

    def matching_path(value):
        if str(value).startswith("/proc/"):
            return SimpleNamespace(resolve=lambda: expected.resolve())
        return native_path(value)

    monkeypatch.setattr(setup_wizard, "Path", matching_path)
    assert setup_wizard._process_executable_matches(10, expected)

    def broken_path(value):
        if str(value).startswith("/proc/"):
            return SimpleNamespace(resolve=lambda: (_ for _ in ()).throw(OSError()))
        return native_path(value)

    monkeypatch.setattr(setup_wizard, "Path", broken_path)
    assert not setup_wizard._process_executable_matches(10, expected)


@pytest.mark.skipif(os.name != "nt", reason="仅 Windows 使用 QueryFullProcessImageNameW")
def test_windows_process_executable_path_reads_real_current_process() -> None:
    actual = setup_wizard._process_executable_path(os.getpid())
    assert actual is not None
    assert actual.is_file()
    assert actual.name.casefold() == "python.exe"
    assert setup_wizard._process_executable_path(0) is None


def write_marker(data_dir: Path, executable: Path, pid: object = 42) -> Path:
    marker = setup_wizard._personal_process_marker(data_dir)
    marker.write_text(
        json.dumps({"pid": pid, "executable": str(executable)}), encoding="utf-8"
    )
    return marker


def test_personal_process_owned_marker_matrix(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    executable = tmp_path / "partyops"
    executable.write_bytes(b"exe")
    monkeypatch.setattr(setup_wizard, "_executable", lambda _name: executable)
    assert not setup_wizard._personal_process_is_owned(tmp_path)

    marker = setup_wizard._personal_process_marker(tmp_path)
    marker.write_text("broken", encoding="utf-8")
    assert not setup_wizard._personal_process_is_owned(tmp_path)
    write_marker(tmp_path, tmp_path / "other")
    assert not setup_wizard._personal_process_is_owned(tmp_path)

    write_marker(tmp_path, executable)
    monkeypatch.setattr(setup_wizard, "_process_executable_matches", lambda *_a: False)
    assert not setup_wizard._personal_process_is_owned(tmp_path)
    monkeypatch.setattr(setup_wizard, "_process_executable_matches", lambda *_a: True)
    assert setup_wizard._personal_process_is_owned(tmp_path)


def test_recover_legacy_personal_marker_requires_three_proofs(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    executable = tmp_path / "partyops"
    executable.write_bytes(b"exe")
    monkeypatch.setattr(setup_wizard, "_executable", lambda _name: executable)
    monkeypatch.setattr(setup_wizard, "_listener_pid_for_loopback_port", lambda _p: None)
    assert not setup_wizard._recover_legacy_personal_process_marker(tmp_path, 18775)

    monkeypatch.setattr(setup_wizard, "_listener_pid_for_loopback_port", lambda _p: 42)
    assert not setup_wizard._recover_legacy_personal_process_marker(tmp_path, 18775)
    lock = tmp_path / ".partyops-instance.lock"
    lock.write_text("41", encoding="ascii")
    assert not setup_wizard._recover_legacy_personal_process_marker(tmp_path, 18775)
    lock.write_text("42", encoding="ascii")
    monkeypatch.setattr(setup_wizard, "_process_executable_matches", lambda *_a: False)
    assert not setup_wizard._recover_legacy_personal_process_marker(tmp_path, 18775)

    recorded: list[int] = []
    monkeypatch.setattr(setup_wizard, "_process_executable_matches", lambda *_a: True)
    monkeypatch.setattr(
        setup_wizard, "_record_personal_pid", lambda _data, pid: recorded.append(pid)
    )
    assert setup_wizard._recover_legacy_personal_process_marker(tmp_path, 18775)
    assert recorded == [42]


def test_stop_personal_process_missing_corrupt_and_identity_mismatch(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    executable = tmp_path / "partyops"
    executable.write_bytes(b"exe")
    monkeypatch.setattr(setup_wizard, "_executable", lambda _name: executable)
    monkeypatch.setattr(
        setup_wizard.socket,
        "create_connection",
        lambda *_a, **_k: (_ for _ in ()).throw(ConnectionRefusedError()),
    )
    assert not setup_wizard._stop_personal_process_for_data_migration(tmp_path, 18775)

    class Connected:
        def __enter__(self):
            return self

        def __exit__(self, *_args):
            return False

    monkeypatch.setattr(setup_wizard.socket, "create_connection", lambda *_a, **_k: Connected())
    with pytest.raises(ValueError, match="缺少受控进程标记"):
        setup_wizard._stop_personal_process_for_data_migration(tmp_path, 18775)

    marker = setup_wizard._personal_process_marker(tmp_path)
    marker.write_text("broken", encoding="utf-8")
    with pytest.raises(ValueError, match="标记损坏"):
        setup_wizard._stop_personal_process_for_data_migration(tmp_path, 18775)

    write_marker(tmp_path, tmp_path / "other")
    assert not setup_wizard._stop_personal_process_for_data_migration(tmp_path, 18775)
    assert not marker.exists()


def test_stop_personal_process_exit_escalation_and_failure(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    executable = tmp_path / "partyops"
    executable.write_bytes(b"exe")
    monkeypatch.setattr(setup_wizard, "_executable", lambda _name: executable)

    write_marker(tmp_path, executable)
    monkeypatch.setattr(setup_wizard, "_process_executable_matches", lambda *_a: True)
    monkeypatch.setattr(
        setup_wizard.os,
        "kill",
        lambda *_a: (_ for _ in ()).throw(ProcessLookupError()),
    )
    assert not setup_wizard._stop_personal_process_for_data_migration(tmp_path, 18775)

    write_marker(tmp_path, executable)
    states = iter([True, True, False, False, False])
    monkeypatch.setattr(
        setup_wizard, "_process_executable_matches", lambda *_a: next(states)
    )
    monkeypatch.setattr(setup_wizard.os, "kill", lambda *_a: None)
    monkeypatch.setattr(setup_wizard.time, "sleep", lambda *_a: None)
    assert setup_wizard._stop_personal_process_for_data_migration(tmp_path, 18775)

    write_marker(tmp_path, executable)
    states = iter([True, True, False])
    monkeypatch.setattr(
        setup_wizard, "_process_executable_matches", lambda *_a: next(states)
    )
    ticks = iter([0.0, 21.0])
    monkeypatch.setattr(setup_wizard.time, "monotonic", lambda: next(ticks))
    monkeypatch.setattr(setup_wizard, "Path", type(tmp_path))
    monkeypatch.setattr(setup_wizard.os, "name", "posix")
    monkeypatch.setattr(setup_wizard.signal, "SIGKILL", 9, raising=False)
    killed: list[int] = []
    monkeypatch.setattr(setup_wizard.os, "kill", lambda _pid, sig: killed.append(sig))
    assert setup_wizard._stop_personal_process_for_data_migration(tmp_path, 18775)
    assert len(killed) == 2

    write_marker(tmp_path, executable)
    monkeypatch.setattr(setup_wizard, "_process_executable_matches", lambda *_a: True)
    ticks = iter([0.0, 21.0])
    monkeypatch.setattr(setup_wizard.time, "monotonic", lambda: next(ticks))
    with pytest.raises(ValueError, match="未能安全停止"):
        setup_wizard._stop_personal_process_for_data_migration(tmp_path, 18775)


def test_record_port_selection_and_atomic_rewrite(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    executable = tmp_path / "partyops"
    executable.write_bytes(b"exe")
    monkeypatch.setattr(setup_wizard, "_executable", lambda _name: executable)
    setup_wizard._record_personal_pid(tmp_path, 0)
    assert not setup_wizard._personal_process_marker(tmp_path).exists()
    setup_wizard._record_personal_process(tmp_path, None)
    setup_wizard._record_personal_process(tmp_path, SimpleNamespace(pid=0))
    setup_wizard._record_personal_process(tmp_path, SimpleNamespace(pid=42))
    assert json.loads(setup_wizard._personal_process_marker(tmp_path).read_text())["pid"] == 42

    monkeypatch.setattr(
        setup_wizard,
        "_loopback_port_available",
        lambda port: port == 18777,
    )
    assert setup_wizard._select_alternative_personal_port(18775) == 18777
    monkeypatch.setattr(setup_wizard, "_loopback_port_available", lambda _p: False)
    with pytest.raises(HostStartupError):
        setup_wizard._select_alternative_personal_port(18775)

    missing = tmp_path / "missing.env"
    with pytest.raises(ValueError, match="受控普通文件"):
        setup_wizard._rewrite_personal_port(missing, 18779)
    config = tmp_path / "personal.env"
    config.write_text(
        "# keep\nPARTYOPS_PORT=18775\nPARTYOPS_BOOTSTRAP_TOKEN=secret\n",
        encoding="utf-8",
    )
    monkeypatch.setattr(
        setup_wizard,
        "load_host_environment",
        lambda path: {"content": path.read_text(encoding="utf-8")},
    )
    result = setup_wizard._rewrite_personal_port(config, 18779)
    assert "PARTYOPS_PORT=18779" in result["content"]
    assert "PARTYOPS_AGENT_PORT=18780" in result["content"]
    assert "PARTYOPS_BOOTSTRAP_TOKEN=secret" in result["content"]


def test_windows_discovers_cross_version_instance_from_lock_and_health(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    """端口被旧版误改后，仍须从实例锁找回真实 rc.4 端口。"""

    old_executable = tmp_path / "旧版" / "PartyOps.exe"
    monkeypatch.setattr(setup_wizard.os, "name", "nt")
    monkeypatch.setattr(
        setup_wizard, "_windows_data_lock_owner_pids", lambda _path: {16908}
    )
    monkeypatch.setattr(
        setup_wizard,
        "_process_executable_path",
        lambda pid: old_executable if pid == 16908 else None,
    )
    monkeypatch.setattr(
        setup_wizard,
        "_loopback_listener_ports_for_pid",
        lambda pid: {18768, 18775} if pid == 16908 else set(),
    )
    monkeypatch.setattr(
        setup_wizard,
        "_personal_health_version",
        lambda port: "1.4.5-rc.4" if port == 18775 else "",
    )

    assert setup_wizard._discover_running_windows_personal(tmp_path, 18776) == (
        16908,
        18775,
        old_executable,
        "1.4.5-rc.4",
    )


@pytest.mark.skipif(os.name != "nt", reason="仅 Windows 提供 Restart Manager")
def test_windows_restart_manager_reports_real_instance_lock_owner(
    tmp_path: Path,
) -> None:
    """真实内核句柄回归，避免只在 mock 中验证跨版本发现。"""

    import msvcrt

    lock = tmp_path / ".partyops-instance.lock"
    with lock.open("w+b") as handle:
        handle.write(str(os.getpid()).encode("ascii"))
        handle.flush()
        handle.seek(0)
        msvcrt.locking(handle.fileno(), msvcrt.LK_NBLCK, 1)
        try:
            assert os.getpid() in setup_wizard._windows_data_lock_owner_pids(tmp_path)
        finally:
            handle.seek(0)
            msvcrt.locking(handle.fileno(), msvcrt.LK_UNLCK, 1)


def test_windows_restart_manager_failure_matrix(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    import ctypes

    lock = tmp_path / ".partyops-instance.lock"
    lock.write_text("fixture", encoding="ascii")
    monkeypatch.setattr(setup_wizard.os, "name", "posix")
    assert setup_wizard._windows_data_lock_owner_pids(tmp_path) == set()
    monkeypatch.setattr(setup_wizard.os, "name", "nt")

    class Manager:
        def __init__(self, stage: str):
            self.stage = stage

        def RmStartSession(self, *_args):
            return 5 if self.stage == "start" else 0

        def RmRegisterResources(self, *_args):
            return 5 if self.stage == "register" else 0

        def RmGetList(self, _session, needed, _count, processes, _reasons):
            if self.stage == "empty":
                return 0
            if self.stage == "unexpected":
                return 5
            if processes is None:
                ctypes.cast(needed, ctypes.POINTER(ctypes.c_uint)).contents.value = 1
                return 234
            return 5

        def RmEndSession(self, *_args):
            return 0

    for stage in ("start", "register", "empty", "unexpected", "second"):
        monkeypatch.setattr(ctypes, "WinDLL", lambda *_args, s=stage: Manager(s))
        assert setup_wizard._windows_data_lock_owner_pids(tmp_path) == set()

    monkeypatch.setattr(
        ctypes,
        "WinDLL",
        lambda *_args: (_ for _ in ()).throw(OSError("Restart Manager unavailable")),
    )
    assert setup_wizard._windows_data_lock_owner_pids(tmp_path) == set()


def test_personal_health_version_accepts_only_full_personal_contract(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    class Response:
        def __init__(self, payload: bytes):
            self.payload = payload

        def __enter__(self):
            return self

        def __exit__(self, *_args):
            return False

        def read(self) -> bytes:
            return self.payload

    valid = json.dumps(
        {
            "status": "ok",
            "app_version": "1.4.5-rc.4",
            "mode": "personal",
            "sqlite": {"safe_version": True, "fts5": True},
        }
    ).encode("utf-8")
    responses = iter([Response(valid), Response(b"{}"), Response(b"{")])
    monkeypatch.setattr(
        setup_wizard.urllib.request,
        "urlopen",
        lambda *_args, **_kwargs: next(responses),
    )
    assert setup_wizard._personal_health_version(18775) == "1.4.5-rc.4"
    assert setup_wizard._personal_health_version(18775) == ""
    assert setup_wizard._personal_health_version(18775) == ""


def test_windows_personal_discovery_rejects_ambiguous_or_invalid_owners(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    monkeypatch.setattr(setup_wizard.os, "name", "posix")
    assert setup_wizard._discover_running_windows_personal(tmp_path, 18775) is None
    monkeypatch.setattr(setup_wizard.os, "name", "nt")
    monkeypatch.setattr(
        setup_wizard, "_windows_data_lock_owner_pids", lambda _path: {1, 2, 3}
    )
    wrong = tmp_path / "python.exe"
    partyops = tmp_path / "PartyOps.exe"
    monkeypatch.setattr(
        setup_wizard,
        "_process_executable_path",
        lambda pid: None if pid == 1 else wrong if pid == 2 else partyops,
    )
    monkeypatch.setattr(
        setup_wizard,
        "_loopback_listener_ports_for_pid",
        lambda pid: {18775, 18776} if pid == 3 else set(),
    )
    monkeypatch.setattr(
        setup_wizard, "_personal_health_version", lambda _port: "1.4.5-rc.4"
    )
    assert setup_wizard._discover_running_windows_personal(tmp_path, 18774) is None
    assert setup_wizard._discover_running_windows_personal(tmp_path, 18775) == (
        3,
        18775,
        partyops,
        "1.4.5-rc.4",
    )

    monkeypatch.setattr(
        setup_wizard, "_personal_health_version", lambda _port: ""
    )
    assert setup_wizard._discover_running_windows_personal(tmp_path, 18775) is None


def test_cross_version_stop_requires_lock_listener_and_actual_executable(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    current = tmp_path / "当前版" / "PartyOps.exe"
    old = tmp_path / "旧版" / "PartyOps.exe"
    current.parent.mkdir()
    old.parent.mkdir()
    current.write_bytes(b"current")
    old.write_bytes(b"old")
    write_marker(tmp_path, old, pid=16908)
    monkeypatch.setattr(setup_wizard, "_executable", lambda _name: current)
    states = iter([True, True, False, False, False])
    monkeypatch.setattr(
        setup_wizard, "_process_executable_matches", lambda *_args: next(states)
    )
    monkeypatch.setattr(
        setup_wizard, "_listener_pid_for_loopback_port", lambda _port: 16908
    )
    monkeypatch.setattr(
        setup_wizard, "_windows_data_lock_owner_pids", lambda _path: {16908}
    )
    killed: list[int] = []
    monkeypatch.setattr(
        setup_wizard.os, "kill", lambda pid, _signal: killed.append(pid)
    )
    monkeypatch.setattr(setup_wizard.time, "sleep", lambda *_args: None)

    assert setup_wizard._stop_personal_process_for_data_migration(tmp_path, 18775)
    assert killed == [16908]
    assert not setup_wizard._personal_process_marker(tmp_path).exists()


def test_cross_version_stop_rejects_each_missing_ownership_proof(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    current = tmp_path / "当前版" / "PartyOps.exe"
    old = tmp_path / "旧版" / "PartyOps.exe"
    current.parent.mkdir()
    old.parent.mkdir()
    current.write_bytes(b"current")
    old.write_bytes(b"old")
    monkeypatch.setattr(setup_wizard, "_executable", lambda _name: current)
    monkeypatch.setattr(
        setup_wizard, "_process_executable_matches", lambda *_args: True
    )

    write_marker(tmp_path, old, pid=16908)
    monkeypatch.setattr(
        setup_wizard, "_listener_pid_for_loopback_port", lambda _port: 99
    )
    monkeypatch.setattr(
        setup_wizard, "_windows_data_lock_owner_pids", lambda _path: {16908}
    )
    assert not setup_wizard._stop_personal_process_for_data_migration(
        tmp_path, 18775
    )

    write_marker(tmp_path, old, pid=16908)
    monkeypatch.setattr(
        setup_wizard, "_listener_pid_for_loopback_port", lambda _port: 16908
    )
    monkeypatch.setattr(
        setup_wizard, "_windows_data_lock_owner_pids", lambda _path: set()
    )
    assert not setup_wizard._stop_personal_process_for_data_migration(
        tmp_path, 18775
    )


def test_launch_personal_cross_version_handoff_repairs_port_before_spawn(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    """复现截图路径：旧版持锁、配置已误改端口时必须原位接管。"""

    data_dir = tmp_path / "个人数据"
    data_dir.mkdir()
    config = tmp_path / "personal.env"
    config.write_text("PARTYOPS_PORT=18776\n", encoding="utf-8")
    current = tmp_path / "当前版" / "PartyOps.exe"
    old = tmp_path / "旧版" / "PartyOps.exe"
    current.parent.mkdir()
    old.parent.mkdir()
    current.write_bytes(b"current")
    old.write_bytes(b"old")
    monkeypatch.setattr(
        setup_wizard,
        "load_host_environment",
        lambda _path: {
            "PARTYOPS_PORT": "18776",
            "PARTYOPS_DATA_DIR": str(data_dir),
        },
    )
    monkeypatch.setattr(
        setup_wizard,
        "_preflight_personal_runtime_access",
        lambda *_args: current,
    )
    monkeypatch.setattr(
        setup_wizard, "_windows_data_lock_owner_pids", lambda _path: {16908}
    )
    monkeypatch.setattr(
        setup_wizard,
        "_discover_running_windows_personal",
        lambda *_args: (16908, 18775, old, "1.4.5-rc.4"),
    )
    rewritten: list[int] = []
    monkeypatch.setattr(
        setup_wizard,
        "_rewrite_personal_port",
        lambda _path, port: rewritten.append(port)
        or {
            "PARTYOPS_PORT": str(port),
            "PARTYOPS_DATA_DIR": str(data_dir),
        },
    )
    markers: list[tuple[int, Path]] = []
    monkeypatch.setattr(
        setup_wizard,
        "_write_personal_process_marker",
        lambda _path, pid, executable: markers.append((pid, executable)),
    )

    class Connected:
        def __enter__(self):
            return self

        def __exit__(self, *_args):
            return False

    monkeypatch.setattr(
        setup_wizard.socket, "create_connection", lambda *_args, **_kwargs: Connected()
    )
    waits = iter(
        [
            HostStartupError(
                setup_wizard.RUNTIME_VERSION_MISMATCH,
                "旧版本仍在运行",
                detail="1.4.5-rc.4",
            ),
            "http://127.0.0.1:18775",
        ]
    )

    def wait(*_args, **_kwargs):
        result = next(waits)
        if isinstance(result, Exception):
            raise result
        return result

    monkeypatch.setattr(setup_wizard, "wait_for_host_health", wait)
    stopped: list[tuple[Path, int]] = []
    monkeypatch.setattr(
        setup_wizard,
        "_stop_personal_process_for_data_migration",
        lambda path, port: stopped.append((path, port)) is None or True,
    )
    monkeypatch.setattr(
        setup_wizard,
        "_spawn",
        lambda *_args, **_kwargs: SimpleNamespace(pid=27620),
    )
    recorded: list[int] = []
    monkeypatch.setattr(
        setup_wizard,
        "_record_personal_process",
        lambda _path, process: recorded.append(process.pid),
    )
    monkeypatch.setattr(
        setup_wizard,
        "_personal_process_is_owned",
        lambda *_args: (_ for _ in ()).throw(AssertionError("已发现实例无需旧路径判断")),
    )

    assert setup_wizard.launch_personal(config) == "http://127.0.0.1:18775"
    assert rewritten == [18775]
    assert markers == [(16908, old)]
    assert stopped == [(data_dir, 18775)]
    assert recorded == [27620]


def test_launch_personal_never_overwrites_marker_before_new_process_is_healthy(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    """新进程撞锁或提前退出时，旧版进程标记必须保持不变。"""

    data_dir = tmp_path / "个人数据"
    data_dir.mkdir()
    config = tmp_path / "personal.env"
    config.write_text("PARTYOPS_PORT=18775\n", encoding="utf-8")
    executable = tmp_path / "PartyOps.exe"
    executable.write_bytes(b"MZ")
    monkeypatch.setattr(
        setup_wizard,
        "load_host_environment",
        lambda _path: {
            "PARTYOPS_PORT": "18775",
            "PARTYOPS_DATA_DIR": str(data_dir),
        },
    )
    monkeypatch.setattr(
        setup_wizard,
        "_preflight_personal_runtime_access",
        lambda *_args: executable,
    )
    monkeypatch.setattr(
        setup_wizard, "_windows_data_lock_owner_pids", lambda _path: set()
    )
    monkeypatch.setattr(
        setup_wizard, "_discover_running_windows_personal", lambda *_args: None
    )
    monkeypatch.setattr(
        setup_wizard.socket,
        "create_connection",
        lambda *_args, **_kwargs: (_ for _ in ()).throw(ConnectionRefusedError()),
    )
    monkeypatch.setattr(
        setup_wizard, "_spawn", lambda *_args, **_kwargs: SimpleNamespace(pid=27620)
    )
    monkeypatch.setattr(
        setup_wizard,
        "wait_for_host_health",
        lambda *_args, **_kwargs: (_ for _ in ()).throw(
            HostStartupError(
                setup_wizard.INSTANCE_ALREADY_RUNNING,
                "旧实例仍在运行",
            )
        ),
    )
    recorded: list[int] = []
    monkeypatch.setattr(
        setup_wizard,
        "_record_personal_process",
        lambda _path, process: recorded.append(process.pid),
    )

    with pytest.raises(HostStartupError) as failure:
        setup_wizard.launch_personal(config)
    assert failure.value.code == setup_wizard.INSTANCE_ALREADY_RUNNING
    assert recorded == []


def test_launch_personal_lock_owner_without_health_fails_before_spawn(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    data_dir = tmp_path / "个人数据"
    data_dir.mkdir()
    config = tmp_path / "personal.env"
    config.write_text("PARTYOPS_PORT=18775\n", encoding="utf-8")
    executable = tmp_path / "PartyOps.exe"
    executable.write_bytes(b"MZ")
    monkeypatch.setattr(
        setup_wizard,
        "load_host_environment",
        lambda _path: {
            "PARTYOPS_PORT": "18775",
            "PARTYOPS_DATA_DIR": str(data_dir),
        },
    )
    monkeypatch.setattr(
        setup_wizard,
        "_preflight_personal_runtime_access",
        lambda *_args: executable,
    )
    monkeypatch.setattr(
        setup_wizard, "_windows_data_lock_owner_pids", lambda _path: {16908}
    )
    monkeypatch.setattr(
        setup_wizard, "_discover_running_windows_personal", lambda *_args: None
    )
    monkeypatch.setattr(
        setup_wizard,
        "_spawn",
        lambda *_args, **_kwargs: (_ for _ in ()).throw(
            AssertionError("持锁实例未确认前禁止启动第二个进程")
        ),
    )

    with pytest.raises(HostStartupError) as failure:
        setup_wizard.launch_personal(config)
    assert failure.value.code == setup_wizard.INSTANCE_ALREADY_RUNNING
    assert "未改动" in failure.value.detail


def test_loopback_port_probe_success_and_failure(
    monkeypatch: pytest.MonkeyPatch
) -> None:
    class Probe:
        def __init__(self, fail: bool):
            self.fail = fail
            self.options: list[tuple] = []

        def __enter__(self):
            return self

        def __exit__(self, *_args):
            return False

        def setsockopt(self, *args):
            self.options.append(args)

        def bind(self, _address):
            if self.fail:
                raise OSError("busy")

    success = Probe(False)
    monkeypatch.setattr(setup_wizard.socket, "socket", lambda *_a: success)
    assert setup_wizard._loopback_port_available(18775)
    monkeypatch.setattr(setup_wizard.os, "name", "posix")
    posix_success = Probe(False)
    monkeypatch.setattr(setup_wizard.socket, "socket", lambda *_a: posix_success)
    assert setup_wizard._loopback_port_available(18775)
    assert posix_success.options == []
    failure = Probe(True)
    monkeypatch.setattr(setup_wizard.socket, "socket", lambda *_a: failure)
    assert not setup_wizard._loopback_port_available(18775)
