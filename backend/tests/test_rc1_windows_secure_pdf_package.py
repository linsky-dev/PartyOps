"""The original Windows x64 builder must package the same secure PDF as source."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "scripts"
if str(SCRIPTS) not in sys.path:
    sys.path.insert(0, str(SCRIPTS))
spec = importlib.util.spec_from_file_location(
    "rc1_windows_secure_pdf_inputs", SCRIPTS / "verify_windows_secure_pdf_package.py"
)
assert spec and spec.loader
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def _fixture(root: Path, *, lock: str = "2.1", project: str = "2.1", req: str = "2.1") -> Path:
    backend = root / "backend"
    backend.mkdir()
    (backend / "uv.lock").write_text(
        f'[[package]]\nname = "pymupdf"\nversion = "{lock}"\n', encoding="utf-8"
    )
    (backend / "pyproject.toml").write_text(
        f'[project]\ndependencies = ["PyMuPDF=={project}"]\n', encoding="utf-8"
    )
    (backend / "requirements.txt").write_text(
        f'fastapi==0.140.7\n# explicit release pin\nPyMuPDF=={req}\n',
        encoding="utf-8",
    )
    return root


def test_matching_safe_pins_are_version_floor_only(tmp_path: Path) -> None:
    assert module.verify_inputs(_fixture(tmp_path)) == "2.1"


@pytest.mark.parametrize(
    ("lock", "project", "req"),
    [("1.26.0", "1.26.0", "1.26.0"),
     ("2.1", "1.26.0", "2.1"),
     ("2.1", "2.1", "1.26.0"),
     ("2.1", "2.2", "2.1")],
)
def test_vulnerable_or_inconsistent_source_cannot_package(
    tmp_path: Path, lock: str, project: str, req: str
) -> None:
    with pytest.raises(ValueError):
        module.verify_inputs(_fixture(tmp_path, lock=lock, project=project, req=req))


def test_installed_build_interpreter_must_match_source(tmp_path: Path, monkeypatch) -> None:
    root = _fixture(tmp_path)
    monkeypatch.setattr(module, "_installed_pdf_version", lambda: "1.26.0")
    with pytest.raises(ValueError, match="WINDOWS_PDF_BUILD_INTERPRETER_VERSION_MISMATCH"):
        module.verify_inputs(root, check_installed=True)
    monkeypatch.setattr(module, "_installed_pdf_version", lambda: "2.1.0")
    assert module.verify_inputs(root, check_installed=True) == "2.1"


@pytest.mark.parametrize("pin", ["PyMuPDF>=2.1", "PyMuPDF==2.1; sys_platform == 'win32'", "PyMuPDF", "PyMuPDF==2.1rc1"])
def test_unlocked_or_ambiguous_requirements_are_rejected(tmp_path: Path, pin: str) -> None:
    root = _fixture(tmp_path)
    (root / "backend/requirements.txt").write_text(pin + "\n", encoding="utf-8")
    with pytest.raises(ValueError, match="WINDOWS_PDF_DEPENDENCY_NOT_EXACTLY_PINNED"):
        module.verify_inputs(root)


def test_windows_builder_enforces_gate_before_and_after_pip_install() -> None:
    source = (ROOT / "packaging/windows/build-windows.ps1").read_text(encoding="utf-8")
    gate = "verify_windows_secure_pdf_package.py"
    assert source.count(gate) == 1
    assert source.index(gate) < source.index("verify-full-function-gate.py")
    assert source.index(gate) < source.index("if (-not $LegacyArchitecture) {")
    assert source.index("if (-not $LegacyArchitecture) {") < source.index(
        "& $Python $securePdfVerifier --root $repoRoot"
    )
    assert source.index("pip install -r") < source.index("--check-installed")
    assert source.index("--check-installed") < source.index("[WINDOWS_X64_PDF_BUILD_ENV_UNSAFE]")
    assert source.count("--check-installed") == 1
    assert "[WINDOWS_X64_PDF_BUILD_ENV_UNSAFE]" in source
