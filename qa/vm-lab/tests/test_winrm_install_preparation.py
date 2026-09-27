"""原版 Win7 配额准备的真实入口合同；只用合成 Guest，不开虚拟机。"""
from __future__ import annotations

import copy
import json
import os
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path
from types import SimpleNamespace

import pytest
import winrm_memory as memory


@pytest.fixture
def guest(tmp_path, monkeypatch):
    state = {"uuid": "11111111-2222-3333-4444-555555555555", "restore_generation": "restore-one"}
    vm = tmp_path / "vm"
    vm.mkdir()
    secret = "fixture-private-must-not-leak"
    credential = {"uuid": state["uuid"], "username": "partyopsqa", "password": secret}
    (vm / "guest-credential.local.json").write_text(json.dumps(credential), encoding="utf-8")
    lab = SimpleNamespace(root=tmp_path, matrix={"targets": {"win7-x64": {
        "os": "windows", "os_release": "7 SP1", "winrm_port": 23171}}},
        state=lambda target: copy.deepcopy(state), live=lambda state: True, vm_dir=lambda target: vm)
    system = {"os_release": "7 SP1", "os_version": "6.1.7601", "boot_id": "boot-one"}
    sid = "S-1-5-21-10-11-12-1001"
    observation = {"hardware_uuid": state["uuid"], "boot_id": "boot-one", "os_version": "6.1.7601",
                   "token_name": "QA-GUEST\\partyopsqa", "token_sid": sid, "administrator": True,
                   "local_accounts": [{"name": "partyopsqa", "sid": sid, "disabled": False}], "active_installers": []}
    job = {"query_succeeded": True, "job_memory_limit_enabled": True, "process_memory_limit_enabled": True,
           "job_memory_limit_bytes": 512 * 1024**2, "process_memory_limit_bytes": 512 * 1024**2}
    peer = SimpleNamespace(memory="150", actions=[], drift=False)

    def request(protocol, action, content=None):
        peer.actions.append(action)
        if action == "Put":
            assert len(content) == 1
            assert content[0].tag.endswith("}MaxMemoryPerShellMB")
            # Put 前必须已经留下原值及变更意图，报告不含秘密。
            intents = list(tmp_path.rglob("change-intent.json"))
            assert intents and any(json.loads(path.read_text())['previous_mib'] == 150 for path in intents)
            peer.memory = content[0].text
        idle = "1" if peer.drift and "Put" in peer.actions else "180000"
        return ET.fromstring(f'<Winrs xmlns="{memory.URI}"><MaxMemoryPerShellMB>{peer.memory}</MaxMemoryPerShellMB><IdleTimeout>{idle}</IdleTimeout></Winrs>')

    def powershell(script, timeout):
        return json.dumps(observation if script == memory.ACCOUNT_AND_PROCESS_PROBE else job)

    monkeypatch.setattr(memory, "probe", lambda *args: copy.deepcopy(system))
    monkeypatch.setattr(memory, "WinRMFiles", lambda *args: SimpleNamespace(powershell=powershell))
    monkeypatch.setattr(memory, "session", lambda *args: SimpleNamespace(protocol=None))
    monkeypatch.setattr(memory, "request", request)
    return SimpleNamespace(lab=lab, state=state, system=system, observation=observation, credential=credential,
                           vm=vm, job=job, peer=peer, root=tmp_path,
                           destination=tmp_path / "reports/win7-x64/run-current", secret=secret)


def prepare(guest):
    return memory.prepare_win7_transport(guest.lab, "win7-x64", guest.destination)


def test_original_150_prepares_once_then_512_only_checks_and_preserves_evidence(guest):
    first = prepare(guest)
    reports = list(guest.destination.glob("*/preparation.json"))
    before = {path: path.read_bytes() for path in reports}
    assert first["status"] == "prepared" and guest.peer.actions == ["Get", "Put", "Get"]
    guest.peer.actions.clear()
    second = prepare(guest)
    assert second["status"] == "checked" and guest.peer.actions == ["Get"]
    assert first["report_path"] != second["report_path"]
    assert all(path.read_bytes() == contents for path, contents in before.items())
    assert len(list(guest.destination.glob("*/change-intent.json"))) == 1
    for path in guest.destination.rglob("*.json"):
        assert guest.secret not in path.read_text(encoding="utf-8")


def test_restore_to_150_creates_new_change_evidence(guest):
    prepare(guest)
    old_reports = {path: path.read_bytes() for path in guest.destination.rglob("*.json")}
    guest.peer.memory = "150"
    guest.state["restore_generation"] = "restore-two"
    result = prepare(guest)
    report = json.loads((Path(result["report_path"]) / "preparation.json").read_text())
    assert result["status"] == "prepared" and report["restore_generation"] == "restore-two"
    assert len(list(guest.destination.glob("*/change-intent.json"))) == 2
    assert all(path.read_bytes() == payload for path, payload in old_reports.items())


@pytest.mark.parametrize("field,value,reason", [
    ("hardware_uuid", "another-guest", "GUEST_CHANGED"),
    ("boot_id", "another-boot", "GUEST_CHANGED"),
    ("token_sid", "S-1-5-18", "ACCOUNT_TOKEN_MISMATCH"),
    ("token_name", "QA-GUEST\\Administrator", "ACCOUNT_TOKEN_MISMATCH"),
    ("administrator", False, "ACCOUNT_TOKEN_MISMATCH"),
    ("local_accounts", [], "LOCAL_ACCOUNT_NOT_PROVEN"),
    ("active_installers", [{"pid": 99, "name": "setup.exe"}], "INSTALLER_ACTIVE_OR_UNKNOWN"),
    ("active_installers", None, "INSTALLER_ACTIVE_OR_UNKNOWN"),
])
def test_wrong_guest_or_account_or_active_installer_rejects_before_config_access(guest, field, value, reason):
    guest.observation[field] = value
    with pytest.raises(RuntimeError, match=reason):
        prepare(guest)
    assert guest.peer.actions == []
    assert not list(guest.destination.glob("*/change-intent.json"))
    assert json.loads(next(guest.destination.glob("*/preparation.json")).read_text())["status"] == "blocked"


@pytest.mark.parametrize("field,value", [("username", "Administrator"), ("uuid", "another-guest")])
def test_registered_credentials_cannot_authorize_another_account_or_uuid(guest, field, value):
    guest.credential[field] = value
    (guest.vm / "guest-credential.local.json").write_text(json.dumps(guest.credential))
    with pytest.raises(RuntimeError, match="REGISTERED_ACCOUNT_MISMATCH"):
        prepare(guest)
    assert guest.peer.actions == []


@pytest.mark.parametrize("value", ["6.1.7600", "10.0.19045"])
def test_original_win7_sp1_version_is_required(guest, value):
    guest.system["os_version"] = value
    with pytest.raises(RuntimeError, match="ORIGINAL_WIN7"):
        prepare(guest)
    assert guest.peer.actions == []


@pytest.mark.parametrize("value", ["256", "1024", "0"])
def test_unknown_quota_is_not_overwritten_or_increased(guest, value):
    guest.peer.memory = value
    with pytest.raises(RuntimeError, match="BASELINE_CHANGED"):
        prepare(guest)
    assert guest.peer.actions == ["Get"]


def test_os_reports_512_but_new_shell_keeps_150_must_fail(guest):
    guest.job["job_memory_limit_bytes"] = 150 * 1024**2
    with pytest.raises(RuntimeError, match="NEW_SHELL_QUOTA_NOT_PROVEN"):
        prepare(guest)
    report = json.loads(next(guest.destination.glob("*/preparation.json")).read_text())
    assert report["configuration"]["changed"] is True
    assert report["status"] == "blocked" and report["runtime_environment_passed"] is False


def test_readback_drift_preserves_change_intent_and_blocks(guest):
    guest.peer.drift = True
    with pytest.raises(RuntimeError, match="READBACK_MISMATCH"):
        prepare(guest)
    assert list(guest.destination.glob("*/change-intent.json"))
    assert json.loads(next(guest.destination.glob("*/preparation.json")).read_text())["status"] == "blocked"


def test_stopped_guest_is_never_started_by_preparation(guest):
    guest.lab.live = lambda state: False
    with pytest.raises(RuntimeError, match="GUEST_STOPPED"):
        prepare(guest)
    assert guest.peer.actions == []


@pytest.mark.skipif(os.name != "nt", reason="仅在 Windows 本地解析 PowerShell，不执行 Guest")
def test_account_and_process_probe_uses_ps2_capabilities_and_parses(tmp_path):
    script = memory.ACCOUNT_AND_PROCESS_PROBE
    for missing_on_ps2 in ("Get-CimInstance", "Get-LocalUser", "ConvertFrom-Json", "ConvertTo-Json", "[ordered]", " -Raw"):
        assert missing_on_ps2 not in script
    path = tmp_path / "账户与安装进程.ps1"
    path.write_text(script, encoding="utf-8")
    command = "$tokens=$null;$errors=$null;[void][Management.Automation.Language.Parser]::ParseFile('" + str(path).replace("'", "''") + "',[ref]$tokens,[ref]$errors);if($errors.Count){exit 1}"
    result = subprocess.run([str(Path(os.environ["SystemRoot"]) / "System32/WindowsPowerShell/v1.0/powershell.exe"),
                             "-NoProfile", "-NonInteractive", "-Command", command], capture_output=True, timeout=30, check=False)
    assert result.returncode == 0, result.stderr.decode(errors="replace")
