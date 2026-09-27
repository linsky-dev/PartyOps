"""对抗门禁：伪造通过、错包、错系统、少步骤及重启替代都须被识别。"""
import copy
import json
from pathlib import Path

import pytest
import yaml
from evidence import (
    aggregate,
    checked_id,
    evaluate,
    now,
    safe_child,
    sha256,
    write_json,
)

ROOT = Path(__file__).resolve().parents[1]
MATRIX = yaml.safe_load((ROOT / "config/test-matrix.yaml").read_text())
SCHEMA = json.loads((ROOT / "schemas/result.schema.json").read_text())


@pytest.fixture
def sample(tmp_path):
    log = tmp_path / "evidence.log"
    log.write_text("仅单元测试合成证据，不能导入真实验收目录。", encoding="utf-8")
    package = {"id": "rpm_x86_64", "version": "1.4.5-rc.6", "sha256": "a" * 64}
    package.update(provenance_status="verified", source_fingerprint="b" * 64)
    result = {"schema_version": 2, "run_id": "unit-test-only", "generated_at": now(),
              "target": "openeuler-iso-x64", "status": "passed", "source_fingerprint": "b" * 64,
              "package": package.copy(), "distribution_match": True,
              "environment": {"vm_uuid": "unit-test-uuid", "media_sha256": "c" * 64, "baseline_id": "clean-os-test", "identity_sha256": sha256(log)},
              "system": {"os": "linux", "arch": "x86_64", "boot_id": "boot-a", "distribution": "openEuler", "distribution_id": "openeuler", "os_release": "24.03", "os_build": "LTS-SP2", "environment_type": "full-system-emulated"},
              "cases": [{"id": case, "status": "passed", "evidence": [{"path": log.name, "sha256": sha256(log)}]} for case in MATRIX["required_cases"]],
              "restart": {"actual": True, "boot_id_before": "boot-a", "boot_id_after": "boot-b"}}
    return result, package, tmp_path


def check(sample, target=None):
    result, package, directory = sample
    expected = dict(target or MATRIX["targets"]["openeuler-iso-x64"])
    expected["runtime_binding"] = {"vm_uuid": "unit-test-uuid", "media_sha256": "c" * 64, "baseline_id": "clean-os-test", "identity_sha256": sha256(directory / "evidence.log") if (directory / "evidence.log").exists() else "d" * 64}
    return evaluate(result, expected, package,
                    MATRIX["required_cases"], directory, SCHEMA, "b" * 64)


def test_complete_evidence_and_timestamp(sample):
    assert now().endswith("+08:00")
    assert check(sample) == []


@pytest.mark.parametrize("field,value,error", [
    ("status", "partial", "RESULT_NOT_PASSED"),
    ("source_fingerprint", "c" * 64, "STALE_SOURCE_FINGERPRINT"),
    ("distribution_match", False, "DISTRIBUTION_MATCH_MISREPORTED"),
    ("schema_version", 9, "INVALID_RESULT_SCHEMA"),
])
def test_invalid_top_level(sample, field, value, error):
    sample[0][field] = value
    assert any(error in entry for entry in check(sample))


@pytest.mark.parametrize("field,value", [("sha256", "c" * 64), ("version", "rc.4"), ("id", "wrong")])
def test_wrong_package(sample, field, value):
    sample[0]["package"][field] = value
    assert f"PACKAGE_{field.upper()}_MISMATCH" in check(sample)


@pytest.mark.parametrize("field,value", [("arch", "aarch64"), ("os", "darwin")])
def test_wrong_guest(sample, field, value):
    sample[0]["system"][field] = value
    assert "GUEST_OS_ISA_MISMATCH" in check(sample)


def test_windows_10_does_not_replace_windows_11(sample):
    sample[0]["system"].update(os="windows", os_release="10")
    assert "GUEST_OS_RELEASE_MISMATCH" in check(sample, MATRIX["targets"]["win11-x64-native"])


@pytest.mark.parametrize("mutation,expected", [
    ("missing", "MISSING_CASE"), ("duplicate", "DUPLICATE_CASE"),
    ("skip", "CASE_NOT_PASSED"), ("empty", "MISSING_EVIDENCE"),
    ("hash", "EVIDENCE_HASH_MISMATCH"), ("traversal", "UNSAFE_OR_MISSING_EVIDENCE"),
])
def test_cases_fail_closed(sample, mutation, expected):
    cases = sample[0]["cases"]
    if mutation == "missing":
        cases.pop()
    elif mutation == "duplicate":
        cases.append(copy.deepcopy(cases[0]))
    elif mutation == "skip":
        cases[0]["status"] = "not_run"
    elif mutation == "empty":
        cases[0]["evidence"] = []
    elif mutation == "hash":
        cases[0]["evidence"][0]["sha256"] = "f" * 64
    else:
        cases[0]["evidence"][0]["path"] = "../outside.log"
    assert any(expected in error for error in check(sample))


def test_reboot_not_process_restart(sample):
    sample[0]["restart"]["boot_id_after"] = "boot-a"
    assert "REBOOT_NOT_PROVEN" in check(sample)
    sample[0]["restart"]["boot_id_before"] = "unrelated"
    assert "REBOOT_SOURCE_MISMATCH" in check(sample)


def test_only_arm_mac_hosted_accepts_documented_cold_start(sample):
    result = sample[0]
    result["restart"] = {"actual": False, "boot_id_before": "a", "boot_id_after": "b",
                         "method": "fresh-job-persisted-state", "job_before": "1", "job_after": "2",
                         "persisted_state_sha256": "c" * 64, "restored_without_reinstall": True}
    assert "RESTART_POLICY_NOT_SATISFIED" in check(sample)
    result["system"].update(os="darwin", arch="arm64", environment_type="hosted-runner")
    assert "REMOTE_ENVIRONMENT_NOT_ALLOWED" in check(sample, MATRIX["targets"]["macos-arm64"])
    assert "RESTART_POLICY_NOT_SATISFIED" in check(sample, MATRIX["targets"]["macos-arm64"])
    result["restart"]["restored_without_reinstall"] = False
    assert "RESTART_POLICY_NOT_SATISFIED" in check(sample, MATRIX["targets"]["macos-arm64"])


def test_evidence_file_missing_or_changed(sample):
    (sample[2] / "evidence.log").write_text("")
    assert any("EVIDENCE_HASH_MISMATCH" in error for error in check(sample))
    (sample[2] / "evidence.log").unlink()
    assert any("EVIDENCE_HASH_MISMATCH" in error for error in check(sample))


def test_all_registered_packages_and_both_windows_required():
    packages = {key: {"sha256": key} for key in MATRIX["packages"]}
    reports = {target: {"runtime_environment_passed": True, "package_sha256": key}
               for key, spec in MATRIX["packages"].items() for target in spec["required_targets"]}
    assert aggregate(MATRIX, packages, reports)["all_packages_runtime_gate"] == "passed"
    reports["win10-x64"]["runtime_environment_passed"] = False
    assert aggregate(MATRIX, packages, reports)["passed_packages"] == len(MATRIX["packages"]) - 1
    reports["win10-x64"]["runtime_environment_passed"] = True
    reports["win11-x64-native"]["package_sha256"] = "old-package"
    assert aggregate(MATRIX, packages, reports)["passed_packages"] == len(MATRIX["packages"]) - 1
    assert aggregate(MATRIX, {}, {})["passed_packages"] == 0


@pytest.mark.parametrize("identifier", ["..", "../other", "X:/", "A", "a/b", "", "x" * 81])
def test_reject_bad_id(identifier):
    with pytest.raises(ValueError):
        checked_id(identifier)


def test_safe_paths_and_atomic_json(tmp_path):
    assert checked_id("win7-x86") == "win7-x86"
    path = safe_child(tmp_path, tmp_path / "child/file.json")
    write_json(path, {"中文": True})
    assert json.loads(path.read_text(encoding="utf-8")) == {"中文": True}
    assert not path.with_suffix(".json.tmp").exists()
    for wrong in (tmp_path, tmp_path / "../outside"):
        with pytest.raises(ValueError):
            safe_child(tmp_path, wrong)


def test_symlink_rejected(tmp_path):
    link = tmp_path / "link"
    try:
        link.symlink_to(tmp_path / "outside", target_is_directory=True)
    except OSError:
        pytest.skip("当前测试身份不能创建符号链接；目标机清理测试仍必须执行")
    with pytest.raises(ValueError):
        safe_child(tmp_path, link / "secret")
