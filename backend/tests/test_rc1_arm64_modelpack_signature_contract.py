"""Contract for native ARM64 verification of the unchanged signed-modelpack helpers."""

from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PROBE = ROOT / "scripts/probe_arm64_signed_modelpack.py"
WORKFLOW = ROOT / ".github/workflows/cloud-runner-smoke.yml"
VERIFIER = ROOT / "backend/app/model_packs.py"
BUILDER = ROOT / "packaging/uos/build-model-pack.py"


def test_real_verifier_and_builder_are_used_not_weakened() -> None:
    probe = PROBE.read_text(encoding="utf-8")
    production = VERIFIER.read_text(encoding="utf-8")
    builder = BUILDER.read_text(encoding="utf-8")
    assert "def _manifest_signature_valid(manifest: dict)" in production
    assert "def _validate_manifest(manifest: dict" in production
    assert 'selected_functions = {"_safe_member", "_manifest_signature_valid", "_validate_manifest"}' in probe
    assert "ast.parse(raw.decode" in probe
    assert 'str(builder)' in probe
    assert '"--architecture", "arm64"' in probe
    assert '"--platform", "windows"' in probe
    assert "test-only-synthetic-data" in probe
    assert "Ed25519PrivateKey.generate()" in probe
    assert "Ed25519PrivateKey" in builder
    assert "TestProblemException" in probe
    assert '"MODEL_PACK_SIGNATURE_INVALID"' in probe
    assert '"MODEL_PACK_HASH_MISMATCH"' in probe
    assert '"MODEL_PACK_PATH_INVALID"' in probe
    assert '"production_key_used": False' in probe
    assert '"production_signed_modelpack_tested": False' in probe
    assert '"release_ready": False' in probe
    assert "TemporaryDirectory(" in probe
    assert "with TemporaryDirectory(" in probe
    assert "PARTYOPS_ARM64_EPHEMERAL_SIGNED_MODEL_PACK_VERIFIER_PASS" in probe


def test_test_keys_and_packages_remain_off_ci_artifact() -> None:
    workflow = WORKFLOW.read_text(encoding="utf-8")
    probe = PROBE.read_text(encoding="utf-8")
    assert "matrix.label == 'windows-11-arm'" in workflow
    assert "scripts/probe_arm64_signed_modelpack.py" in workflow
    assert "artifacts/arm64-crypto-probe/modelpack-signature.json" in workflow
    assert "permissions:\n  contents: read" in workflow
    assert 'name: arm64-crypto-build-only-' in workflow
    assert '"production_signed_modelpack_tested": False' in probe
    assert '"whole_app_installed": False' in probe
    assert '"wps_modified": False' in probe
    assert "private_path = folder /" in probe
    assert "package = folder /" in probe
    assert 'output.write_text(json.dumps(evidence' in probe
