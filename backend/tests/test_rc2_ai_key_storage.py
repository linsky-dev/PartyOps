"""Machine-key protection: preserve compatibility and prevent first-use races."""
from __future__ import annotations

import os
import stat
from concurrent.futures import ThreadPoolExecutor

import pytest

from app import ai_service
from app.problems import ProblemException


def test_concurrent_first_use_keeps_one_machine_key(tmp_path, monkeypatch):
    path = tmp_path / "ai-fernet.key"
    monkeypatch.setattr(ai_service, "_machine_key_path", lambda: path)
    messages = [f"test-value-{i}" for i in range(16)]
    with ThreadPoolExecutor(max_workers=8) as pool:
        ciphertexts = list(pool.map(ai_service.encrypt_api_key, messages))
    assert [ai_service.decrypt_api_key(item) for item in ciphertexts] == messages
    before = path.read_bytes()
    ai_service.encrypt_api_key("another-test-value")
    assert path.read_bytes() == before
    assert list(tmp_path.iterdir()) == [path]


@pytest.mark.skipif(os.name == "nt", reason="POSIX permission semantics; Windows ACL is a separate native gate")
def test_new_key_is_private_before_atomic_publication(tmp_path, monkeypatch):
    path = tmp_path / "ai-fernet.key"
    monkeypatch.setattr(ai_service, "_machine_key_path", lambda: path)
    replace = os.replace
    observed = []
    def inspect(source, destination):
        observed.append(stat.S_IMODE(source.stat().st_mode))
        assert destination == path
        replace(source, destination)
    monkeypatch.setattr(ai_service.os, "replace", inspect)
    encrypted = ai_service.encrypt_api_key("test-value")
    assert observed == [0o600]
    assert stat.S_IMODE(path.stat().st_mode) == 0o600
    assert ai_service.decrypt_api_key(encrypted) == "test-value"


def test_failed_key_publication_leaves_no_partial_key(tmp_path, monkeypatch):
    path = tmp_path / "ai-fernet.key"
    monkeypatch.setattr(ai_service, "_machine_key_path", lambda: path)
    def fail(*_args):
        raise OSError("synthetic atomic replace failure")
    with monkeypatch.context() as scoped:
        scoped.setattr(ai_service.os, "replace", fail)
        with pytest.raises(OSError, match="synthetic"):
            ai_service.encrypt_api_key("test-value")
    assert not path.exists()
    assert not list(tmp_path.iterdir())
    ciphertext = ai_service.encrypt_api_key("test-value")
    assert ai_service.decrypt_api_key(ciphertext) == "test-value"


def test_non_regular_key_path_rejected_without_replacement(tmp_path, monkeypatch):
    path = tmp_path / "ai-fernet.key"
    path.mkdir()
    monkeypatch.setattr(ai_service, "_machine_key_path", lambda: path)
    with pytest.raises(ProblemException) as exc:
        ai_service.decrypt_api_key("synthetic")
    assert exc.value.code == "AI_KEY_UNAVAILABLE"
    assert path.is_dir()
