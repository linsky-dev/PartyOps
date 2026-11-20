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
    assert 'contents: read' in workflow
    assert 'BUILD_ONLY' in workflow
