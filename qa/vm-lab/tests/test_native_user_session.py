"""诊断只接受真实安装绑定下的专用用户沿用关系；全部路径为测试临时目录。"""
from __future__ import annotations

from types import SimpleNamespace

import pytest
from evidence import sha256, write_json
from native_user_session import verify_session


@pytest.fixture
def session_fixture(tmp_path):
    lab = SimpleNamespace(root=tmp_path / "lab", matrix={"defaults": {"fallback_root": str(tmp_path / "work")}})
    run = lab.root / "reports/win11-x64-native/native-new"
    old_run = run.parent / "native-old"
    run.mkdir(parents=True)
    old_run.mkdir()
    workspace = tmp_path / "work/native/win11-x64" / run.name / "PartyOpsNativeQA"
    old_workspace = workspace.parent.parent / old_run.name / "PartyOpsNativeQA"
    owner = {"schema_version": 1, "purpose": "PartyOps native standard-user validation", "status": "prepared",
             "ownership_id": "a" * 32, "username": "PartyOpsNativeQA", "sid": "fixture-sid", "controller_sid": "controller-sid",
             "machine_name": "fixture-machine", "hardware_uuid": "fixture-uuid", "run_directory": str(old_run),
             "native_workspace": str(old_workspace), "data_directory": str(old_workspace / ("中文 空格业务数据-" + "a" * 32))}
    owner_path = lab.root / "state/native/PartyOpsNativeQA/private/ownership.json"
    write_json(owner_path, owner)
    protection = {"existing_native_account": {"sid": owner["sid"], "ownership_sha256": sha256(owner_path),
                  "original_run": str(old_run), "data_directory": owner["data_directory"]}}
    write_json(run / "protection.json", protection)
    install = {"installed_executable_sha256": "b" * 64}
    write_json(run / "install-result.json", install)
    binding = {"source_fingerprint": "c" * 64, "package": {"sha256": "d" * 64}, "protection_sha256": sha256(run / "protection.json"),
               "windows_payload": {"executables": {"PartyOps.exe": {"sha256": "b" * 64}, "PartyOpsWizard.exe": {"sha256": "e" * 64}}}}
    write_json(run / "install-binding.json", binding)
    session = {**owner, "schema_version": 2, "purpose": "PartyOps native standard-user revalidation", "run_directory": str(run),
               "native_workspace": str(workspace), "original_ownership_path": str(owner_path), "original_ownership_sha256": sha256(owner_path),
               "original_run": str(old_run), "original_data_directory": owner["data_directory"], "runtime_environment_passed": False,
               "account_changed": False, "credential_changed": False, "data_mode": "Retain",
               "install_result_sha256": sha256(run / "install-result.json"), "install_binding_sha256": sha256(run / "install-binding.json"),
               "protection_sha256": binding["protection_sha256"], "source_fingerprint": binding["source_fingerprint"], "package_sha256": binding["package"]["sha256"],
               "app_sha256": "b" * 64, "wizard_sha256": "e" * 64}
    write_json(run / "native-user-session.json", session)
    return SimpleNamespace(lab=lab, run=run, workspace=workspace, owner=owner, owner_path=owner_path, session=session,
                           binding=binding, install=install, verify=lambda: verify_session(lab, run, binding, install, owner))


@pytest.mark.parametrize("mode", ["Retain", "Fresh"])
def test_bound_session_allows_only_declared_data_mode_without_modifying_original(session_fixture, mode):
    f = session_fixture
    previous = f.owner_path.read_bytes()
    f.session["data_mode"] = mode
    if mode == "Fresh":
        f.session["data_directory"] = str(f.workspace / ("中文 空格业务数据-" + f.owner["ownership_id"]))
    write_json(f.run / "native-user-session.json", f.session)
    result = f.verify()
    assert result["data_mode"] == mode and result["data_directory"] == f.session["data_directory"]
    assert result["sha256"] == sha256(f.run / "native-user-session.json")
    assert f.owner_path.read_bytes() == previous


@pytest.mark.parametrize("field", ["run_directory", "native_workspace", "original_run", "original_ownership_path", "original_ownership_sha256",
                                   "install_result_sha256", "install_binding_sha256", "source_fingerprint", "package_sha256", "app_sha256",
                                   "wizard_sha256", "data_directory", "data_mode", "sid", "hardware_uuid", "controller_sid"])
def test_rejects_imported_session_or_changed_install_and_data(session_fixture, field):
    f = session_fixture
    f.session[field] = "foreign"
    write_json(f.run / "native-user-session.json", f.session)
    with pytest.raises(RuntimeError, match="NATIVE_USER_SESSION_"):
        f.verify()


def test_replaced_original_ownership_and_backup_protection_are_rejected(session_fixture):
    f = session_fixture
    f.owner_path.write_bytes(b"{}")
    with pytest.raises(RuntimeError, match="ORIGINAL_OWNERSHIP_CHANGED"):
        f.verify()
    write_json(f.owner_path, f.owner)
    (f.run / "protection.json").write_bytes(b"{}")
    with pytest.raises(RuntimeError, match="PROTECTION_CHANGED"):
        f.verify()
