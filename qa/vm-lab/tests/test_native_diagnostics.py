"""本机诊断汇总反例；全部报告、包与可执行文件只使用临时合成夹具。"""
import copy
import json
import shutil
from pathlib import Path
from types import SimpleNamespace

import evidence
import native_diagnostics as diagnostics
import native_windows
import pytest


@pytest.fixture
def fixture(tmp_path, monkeypatch):
    target = "win11-x64-native"
    source = "b" * 64
    lab = SimpleNamespace(root=tmp_path / "lab", matrix={"targets": {target: {"backend": "native-host", "os": "windows", "os_release": "11", "arch": "x86_64"}},
                                                      "defaults": {"fallback_root": str(tmp_path / "fallback")}})
    run = lab.root / "reports" / target / diagnostics.HISTORICAL_RUN
    run.mkdir(parents=True)
    install_dir = tmp_path / "installed"
    install_dir.mkdir()
    executable = install_dir / "PartyOps.exe"
    executable.write_bytes(b"synthetic installed executable")
    wizard = install_dir / "PartyOpsWizard.exe"
    wizard.write_bytes(b"synthetic wizard")
    monkeypatch.setattr(diagnostics, "INSTALL_DIRECTORY", install_dir)
    system = {"os": "windows", "arch": "x86_64", "os_release": "11", "os_version": "10.0.26200", "os_build": "26200.9168",
              "display_version": "25H2", "edition": "Professional", "host_id": "d" * 64,
              "environment_type": "native-host", "boot_id": "2026-09-08T00:00:00.0000000Z"}
    account = {"username": "PartyOpsNativeQA", "sid": "S-1-5-21-123-456-789-1005", "enabled": True, "is_admin": False, "is_user": True,
               "hardware_uuid": "12345678-1234-4321-abcd-123456789012", "machine_name": "synthetic-host",
               "user_profile": r"C:\Users\PartyOpsNativeQA", "local_appdata": r"C:\Users\PartyOpsNativeQA\AppData\Local"}
    monkeypatch.setattr(diagnostics, "read_account", lambda: copy.deepcopy(account))
    monkeypatch.setattr(diagnostics, "read_host", lambda: copy.deepcopy(system))
    package = {"id": "windows_amd64", "version": "1.4.5-rc.6", "sha256": "a" * 64, "source_fingerprint": source,
               "provenance_status": "verified", "build_receipt_sha256": "c" * 64}
    install = {"target": target, "environment_type": "native-host", "package": package, "installer_exit_code": 0,
               "install_dir": str(install_dir), "registered_path": str(install_dir), "registered_version": package["version"],
               "installed_executable_sha256": evidence.sha256(executable), "scope": "same-version-current-candidate-replacement",
               "runtime_environment_passed": False}
    evidence.write_json(run / "candidate.json", package)
    evidence.write_json(run / "install-result.json", install)
    evidence.write_json(run / "install-start.json", {**install, "boot_id": system["boot_id"]})
    work = Path(lab.matrix["defaults"]["fallback_root"]) / "native/win11-x64" / diagnostics.HISTORICAL_RUN / "PartyOpsNativeQA"
    data = str(work / ("中文 空格业务数据-" + "1" * 32))
    for index, filename in enumerate(("standard-user-latest.json", "standard-user-personal-permission.json", "standard-user-wizard.json")):
        directory = run / ("standard-user-probe-" + f"{index + 1:032x}")
        app = wizard if "wizard" in filename else executable
        selftest = {"passed": True, "desktop_user": True, "version": package["version"], "data_dir_writable": True}
        child = {"target": target, "environment_type": "native-host", "status": "passed", "runtime_environment_passed": False,
                 "sid": account["sid"], "session_id": 1, "is_admin": False, "run_directory": str(run),
                 "app_sha256": evidence.sha256(executable), "exit_code": 0, "selftest": selftest,
                 "process": {"pid": index + 100, "owner_sid": account["sid"], "session_id": 1, "executable_path": str(app)}}
        evidence.write_json(directory / "result.json", child)
        receipt = {**account, "status": "passed", "runtime_environment_passed": False, "session_id": 1,
                   "run_directory": str(run), "native_workspace": str(work), "data_directory": data,
                   "app_sha256": evidence.sha256(executable), "install_result_sha256": evidence.sha256(run / "install-result.json"),
                   "secondary_logon_exit_code": 0, "report_directory": str(directory), "result_sha256": evidence.sha256(directory / "result.json")}
        if "wizard" in filename:
            receipt["wizard"] = {"sha256": evidence.sha256(wizard)}
        evidence.write_json(run / filename, receipt)
    context = {"host_id": system["host_id"], "identity_sha256": native_windows.identity_digest(system), "boot_id": system["boot_id"],
               "package_sha256": package["sha256"], "source_fingerprint": source, "executable_sha256": install["installed_executable_sha256"],
               "account_sid": account["sid"], "target": target, "data_dir": data,
               "identity_receipt_sha256": evidence.sha256(run / "standard-user-latest.json"),
               "wizard_receipt_sha256": evidence.sha256(run / "standard-user-wizard.json")}
    for phase, (_, filename) in diagnostics.STAGES.items():
        if filename.startswith("business/") and phase not in {"revalidate", "models"}:
            record = {"scope": "native-standard-user-installed-business", "phase": phase, "environment_type": "native-host",
                      "context": context, "runtime_environment_passed": False, "status": "passed",
                      "checks": [{"id": phase + "-actual-check", "status": "passed"}]}
            if phase == "formatter-probe":
                record.update(status="failed", checks=[], error="NATIVE_LISTENER_OWNER_MISMATCH")
            evidence.write_json(run / filename, record)
    return SimpleNamespace(lab=lab, target=target, package=package, source=source, system=system, account=account, run=run, executable=executable,
                           summary=lambda: diagnostics.summary(lab, target, package, source, run_directory=run))


def mutate(path, callback):
    value = json.loads(path.read_text(encoding="utf-8"))
    callback(value)
    evidence.write_json(path, value)


def add_runtime_receipt(run, fixture, session):
    directory = run / ("standard-user-probe-" + "4" * 32)
    value = json.loads((run / "standard-user-latest.json").read_text(encoding="utf-8"))
    old_child = Path(value["report_directory"]) / "result.json"
    child = json.loads(old_child.read_text(encoding="utf-8"))
    runtime = {"owner_sid": fixture.account["sid"], "session_id": 1, "pid": 300,
               "executable_path": str(fixture.executable), "sha256": evidence.sha256(fixture.executable),
               "started_at": "2026-09-08T01:00:00.0000000Z", "created_at": "2026-09-08T01:00:01.0000000Z",
               "launcher_exit_code": 0, "port": 18825, "address": "127.0.0.1"}
    child.pop("process", None)
    child.update(scope="standard-token-retained-personal-runtime-ready", gui_verified=False,
                 first_configuration_verified=False, runtime=runtime, exit_code=None,
                 native_user_session_sha256=session["sha256"], data_mode="Retain")
    evidence.write_json(directory / "result.json", child)
    value.update(report_directory=str(directory), result_sha256=evidence.sha256(directory / "result.json"),
                 runtime=runtime, gui_verified=False, first_configuration_verified=False,
                 secondary_logon_exit_code=None, native_user_session_sha256=session["sha256"], data_mode="Retain")
    evidence.write_json(run / "standard-user-runtime.json", value)
    return value


@pytest.fixture
def retained(fixture, monkeypatch):
    run = fixture.run
    payload = {"executables": {"PartyOps.exe": {"sha256": evidence.sha256(fixture.executable)}}, "manifest": {"sha256": "e" * 64}}
    binding = {"package": fixture.package, "windows_payload": payload}
    evidence.write_json(run / "install-binding.json", binding)
    monkeypatch.setattr(diagnostics, "verify_install_binding", lambda *_: binding)
    for filename in ("install-result.json", "install-start.json"):
        mutate(run / filename, lambda value: value.update(expected_payload=payload, install_binding_sha256=evidence.sha256(run / "install-binding.json"), installed_manifest_sha256="e" * 64))
    ordinary = json.loads((run / "standard-user-latest.json").read_text(encoding="utf-8"))
    session = {"sha256": "f" * 64, "data_mode": "Retain", "data_directory": ordinary["data_directory"]}
    evidence.write_json(run / "native-user-session.json", {"synthetic": True})
    monkeypatch.setattr(diagnostics, "verify_session", lambda *_: session)
    for filename in ("standard-user-latest.json", "standard-user-personal-permission.json"):
        path = run / filename
        value = json.loads(path.read_text(encoding="utf-8"))
        child = Path(value["report_directory"]) / "result.json"
        mutate(child, lambda item: item.update(native_user_session_sha256=session["sha256"], data_mode="Retain"))
        mutate(path, lambda item, child=child: item.update(native_user_session_sha256=session["sha256"], data_mode="Retain",
               result_sha256=evidence.sha256(child), install_result_sha256=evidence.sha256(run / "install-result.json")))
    add_runtime_receipt(run, fixture, session)
    for path in (run / "business").glob("*.json"):
        mutate(path, lambda value: value["context"].update(identity_receipt_sha256=evidence.sha256(run / "standard-user-latest.json"),
               wizard_receipt_sha256=None, native_user_session_sha256=session["sha256"], port=18825,
               runtime_receipt_sha256=evidence.sha256(run / "standard-user-runtime.json")))
    template = json.loads((run / "business/business.json").read_text(encoding="utf-8"))
    revalidate = copy.deepcopy(template)
    revalidate.update(phase="revalidate", checks=[{"id": "retained-account-task-attachment-on-new-installed-runtime", "status": "passed", "result": {"previous_evidence_reaccepted": False}}])
    evidence.write_json(run / "business/revalidate.json", revalidate)
    models = copy.deepcopy(template)
    models.update(phase="models", checks=[{"id": name, "status": "passed"} for name in
                  ("signed-model-embedding", "actual-semantic-search", "signed-model-llm", "real-local-llm-inference")],
                  network={"host_outbound_isolation_verified": False})
    evidence.write_json(run / "business/models.json", models)
    wps = run / "business/formatter-golden-aabbcc/evidence.json"
    control = wps.parent / "host-controls/progress.json.redacted.json"
    evidence.write_json(control, [{"state": "failed"}])
    evidence.write_json(wps, {"scope": "native-standard-user-installed-wps-golden", "runtime_environment_passed": False,
        "context": template["context"], "status": "failed", "error": "NATIVE_FORMATTER_JOB_NOT_COMPLETED", "outputs": [],
        "jobs": [{"host_controls": [{"evidence": "host-controls/progress.json.redacted.json", "sha256": evidence.sha256(control)}],
                  "snapshots": [{"response": {"items": [{"error_code": "SOURCE_FORMATTER_FAILED"}]}}]}]})
    fixture.wps = wps
    fixture.control = control
    return fixture


def test_only_bound_partial_progress_is_shown_and_reports_never_written(fixture, monkeypatch):
    files = {path: path.read_bytes() for path in fixture.run.rglob("*.json")}
    monkeypatch.setattr(Path, "write_text", lambda *args, **kwargs: pytest.fail("只读汇总不得写实际报告"))
    result = fixture.summary()
    assert result["status"] == "partial" and result["observed_passed_stage_count"] == 7
    assert result["runtime_environment_passed"] is False and result["full_lifecycle_passed"] is False
    assert result["accepted_case_count"] == 0 and "cases" not in result
    assert result["stages"]["formatter-probe"]["status"] == "failed"
    assert "NATIVE_LISTENER_OWNER_MISMATCH" in result["remaining_blockers"]
    assert result["stages"]["replacement-install"]["recorded_scope"] == "same-version-current-candidate-replacement"
    assert all(path.read_bytes() == content for path, content in files.items())


@pytest.mark.parametrize("change,error", [
    ("package", "PACKAGE_BINDING_MISMATCH:sha256"), ("source", "CURRENT_PACKAGE_UNBOUND"),
    ("executable", "INSTALLED_EXECUTABLE_CHANGED"), ("boot", "BOOT_CHANGED"),
    ("account", "RECEIPT_ACCOUNT_MISMATCH"), ("host", "RECEIPT_HOST_MISMATCH"),
    ("admin", "CURRENT_STANDARD_ACCOUNT_UNPROVEN"), ("child", "CHILD_REPORT_CHANGED"),
    ("other_run", "RECEIPT_PATH_MISMATCH:run_directory"), ("private", "CHILD_REPORT_PATH_INVALID"),
])
def test_stale_or_foreign_context_cannot_be_imported(fixture, change, error):
    if change == "package":
        fixture.package["sha256"] = "e" * 64
    elif change == "source":
        fixture.package["source_fingerprint"] = "e" * 64
    elif change == "executable":
        fixture.executable.write_bytes(b"changed executable")
    elif change == "boot":
        fixture.system["boot_id"] = "2026-09-09T00:00:00.0000000Z"
    elif change == "account":
        fixture.account["sid"] = "S-1-5-21-123-456-789-1006"
    elif change == "host":
        fixture.account["hardware_uuid"] = "different-host"
    elif change == "admin":
        fixture.account["is_admin"] = True
    elif change == "child":
        path = fixture.run / ("standard-user-probe-" + f"{1:032x}") / "result.json"
        mutate(path, lambda value: value.update(sid="foreign-sid"))
    elif change == "other_run":
        mutate(fixture.run / "standard-user-latest.json", lambda value: value.update(run_directory=str(fixture.run.parent / "old-run")))
    else:
        mutate(fixture.run / "standard-user-latest.json", lambda value: value.update(report_directory=str(fixture.run / "private")))
    result = fixture.summary()
    assert result["status"] == "blocked"
    assert any(error in reason for reason in result["binding_errors"])
    assert not any(stage["status"] == "passed" for stage in result["stages"].values())


@pytest.mark.parametrize("field", ["host_id", "identity_sha256", "account_sid", "boot_id", "source_fingerprint", "package_sha256",
                                   "executable_sha256", "identity_receipt_sha256", "wizard_receipt_sha256", "target", "data_dir"])
def test_business_phase_rejects_each_foreign_binding_without_hiding_other_progress(fixture, field):
    mutate(fixture.run / "business/ocr.json", lambda value: value["context"].update({field: "foreign"}))
    result = fixture.summary()
    assert result["stages"]["ocr"]["status"] == "rejected"
    assert result["stages"]["ocr"]["error"].endswith(":" + field)
    assert result["stages"]["business"]["status"] == "passed"
    assert result["runtime_environment_passed"] is False


def test_claimed_success_without_checks_is_rejected(fixture):
    mutate(fixture.run / "business/formatter-probe.json", lambda value: value.update(status="passed"))
    stage = fixture.summary()["stages"]["formatter-probe"]
    assert stage["status"] == "rejected" and stage["error"] == "NATIVE_DIAGNOSTIC_PASSED_WITHOUT_CHECKS"


def test_current_executable_does_not_allow_other_user_process_receipt(fixture):
    receipt = fixture.run / "standard-user-latest.json"
    value = json.loads(receipt.read_text())
    child = Path(value["report_directory"]) / "result.json"
    mutate(child, lambda value: value["process"].update(owner_sid="other-owner"))
    mutate(receipt, lambda value: value.update(result_sha256=evidence.sha256(child)))
    assert "NATIVE_DIAGNOSTIC_CHILD_PROCESS_MISMATCH" in fixture.summary()["binding_errors"]


def test_controller_shows_partial_stages_but_never_accepts_them(fixture, monkeypatch):
    import lab as controller
    import provenance

    fixture.lab.matrix["packages"] = {"windows_amd64": {"required_targets": [fixture.target]}}
    monkeypatch.setattr(native_windows, "probe", lambda *args: fixture.system)
    monkeypatch.setattr(provenance, "bind_package", lambda *args: fixture.package)
    original_summary = diagnostics.summary
    monkeypatch.setattr(diagnostics, "summary", lambda lab, target, package, source, *args, **kwargs: original_summary(lab, target, package, source, run_directory=fixture.run))
    result = controller.check_target(fixture.lab, fixture.target, {"windows_amd64": fixture.package}, {}, None, fixture.source)
    assert result["status"] == "blocked" and result["runtime_environment_passed"] is False
    assert result["real_environment_passed"] is False
    assert result["diagnostics_summary"]["observed_passed_stage_count"] == 7
    assert "FULL_LIFECYCLE_EVIDENCE_MISSING" in result["errors"]
    assert "NATIVE_LISTENER_OWNER_MISMATCH" in result["errors"]


def test_new_product_source_marks_old_progress_stale_without_reading_account(fixture, monkeypatch):
    monkeypatch.setattr(diagnostics, "read_account", lambda: pytest.fail("旧源码无需查询账户"))
    result = diagnostics.summary(fixture.lab, fixture.target, {}, "f" * 64, run_directory=fixture.run)
    assert result["status"] == "stale_source"
    assert result["observed_passed_stage_count"] == 0 and result["accepted_case_count"] == 0
    assert result["runtime_environment_passed"] is False and result["full_lifecycle_passed"] is False
    assert result["recorded_package"]["source_fingerprint"] == fixture.source
    assert all(stage["status"] == ("stale_source" if Path(stage["evidence_path"]).exists() else "not_run") for stage in result["stages"].values())
    assert result["binding_errors"] == ["NATIVE_DIAGNOSTIC_STALE_SOURCE_FINGERPRINT"]


def test_changed_wizard_executable_invalidates_all_dependent_business_stages(fixture):
    (fixture.executable.parent / "PartyOpsWizard.exe").write_bytes(b"changed wizard")
    result = fixture.summary()
    assert "NATIVE_DIAGNOSTIC_WIZARD_EXECUTABLE_CHANGED" in result["binding_errors"]
    assert result["stages"]["standard-user-startup"]["status"] == "passed"
    for name, (_, filename) in diagnostics.STAGES.items():
        if filename.startswith("business/") and (fixture.run / filename).exists():
            assert result["stages"][name]["status"] == "rejected"


def test_explicit_new_run_does_not_import_old_unbound_diagnostics(fixture, monkeypatch):
    run = fixture.run.parent / "native-new-candidate"
    shutil.copytree(fixture.run, run)
    monkeypatch.setattr(diagnostics, "read_account", lambda: pytest.fail("新 run 缺构建绑定不得查询账户"))
    result = diagnostics.summary(fixture.lab, fixture.target, fixture.package, fixture.source, run_directory=run)
    assert result["status"] == "blocked" and result["run_directory"] == str(run)
    assert any("install-binding.json" in error for error in result["binding_errors"])
    assert result["observed_passed_stage_count"] == 0 and result["accepted_case_count"] == 0
    assert all(str(run) in stage["evidence_path"] and stage["status"] == ("rejected" if Path(stage["evidence_path"]).exists() else "not_run") for stage in result["stages"].values())


def test_explicit_new_run_stale_source_does_not_read_other_run_or_account(fixture, monkeypatch):
    run = fixture.run.parent / "native-other-source"
    run.mkdir()
    evidence.write_json(run / "candidate.json", fixture.package)
    monkeypatch.setattr(diagnostics, "read_account", lambda: pytest.fail("旧源码无需查询账户"))
    monkeypatch.setattr(diagnostics, "verify_install_binding", lambda *_: pytest.fail("旧源码不能追认新构建"))
    result = diagnostics.summary(fixture.lab, fixture.target, {}, "f" * 64, run_directory=run)
    assert result["status"] == "stale_source" and result["run_directory"] == str(run)
    assert result["observed_passed_stage_count"] == 0
    assert all(stage["status"] == "not_run" for stage in result["stages"].values())


@pytest.mark.parametrize("location", ["outside", "nested"])
def test_explicit_run_rejects_foreign_or_nested_report_path(fixture, location):
    run = fixture.lab.root / "outside" if location == "outside" else fixture.run / "nested"
    with pytest.raises((RuntimeError, ValueError)):
        diagnostics.summary(fixture.lab, fixture.target, fixture.package, fixture.source, run_directory=run)


def test_explicit_bound_new_run_requires_own_payload_and_workspace(fixture, monkeypatch):
    old_bytes = {path: path.read_bytes() for path in fixture.run.rglob("*.json")}
    run = fixture.run.parent / "native-new-bound-candidate"
    shutil.copytree(fixture.run, run)
    # 合成报告改为本轮路径，并重算所有分层绑定；构建权威单独由 native_install 测试覆盖。
    for path in run.rglob("*.json"):
        path.write_text(path.read_text(encoding="utf-8").replace(diagnostics.HISTORICAL_RUN, run.name), encoding="utf-8")
    payload = {"executables": {"PartyOps.exe": {"sha256": evidence.sha256(fixture.executable)}}, "manifest": {"sha256": "e" * 64}}
    binding = {"package": fixture.package, "windows_payload": payload}
    evidence.write_json(run / "install-binding.json", binding)
    def verify_binding(_lab, actual_run, actual_source):
        assert actual_run == run and actual_source == fixture.source
        return binding
    monkeypatch.setattr(diagnostics, "verify_install_binding", verify_binding)
    for filename in ("install-result.json", "install-start.json"):
        mutate(run / filename, lambda value: value.update(scope="current-candidate-replacement", expected_payload=payload,
               install_binding_sha256=evidence.sha256(run / "install-binding.json"), installed_manifest_sha256="e" * 64))
    for filename in ("standard-user-latest.json", "standard-user-personal-permission.json", "standard-user-wizard.json"):
        def update_receipt(value):
            value.update(install_result_sha256=evidence.sha256(run / "install-result.json"),
                         result_sha256=evidence.sha256(Path(value["report_directory"]) / "result.json"))
        mutate(run / filename, update_receipt)
    for path in (run / "business").glob("*.json"):
        mutate(path, lambda value: value["context"].update(identity_receipt_sha256=evidence.sha256(run / "standard-user-latest.json"),
               wizard_receipt_sha256=evidence.sha256(run / "standard-user-wizard.json")))
    result = diagnostics.summary(fixture.lab, fixture.target, fixture.package, fixture.source, run_directory=run)
    assert result["status"] == "partial" and result["observed_passed_stage_count"] == 7, result
    assert result["accepted_case_count"] == 0 and result["runtime_environment_passed"] is False
    assert all(path.read_bytes() == content for path, content in old_bytes.items())
    # 合法沿用原数据必须经过会话权威检查，子进程、回执和业务三层也绑定同一新 session。
    retained_data = json.loads((fixture.run / "standard-user-latest.json").read_text(encoding="utf-8"))["data_directory"]
    session = {"sha256": "a" * 64, "data_mode": "Retain", "data_directory": retained_data}
    evidence.write_json(run / "native-user-session.json", {"synthetic": True})
    monkeypatch.setattr(diagnostics, "verify_session", lambda *_: session)
    for filename in ("standard-user-latest.json", "standard-user-personal-permission.json", "standard-user-wizard.json"):
        receipt_path = run / filename
        child_path = Path(json.loads(receipt_path.read_text(encoding="utf-8"))["report_directory"]) / "result.json"
        mutate(child_path, lambda value: value.update(native_user_session_sha256=session["sha256"], data_mode="Retain"))
        mutate(receipt_path, lambda value, child_path=child_path: value.update(native_user_session_sha256=session["sha256"], data_mode="Retain",
               data_directory=retained_data, result_sha256=evidence.sha256(child_path)))
    add_runtime_receipt(run, fixture, session)
    for path in (run / "business").glob("*.json"):
        mutate(path, lambda value: value["context"].update(data_dir=retained_data,
               identity_receipt_sha256=evidence.sha256(run / "standard-user-latest.json"),
               wizard_receipt_sha256=None, port=18825, native_user_session_sha256=session["sha256"],
               runtime_receipt_sha256=evidence.sha256(run / "standard-user-runtime.json")))
    retained = diagnostics.summary(fixture.lab, fixture.target, fixture.package, fixture.source, run_directory=run)
    assert retained["observed_passed_stage_count"] == 7 and retained["standard_user_session"] == session, retained
    mutate(run / "standard-user-latest.json", lambda value: value.update(native_user_session_sha256="foreign"))
    rejected = diagnostics.summary(fixture.lab, fixture.target, fixture.package, fixture.source, run_directory=run)
    assert rejected["binding_errors"] == ["NATIVE_DIAGNOSTIC_RECEIPT_SESSION_MISMATCH"]
    mutate(run / "install-result.json", lambda value: value.update(installed_manifest_sha256="foreign"))
    rejected = diagnostics.summary(fixture.lab, fixture.target, fixture.package, fixture.source, run_directory=run)
    assert rejected["binding_errors"] == ["NATIVE_DIAGNOSTIC_EXPECTED_PAYLOAD_MISMATCH"]
    assert rejected["observed_passed_stage_count"] == 0


def test_default_never_guesses_old_or_newest_directory(fixture, monkeypatch):
    (fixture.run.parent / "native-newer-name").mkdir()
    monkeypatch.setattr(diagnostics, "read_account", lambda: pytest.fail("未登记不能读取账户"))
    result = diagnostics.summary(fixture.lab, fixture.target, fixture.package, fixture.source)
    assert result["status"] == "blocked" and result["run_directory"] is None
    assert result["observed_passed_stage_count"] == 0
    assert "NATIVE_CURRENT_RUN_REGISTRATION_REQUIRED" in result["binding_errors"][0]


def test_explicit_current_binding_keeps_retained_models_and_real_wps_failure(retained):
    before = {path: path.read_bytes() for path in retained.run.rglob("*.json")}
    registration = diagnostics.register_current(retained.lab, retained.target, retained.package, retained.source, retained.run,
                                               wps_evidence=[retained.wps])
    assert registration["binding"]["source_fingerprint"] == retained.source
    assert set(registration["records"]) == set(diagnostics.CURRENT_FILES)
    result = diagnostics.summary(retained.lab, retained.target, retained.package, retained.source)
    assert not result["binding_errors"] and result["status"] == "partial", result
    assert result["stages"]["revalidate"]["status"] == "passed"
    assert result["stages"]["resume-configure"]["status"] == "rejected"
    models = result["stages"]["models"]
    assert models["status"] == "passed" and models["coverage"] == "vector-search-and-local-llm-only"
    assert models["offline_isolation_verified"] is models["needle_verified"] is models["complete_models_case_passed"] is False
    wps = result["stages"]["wps:formatter-golden-aabbcc"]
    assert wps["status"] == "failed" and wps["error_codes"] == ["SOURCE_FORMATTER_FAILED"]
    assert wps["outputs_count"] == 0 and wps["complete_wps_case_passed"] is False
    assert result["accepted_case_count"] == 0 and result["runtime_environment_passed"] is result["full_lifecycle_passed"] is False
    assert all(path.read_bytes() == content for path, content in before.items())


@pytest.mark.parametrize("file", diagnostics.CURRENT_FILES)
def test_registered_current_rejects_changed_core_receipt(retained, file):
    diagnostics.register_current(retained.lab, retained.target, retained.package, retained.source, retained.run)
    path = retained.run / file
    mutate(path, lambda value: value.update(synthetic_change=True))
    result = diagnostics.summary(retained.lab, retained.target, retained.package, retained.source)
    assert result["status"] == "blocked" and result["observed_passed_stage_count"] == 0
    assert "NATIVE_CURRENT_RECORD_CHANGED:" + file in result["binding_errors"][0]


def test_registered_current_retains_old_registration_and_marks_source_stale(retained, monkeypatch):
    diagnostics.register_current(retained.lab, retained.target, retained.package, retained.source, retained.run)
    path = diagnostics.current_path(retained.lab, retained.target)
    content = path.read_bytes()
    second = diagnostics.register_current(retained.lab, retained.target, retained.package, retained.source, retained.run)
    assert Path(second["previous_registration"]["path"]).read_bytes() == content
    monkeypatch.setattr(diagnostics, "read_account", lambda: pytest.fail("旧源码应先阻断，不读账户"))
    result = diagnostics.summary(retained.lab, retained.target, {}, "9" * 64)
    assert result["status"] == "stale_source" and result["observed_passed_stage_count"] == 0
    assert result["binding_errors"] == ["NATIVE_DIAGNOSTIC_STALE_SOURCE_FINGERPRINT"]


@pytest.mark.parametrize("field", ["host_id", "identity_sha256", "account_sid", "boot_id", "package_sha256"])
def test_registered_current_host_or_package_cannot_be_replaced(retained, field):
    diagnostics.register_current(retained.lab, retained.target, retained.package, retained.source, retained.run)
    path = diagnostics.current_path(retained.lab, retained.target)
    mutate(path, lambda value: value["binding"].update({field: "foreign"}))
    result = diagnostics.summary(retained.lab, retained.target, retained.package, retained.source)
    assert result["status"] == "blocked" and result["observed_passed_stage_count"] == 0
    assert "NATIVE_CURRENT_HOST_SOURCE_PACKAGE_BINDING_CHANGED" in result["binding_errors"]


@pytest.mark.parametrize("field", ["native_user_session_sha256", "runtime_receipt_sha256", "port"])
def test_new_business_cannot_reuse_old_session_or_runtime_receipt(retained, field):
    mutate(retained.run / "business/models.json", lambda value: value["context"].update({field: "foreign"}))
    result = retained.summary()
    assert result["stages"]["models"]["status"] == "rejected"
    assert result["stages"]["models"]["error"].endswith(":" + field)
    assert result["stages"]["business"]["status"] == "passed"


def test_old_business_copy_and_claimed_models_complete_are_rejected(retained):
    mutate(retained.run / "business/revalidate.json", lambda value: value["checks"][0]["result"].update(previous_evidence_reaccepted=True))
    mutate(retained.run / "business/models.json", lambda value: value.update(checks=[{"id": "llm-only", "status": "passed"}]))
    result = retained.summary()
    assert result["stages"]["revalidate"]["error"] == "NATIVE_DIAGNOSTIC_OLD_EVIDENCE_REACCEPTED"
    assert result["stages"]["models"]["error"] == "NATIVE_DIAGNOSTIC_MODEL_CHECKS_INCOMPLETE"


def test_actual_wps_failure_controls_cannot_be_silently_changed(retained):
    retained.control.write_text('[{"changed":true}]', encoding="utf-8")
    result = diagnostics.summary(retained.lab, retained.target, retained.package, retained.source, run_directory=retained.run, wps_evidence=[retained.wps])
    assert result["stages"]["wps:formatter-golden-aabbcc"]["error"] == "NATIVE_DIAGNOSTIC_WPS_CONTROL_CHANGED"
    with pytest.raises(RuntimeError, match="WPS_EVIDENCE_REJECTED"):
        diagnostics.register_current(retained.lab, retained.target, retained.package, retained.source, retained.run, wps_evidence=[retained.wps])
