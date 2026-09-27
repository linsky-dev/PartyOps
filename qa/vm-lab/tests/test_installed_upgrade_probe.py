"""升级探针只读核验的反例；所有合成材料仅用于 pytest 临时目录。"""
import hashlib
import importlib.util
import json
import sqlite3
import zipfile
from pathlib import Path
from types import SimpleNamespace

import pytest


def load_probe():
    spec = importlib.util.spec_from_file_location(
        "installed_upgrade_probe", Path(__file__).resolve().parents[1] / "guest/installed-upgrade-probe.py")
    probe = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(probe)
    return probe


@pytest.mark.parametrize("fault", [None, "revision", "user", "attachment"])
def test_database_or_attachment_mismatch_fails(tmp_path, fault):
    probe = load_probe()
    with sqlite3.connect(tmp_path / "partyops.db") as database:
        database.execute("CREATE TABLE alembic_version (version_num TEXT)")
        database.execute("INSERT INTO alembic_version VALUES (?)", ("0023" if fault == "revision" else "0026",))
        database.execute("CREATE TABLE users (id TEXT, display_name TEXT)")
        database.execute("INSERT INTO users VALUES (?, ?)",
                         ("rc4-native-upgrade-admin", "错误" if fault == "user" else "原生覆盖升级管理员"))
    (tmp_path / "attachments").mkdir()
    attachment = tmp_path / "attachments/preserved.txt"
    attachment.write_bytes(b"original")
    expected = "0" * 64 if fault == "attachment" else probe.digest(attachment)
    if fault:
        with pytest.raises(RuntimeError):
            probe.inspect_data(tmp_path, "0026", expected)
    else:
        assert probe.inspect_data(tmp_path, "0026", expected)["fixture_user_preserved"] is True


@pytest.mark.parametrize("fault", [None, "missing", "duplicate", "revision", "files", "database", "hash", "size"])
def test_backup_manifest_cannot_hide_corruption(tmp_path, fault):
    probe = load_probe()
    root = tmp_path / "backups"
    root.mkdir()
    payload = b"synthetic-database"
    item = {"path": "database/partyops.db" if fault != "database" else "attachment.txt",
            "size": 1 if fault == "size" else len(payload),
            "sha256": "0" * 64 if fault == "hash" else hashlib.sha256(payload).hexdigest()}
    manifest = {"schema_version": "0026" if fault == "revision" else "0023",
                "files": [] if fault == "files" else [item]}
    if fault != "missing":
        for index in range(2 if fault == "duplicate" else 1):
            with zipfile.ZipFile(root / f"PartyOps-pre-upgrade-{index}.partyops-backup", "w") as archive:
                archive.writestr("manifest.json", json.dumps(manifest))
                archive.writestr(item["path"], payload)
    if fault:
        with pytest.raises(RuntimeError):
            probe.inspect_backups(tmp_path, "0023")
    else:
        result = probe.inspect_backups(tmp_path, "0023")
        assert result["verified_files"] == 1 and result["schema"] == "0023"


@pytest.mark.parametrize("fault", [None, "ownership", "root", "platform", "outside", "nested", "hash",
                                   "early_exit", "timeout", "health", "slow_stop", "recheck"])
def test_installed_process_orchestration(tmp_path, monkeypatch, fault):
    probe = load_probe()
    executable = tmp_path / "program"
    executable.write_bytes(b"synthetic-program")
    version = tmp_path / "VERSION"
    version.write_text("synthetic-version")
    boot = tmp_path / "boot-id"
    boot.write_text("synthetic-boot-id")
    marker = tmp_path / "marker"
    marker.write_text(json.dumps({"uuid": "wrong" if fault == "ownership" else "owned", "purpose": "disposable-qa"}))
    data = tmp_path / "data"
    data.mkdir()
    args = SimpleNamespace(uuid="owned", data=data, output=tmp_path / "evidence",
                           original_schema="old", expected_schema="new", attachment_sha256="a" * 64,
                           executable_sha256="0" * 64 if fault == "hash" else probe.digest(executable),
                           recheck=fault == "recheck")
    if fault == "outside":
        args.output = tmp_path.parent / "outside"
    if fault == "nested":
        args.output = data / "evidence"

    class SandboxPath:
        def __new__(cls, value):
            return {"/opt/partyops/partyops": executable, "/opt/partyops/VERSION": version,
                    "/proc/sys/kernel/random/boot_id": boot, "/etc/partyops-vm-lab.json": marker}.get(str(value), Path(value))

        @staticmethod
        def home():
            return tmp_path

    monkeypatch.setattr(probe, "Path", SandboxPath)
    monkeypatch.setattr(probe.platform, "system", lambda: "Darwin" if fault == "platform" else "Linux")
    monkeypatch.setattr(probe.os, "getuid", lambda: 0 if fault == "root" else 1000, raising=False)
    monkeypatch.setattr(probe, "inspect_data", lambda data, revision, sha: {"schema": revision})
    monkeypatch.setattr(probe, "inspect_backups", lambda data, schema: {"schema": schema})
    monkeypatch.setenv("LD_LIBRARY_PATH", "forbidden")
    monkeypatch.setenv("PARTYOPS_DATA_DIR", "forbidden")
    monkeypatch.setattr(probe.time, "sleep", lambda _: None)
    ticks = iter([0, 301])
    monkeypatch.setattr(probe.time, "monotonic", lambda: next(ticks))

    class Process:
        stopped = False
        killed = False

        def poll(self):
            return 1 if fault == "early_exit" or self.stopped else None

        def terminate(self):
            self.stopped = True

        def wait(self, timeout):
            if fault == "slow_stop" and not self.killed:
                raise probe.subprocess.TimeoutExpired("synthetic", timeout)

        def kill(self):
            self.killed = True

    process = Process()

    def launch(command, **kwargs):
        assert command == [str(executable)]
        assert "LD_LIBRARY_PATH" not in kwargs["env"]
        assert kwargs["env"]["PARTYOPS_DATA_DIR"] == str(data)
        return process

    monkeypatch.setattr(probe.subprocess, "Popen", launch)

    class Opener:
        count = 0

        def open(self, url, timeout):
            self.count += 1
            if fault == "timeout" or self.count == 1:
                raise probe.urllib.error.URLError("synthetic-delay")
            import io
            return io.StringIO(json.dumps({"status": "bad" if fault == "health" else "ok", "mode": "host"}))

    if fault != "timeout":
        monkeypatch.setattr(probe.time, "monotonic", lambda: 0)
    monkeypatch.setattr(probe.urllib.request, "build_opener", lambda *args: Opener())
    if fault not in {None, "recheck"}:
        with pytest.raises(RuntimeError):
            probe.run(args)
    else:
        probe.run(args)
    report = args.output / "upgrade-evidence.json"
    if fault in {"ownership", "root", "platform", "outside", "nested", "hash"}:
        assert not report.exists()
    else:
        result = json.loads(report.read_text(encoding="utf-8"))
        assert result["status"] == ("passed" if fault in {None, "recheck"} else "failed")
        assert result["runtime_environment_passed"] is False


def test_cli_arguments_are_forwarded(tmp_path, monkeypatch):
    probe = load_probe()
    monkeypatch.setattr("sys.argv", ["probe", "--uuid", "owned", "--executable-sha256", "a" * 64,
                                    "--attachment-sha256", "b" * 64, "--original-schema", "old",
                                    "--expected-schema", "new", "--data", str(tmp_path / "data"),
                                    "--output", str(tmp_path / "evidence"), "--recheck"])
    calls = []
    monkeypatch.setattr(probe, "run", calls.append)
    probe.main()
    assert calls[0].recheck is True and calls[0].expected_schema == "new"
