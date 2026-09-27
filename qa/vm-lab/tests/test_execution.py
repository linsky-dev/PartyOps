"""续跑和升级基线的行为测试，不调用真实 Guest 或互联网。"""
from types import SimpleNamespace

import pytest
from evidence import sha256
from execution import (
    Journal,
    can_resume_clean_start,
    clean_start_binding,
    select_upgrade_baseline,
)
from lab import load_configuration


def test_resume_reuses_only_unchanged_context_and_files(tmp_path):
    lab = SimpleNamespace(root=tmp_path)
    context = {"package_sha256": "a", "source_fingerprint": "b", "vm_uuid": "original", "baseline_id": "one"}
    journal = Journal(lab, "uos", context, False)
    assert journal.step("install", lambda: {"actual": True}) == {"actual": True}
    resumed = Journal(lab, "uos", context, True)
    assert resumed.reused
    assert resumed.step("install", lambda: pytest.fail("不应重复完成步骤")) == {"actual": True}
    (journal.directory / "install.json").write_text('{}')
    with pytest.raises(RuntimeError, match="RESUME_STEP_EVIDENCE_CHANGED"):
        resumed.step("install", dict)


@pytest.mark.parametrize("field", ["package_sha256", "source_fingerprint", "vm_uuid", "baseline_id"])
def test_changed_inputs_archive_old_run_and_execute_again(tmp_path, field):
    lab = SimpleNamespace(root=tmp_path)
    context = {"package_sha256": "a", "source_fingerprint": "b", "vm_uuid": "original", "baseline_id": "one"}
    old = Journal(lab, "uos", context, False)
    old.step("install", lambda: {"old": True})
    new = Journal(lab, "uos", {**context, field: "changed"}, True)
    assert not new.reused and new.data["resume_invalidated"]
    assert new.step("install", lambda: {"new": True}) == {"new": True}
    assert (old.directory / "install.json").exists()


def test_upgrade_selects_closest_older_matching_arch_and_hash(tmp_path):
    matrix, _ = load_configuration()
    matrix["retain_obsolete_packages"] = True
    for version, arch in [("1.4.4", "amd64"), ("1.4.5-rc.5", "amd64"), ("1.4.5-rc.6", "amd64"), ("1.4.5-rc.5", "arm64")]:
        path = tmp_path / f"PartyOps_{version}_linux_{arch}.deb"
        path.write_bytes((version + arch).encode())
        path.with_suffix('.deb.sha256').write_text(sha256(path))
    result = select_upgrade_baseline(matrix, "linux_amd64", [tmp_path])
    assert result["version"] == "1.4.5-rc.5"
    assert result["path"].endswith("amd64.deb")
    (tmp_path / "PartyOps_1.4.5-rc.5_linux_amd64.deb").write_bytes(b"tampered")
    assert select_upgrade_baseline(matrix, "linux_amd64", [tmp_path])["version"] == "1.4.4"


def test_missing_upgrade_does_not_use_old_database_fixture(tmp_path):
    matrix, _ = load_configuration()
    matrix["retain_obsolete_packages"] = True
    (tmp_path / "PartyOps_1.4.4_linux_amd64.deb").write_bytes(b"unverifiable")
    (tmp_path / "old-0023.db").write_bytes(b"old database is not an installer")
    with pytest.raises(RuntimeError, match="VERIFIABLE_OLDER_PACKAGE_MISSING"):
        select_upgrade_baseline(matrix, "linux_amd64", [tmp_path])


def test_upgrade_roots_reuse_existing_packages_and_ignore_empty_checksum(tmp_path):
    matrix, _ = load_configuration()
    matrix["retain_obsolete_packages"] = True
    directory = tmp_path / "older-releases"
    directory.mkdir()
    matrix["packages"]["macos_x86_64"]["upgrade_roots"] = [str(directory)]
    path = directory / "PartyOps_1.4.5-rc.2_macos_x86_64.pkg"
    path.write_bytes(b"synthetic older installer")
    checksum = path.with_suffix('.pkg.sha256')
    checksum.write_text("")
    with pytest.raises(RuntimeError, match="VERIFIABLE_OLDER_PACKAGE_MISSING"):
        select_upgrade_baseline(matrix, "macos_x86_64", [])
    checksum.write_text(sha256(path))
    assert select_upgrade_baseline(matrix, "macos_x86_64", [])["path"] == str(path.resolve())


def test_latest_only_policy_does_not_resurrect_old_upgrade_packages(tmp_path):
    matrix, _ = load_configuration()
    matrix["retain_obsolete_packages"] = False
    path = tmp_path / "PartyOps_1.4.5-rc.2_macos_x86_64.pkg"
    path.write_bytes(b"synthetic old package awaiting cleanup")
    path.with_suffix('.pkg.sha256').write_text(sha256(path))
    with pytest.raises(RuntimeError, match="OLDER_PACKAGE_REMOVED_BY_RETENTION_POLICY"):
        select_upgrade_baseline(matrix, "macos_x86_64", [tmp_path])


def test_resume_rechecks_nested_diagnostic_logs(tmp_path):
    lab = SimpleNamespace(root=tmp_path)
    report = tmp_path / "reports/uos/diagnostic"
    report.mkdir(parents=True)
    log = report / "actual-install.log"
    log.write_text("unit test only")
    journal = Journal(lab, "uos", {}, False)
    journal.step("install", lambda: {"report_path": str(report)})
    log.write_text("changed underneath unchanged controller.json")
    resumed = Journal(lab, "uos", {}, True)
    with pytest.raises(RuntimeError, match="RESUME_DEPENDENT_EVIDENCE_CHANGED"):
        resumed.step("install", dict)


def test_failed_actual_execution_is_preserved_but_retried_on_resume(tmp_path):
    lab = SimpleNamespace(root=tmp_path)
    report = tmp_path / "reports/uos/install"
    report.mkdir(parents=True)
    (report / "actual-install.log").write_text("unit test: package manager failed")
    journal = Journal(lab, "uos", {}, False)
    with pytest.raises(RuntimeError, match="EXECUTION_EXIT_NONZERO"):
        journal.step("install", lambda: {"exit_code": 1, "report_path": str(report)})
    assert journal.data["steps"]["install"]["status"] == "blocked"
    assert (journal.directory / journal.data["steps"]["install"]["path"]).is_file()
    resumed = Journal(lab, "uos", {}, True)
    assert resumed.step("install", lambda: {"exit_code": 0, "retried": True})["retried"]


def test_matrix_has_only_original_required_linux_targets():
    matrix, media = load_configuration()
    assert len(matrix["targets"]) >= 11 and len(matrix["packages"]) >= 10
    assert matrix["packages"]["linux_amd64"]["required_targets"] == ["uos-deb-x64"]
    assert matrix["packages"]["linux_arm64"]["required_targets"] == ["uos-deb-arm64"]
    assert not any("openeuler-rpm" in name or ("deepin" in name and name != "deepin-deb-loong64") for name in matrix["targets"])
    for target in matrix["targets"].values():
        if target["os"] == "linux":
            assert media[target["media"]]["format"] == "iso"
    assert matrix["local_only"] and not matrix["targets"]["macos-arm64"]["hosted_fallback"]


def test_resume_after_prestart_memory_block_still_restores_clean_baseline(tmp_path):
    lab = SimpleNamespace(root=tmp_path)
    context = {"package_sha256": "new", "restore_generation": "old-generation"}
    initial = Journal(lab, "win10", context, False)
    initial.data.update(status="blocked", errors=["HOST_MEMORY_HEADROOM"])
    initial.save()
    resumed = Journal(lab, "win10", context, True)
    state = {"clean_baseline": {"id": "clean-original"}, "restore_generation": "old-generation"}
    assert resumed.reused and not can_resume_clean_start(resumed, state)
    resumed.data["clean_start"] = clean_start_binding(state)
    assert can_resume_clean_start(resumed, state)


@pytest.mark.parametrize("field,value", [("baseline_id", "other-baseline"),
                                         ("restore_generation", "other-restore")])
def test_resume_never_reuses_product_steps_after_unproven_clean_start(tmp_path, field, value):
    lab = SimpleNamespace(root=tmp_path)
    state = {"clean_baseline": {"id": "clean-original"}, "restore_generation": "restore-one"}
    initial = Journal(lab, "win10", {}, False)
    initial.data["clean_start"] = {**clean_start_binding(state), field: value}
    initial.step("install", lambda: {"installed": "new-package"})
    resumed = Journal(lab, "win10", {}, True)
    with pytest.raises(RuntimeError, match="RESUME_CLEAN_BASELINE_NOT_PROVEN"):
        can_resume_clean_start(resumed, state)


@pytest.mark.parametrize("resume", [False, True])
def test_win7_install_completion_does_not_call_unported_ssh_business(tmp_path, monkeypatch, resume):
    import execution
    import winrm_memory

    lab = SimpleNamespace(root=tmp_path, matrix={"targets": {"win7-x64": {"winrm_port": 23171}}})
    journal = Journal(lab, "win7-x64", {}, False)
    if resume:
        journal.step("installed-package-diagnostic", lambda: {"status": "partial"})
        journal = Journal(lab, "win7-x64", {}, True)
    calls = []
    monkeypatch.setattr(winrm_memory, "prepare_win7_transport", lambda *args: calls.append("transport-preparation"))

    def fake_spec(name, path):
        def load(module):
            if "install.py" in path.name:
                module.exercise = lambda *args, **kwargs: calls.append("actual-install-diagnostic") or {"status": "partial"}
            else:
                module.exercise = lambda *args, **kwargs: pytest.fail("Win7 不能进入尚未适配的 SSH 业务入口")
        return SimpleNamespace(loader=SimpleNamespace(exec_module=load))
    monkeypatch.setattr(execution.importlib.util, "spec_from_file_location", fake_spec)
    monkeypatch.setattr(execution.importlib.util, "module_from_spec", lambda spec: SimpleNamespace())
    with pytest.raises(RuntimeError, match="WIN7_STANDARD_USER_PREPARE_AND_INTERACTIVE_PROBE_REQUIRED"):
        execution.windows_lifecycle(lab, "win7-x64", tmp_path, journal)
    assert calls == (["transport-preparation"] if resume else ["transport-preparation", "actual-install-diagnostic"])
    assert list(journal.data["steps"]) == ["installed-package-diagnostic"]
    assert journal.data["steps"]["installed-package-diagnostic"]["status"] == "completed"
    assert journal.data["runtime_environment_passed"] is False
