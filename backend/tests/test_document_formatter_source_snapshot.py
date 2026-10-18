from __future__ import annotations

import importlib.util
from pathlib import Path
from types import ModuleType

import pytest

ROOT = Path(__file__).resolve().parents[2]


def _module() -> ModuleType:
    spec = importlib.util.spec_from_file_location(
        "partyops_formatter_source_snapshot",
        ROOT / "scripts" / "verify-document-formatter-source-snapshot.py",
    )
    assert spec and spec.loader
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def test_vendored_formatter_source_matches_explicit_mac_quote_snapshot() -> None:
    # 当前公开树包含已授权的 Mac 引号修订；不能冒充 Windows 上游快照。
    module = _module()
    source = ROOT / "vendor" / "document-formatter-source"
    result = module.verify(source, "mac-quotes-tnr")
    assert result["passed"] is True
    assert result["file_count"] == 898
    assert result["aggregate_sha256"] == "ac8466edf2513ea8e3fe9e61d3b86fb8d7a72ceb6cce366f2d19b59d9c9be171"
    with pytest.raises(RuntimeError, match="FORMATTER_SOURCE_DIVERGED"):
        module.verify(source)


def test_snapshot_verifier_rejects_changed_function_source(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    source = tmp_path / "source"
    target = source / "src" / "Formatter.cs"
    target.parent.mkdir(parents=True)
    target.write_text("class Formatter {}\n", encoding="utf-8")
    module = _module()
    count, digest = module.fingerprint(source)
    monkeypatch.setattr(module, "EXPECTED_FILE_COUNT", count)
    monkeypatch.setattr(module, "EXPECTED_AGGREGATE_SHA256", digest)
    assert module.verify(source)["passed"] is True
    target.write_text("class Formatter { int Changed; }\n", encoding="utf-8")
    with pytest.raises(RuntimeError, match="FORMATTER_SOURCE_DIVERGED"):
        module.verify(source)
