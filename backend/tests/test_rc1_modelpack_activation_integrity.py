"""Regression: activating a model must never trust cached size/mtime fingerprints."""

from __future__ import annotations

import hashlib
import os
from types import SimpleNamespace

import pytest

from app import model_packs
from app.enums import ModelPackStatus
from app.problems import ProblemException


def test_activation_rehashes_model_after_same_size_and_mtime_tampering(monkeypatch, tmp_path):
    models = tmp_path / "modelpacks"
    runtime = models / "test-only-pack"
    runtime.mkdir(parents=True)
    original = b"valid-on"
    corrupted = b"wrong-on"
    assert len(original) == len(corrupted)

    model_file = runtime / "weights.onnx"
    model_file.write_bytes(original)
    expected = hashlib.sha256(original).hexdigest()
    settings = SimpleNamespace(models_dir=models, app_version="1.4.6-rc.1")
    monkeypatch.setattr(model_packs, "get_settings", lambda: settings)
    pack = SimpleNamespace(
        id="test-integrity-cache",
        install_key="test-only-pack",
        capabilities=["embedding"],
        min_runtime_version="1.4.1",
        status=ModelPackStatus.INSTALLED,
        manifest={"files": {"weights.onnx": {"size": len(original), "sha256": expected}}},
    )
    model_packs._verification_cache.clear()
    try:
        assert model_packs.verify_installed_pack(pack)
        prior = model_file.stat()
        model_file.write_bytes(corrupted)
        os.utime(model_file, ns=(prior.st_atime_ns, prior.st_mtime_ns))
        current = model_file.stat()
        assert current.st_size == prior.st_size
        assert current.st_mtime_ns == prior.st_mtime_ns
        # The status cache has old valid metadata; activation is a security gate,
        # not a status read. It must discard the cache and refuse the altered bytes.
        db = SimpleNamespace(flush=lambda: None)
        with pytest.raises(ProblemException) as caught:
            model_packs.activate_model_pack(db, pack, "embedding", "test-admin")
        assert caught.value.code == "MODEL_PACK_CORRUPT"
        assert pack.status == ModelPackStatus.CORRUPT
    finally:
        model_packs._verification_cache.clear()
