"""安装探针隔离测试：主机上绝不执行 Guest 包管理器或 sudo。"""
import importlib.util
import json
import subprocess
import sys
from pathlib import Path
from types import SimpleNamespace

import pytest

HERE = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("guest_probe", HERE / "guest/linux-installed-probe.py")
probe = importlib.util.module_from_spec(spec)
spec.loader.exec_module(probe)


@pytest.fixture
def guest(tmp_path, monkeypatch):
    marker = tmp_path / "marker.json"
    marker.write_text(json.dumps({"uuid": "owned-uuid", "purpose": "disposable-qa"}))
    boot = tmp_path / "boot"
    boot.write_text("boot-id")
    real_path = Path
    def path(value):
        return {"/etc/partyops-vm-lab.json": marker, "/proc/sys/kernel/random/boot_id": boot}.get(str(value), real_path(value))
    monkeypatch.setattr(probe, "Path", path)
    monkeypatch.setattr(probe.os, "getuid", lambda: 1000, raising=False)
    monkeypatch.setattr(probe.platform, "machine", lambda: "x86_64")
    package = tmp_path / "candidate.rpm"
    package.write_bytes(b"unit-test-package-not-real")
    output = tmp_path / "evidence"
    def arguments(suffix="rpm", digest=None):
        selected = package.with_suffix("." + suffix)
        selected.write_bytes(package.read_bytes())
        monkeypatch.setattr(sys, "argv", ["probe", "--uuid", "owned-uuid", "--package", str(selected),
                                          "--sha256", digest or probe.digest(selected), "--arch", "x86_64", "--output", str(output)])
    arguments()
    return package, output, marker, arguments


@pytest.mark.parametrize("extension", ["rpm", "deb"])
def test_install_probe_is_never_full_gate(guest, monkeypatch, extension):
    _, output, _, arguments = guest
    arguments(extension)
    calls = []
    def invoke(command, **kwargs):
        calls.append(command)
        kwargs["stdout"].write("unit-test-evidence")
        return SimpleNamespace(returncode=0)
    monkeypatch.setattr(probe.subprocess, "run", invoke)
    assert probe.main() == 0
    result = json.loads((output / "installed-probe.json").read_text())
    assert result["runtime_environment_passed"] is False
    assert result["status"] == "partial"
    assert any(command[0] == "sudo" for command in calls)
    assert "--nodeps" not in str(calls)


@pytest.mark.parametrize("reason", ["uid", "uuid", "arch", "hash", "format"])
def test_refuses_wrong_guest_and_package(guest, monkeypatch, reason):
    _, _, marker, arguments = guest
    if reason == "uid":
        monkeypatch.setattr(probe.os, "getuid", lambda: 0)
    elif reason == "uuid":
        marker.write_text("{}")
    elif reason == "arch":
        monkeypatch.setattr(probe.platform, "machine", lambda: "aarch64")
    elif reason == "hash":
        arguments(digest="f" * 64)
    else:
        arguments("exe")
    with pytest.raises(RuntimeError):
        probe.main()


def test_metadata_failure_stops_install(guest, monkeypatch):
    monkeypatch.setattr(probe.subprocess, "run", lambda *a, **kw: SimpleNamespace(returncode=2))
    assert probe.main() == 2
    result = json.loads((guest[1] / "installed-probe.json").read_text())
    assert len(result["steps"]) == 1
    assert result["status"] == "failed"


def test_install_timeout_retains_failed_evidence(guest, monkeypatch):
    def invoke(command, **kwargs):
        if command[0] == "sudo" and "-Uvh" in command:
            raise subprocess.TimeoutExpired(command, 900)
        return SimpleNamespace(returncode=0)
    monkeypatch.setattr(probe.subprocess, "run", invoke)
    assert probe.main() == 2
    result = json.loads((guest[1] / "installed-probe.json").read_text())
    assert any(step.get("error") == "TIMEOUT" for step in result["steps"])
    assert not any(step["id"] == "package-selftest-standard-user" for step in result["steps"])
