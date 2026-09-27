"""拒绝把非登记桌面或变更后的计划任务当作安装上下文。"""
import copy
import json
from types import SimpleNamespace

import pytest
import windows_interactive_install as interactive
from evidence import sha256, write_json
from windows_interactive_install import stage_package, validate_desktop, validate_task

SID = "S-1-5-21-1-2-3-1001"
UUID = "406ef8ee-e342-405f-933f-7ddce0a55f1b"
DESKTOP = {"uuid": UUID, "sid": SID, "name": "WIN7\\partyopsqa", "administrator": True, "desktop_sessions": [1]}
TASK = {"sid": SID, "logon_type": 3, "run_level": 1, "action_matches": True}


def test_registered_interactive_admin_only():
    validate_desktop(DESKTOP, UUID)
    validate_task(TASK, SID)


@pytest.mark.parametrize("change", [{"uuid": "other"}, {"name": "other"}, {"sid": "S-1-5-18"},
                                    {"administrator": False}, {"desktop_sessions": []},
                                    {"desktop_sessions": [0]}, {"desktop_sessions": [1, 2]}])
def test_reject_wrong_or_noninteractive_identity(change):
    value = copy.deepcopy(DESKTOP)
    value.update(change)
    with pytest.raises(RuntimeError, match="REGISTERED_ADMIN_DESKTOP"):
        validate_desktop(value, UUID)


@pytest.mark.parametrize("change", [{"sid": "S-1-5-18"}, {"logon_type": 1}, {"run_level": 0}, {"action_matches": False}])
def test_reject_changed_task_binding(change):
    with pytest.raises(RuntimeError, match="TASK_BINDING_CHANGED"):
        validate_task({**TASK, **change}, SID)


@pytest.mark.parametrize("actual,transfers", [("a" * 64, 0), ("missing", 1), ("b" * 64, 1)])
def test_only_live_matching_package_hash_skips_transfer(tmp_path, actual, transfers):
    calls = []
    files = SimpleNamespace(powershell=lambda *args, **kwargs: actual,
                            put=lambda *args: calls.append(args))
    package = {"path": "PartyOps_1.4.5-rc.6_windows7_amd64.exe", "sha256": "a" * 64}
    stage_package(files, package, tmp_path)
    assert len(calls) == transfers
    assert (tmp_path / "package-transfer.json").exists()


@pytest.mark.parametrize("changed", [None, "package", "snapshot", "script", "remote_script"])
def test_resume_only_collects_original_bound_task(tmp_path, monkeypatch, changed):
    destination = tmp_path / "install-123456789abc"
    destination.mkdir()
    wrapper = destination / "interactive-installer.ps1"
    wrapper.write_text("# 原任务脚本\n", encoding="utf-8")
    desktop = {**DESKTOP, "boot_id": "boot-original"}
    record = {"desktop": desktop, "package_sha256": "a" * 64,
              "restore_generation": "restore-original", "script_sha256": sha256(wrapper),
              "task_name": "PartyOps-QA-" + destination.name,
              "remote": "C:\\PartyOps-QA\\" + destination.name}
    write_json(destination / "interactive-task.json", record)
    calls = []

    def powershell(script, **kwargs):
        calls.append(script)
        assert ".Run(" not in script and "RegisterTaskDefinition" not in script
        if len(calls) == 1:
            return "wrong" if changed == "remote_script" else record["script_sha256"]
        return json.dumps({**TASK, "state": 3, "exit_code": 0, "result_exists": True, "error_exists": False})

    def get(remote, local, **kwargs):
        write_json(local, {"exit_code": 0, "interactive_execution": {
            "sid": SID, "session_id": 1, "boot_id": "boot-original"}})

    monkeypatch.setattr(interactive, "WinRMFiles", lambda *args: SimpleNamespace(powershell=powershell, get=get))
    state = {"uuid": UUID, "restore_generation": "changed" if changed == "snapshot" else "restore-original"}
    package = {"sha256": "b" * 64 if changed == "package" else "a" * 64}
    if changed == "script":
        wrapper.write_text("# 已变化", encoding="utf-8")
    lab = SimpleNamespace(state=lambda target: state)
    if changed:
        with pytest.raises(RuntimeError, match="RESUME_.*CHANGED"):
            interactive.resume_install(lab, "win7-x64", destination, package)
    else:
        result = interactive.resume_install(lab, "win7-x64", destination, package)
        assert result["exit_code"] == 0 and len(calls) == 2
