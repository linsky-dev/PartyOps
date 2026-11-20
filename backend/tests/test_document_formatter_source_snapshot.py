from __future__ import annotations

import importlib.util
import json
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


def test_stage_creates_locked_upstream_without_changing_vendored_source(tmp_path: Path) -> None:
    module = _module()
    vendor = ROOT / "vendor" / "document-formatter-source"
    destination = tmp_path / "upstream"
    original = (vendor / module.PUNCTUATION_SOURCE).read_bytes()
    staged = module.stage_upstream(vendor, destination)
    assert staged["file_count"] == 898
    assert staged["aggregate_sha256"] == "7ae0eb67a0cb6a2d4a332cde74adf8977d93ae73541f864f01df214d39fefdf2"
    assert (vendor / module.PUNCTUATION_SOURCE).read_bytes() == original
    assert (destination / module.PUNCTUATION_SOURCE).read_bytes() != original
    with pytest.raises(RuntimeError, match="FORMATTER_SOURCE_DIVERGED"):
        module.verify(destination, "mac-quotes-tnr")
    with pytest.raises(RuntimeError, match="FORMATTER_SOURCE_STAGE_INVALID"):
        module.stage_upstream(vendor, destination)
    assert module.verify(destination) == staged
    assert module.stage_upstream(destination, tmp_path / "explicit-upstream") == staged


def test_linux_managed_runtime_matches_upstream_manifest_and_files(tmp_path: Path) -> None:
    module = _module()
    upstream = {"aggregate_sha256": module.EXPECTED_AGGREGATE_SHA256, "file_count": 898}
    host = tmp_path / "PartyOps.DocumentFormatter.Host.exe"
    host.write_bytes(b"managed host test fixture")
    rules = tmp_path / "PartyOps.DocumentFormatter.AddIn.dll"
    rules.write_bytes(b"managed rules test fixture")
    filename = tmp_path / "source-host.json"
    manifest = {
        "schema": 2,
        "platform": "windows",
        "architecture": "AnyCPU",
        "adapter": "dotnet-framework-com-source",
        "source_snapshot_sha256": upstream["aggregate_sha256"],
        "source_snapshot_files": upstream["file_count"],
        "host_sha256": module._content_sha256(host),
        "rules_sha256": module._content_sha256(rules),
    }
    filename.write_text(json.dumps(manifest), encoding="utf-8")
    module.verify_managed_runtime(tmp_path, upstream)

    for key, wrong_value in (
        ("source_snapshot_sha256", module.MAC_QUOTE_AGGREGATE_SHA256),
        ("architecture", "x64"),
    ):
        filename.write_text(json.dumps(manifest | {key: wrong_value}), encoding="utf-8")
        with pytest.raises(RuntimeError, match="FORMATTER_MANAGED_SOURCE_DIVERGED"):
            module.verify_managed_runtime(tmp_path, upstream)

    filename.write_text(json.dumps(manifest), encoding="utf-8")
    rules.write_bytes(b"changed rules")
    with pytest.raises(RuntimeError, match="FORMATTER_MANAGED_HASH_DIVERGED"):
        module.verify_managed_runtime(tmp_path, upstream)
