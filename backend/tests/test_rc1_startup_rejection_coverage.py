"""1.4.6 RC1: 启动与密码学自检必须在异常输入下显式失败。

这些回归测试覆盖正式安装包入口的拒绝分支，不降低既有 95% 行、
92% 分支覆盖率门禁，也不代替目标系统原生安装验收。
"""

from __future__ import annotations

from pathlib import Path

import pytest

from app import startup_selftest


def test_formatter_probe_rejects_oversized_endpoint_record(tmp_path: Path) -> None:
    """端点记录超出固定上限时，在解析 JSON 或请求端口之前拒绝。"""
    path = tmp_path / "logs" / "official-format-endpoint.json"
    path.parent.mkdir()
    path.write_bytes(b"x" * 4097)

    with pytest.raises(RuntimeError, match="排版端点记录无效"):
        startup_selftest._probe_owned_formatter(tmp_path, expected_pid=24680)


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
            return b"\\x00" * 32

    monkeypatch.setattr(hashes, "Hash", CorruptHash)
    with pytest.raises(RuntimeError, match="SM3 已知答案检查失败"):
        startup_selftest.verify_sm_runtime()
