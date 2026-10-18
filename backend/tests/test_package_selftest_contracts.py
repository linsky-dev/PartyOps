"""合成安装包的真实来源、资源与有限候选验收契约；不代表原生 WPS 验收。"""
from __future__ import annotations

import hashlib
import json
import os
import sys
from pathlib import Path
from types import SimpleNamespace

import pytest

from app import package_selftest as check


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def write_json(path: Path, payload: object) -> None:
    path.write_text(json.dumps(payload, ensure_ascii=False), encoding="utf-8")


def platform_fixture(monkeypatch, system="darwin", machine="arm64"):
    class PosixOS:
        name = "posix"
        def __getattr__(self, name):
            return getattr(os, name)
    monkeypatch.setattr(check, "os", PosixOS())
    # 仅替换被测模块平台视图，不影响 Windows pytest/后台任务的真实平台。
    monkeypatch.setattr(check, "sys", SimpleNamespace(platform=system, maxsize=sys.maxsize, executable=sys.executable))
    monkeypatch.setattr(check, "platform", SimpleNamespace(machine=lambda: machine))


@pytest.fixture
def mac_package(monkeypatch, tmp_path):
    platform_fixture(monkeypatch)
    runtime = tmp_path / "PartyOps.app" / "Contents" / "MacOS"
    runtime.mkdir(parents=True)
    root = runtime.parent / "Resources" / "formatter-host"
    root.mkdir(parents=True)
    host = root / "partyops-document-formatter-host"
    host.write_bytes(b"synthetic self-contained native host")
    plugins = root / "wps-formatter-plugin"
    plugins.mkdir()
    resources = {}
    for name in check.MAC_PLUGIN_FILES:
        data = f"synthetic package resource: {name}".encode()
        (plugins / name).write_bytes(data)
        resources[name] = digest(data)
    (root / "LICENSE-MONO-RUNTIME.txt").write_text("Synthetic fixture Mono redistribution notice", encoding="utf-8")
    managed = root / "managed-host.bin"
    managed.write_bytes(b"synthetic embedded managed host")
    record = {
        "schema": 3, "platform": "macos", "architecture": "arm64",
        "adapter": check.MAC_OBJECT_ADAPTER, "source_project": "PartyOps.DocumentFormatter.AddIn",
        "source_snapshot_sha256": check.MAC_OBJECT_SOURCE_SHA256, "source_snapshot_files": 898,
        "rules_sha256": check.MAC_OBJECT_RULES_SHA256, "features": list(check.MAC_FEATURES),
        "host_sha256": digest(host.read_bytes()), "timezone": "Asia/Shanghai", "self_contained": True,
        "minimum_macos": "15.0", "acceptance_profile": "mac-object-limited-candidate",
        "limitations": list(check.MAC_LIMITATIONS), "built_at": "2026-10-16T10:00:00+08:00",
        "feature_validation": dict.fromkeys(check.MAC_FEATURES, "pending-target-package-validation"),
        "plugin_resources_sha256": resources, "native_bundle_runtime": "Mono JIT compiler version synthetic-fixture",
        "managed_host_sha256": digest(managed.read_bytes()), "resource_catalog_source_sha256": digest(b"synthetic resource catalog"),
        "pre_sign_host_sha256": digest(b"synthetic pre-sign host"),
    }
    evidence = {
        "schema": 2, "status": "limited-candidate", "acceptance_profile": "mac-object-limited-candidate",
        "platform": "macos", "architecture": "arm64", "adapter": check.MAC_OBJECT_ADAPTER, "provider": "wps",
        "host_sha256": record["host_sha256"], "features": list(check.MAC_FEATURES), "feature_cases": 10,
        "source_snapshot_sha256": check.MAC_OBJECT_SOURCE_SHA256, "rules_sha256": check.MAC_OBJECT_RULES_SHA256,
        "plugin_resources_sha256": resources, "limitations": list(check.MAC_LIMITATIONS), "timezone": "Asia/Shanghai",
        "package_validation_passed": False, "publication_ready": False, "silent": False,
        "rollback_capability_verified": False, "document_cycle_identity_proven": False,
        "cases": [{"case": name, "status": "not-run"} for name in check.MAC_CASES],
        "final_host_verified": False, "feature_validation": dict.fromkeys(check.MAC_FEATURES, "not-run"),
        "verified_at": "2026-10-16T10:05:00+08:00",
    }
    record_path, evidence_path = root / "source-host.json", root / "runtime-evidence.json"
    write_json(record_path, record)
    write_json(evidence_path, evidence)
    return SimpleNamespace(runtime=runtime, root=root, host=host, plugins=plugins, record=record, record_path=record_path, evidence=evidence, evidence_path=evidence_path)


def validate_mac_record(bundle):
    write_json(bundle.record_path, bundle.record)
    return check._validate_source_formatter_record(bundle.host, None, bundle.record_path)


def validate_mac_evidence(bundle):
    write_json(bundle.evidence_path, bundle.evidence)
    return check._validate_source_formatter_evidence(bundle.evidence_path, bundle.host, check.MAC_OBJECT_ADAPTER)


def passed_cases(bundle, *, kind="native-selfcontained-host", tested="host_sha256"):
    for case in bundle.evidence["cases"]:
        case.update(status="passed", execution_kind=kind, source_unchanged=True, cleanup_confirmed=True,
                    lease_released=True, registration_owned=True, capability_revoked=True,
                    receipt_sha256=digest(case["case"].encode()), outputs=[{"bytes": 42, "sha256": digest(b"synthetic output")}])
    bundle.evidence["tested_host_sha256"] = bundle.record[tested]
    bundle.evidence["final_host_verified"] = kind == "native-selfcontained-host" and tested == "host_sha256"
    bundle.evidence["feature_validation"] = dict.fromkeys(check.MAC_FEATURES, "passed")


def test_mac_candidate_record_and_pending_evidence_are_valid_without_claiming_release(mac_package):
    record = validate_mac_record(mac_package)
    evidence = validate_mac_evidence(mac_package)
    assert record["acceptance_profile"] == "mac-object-limited-candidate"
    assert evidence["final_host_verified"] is False
    assert evidence["publication_ready"] is False
    assert evidence["feature_validation"] == dict.fromkeys(check.MAC_FEATURES, "not-run")


@pytest.mark.parametrize("machine,architecture,minimum", [("arm64", "arm64", "15.0"), ("x86_64", "x86_64", "11.0")])
def test_mac_record_accepts_only_its_platform_architecture_and_minimum(mac_package, monkeypatch, machine, architecture, minimum):
    monkeypatch.setattr(check, "platform", SimpleNamespace(machine=lambda: machine))
    mac_package.record.update(architecture=architecture, minimum_macos=minimum)
    assert validate_mac_record(mac_package)["architecture"] == architecture
    mac_package.record["architecture"] = "x64"
    with pytest.raises(RuntimeError, match="来源清单无效"):
        validate_mac_record(mac_package)


def test_mac_validator_rejects_non_mac_runtime_architecture(mac_package, monkeypatch):
    platform_fixture(monkeypatch, system="linux", machine="loongarch64")
    with pytest.raises(RuntimeError, match="目标架构无效"):
        check._validate_mac_object_record(mac_package.host, mac_package.record)


@pytest.mark.parametrize("field,value", [("self_contained", 1), ("schema", 2), ("minimum_macos", "11.0"), ("host_sha256", "0" * 64), ("source_snapshot_sha256", "0" * 64), ("limitations", []), ("capabilities", 25), ("word_vtable_map_sha256", "0" * 64), ("wps_sdk_header_sha256", "0" * 64), ("wps_sdk_matched_methods", 528), ("wps_sdk_mismatched_methods", 0)])
def test_mac_record_rejects_cross_profile_claims_and_tampered_provenance(mac_package, field, value):
    mac_package.record[field] = value
    with pytest.raises(RuntimeError, match="来源清单无效"):
        validate_mac_record(mac_package)


@pytest.mark.parametrize("validation", [None, {}, {"format": "pending-target-package-validation"}, dict.fromkeys(check.MAC_FEATURES, "passed")])
def test_mac_build_record_cannot_invent_target_feature_acceptance(mac_package, validation):
    mac_package.record["feature_validation"] = validation
    with pytest.raises(RuntimeError, match="不能伪造六功能"):
        validate_mac_record(mac_package)


@pytest.mark.parametrize("damage", ["missing-directory", "extra-file", "missing-file", "missing-digest", "tampered-file", "empty-file", "bad-digest"])
def test_mac_plugin_resources_require_exact_inventory_and_actual_hashes(mac_package, damage):
    name = "main.js"
    if damage == "missing-directory":
        mac_package.plugins.rename(mac_package.root / "removed-plugin")
    elif damage == "extra-file":
        (mac_package.plugins / "unexpected.js").write_bytes(b"unexpected")
    elif damage == "missing-file":
        (mac_package.plugins / name).unlink()
    elif damage == "missing-digest":
        del mac_package.record["plugin_resources_sha256"][name]
    elif damage == "tampered-file":
        (mac_package.plugins / name).write_bytes(b"changed after manifest")
    elif damage == "empty-file":
        (mac_package.plugins / name).write_bytes(b"")
    else:
        mac_package.record["plugin_resources_sha256"][name] = "0" * 64
    with pytest.raises(RuntimeError, match="七资源不完整|资源摘要不匹配"):
        validate_mac_record(mac_package)


def test_mac_plugin_resource_link_is_not_a_packaged_regular_file(mac_package):
    resource = mac_package.plugins / "main.js"
    resource.unlink()
    target = mac_package.root / "external.js"
    target.write_bytes(b"synthetic package resource: main.js")
    try:
        resource.symlink_to(target)
    except OSError:
        pytest.skip("当前 Windows 不允许创建符号链接")
    with pytest.raises(RuntimeError, match="资源摘要不匹配"):
        validate_mac_record(mac_package)


@pytest.mark.parametrize("damage", ["missing-license", "empty-license", "missing-runtime", "bad-managed-hash", "nonstring-catalog-hash", "bad-build-time", "utc-build-time"])
def test_mac_runtime_license_build_binding_and_time_are_required(mac_package, damage):
    license_path = mac_package.root / "LICENSE-MONO-RUNTIME.txt"
    if damage == "missing-license":
        license_path.unlink()
    elif damage == "empty-license":
        license_path.write_bytes(b"")
    elif damage == "missing-runtime":
        mac_package.record["native_bundle_runtime"] = "unknown"
    elif damage == "bad-managed-hash":
        mac_package.record["managed_host_sha256"] = "invalid"
    elif damage == "nonstring-catalog-hash":
        mac_package.record["resource_catalog_source_sha256"] = None
    elif damage == "bad-build-time":
        mac_package.record["built_at"] = "not a date"
    else:
        mac_package.record["built_at"] = "2026-10-16T02:00:00+00:00"
    with pytest.raises(RuntimeError, match="来源与许可不完整|构建绑定无效|构建时间无效"):
        validate_mac_record(mac_package)


@pytest.mark.parametrize("field,value", [("publication_ready", True), ("package_validation_passed", True), ("capabilities", 25), ("golden_sha256", "0" * 64), ("rendered_pages", 3), ("status", "passed"), ("host_sha256", "0" * 64)])
def test_mac_limited_evidence_cannot_masquerade_as_full_release(mac_package, field, value):
    mac_package.evidence[field] = value
    with pytest.raises(RuntimeError, match="不能冒用金样"):
        validate_mac_evidence(mac_package)


@pytest.mark.parametrize("field", ["package_validation_passed", "publication_ready", "silent", "rollback_capability_verified", "document_cycle_identity_proven"])
def test_mac_candidate_restrictions_are_boolean_false_not_numeric_zero(mac_package, field):
    mac_package.evidence[field] = 0
    with pytest.raises(RuntimeError, match="必须为布尔假"):
        validate_mac_evidence(mac_package)


@pytest.mark.parametrize("cases", [None, [], [{"case": name, "status": "not-run"} for name in reversed(check.MAC_CASES)], [None] * 10])
def test_mac_evidence_requires_ordered_six_feature_ten_case_inventory(mac_package, cases):
    mac_package.evidence["cases"] = cases
    with pytest.raises(RuntimeError, match="六功能十场景"):
        validate_mac_evidence(mac_package)


@pytest.mark.parametrize("state,case_index", [("unknown", 0), ("passed-with-limitations", 1)])
def test_mac_evidence_does_not_waive_non_format_features(mac_package, state, case_index):
    mac_package.evidence["cases"][case_index]["status"] = state
    with pytest.raises(RuntimeError, match="其它功能不可"):
        validate_mac_evidence(mac_package)


@pytest.mark.parametrize("damage", ["execution-kind", "cleanup-false", "registration-integer", "receipt-nonstring", "receipt-bad-hash", "outputs-missing", "outputs-empty", "output-not-object", "output-bool-size", "output-zero-size", "output-nonstring-hash", "output-bad-hash"])
def test_mac_passed_case_needs_real_receipt_output_and_owned_cleanup_contract(mac_package, damage):
    passed_cases(mac_package)
    case = mac_package.evidence["cases"][0]
    if damage == "execution-kind":
        case["execution_kind"] = "placeholder"
    elif damage == "cleanup-false":
        case["cleanup_confirmed"] = False
    elif damage == "registration-integer":
        case["registration_owned"] = 1
    elif damage == "receipt-nonstring":
        case["receipt_sha256"] = None
    elif damage == "receipt-bad-hash":
        case["receipt_sha256"] = "invalid"
    elif damage == "outputs-missing":
        del case["outputs"]
    elif damage == "outputs-empty":
        case["outputs"] = []
    elif damage == "output-not-object":
        case["outputs"] = [None]
    elif damage == "output-bool-size":
        case["outputs"][0]["bytes"] = True
    elif damage == "output-zero-size":
        case["outputs"][0]["bytes"] = 0
    elif damage == "output-nonstring-hash":
        case["outputs"][0]["sha256"] = None
    else:
        case["outputs"][0]["sha256"] = "bad"
    with pytest.raises(RuntimeError, match="真实回执/输出或清理"):
        validate_mac_evidence(mac_package)


@pytest.mark.parametrize("kind,tested,final", [("native-selfcontained-host", "host_sha256", True), ("managed-source-host", "managed_host_sha256", False), ("native-selfcontained-host", "pre_sign_host_sha256", False)])
def test_mac_tested_host_binds_exact_native_managed_or_presign_artifact(mac_package, kind, tested, final):
    passed_cases(mac_package, kind=kind, tested=tested)
    evidence = validate_mac_evidence(mac_package)
    assert evidence["final_host_verified"] is final
    assert evidence["publication_ready"] is False


@pytest.mark.parametrize("tested_hash", [None, 17, "invalid", "0" * 64])
def test_mac_passed_cases_cannot_claim_unbound_or_missing_tested_host(mac_package, tested_hash):
    passed_cases(mac_package)
    mac_package.evidence["tested_host_sha256"] = tested_hash
    with pytest.raises(RuntimeError, match="实测Host"):
        validate_mac_evidence(mac_package)


def test_mac_format_limitations_require_manual_review_and_correct_summary(mac_package):
    passed_cases(mac_package)
    case = mac_package.evidence["cases"][0]
    case.update(status="passed-with-limitations", manual_review_required=True)
    mac_package.evidence["feature_validation"]["format"] = "passed-with-limitations"
    assert validate_mac_evidence(mac_package)["final_host_verified"] is True
    del case["manual_review_required"]
    with pytest.raises(RuntimeError, match="人工核对"):
        validate_mac_evidence(mac_package)


@pytest.mark.parametrize("conversion_state,expected", [("failed", "failed"), ("not-run", "not-run"), ("passed", "passed")])
def test_mac_conversion_summary_aggregates_all_export_cases(mac_package, conversion_state, expected):
    passed_cases(mac_package)
    mac_package.evidence["cases"][4]["status"] = conversion_state
    mac_package.evidence["final_host_verified"] = conversion_state == "passed"
    mac_package.evidence["feature_validation"]["convert"] = expected
    assert validate_mac_evidence(mac_package)["feature_validation"]["convert"] == expected
    mac_package.evidence["feature_validation"]["convert"] = "incorrect"
    with pytest.raises(RuntimeError, match="六功能汇总"):
        validate_mac_evidence(mac_package)


@pytest.mark.parametrize("damage", ["false-final-claim", "integer-final-claim", "invalid-time", "utc-time"])
def test_mac_final_claim_and_verification_time_must_match_actual_cases(mac_package, damage):
    if damage == "false-final-claim":
        mac_package.evidence["final_host_verified"] = True
    elif damage == "integer-final-claim":
        mac_package.evidence["final_host_verified"] = 0
    elif damage == "invalid-time":
        mac_package.evidence["verified_at"] = "bad"
    else:
        mac_package.evidence["verified_at"] = "2026-10-16T02:00:00+00:00"
    with pytest.raises(RuntimeError, match="最终Host验收结论|实测时间无效"):
        validate_mac_evidence(mac_package)


@pytest.fixture
def linux_formatter(monkeypatch, tmp_path):
    platform_fixture(monkeypatch, system="linux", machine="aarch64")
    runtime = tmp_path / "native-package"
    root = runtime / "formatter-host"
    root.mkdir(parents=True)
    host = root / "partyops-document-formatter-host"
    host.write_bytes(b"synthetic native Linux formatter")
    native = {}
    for name in ("word-vtable-map.json", "LICENSE-WPS-SDK.txt", "LICENSE-MONO-RUNTIME.txt"):
        path = root / name
        path.write_bytes(f"synthetic native dependency: {name}".encode())
        native[name] = digest(path.read_bytes())
    record = {"schema": 2, "timezone": "Asia/Shanghai", "source_project": "PartyOps.DocumentFormatter.AddIn",
              "source_snapshot_sha256": check.FORMATTER_SOURCE_SNAPSHOT_SHA256, "source_snapshot_files": check.FORMATTER_SOURCE_SNAPSHOT_FILES,
              "platform": "linux", "architecture": "arm64", "features": list(check.MAC_FEATURES), "capabilities": 25,
              "host_sha256": digest(host.read_bytes()), "adapter": "wps-native-source-adapter", "built_at": "2026-10-16T10:00:00+08:00",
              "word_vtable_map_sha256": native["word-vtable-map.json"], "wps_sdk_header_sha256": check.FORMATTER_WPS_SDK_HEADER_SHA256,
              "wps_sdk_matched_methods": 528, "wps_sdk_mismatched_methods": 0, "native_bundle_runtime": "Mono JIT compiler version synthetic-fixture"}
    evidence = {"schema": 1, "status": "passed", "timezone": "Asia/Shanghai", "verified_at": "2026-10-16T10:05:00+08:00",
                "platform": "linux", "architecture": "arm64", "provider": "wps", "adapter": record["adapter"], "host_sha256": record["host_sha256"],
                "source_sha256": check.FORMATTER_SOURCE_FIXTURE_SHA256, "golden_sha256": check.FORMATTER_GOLDEN_FIXTURE_SHA256,
                "semantic_signature_sha256": digest(b"synthetic signature"), "parity_evidence_sha256": digest(b"synthetic parity"),
                "feature_evidence_sha256": digest(b"synthetic features"), "rendered_pages": 3, "feature_cases": 10,
                "features": list(check.MAC_FEATURES), "capabilities": 25, "native_adapter_files_sha256": native}
    record_path, evidence_path = root / "source-host.json", root / "runtime-evidence.json"
    write_json(record_path, record)
    write_json(evidence_path, evidence)
    return SimpleNamespace(runtime=runtime, root=root, host=host, record=record, record_path=record_path, evidence=evidence, evidence_path=evidence_path)


def test_linux_formatter_source_and_evidence_accept_matching_package_files(linux_formatter):
    bundle = linux_formatter
    assert check._validate_source_formatter_record(bundle.host, None, bundle.record_path)["architecture"] == "arm64"
    assert check._validate_source_formatter_evidence(bundle.evidence_path, bundle.host, bundle.record["adapter"])["capabilities"] == 25


@pytest.mark.parametrize("damage", ["missing-vtable", "empty-license", "changed-vtable", "wrong-sdk", "wrong-method-count", "mismatched-methods", "missing-mono", "bad-time", "utc-time"])
def test_linux_formatter_requires_bound_native_dependencies_and_build_time(linux_formatter, damage):
    bundle = linux_formatter
    if damage == "missing-vtable":
        (bundle.root / "word-vtable-map.json").unlink()
    elif damage == "empty-license":
        (bundle.root / "LICENSE-WPS-SDK.txt").write_bytes(b"")
    elif damage == "changed-vtable":
        (bundle.root / "word-vtable-map.json").write_bytes(b"changed after source record")
    elif damage == "wrong-sdk":
        bundle.record["wps_sdk_header_sha256"] = "0" * 64
    elif damage == "wrong-method-count":
        bundle.record["wps_sdk_matched_methods"] = 527
    elif damage == "mismatched-methods":
        bundle.record["wps_sdk_mismatched_methods"] = 1
    elif damage == "missing-mono":
        bundle.record["native_bundle_runtime"] = "unknown"
    elif damage == "bad-time":
        bundle.record["built_at"] = "not a date"
    else:
        bundle.record["built_at"] = "2026-10-16T02:00:00+00:00"
    write_json(bundle.record_path, bundle.record)
    with pytest.raises(RuntimeError, match="原生运行时不完整|构建时间无效|不是北京时间"):
        check._validate_source_formatter_record(bundle.host, None, bundle.record_path)


@pytest.mark.parametrize("damage", ["missing-inventory", "extra-inventory", "missing-file", "tampered-license"])
def test_linux_evidence_hashes_exact_packaged_native_dependencies(linux_formatter, damage):
    bundle = linux_formatter
    if damage == "missing-inventory":
        bundle.evidence["native_adapter_files_sha256"] = None
    elif damage == "extra-inventory":
        bundle.evidence["native_adapter_files_sha256"]["untrusted.dll"] = "0" * 64
    elif damage == "missing-file":
        (bundle.root / "LICENSE-MONO-RUNTIME.txt").unlink()
    else:
        (bundle.root / "LICENSE-WPS-SDK.txt").write_bytes(b"tampered after runtime evidence")
    write_json(bundle.evidence_path, bundle.evidence)
    with pytest.raises(RuntimeError, match="真实 WPS 验收证据无效"):
        check._validate_source_formatter_evidence(bundle.evidence_path, bundle.host, bundle.record["adapter"])


@pytest.fixture
def complete_candidate(mac_package, monkeypatch, tmp_path):
    """只替换包内外部程序/SQLite/AI模块边界，来源与证据校验保持真实执行。"""
    from app.database import db_runtime
    bundle = mac_package
    contents = check._runtime_contents(bundle.runtime)
    frontend = contents / "frontend"
    (frontend / "assets").mkdir(parents=True)
    (frontend / "assets" / "app.js").write_bytes(b"synthetic frontend")
    (frontend / "index.html").write_text('<script src="assets/app.js"></script>', encoding="utf-8")
    ocr, tessdata, _library = check._ocr_runtime(bundle.runtime)
    ocr.write_bytes(b"synthetic OCR executable")
    tessdata.mkdir(parents=True)
    (tessdata / "chi_sim.traineddata").write_bytes(b"synthetic language data")
    llama = check._native_executable(bundle.runtime, "llama-server")
    llama.write_bytes(b"synthetic LLM executable")
    office = contents / "office-runtime" / "LibreOffice.app" / "Contents" / "MacOS" / "soffice"
    office.parent.mkdir(parents=True)
    office.write_bytes(b"synthetic office executable")
    monkeypatch.setattr(db_runtime, "validate_capabilities", lambda: {"version": "3.50.4", "safe_version": True, "fts5": True})
    import_module = check.importlib.import_module
    def packaged_import(name):
        if name in {"numpy", "onnxruntime", "tokenizers"}:
            return SimpleNamespace(__version__="synthetic-package-version")
        return import_module(name)
    monkeypatch.setattr(check.importlib, "import_module", packaged_import)
    monkeypatch.setattr(check.tempfile, "tempdir", str(tmp_path))
    monkeypatch.setenv("MONO_ENV_OPTIONS", "synthetic parent options")
    monkeypatch.setenv("MONO_BUNDLED_OPTIONS", "synthetic bundled options")
    calls = []
    def native_run(command, **options):
        import subprocess
        calls.append((list(command), options))
        if command[0] == str(ocr):
            assert command[1:] == ["--list-langs"]
            return subprocess.CompletedProcess(command, 0, "List of available languages:\nchi_sim\n", "")
        if command[0] == str(llama):
            assert command[1:] == ["--version"] and options["timeout"] == 120
        elif command[0] == str(office):
            assert command[1] == "--headless" and command[2].startswith("-env:UserInstallation=file:")
            assert command[3] == "--version"
        elif command[0] == str(bundle.host):
            assert command[1] == "--self-test"
            write_json(Path(command[2]), {"schema": 1, "passed": True, "source_project": "PartyOps.DocumentFormatter.AddIn", "features": list(check.MAC_FEATURES), "engine": "synthetic-source-batch-host"})
        else:
            raise AssertionError(f"包自检不得回退系统程序：{command}")
        return subprocess.CompletedProcess(command, 0, b"synthetic runtime version", b"")
    monkeypatch.setattr(check.subprocess, "run", native_run)
    bundle.ocr, bundle.llama, bundle.office = ocr, llama, office
    bundle.calls, bundle.native_run, bundle.packaged_import = calls, native_run, packaged_import
    return bundle


def test_complete_candidate_selftest_checks_all_dependencies_but_preserves_limitations(complete_candidate):
    bundle = complete_candidate
    result = check.run_selftest(bundle.runtime)
    assert result["passed"] is True and result["runtime_profile"] == "full"
    assert result["crypto"] == "tls-ed25519-passed"
    assert set(result["smart_runtime"]) == {"numpy", "onnxruntime", "tokenizers"}
    formatter = result["document_formatter"]
    assert formatter["package_function_validation"] == "pending"
    assert formatter["final_host_verified"] is False
    assert formatter["declared_catalog_entries"] == 25
    assert "capabilities" not in formatter
    assert [Path(command[0]).name for command, _options in bundle.calls] == ["tesseract", "llama-server", "soffice", "partyops-document-formatter-host"]
    formatter_env = bundle.calls[-1][1]["env"]
    assert formatter_env["MONO_CONFIG"] == "/dev/null"
    assert "MONO_ENV_OPTIONS" not in formatter_env and "MONO_BUNDLED_OPTIONS" not in formatter_env
    assert bundle.calls[0][1]["env"]["MONO_ENV_OPTIONS"] == "synthetic parent options"
    assert os.environ["MONO_ENV_OPTIONS"] == "synthetic parent options"


@pytest.mark.parametrize("failure", ["smart-import", "missing-llama", "office-exit", "formatter-report", "ocr-timeout"])
def test_package_cli_rejects_dependency_or_native_failures_without_success_claim(complete_candidate, monkeypatch, capsys, failure):
    import subprocess
    bundle = complete_candidate
    expected = {"smart-import": "onnxruntime", "missing-llama": "本地 LLM 运行时缺失", "office-exit": "公文转换运行时无法启动", "formatter-report": "原排版源码宿主自检结果无效", "ocr-timeout": "timed out"}[failure]
    if failure == "smart-import":
        def missing(name):
            if name == "onnxruntime":
                raise ModuleNotFoundError("onnxruntime missing from synthetic package")
            return bundle.packaged_import(name)
        monkeypatch.setattr(check.importlib, "import_module", missing)
    elif failure == "missing-llama":
        bundle.llama.unlink()
    else:
        def failed_native(command, **options):
            if failure == "office-exit" and command[0] == str(bundle.office):
                return subprocess.CompletedProcess(command, 17, "中文原生错误".encode(), b"x" * 2000)
            if failure == "formatter-report" and command[0] == str(bundle.host):
                write_json(Path(command[2]), {"schema": 1, "passed": False})
                return subprocess.CompletedProcess(command, 0, b"", b"")
            if failure == "ocr-timeout" and command[0] == str(bundle.ocr):
                raise subprocess.TimeoutExpired(command, 30)
            return bundle.native_run(command, **options)
        monkeypatch.setattr(check.subprocess, "run", failed_native)
    assert check.main(bundle.runtime) == 2
    result = json.loads(capsys.readouterr().out)
    assert result["passed"] is False and expected in result["error"]
    if failure == "office-exit":
        assert "退出码 17" in result["error"] and "中文原生错误" in result["error"]
        assert len(result["error"]) < 1800


@pytest.fixture
def complete_loong_core(linux_formatter, monkeypatch, tmp_path):
    """真实 ELF/清单绑定决定 core，不能凭环境变量免查完整包依赖。"""
    import subprocess

    from app.database import db_runtime
    bundle = linux_formatter
    platform_fixture(monkeypatch, system="linux", machine="loongarch64")
    for payload, path in ((bundle.record, bundle.record_path), (bundle.evidence, bundle.evidence_path)):
        payload["architecture"] = "loong64"
        write_json(path, payload)
    header = bytearray(64)
    header[:6] = b"\x7fELF\x02\x01"
    header[18:20] = (258).to_bytes(2, "little")
    header[48:52] = (3).to_bytes(4, "little")
    (bundle.runtime / "partyops").write_bytes(header)
    manifest = {"schema_version": 1, "product": "PartyOps", "platform": "linux-deb", "architecture": "loong64", "runtime_profile": "core",
                "files": [{"path": "partyops", "size": len(header), "sha256": digest(header)}]}
    write_json(bundle.runtime / "release-manifest.json", manifest)
    frontend = bundle.runtime / "frontend"
    frontend.mkdir()
    (frontend / "index.html").write_text("<!doctype html>", encoding="utf-8")
    ocr, tessdata, _library = check._ocr_runtime(bundle.runtime)
    ocr.parent.mkdir(parents=True)
    ocr.write_bytes(b"synthetic OCR")
    tessdata.mkdir()
    (tessdata / "chi_sim.traineddata").write_bytes(b"synthetic language")
    office = check._office_executable(bundle.runtime)
    office.parent.mkdir(parents=True)
    office.write_bytes(b"synthetic office")
    monkeypatch.setattr(db_runtime, "validate_capabilities", lambda: {"safe_version": True, "fts5": True})
    monkeypatch.setattr(check.tempfile, "tempdir", str(tmp_path))
    imported, calls = [], []
    real_import = check.importlib.import_module
    def core_import(name):
        if name in {"numpy", "onnxruntime", "tokenizers"}:
            imported.append(name)
            assert name == "numpy", "已验证 core 不应要求包中未提供的 AI 模块"
            return SimpleNamespace(__version__="synthetic-core-numpy")
        return real_import(name)
    def core_native(command, **options):
        calls.append(list(command))
        assert Path(command[0]) in {ocr, office, bundle.host}
        if Path(command[0]) == bundle.host:
            write_json(Path(command[2]), {"schema": 1, "passed": True, "source_project": "PartyOps.DocumentFormatter.AddIn", "features": list(check.MAC_FEATURES), "engine": "synthetic-native-source-host"})
        return subprocess.CompletedProcess(command, 0, "chi_sim\n", "")
    monkeypatch.setattr(check.importlib, "import_module", core_import)
    monkeypatch.setattr(check.subprocess, "run", core_native)
    bundle.calls, bundle.imported, bundle.manifest = calls, imported, manifest
    return bundle


def test_verified_loong_core_omits_only_declared_local_ai_and_still_checks_resources(complete_loong_core):
    bundle = complete_loong_core
    result = check.run_selftest(bundle.runtime)
    assert result["runtime_profile"] == "core" and result["passed"] is True
    assert result["llama"] == "not-provided" and bundle.imported == ["numpy"]
    assert {item["capability"] for item in result["deferred_checks"]} == {"semantic_rerank", "local_llm"}
    assert [Path(command[0]).name for command in bundle.calls] == ["tesseract", "soffice", "partyops-document-formatter-host"]
    assert result["document_formatter"]["capabilities"] == 25
    assert result["crypto"] == "tls-ed25519-passed"
    (bundle.runtime / "ocr" / "tessdata" / "chi_sim.traineddata").unlink()
    with pytest.raises(RuntimeError, match="中文 OCR 运行时不完整"):
        check.run_selftest(bundle.runtime)


@pytest.mark.parametrize("damage", ["missing-manifest", "changed-executable"])
def test_loong_core_rejects_unproven_identity_before_running_native_programs(complete_loong_core, damage):
    bundle = complete_loong_core
    if damage == "missing-manifest":
        (bundle.runtime / "release-manifest.json").unlink()
    else:
        with (bundle.runtime / "partyops").open("ab") as stream:
            stream.write(b"changed after manifest")
    with pytest.raises(RuntimeError, match="龙芯安装包身份无效"):
        check.run_selftest(bundle.runtime)
    assert bundle.calls == [] and bundle.imported == []


def test_native_dependency_removed_after_source_validation_cannot_report_success(complete_loong_core, monkeypatch):
    bundle = complete_loong_core
    validate = check._validate_source_formatter_record
    def remove_after_validation(*args):
        record = validate(*args)
        # 模拟自检期间文件被移除，后续阶段必须重新确认实际资源存在。
        (bundle.root / "LICENSE-MONO-RUNTIME.txt").unlink()
        return record
    monkeypatch.setattr(check, "_validate_source_formatter_record", remove_after_validation)
    with pytest.raises(RuntimeError, match="原排版源码宿主缺少 WPS 原生适配资源"):
        check.run_selftest(bundle.runtime)
    assert all(Path(command[0]) != bundle.host for command in bundle.calls)
