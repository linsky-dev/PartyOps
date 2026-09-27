"""原版系统门禁反例，合成输入不构成 Guest 运行证据。"""
import json
from pathlib import Path

import pytest
from evidence import evaluate, sha256


@pytest.fixture
def original_sample(tmp_path):
    log = tmp_path / "identity.json"
    log.write_text('{"scope":"unit-test-only"}', encoding="utf-8")
    package = {"id": "linux_amd64", "version": "1.4.5-rc.6", "sha256": "a" * 64}
    package.update(provenance_status="verified", source_fingerprint="b" * 64)
    result = {
        "schema_version": 2, "run_id": "unit-test-only", "generated_at": "2026-09-05T00:00:00+08:00",
        "target": "uos-deb-x64", "status": "passed", "source_fingerprint": "b" * 64,
        "package": package, "distribution_match": True,
        "system": {"os": "linux", "arch": "x86_64", "boot_id": "boot-1", "distribution": "UOS",
                   "os_release": "20", "os_build": "1070", "environment_type": "full-system-emulated"},
        "environment": {"vm_uuid": "12345678-1234-1234-1234-123456789012", "media_sha256": "c" * 64,
                        "baseline_id": "clean-os-test", "identity_sha256": sha256(log)},
        "cases": [{"id": "clean_install", "status": "passed", "evidence": [{"path": log.name, "sha256": sha256(log)}]}],
        "restart": {"actual": True, "boot_id_before": "boot-1", "boot_id_after": "boot-2"},
    }
    target = {"os": "linux", "arch": "x86_64", "distribution": "UOS", "os_release": "20", "os_build": "1070",
              "runtime_binding": dict(result["environment"]), "local_only": True}
    schema = json.loads((Path(__file__).resolve().parents[1] / "schemas/result.schema.json").read_text())
    return result, target, package, tmp_path, schema


def errors(sample):
    result, target, package, directory, schema = sample
    return evaluate(result, target, package, ["clean_install"], directory, schema, "b" * 64)


def test_honest_distribution_mismatch_is_still_rejected(original_sample):
    original_sample[0]["system"]["distribution"] = "Deepin"
    original_sample[0]["distribution_match"] = False
    assert "GUEST_DISTRIBUTION_MISMATCH" in errors(original_sample)


def test_wrong_original_build_is_rejected(original_sample):
    original_sample[0]["system"]["os_build"] = "1050"
    assert "GUEST_OS_BUILD_MISMATCH" in errors(original_sample)


@pytest.mark.parametrize("field", ["vm_uuid", "media_sha256", "baseline_id", "identity_sha256"])
def test_old_vm_or_media_or_snapshot_cannot_replace_new_environment(original_sample, field):
    original_sample[0]["environment"][field] = "f" * 64 if field.endswith("sha256") else "different-environment"
    assert "RUNTIME_BINDING_MISMATCH: " + field in errors(original_sample)


def test_local_only_rejects_hosted_evidence(original_sample):
    original_sample[0]["system"]["environment_type"] = "hosted-runner"
    assert "REMOTE_ENVIRONMENT_NOT_ALLOWED" in errors(original_sample)
