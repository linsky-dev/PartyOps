"""工作区误报仅对已核实位置和原始内容有效。"""

from __future__ import annotations

import hashlib
import importlib.util
import json
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
PRODUCTION_PYTEST_FINDINGS_SHA256 = "10a00fd5ee20fe3e21c38a1eab450f3c86a4057ff90505c291cb440a00124125"
SPEC = importlib.util.spec_from_file_location(
    "workspace_gitleaks_exceptions", ROOT / "scripts/build-workspace-gitleaks-ignore.py"
)
assert SPEC is not None and SPEC.loader is not None
exceptions = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(exceptions)


def _synthetic_manifest(monkeypatch: pytest.MonkeyPatch | None = None) -> dict[str, object]:
    """构造仅供单测的合成记录；不声称这些发现曾获历史批准。"""
    findings = [
        {
            "path": f"{exceptions.PYTEST_PREFIX}synthetic-finding-{index:02d}.py",
            "rule": "private-key" if index % 2 == 0 else "generic-api-key",
            "line": index + 1,
        }
        for index in range(68)
    ]
    encoded = json.dumps(
        [(item["path"], item["rule"], item["line"]) for item in findings],
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
    ).encode("utf-8")
    findings_digest = hashlib.sha256(encoded).hexdigest()
    if monkeypatch is not None:
        # 仅替换导入模块的测试期常量，生产脚本的历史摘要保持不变。
        monkeypatch.setattr(exceptions, "PYTEST_FINDINGS_SHA256", findings_digest)
    filenames = {item["path"] for item in findings} | {item[0] for item in exceptions.EXTRA_FINDINGS}
    return {
        "schema": 1,
        "receipt_sha256": exceptions.RECEIPT_SHA256,
        "pytest_findings": findings,
        "extra_findings": [
            {"path": path, "rule": rule, "line": line}
            for path, rule, line in sorted(exceptions.EXTRA_FINDINGS)
        ],
        "file_sha256": {
            name: {"digest": hashlib.sha256(f"synthetic:{name}".encode("utf-8")).hexdigest()}
            for name in filenames
        },
    }


def _save_manifest(tmp_path: Path, manifest: dict[str, object]) -> Path:
    saved = tmp_path / "synthetic-manifest.json"
    saved.write_text(json.dumps(manifest), encoding="utf-8")
    return saved


def _isolated(tmp_path: Path, monkeypatch: pytest.MonkeyPatch,
              content: bytes | None) -> tuple[Path, Path, str]:
    root = tmp_path / "checkout"
    root.mkdir()
    manifest = _synthetic_manifest(monkeypatch)
    name = manifest["pytest_findings"][0]["path"]
    if content is not None:
        candidate = root / name
        candidate.parent.mkdir(parents=True)
        candidate.write_bytes(content)
        manifest["file_sha256"][name]["digest"] = hashlib.sha256(content).hexdigest()
    saved = _save_manifest(tmp_path, manifest)
    return root, saved, name


def test_original_module_rejects_synthetic_manifest(tmp_path: Path) -> None:
    root = tmp_path / "checkout"
    root.mkdir()
    manifest = _save_manifest(tmp_path, _synthetic_manifest())
    assert exceptions.PYTEST_FINDINGS_SHA256 == PRODUCTION_PYTEST_FINDINGS_SHA256
    with pytest.raises(ValueError, match="GITLEAKS_EXCEPTION_PYTEST_SCOPE_INVALID"):
        exceptions.validate(root, manifest)


def test_clean_checkout_needs_no_historical_pytest_files(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    root, manifest, _ = _isolated(tmp_path, monkeypatch, content=None)
    assert exceptions.validate(root, manifest) == []


def test_only_present_synthetic_finding_gets_fingerprint(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    root, manifest, name = _isolated(tmp_path, monkeypatch, content=b"synthetic fixture")
    fingerprints = exceptions.validate(root, manifest)
    assert fingerprints == [f"{(root / name).as_posix()}:private-key:1"]


def test_same_path_changed_content_is_rejected(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    root, changed, name = _isolated(tmp_path, monkeypatch, content=b"synthetic fixture")
    (root / name).write_bytes(b"changed fixture")
    with pytest.raises(ValueError, match="GITLEAKS_EXCEPTION_CONTENT_CHANGED"):
        exceptions.validate(root, changed)


def test_different_location_cannot_join_exception_list(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    root, changed, _ = _isolated(tmp_path, monkeypatch, content=None)
    manifest = json.loads(changed.read_text(encoding="utf-8"))
    manifest["extra_findings"][0]["path"] = "backend/app/unknown.py"
    changed.write_text(json.dumps(manifest), encoding="utf-8")
    with pytest.raises(ValueError, match="GITLEAKS_EXCEPTION_EXTRA_SCOPE_INVALID"):
        exceptions.validate(root, changed)
