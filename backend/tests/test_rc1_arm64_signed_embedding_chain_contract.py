"""Ensure the ARM64 signed-pack to ONNX bridge cannot bypass production checks."""

from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PROBE = ROOT / "scripts/probe_arm64_signed_embedding_chain.py"
WORKFLOW = ROOT / ".github/workflows/cloud-runner-smoke.yml"


def test_chain_uses_unchanged_real_partyops_builder_verifier_and_embedding() -> None:
    source = PROBE.read_text(encoding="utf-8")
    assert "from probe_arm64_signed_modelpack import" in source
    assert "load_unchanged_modelpack_verifier" in source
    assert "_load_unchanged_runtime" in source
    assert "_create_synthetic_components" in source
    assert 'repo / "packaging" / "uos" / "build-model-pack.py"' in source
    assert 'repo / "backend" / "app" / "model_packs.py"' in source
    assert 'repo / "backend" / "app" / "local_ai.py"' in source
    assert 'records, signed = validate(manifest, archive)' in source
    assert 'for name in records:' in source
    assert "runtime.encode(pack, texts)" in source
    assert "runtime.encode(pack, [\"hello world\"], is_query=True)" in source
    assert 'expect_denied(validate, corrupted, "MODEL_PACK_HASH_MISMATCH")' in source
    assert '"signed_rc6_model_pack_tested": False' in source
    assert '"full_app_import_and_activation": "NOT_RUN"' in source
    assert '"usable_for_release": False' in source


def test_ephemeral_keys_and_signed_archives_not_uploaded_to_actions_artifact() -> None:
    source = PROBE.read_text(encoding="utf-8")
    workflow = WORKFLOW.read_text(encoding="utf-8")
    assert "with TemporaryDirectory(" in source
    assert "Ed25519PrivateKey.generate()" in source
    assert "private = scratch /" in source
    assert "bundle = scratch /" in source
    assert 'source_sha' in source
    assert 'signed-embedding-chain.json' in workflow
    assert 'scripts/probe_arm64_signed_embedding_chain.py' in workflow
    assert "matrix.label == 'windows-11-arm'" in workflow
    assert "numpy==2.3.5" in workflow and "onnxruntime==1.24.2" in workflow
    assert "onnx==1.23.2" in workflow and "tokenizers==0.23.3" in workflow
    assert "contents: read" in workflow
    assert "artifacts/arm64-crypto-probe/signed-embedding-chain.json" in workflow
    assert "private-key.pem" not in workflow
    assert ".partyops-modelpack" not in workflow
