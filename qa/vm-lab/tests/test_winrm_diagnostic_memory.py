"""专用 Guest 配额调整只写授权属性，状态漂移必须拒绝。"""
import importlib.util
import json
import xml.etree.ElementTree as ET
from pathlib import Path
from types import SimpleNamespace

import pytest

SCRIPT = Path(__file__).resolve().parents[1] / "scripts/configure-winrm-diagnostic-memory.py"
SPEC = importlib.util.spec_from_file_location("winrm_diagnostic_memory", SCRIPT)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class Configuration:
    def __init__(self, memory="150", drift=False):
        self.memory, self.drift, self.actions = memory, drift, []

    def request(self, protocol, action, content=None):
        self.actions.append(action)
        if action == "Put":
            assert len(content) == 1
            assert content[0].tag == "{" + MODULE.winrm_memory.URI + "}MaxMemoryPerShellMB"
            self.memory = content[0].text
        idle = "1" if self.drift and "Put" in self.actions else "180000"
        return ET.fromstring(f'<Winrs xmlns="{MODULE.winrm_memory.URI}"><MaxMemoryPerShellMB>{self.memory}</MaxMemoryPerShellMB><IdleTimeout>{idle}</IdleTimeout><MaxShellRunTime>2147483647</MaxShellRunTime></Winrs>')


def test_read_only_never_puts(monkeypatch):
    server = Configuration()
    monkeypatch.setattr(MODULE.winrm_memory, "request", server.request)
    assert MODULE.winrm_memory.configure(None) == {"previous_mib": 150, "current_mib": 150, "changed": False}
    assert server.actions == ["Get"]


def test_changes_only_memory_and_verifies_other_properties(monkeypatch):
    server = Configuration()
    monkeypatch.setattr(MODULE.winrm_memory, "request", server.request)
    result = MODULE.winrm_memory.configure(None, 512)
    assert result["previous_mib"] == 150 and result["current_mib"] == 512
    assert result["other_configuration_unchanged"] and result["new_shell_required"]
    assert server.actions == ["Get", "Put", "Get"]


def test_unexpected_prior_value_rejected_before_write(monkeypatch):
    server = Configuration("1024")
    monkeypatch.setattr(MODULE.winrm_memory, "request", server.request)
    with pytest.raises(RuntimeError, match="BASELINE_CHANGED"):
        MODULE.winrm_memory.configure(None, 512)
    assert server.actions == ["Get"]


def test_unrelated_configuration_drift_cannot_be_reported_success(monkeypatch):
    server = Configuration(drift=True)
    monkeypatch.setattr(MODULE.winrm_memory, "request", server.request)
    with pytest.raises(RuntimeError, match="READBACK_MISMATCH"):
        MODULE.winrm_memory.configure(None, 512)


@pytest.mark.parametrize("memory", ["", "unlimited", "-1"])
def test_invalid_remote_value_rejected(monkeypatch, memory):
    server = Configuration(memory)
    monkeypatch.setattr(MODULE.winrm_memory, "request", server.request)
    with pytest.raises(RuntimeError, match="VALUE_INVALID"):
        MODULE.winrm_memory.configure(None, 512)
    assert server.actions == ["Get"]


@pytest.mark.parametrize("desired", [0, 149, 2048])
def test_unbounded_or_undersized_quota_rejected(monkeypatch, desired):
    server = Configuration()
    monkeypatch.setattr(MODULE.winrm_memory, "request", server.request)
    with pytest.raises(ValueError, match="OUTSIDE_LAB_BOUNDARY"):
        MODULE.winrm_memory.configure(None, desired)
    assert server.actions == ["Get"]


def test_explicit_prior_value_allows_reversible_restore(monkeypatch):
    server = Configuration("512")
    monkeypatch.setattr(MODULE.winrm_memory, "request", server.request)
    result = MODULE.winrm_memory.configure(None, 150, expected=512)
    assert result["previous_mib"] == 512 and result["current_mib"] == 150


@pytest.mark.parametrize("memory, passed", [("150", False), ("512", True)])
def test_acceptance_preflight_records_result_without_changing_guest(monkeypatch, tmp_path, memory, passed):
    server = Configuration(memory)
    monkeypatch.setattr(MODULE.winrm_memory, "request", server.request)
    monkeypatch.setattr(MODULE, "ROOT", tmp_path)
    monkeypatch.setattr(MODULE.sys, "argv", [str(SCRIPT), "--require-mib", "512"])
    monkeypatch.setattr(MODULE, "load_configuration", lambda: ({"defaults": {"primary_root": str(tmp_path)}}, {}))
    lab = SimpleNamespace(state=lambda target: {"uuid": MODULE.EXPECTED_UUID}, live=lambda state: True)
    monkeypatch.setattr(MODULE, "QemuLab", lambda *args: lab)
    monkeypatch.setattr(MODULE, "session", lambda *args: SimpleNamespace(protocol=None))
    monkeypatch.setattr(MODULE.winrm_memory, "inspect_win7_context", lambda *args: ({}, {}))
    if passed:
        MODULE.main()
    else:
        with pytest.raises(RuntimeError, match="SHELL_MEMORY_QUOTA_TOO_SMALL"):
            MODULE.main()
    report = json.loads(next(tmp_path.glob("winrm-quota-preflight-512-*.json")).read_text(encoding="utf-8"))
    assert report["transport_memory_preflight_passed"] is passed
    assert report["runtime_environment_passed"] is False
    assert server.actions == ["Get"]
