"""Only PyMuPDF and its newly necessary transitive dependencies may change."""

from __future__ import annotations

import importlib.util
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location(
    "rc1_pdf_lock_delta", ROOT / "scripts/rc1_windows_pdf_lock_delta.py"
)
assert spec and spec.loader
guard = importlib.util.module_from_spec(spec)
spec.loader.exec_module(guard)


def lock(project_version: str, pdf_version: str, other_version: str, extra: str) -> str:
    dependencies = '[{name = "networkx"}]' if extra else "[]"
    return f'''version = 1
requires-python = ">=3.11"

[[package]]
name = "partyops"
version = "{project_version}"
dependencies = [{{name = "pymupdf"}}, {{name = "other"}}]
[package.metadata]
requires-dist = [{{name = "pymupdf", specifier = "=={pdf_version}"}}, {{name = "other", specifier = "==1.0"}}]

[[package]]
name = "pymupdf"
version = "{pdf_version}"
dependencies = {dependencies}

[[package]]
name = "other"
version = "{other_version}"

{extra}
'''


def compare(tmp_path: Path, old: str, new: str) -> dict:
    original = tmp_path / "original.lock"
    resolved = tmp_path / "candidate.lock"
    original.write_text(old, encoding="utf-8")
    resolved.write_text(new, encoding="utf-8")
    return guard.validate_lock_delta(original, resolved)


EXTRA = '[[package]]\nname = "networkx"\nversion = "3.7"\n'
BASELINE = lock("1.4.6rc1", "1.26.0", "1.0", "")
CANDIDATE = lock("1.4.6rc1", "2.1", "1.0", EXTRA)


def test_only_pdf_and_reachable_transitives_may_be_added(tmp_path: Path):
    result = compare(tmp_path, BASELINE, CANDIDATE)
    assert result["all_other_locked_distributions_unchanged"]
    assert result["new_transitive_distributions"] == {"networkx": ["3.7"]}
    assert not result["release_approved"]
    assert not result["production_lock_modified"]


def test_accepts_only_python_markers_from_new_pymupdf_dependencies(tmp_path: Path):
    marker_first = "python_full_version < '3.12'"
    marker_second = "python_full_version >= '3.12'"
    alternate_deps = (
        '[{name = "networkx", marker = "python_full_version < \'3.12\'"}, '
        '{name = "networkx", marker = "python_full_version >= \'3.12\'"}]'
    )
    original_deps = '[{name = "networkx"}]'
    with_markers = CANDIDATE.replace(
        'requires-python = ">=3.11"',
        'requires-python = ">=3.11"\nresolution-markers = '
        '["python_full_version >= \'3.12\'", "python_full_version < \'3.12\'"]'
    ).replace(original_deps, alternate_deps)
    result = compare(tmp_path, BASELINE, with_markers)
    assert result["all_other_locked_distributions_unchanged"]
    assert result["new_transitive_distributions"] == {"networkx": ["3.7"]}
    assert marker_first in with_markers and marker_second in with_markers


def test_rejects_unrelated_resolver_partition(tmp_path: Path):
    malicious = CANDIDATE.replace(
        'requires-python = ">=3.11"',
        'requires-python = ">=3.11"\nresolution-markers = ["os_name == \'nt\'"]'
    )
    with pytest.raises(ValueError, match="resolver partitions"):
        compare(tmp_path, BASELINE, malicious)


def test_rejects_unrelated_version_upgrade(tmp_path: Path):
    with pytest.raises(ValueError, match="other"):
        compare(tmp_path, BASELINE, lock("1.4.6rc1", "2.1", "1.1", EXTRA))


def test_rejects_unrelated_new_package(tmp_path: Path):
    injected = EXTRA + '[[package]]\nname = "unexpected"\nversion = "0.1"\n'
    with pytest.raises(ValueError, match="unexpected"):
        compare(tmp_path, BASELINE, lock("1.4.6rc1", "2.1", "1.0", injected))


def test_rejects_changes_to_original_application_version(tmp_path: Path):
    with pytest.raises(ValueError, match="partyops"):
        compare(tmp_path, BASELINE, lock("1.4.7", "2.1", "1.0", EXTRA))


def test_rejects_missing_original_dependency(tmp_path: Path):
    changed = CANDIDATE.replace('[[package]]\nname = "other"\nversion = "1.0"\n', "")
    with pytest.raises(ValueError, match="removed"):
        compare(tmp_path, BASELINE, changed)
