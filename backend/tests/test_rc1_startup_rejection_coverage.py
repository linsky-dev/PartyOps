"""RC1 的国密启动已知答案拒绝路径回归，不涉及既有排版模块。"""

from __future__ import annotations

import pytest

from app import startup_selftest


def test_sm3_known_answer_mismatch_fails_closed(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """启动期不能把损坏的 SM3 provider 输出当作国密探测通过。"""
    from cryptography.hazmat.primitives import hashes

    original_hash = hashes.Hash

    class CorruptHash:
        def __init__(self, algorithm, *args, **kwargs):
            self._hash = original_hash(algorithm, *args, **kwargs)

        def update(self, data: bytes) -> None:
            self._hash.update(data)

        def finalize(self) -> bytes:
            self._hash.finalize()
            return bytes(32)

    monkeypatch.setattr(hashes, "Hash", CorruptHash)
    with pytest.raises(RuntimeError, match="SM3 已知答案检查失败"):
        startup_selftest.verify_sm_runtime()
