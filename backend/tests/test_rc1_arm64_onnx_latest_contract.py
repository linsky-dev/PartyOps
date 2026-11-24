"""Contract for isolated 2026 ONNX Runtime native ARM64 candidate."""

from __future__ import annotations

import ast
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def test_latest_onnx_arm64_job_pins_official_wheel_and_real_runtime() -> None:
    source = (ROOT / ".github/workflows/cloud-arm64-onnx-latest-candidate.yml").read_text(
        encoding="utf-8"
    )
    assert "runs-on: windows-11-arm" in source
    assert "architecture: arm64" in source
    assert "onnxruntime==1.32.0" in source
    assert "onnxruntime-1.32.0-cp312-cp312-win_arm64.whl" in source
    assert "64d394c69d561af3efd4d4f49dcac8f11d0ce135a20701c16d53b74056cd4cf8" in source
    assert "--only-binary=:all:" in source
    assert "scripts/probe_arm64_partyops_embedding_runtime.py" in source
    assert "scripts/probe_arm64_embedding_contract.py" in source
    assert "scripts/probe_arm64_onnx_latest_candidate.py" in source
    assert "persist-credentials: false" in source
    assert "BUILD_ONLY" in source


def test_latest_onnx_candidate_is_explicitly_nonproduction() -> None:
    source = (ROOT / "scripts/probe_arm64_onnx_latest_candidate.py").read_text(
        encoding="utf-8"
    )
    ast.parse(source)
    assert 'importlib.metadata.version("onnxruntime")' in source
    assert 'runtime.get("status") != "PASS"' in source
    assert '"production_lock_modified": False' in source
    assert '"real_bge_weight_tested": False' in source
    assert '"release_ready": False' in source
