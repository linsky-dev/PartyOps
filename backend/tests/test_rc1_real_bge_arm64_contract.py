"""Native real BGE experiment must stay isolated, pinned and fail closed."""

from __future__ import annotations

import hashlib
import io
import runpy
import urllib.request
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
PROBE = ROOT / "scripts" / "probe_arm64_real_bge_candidate.py"
WORKFLOW = ROOT / ".github" / "workflows" / "cloud-arm64-real-bge-candidate.yml"


def _scope():
    return runpy.run_path(str(PROBE))


def test_fixed_model_inputs_and_existing_business_source_remain_separate() -> None:
    scope = _scope()
    assert scope["ONNX_COMMIT"] == "18e2da99700c49ed48c0a0e3683da39348fbbb36"
    assert scope["TOKENIZER_COMMIT"] == "7999e1d3359715c523056ef9478215996d62a620"
    assets = scope["ASSETS"]
    assert len(assets) == 3
    assert assets[2][3] == "48cea5d44424912a6fd1ea647bf4fe50b55ab8b1e5879c3275f80e339e8fae26"
    assert {item[2] for item in assets} == {
        "onnx/model.onnx", "onnx/model.onnx_data", "tokenizer.json",
    }
    assert all(len(item[3]) == 64 and item[4] > 0 for item in assets)
    script = PROBE.read_text(encoding="utf-8")
    workflow = WORKFLOW.read_text(encoding="utf-8")
    assert "from probe_arm64_partyops_embedding_runtime import _load_unchanged_runtime" in script
    assert '"real_model_weights_executed": True' in script
    assert '"production_signed_modelpack_tested": False' in script
    assert '"equivalent_to_signed_rc6_model": False' in script
    assert '"partyops_app_installed": False' in script
    assert '"release_ready": False' in script
    assert "windows-11-arm" in workflow
    assert "contents: read" in workflow
    assert "BUILD_ONLY" in workflow
    assert "scripts/probe_arm64_real_bge_candidate.py" in workflow
    assert "backend/app/local_ai.py" not in workflow
    assert "artifacts/arm64-real-bge/evidence.json" in workflow


def test_hash_mismatch_removes_untrusted_model(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    source = b"this is not a valid model"
    monkeypatch.setattr(urllib.request, "urlopen", lambda *_args, **_kwargs: io.BytesIO(source))
    downloader = _scope()["_download_verified"]
    with pytest.raises(ValueError, match="BGE_ASSET_DIGEST_INVALID"):
        downloader(tmp_path, "BAAI/test", "a" * 40, "model.onnx", "0" * 64, 1024)
    assert not (tmp_path / "model.onnx").exists()


def test_oversized_or_empty_download_cannot_be_accepted(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch,
) -> None:
    downloader = _scope()["_download_verified"]
    monkeypatch.setattr(urllib.request, "urlopen", lambda *_args, **_kwargs: io.BytesIO(b"overflow"))
    with pytest.raises(ValueError, match="BGE_ASSET_EXCEEDS_SIZE_CAP"):
        downloader(tmp_path, "BAAI/test", "a" * 40, "onnx/model.onnx", "0" * 64, 3)
    assert not (tmp_path / "onnx" / "model.onnx").exists()
    monkeypatch.setattr(urllib.request, "urlopen", lambda *_args, **_kwargs: io.BytesIO(b""))
    with pytest.raises(ValueError, match="BGE_ASSET_DIGEST_INVALID"):
        downloader(tmp_path, "BAAI/test", "a" * 40, "empty.json", hashlib.sha256(b"").hexdigest(), 128)
    assert not (tmp_path / "empty.json").exists()


def test_matching_hash_can_only_write_to_isolated_test_folder(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch,
) -> None:
    raw = b"model bytes for synthetic downloader contract"
    monkeypatch.setattr(urllib.request, "urlopen", lambda *_args, **_kwargs: io.BytesIO(raw))
    record = _scope()["_download_verified"](
        tmp_path, "BAAI/test", "a" * 40, "onnx/model.onnx",
        hashlib.sha256(raw).hexdigest(), 1024,
    )
    assert record["sha256"] == hashlib.sha256(raw).hexdigest()
    assert (tmp_path / "onnx" / "model.onnx").read_bytes() == raw
