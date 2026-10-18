"""工作区误报仅对已核实位置和原始内容有效。"""

from __future__ import annotations

import hashlib
import importlib.util
import json
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
MANIFEST = ROOT / "qa/vm-lab/release-preparation/gitleaks-workspace-exceptions-20260923.json"
SPEC = importlib.util.spec_from_file_location(
    "workspace_gitleaks_exceptions", ROOT / "scripts/build-workspace-gitleaks-ignore.py"
)
assert SPEC is not None and SPEC.loader is not None
exceptions = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(exceptions)


def _isolated(tmp_path: Path, content: bytes | None) -> tuple[Path, Path, str]:
    root = tmp_path / "checkout"
    root.mkdir()
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    name = manifest["pytest_findings"][0]["path"]
    if content is not None:
        candidate = root / name
        candidate.parent.mkdir(parents=True)
        candidate.write_bytes(content)
        manifest["file_sha256"][name]["digest"] = hashlib.sha256(content).hexdigest()
    saved = tmp_path / "manifest.json"
    saved.write_text(json.dumps(manifest), encoding="utf-8")
    return root, saved, name


def test_clean_checkout_needs_no_historical_pytest_files(tmp_path: Path) -> None:
    root, manifest, _ = _isolated(tmp_path, content=None)
    assert exceptions.validate(root, manifest) == []


def test_only_present_approved_finding_gets_fingerprint(tmp_path: Path) -> None:
    root, manifest, name = _isolated(tmp_path, content=b"synthetic fixture")
    fingerprints = exceptions.validate(root, manifest)
    assert fingerprints == [f"{(root / name).as_posix()}:private-key:1"]


def test_same_path_changed_content_is_rejected(tmp_path: Path) -> None:
    root, changed, name = _isolated(tmp_path, content=b"synthetic fixture")
    (root / name).write_bytes(b"changed fixture")
    with pytest.raises(ValueError, match="GITLEAKS_EXCEPTION_CONTENT_CHANGED"):
        exceptions.validate(root, changed)


def test_different_location_cannot_join_exception_list(tmp_path: Path) -> None:
    root, changed, _ = _isolated(tmp_path, content=None)
    manifest = json.loads(changed.read_text(encoding="utf-8"))
    manifest["extra_findings"][0]["path"] = "backend/app/unknown.py"
    changed.write_text(json.dumps(manifest), encoding="utf-8")
    with pytest.raises(ValueError, match="GITLEAKS_EXCEPTION_EXTRA_SCOPE_INVALID"):
        exceptions.validate(root, changed)
