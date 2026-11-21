"""Contract: new ARM64 ONNX research must not affect existing x64 lock or release."""

from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def test_isolated_arm64_onnx_candidate_never_claims_release_readiness() -> None:
    script = (ROOT / "scripts/probe_arm64_onnx_candidate.py").read_text(encoding="utf-8")
    workflow = (ROOT / ".github/workflows/cloud-arm64-onnx-candidate.yml").read_text(encoding="utf-8")
    assert 'platform.machine().lower() not in ("arm64", "aarch64")' in script
    assert '"SYNTHETIC_MODEL_ONLY"' in script
    assert 'np.testing.assert_array_equal' in script
    assert '"partyops_app_installed": False' in script
    assert '"partyops_models_verified": False' in script
    assert '"release_ready": False' in script
    assert '--only-binary=:all:' in workflow
    assert 'windows-11-arm' in workflow
    assert 'numpy==2.3.5' in workflow
    assert 'onnxruntime==1.24.2' in workflow
    assert 'onnx==1.23.2' in workflow
    assert 'tokenizers==0.23.3' in workflow
    assert 'scripts/probe_arm64_embedding_contract.py' in workflow
    embedding = (ROOT / "scripts/probe_arm64_embedding_contract.py").read_text(encoding="utf-8")
    assert "Tokenizer.from_file" in embedding
    assert "encode_batch" in embedding
    assert "input_ids" in embedding and "attention_mask" in embedding
    assert "np.testing.assert_array_equal" in embedding
    assert '"real_bge_model_tested": False' in embedding
    assert '"release_ready": False' in embedding
    assert 'contents: read' in workflow
    assert 'BUILD_ONLY' in workflow

def test_isolated_probe_executes_real_embedding_runtime_class_without_production_changes() -> None:
    script = (ROOT / "scripts/probe_arm64_partyops_embedding_runtime.py").read_text(encoding="utf-8")
    workflow = (ROOT / ".github/workflows/cloud-arm64-onnx-candidate.yml").read_text(encoding="utf-8")
    business = (ROOT / "backend/app/local_ai.py").read_text(encoding="utf-8")
    assert 'class EmbeddingRuntime' in business
    assert 'ast.parse(source_text)' in script
    assert 'item.name == "EmbeddingRuntime"' in script
    assert 'exec(compile(program' in script
    assert 'engine.encode(pack' in script
    assert 'engine.loaded_for(pack.id)' in script
    assert 'engine.unload()' in script
    assert 'MODEL_DIMENSION_MISMATCH' in script
    assert 'MODEL_POOLING_INVALID' in script
    assert '"full_app_imported": False' in script
    assert '"real_bge_model_tested": False' in script
    assert '"release_ready": False' in script
    assert 'scripts/probe_arm64_partyops_embedding_runtime.py' in workflow
    assert 'partyops-embedding-runtime.json' in workflow
