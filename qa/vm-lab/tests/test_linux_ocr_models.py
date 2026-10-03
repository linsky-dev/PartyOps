"""验证 Linux Guest QA 控制器的 core 身份与不加载复杂模型契约。"""
import hashlib
import importlib.util
import json
from pathlib import Path

import pytest

SCRIPT = Path(__file__).resolve().parents[1] / "guest/linux-ocr-models.py"
SPEC = importlib.util.spec_from_file_location("linux_ocr_models", SCRIPT)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


def installed_fixture(tmp_path):
    executable = tmp_path / "partyops"
    header = bytearray(64)
    header[:6] = b"\x7fELF\x02\x01"
    header[18:20] = (258).to_bytes(2, "little")
    header[48:52] = (3).to_bytes(4, "little")
    executable.write_bytes(header + b"test-runtime")
    payload = {
        "schema_version": 1,
        "product": "PartyOps",
        "version": "1.4.5-rc.6",
        "source_commit": "a" * 40,
        "platform": "linux-deb",
        "architecture": "loong64",
        "runtime_profile": "core",
        "files": [{
            "path": "partyops",
            "size": executable.stat().st_size,
            "sha256": hashlib.sha256(executable.read_bytes()).hexdigest(),
        }],
    }
    (tmp_path / "release-manifest.json").write_text(json.dumps(payload), encoding="utf-8")
    return executable


def verified_identity():
    return {
        "package_identity_status": "verified",
        "architecture": "loong64",
        "runtime_profile": "core",
        "elf_machine": 258,
        "manifest_sha256": "a" * 64,
        "executable_sha256": "b" * 64,
    }


def core_status():
    return {
        "runtime_profile": "core",
        "supported_capabilities": MODULE.CORE_CAPABILITIES.copy(),
        "ready": False,
        "embedding_available": False,
        "llm_available": False,
        "embedding_loaded": False,
        "llm_running": False,
        "intent_available": False,
        "embedding_pack_id": None,
        "llm_pack_id": None,
        "intent_pack_id": None,
    }


def test_installed_core_identity_binds_manifest_and_loongarch_elf(tmp_path):
    executable = installed_fixture(tmp_path)
    identity = MODULE.verify_core_installed_identity(executable, architecture="loongarch64")

    assert identity["package_identity_status"] == "verified"
    assert identity["runtime_profile"] == "core"
    assert identity["architecture"] == "loong64"
    assert identity["elf_machine"] == 258
    assert identity["manifest_sha256"] == hashlib.sha256(
        (tmp_path / "release-manifest.json").read_bytes()
    ).hexdigest()
    assert identity["executable_sha256"] == hashlib.sha256(executable.read_bytes()).hexdigest()


@pytest.mark.parametrize(
    "fault, expected",
    [
        ("wrong_architecture", "CORE_INSTALLED_MANIFEST_IDENTITY_MISMATCH"),
        ("wrong_profile", "CORE_INSTALLED_MANIFEST_IDENTITY_MISMATCH"),
        ("wrong_hash", "CORE_INSTALLED_EXECUTABLE_MANIFEST_MISMATCH"),
        ("wrong_elf_machine", "CORE_INSTALLED_ELF_INVALID"),
        ("wrong_host_architecture", "CORE_INSTALLED_ARCHITECTURE_MISMATCH"),
    ],
)
def test_installed_core_identity_rejects_wrong_manifest_or_elf(tmp_path, fault, expected):
    executable = installed_fixture(tmp_path)
    manifest_path = tmp_path / "release-manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    architecture = "loongarch64"
    if fault == "wrong_architecture":
        manifest["architecture"] = "amd64"
    elif fault == "wrong_profile":
        manifest["runtime_profile"] = "full"
    elif fault == "wrong_hash":
        manifest["files"][0]["sha256"] = "0" * 64
    elif fault == "wrong_elf_machine":
        data = bytearray(executable.read_bytes())
        data[18:20] = (62).to_bytes(2, "little")
        executable.write_bytes(data)
    elif fault == "wrong_host_architecture":
        architecture = "x86_64"
    manifest_path.write_text(json.dumps(manifest), encoding="utf-8")

    with pytest.raises(RuntimeError, match=expected):
        MODULE.verify_core_installed_identity(executable, architecture=architecture)


@pytest.mark.parametrize(
    "field, value",
    [
        ("runtime_profile", "full"),
        ("supported_capabilities", [*MODULE.CORE_CAPABILITIES, "local_llm"]),
        ("embedding_available", True),
        ("llm_available", True),
        ("embedding_loaded", True),
        ("llm_running", True),
        ("intent_available", True),
        ("embedding_pack_id", "unexpected-pack"),
        ("llm_pack_id", "unexpected-pack"),
        ("intent_pack_id", "unexpected-pack"),
    ],
)
def test_core_runtime_status_rejects_profile_capability_or_loaded_model_drift(field, value):
    status = core_status()
    status[field] = value

    with pytest.raises(RuntimeError):
        MODULE.validate_core_runtime_status(status, verified_identity())


def test_core_runtime_status_requires_independently_verified_identity():
    identity = verified_identity()
    identity["executable_sha256"] = "not-a-sha256"

    with pytest.raises(RuntimeError, match="CORE_INSTALLED_IDENTITY_UNPROVEN"):
        MODULE.validate_core_runtime_status(core_status(), identity)


def test_masked_false_api_fields_do_not_claim_underlying_model_activity_is_verified():
    status = core_status()
    # 仅作为反例输入：API 自报字段可以掩蔽底层活动，控制器没有独立观测该活动。
    status["test_only_unobserved_system_model_process"] = "running"
    evidence = MODULE.validate_core_runtime_status(status, verified_identity())

    assert evidence["evidence_source"] == "authenticated /api/v1/ai/runtime/status response"
    assert evidence["api_reported"]["embedding_loaded"] is False
    assert evidence["api_reported"]["llm_running"] is False
    assert evidence["actual_model_activity_verified"] is False
    assert "system_model_activity_verified" not in evidence
    assert "test_only_unobserved_system_model_process" not in evidence["api_reported"]


def test_core_identity_must_match_configured_lifecycle_executable_hash():
    identity = verified_identity()
    digest = identity["executable_sha256"]

    assert MODULE.bind_core_identity_to_lifecycle(
        identity, {"executable_sha256": digest}
    )["lifecycle_executable_sha256"] == digest
    with pytest.raises(RuntimeError, match="CORE_LIFECYCLE_EXECUTABLE_BINDING_MISSING"):
        MODULE.bind_core_identity_to_lifecycle(identity, {})
    with pytest.raises(RuntimeError, match="CORE_LIFECYCLE_EXECUTABLE_BINDING_MISMATCH"):
        MODULE.bind_core_identity_to_lifecycle(identity, {"executable_sha256": "c" * 64})


def test_core_activation_requires_exact_409_problem_code():
    assert MODULE.validate_unsupported_activation(
        409, {"code": "LOCAL_AI_PACKAGE_UNSUPPORTED"}, "embedding"
    ) == {"capability": "embedding", "http_status": 409,
          "code": "LOCAL_AI_PACKAGE_UNSUPPORTED"}
    for status, payload in (
        (404, {"code": "LOCAL_AI_PACKAGE_UNSUPPORTED"}),
        (409, {"code": "MODEL_PACK_NOT_FOUND"}),
    ):
        with pytest.raises(RuntimeError):
            MODULE.validate_unsupported_activation(status, payload, "llm")


@pytest.mark.parametrize(
    "case, result",
    [
        ("create", {"engine": "rules", "intent": "task.create", "operation": "write",
                    "can_execute": False, "flags": []}),
        ("search", {"engine": "rules", "intent": "search.query", "operation": "read",
                    "can_execute": False, "flags": []}),
        ("negated", {"engine": "rules", "intent": "unknown", "operation": "none",
                     "can_execute": False, "flags": ["NEGATED"]}),
        ("injection", {"engine": "rules", "intent": "unknown", "operation": "none",
                       "can_execute": False, "flags": ["PROMPT_INJECTION"]}),
        ("ambiguous", {"engine": "rules", "intent": "unknown", "operation": "none",
                       "can_execute": False, "flags": ["AMBIGUOUS"]}),
    ],
)
def test_core_intent_fallback_is_rule_preview_only(case, result):
    assert MODULE.core_fallback_expectations(case, result) is True


def test_core_intent_fallback_rejects_needle_or_executable_result():
    result = {"engine": "needle", "intent": "task.create", "operation": "write",
              "can_execute": False, "flags": []}
    with pytest.raises(RuntimeError, match="CORE_INTENT_RULE_FALLBACK_NOT_USED"):
        MODULE.core_fallback_expectations("create", result)
    result.update(engine="rules", can_execute=True)
    with pytest.raises(RuntimeError, match="CORE_INTENT_PREVIEW_EXECUTABLE"):
        MODULE.core_fallback_expectations("create", result)


def test_full_inference_paths_remain_present():
    source = SCRIPT.read_text(encoding="utf-8")
    assert "SIGNED_INTENT_MODEL_REQUIRED" in source
    assert 'run.checked("real-semantic-search"' in source
    assert 'run.checked("real-local-llm-inference"' in source
