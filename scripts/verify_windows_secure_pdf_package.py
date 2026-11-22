"""Fail closed before PyInstaller packages PartyOps Windows x64 PDF binaries.

A safe uv.lock entry alone is insufficient: the original installer build uses
requirements.txt, and the PyInstaller interpreter may contain another version.
This guard checks all three source pins and, after pip installation, the actual
distribution installed in the interpreter used by the builder.

This only proves source/installed version consistency and a conservative
security floor. It does NOT certify PyMuPDF licensing, wheel signatures, PDF
behavior, native WPS, cross-platform compatibility or release approval.
"""

from __future__ import annotations

import argparse
import importlib.metadata
import re
import sys
import tomllib
from pathlib import Path

from rc1_release_security import assert_safe_pymupdf_lock

_PIN = re.compile(r"pymupdf\s*==\s*([0-9]+\.[0-9]+(?:\.[0-9]+)?)", re.IGNORECASE)


def _normalized_version(version: str) -> tuple[int, int, int]:
    if not re.fullmatch(r"[0-9]+\.[0-9]+(?:\.[0-9]+)?", version):
        raise ValueError("WINDOWS_PDF_PACKAGE_VERSION_INVALID")
    parts = [int(part) for part in version.split(".")]
    return (parts + [0, 0, 0])[:3]  # 2.1 and 2.1.0 are the same release.


def _one_exact_pin(dependencies: list[str], location: str) -> str:
    matches: list[str] = []
    for requirement in dependencies:
        if not isinstance(requirement, str):
            raise ValueError("WINDOWS_PDF_DEPENDENCIES_INVALID:" + location)
        stripped = requirement.strip()
        if re.match(r"^pymupdf(?:\b|\s|[<>=!~])", stripped, flags=re.IGNORECASE):
            match = _PIN.fullmatch(stripped)
            if match is None:
                raise ValueError("WINDOWS_PDF_DEPENDENCY_NOT_EXACTLY_PINNED:" + location)
            matches.append(match.group(1))
    if len(matches) != 1:
        raise ValueError("WINDOWS_PDF_EXACTLY_ONE_PIN_REQUIRED:" + location)
    return matches[0]


def _installed_pdf_version() -> str:
    """One overridable seam so unit tests never patch global metadata APIs."""
    return importlib.metadata.version("PyMuPDF")


def verify_inputs(root: Path, *, check_installed: bool = False) -> str:
    backend = root.resolve(strict=True) / "backend"
    lock_version = assert_safe_pymupdf_lock(backend / "uv.lock")
    with (backend / "pyproject.toml").open("rb") as stream:
        project = tomllib.load(stream)
    dependencies = project.get("project", {}).get("dependencies")
    if not isinstance(dependencies, list):
        raise ValueError("WINDOWS_PDF_PROJECT_DEPENDENCIES_MISSING")
    project_version = _one_exact_pin(dependencies, "pyproject.toml")
    requirements = (backend / "requirements.txt").read_text(encoding="utf-8").splitlines()
    requirements_version = _one_exact_pin(
        [line.strip() for line in requirements if line.strip() and not line.lstrip().startswith("#")],
        "requirements.txt",
    )
    locked = _normalized_version(lock_version)
    if _normalized_version(project_version) != locked or _normalized_version(requirements_version) != locked:
        raise ValueError(
            "WINDOWS_PDF_SOURCE_LOCK_MISMATCH: uv.lock="
            + lock_version + ", pyproject.toml=" + project_version
            + ", requirements.txt=" + requirements_version
        )
    if check_installed:
        try:
            installed = _installed_pdf_version()
        except importlib.metadata.PackageNotFoundError:
            raise ValueError("WINDOWS_PDF_BUILD_INTERPRETER_PYMUPDF_MISSING") from None
        if _normalized_version(installed) != locked:
            raise ValueError(
                "WINDOWS_PDF_BUILD_INTERPRETER_VERSION_MISMATCH: "
                + installed + " != " + lock_version
            )
    return lock_version


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--check-installed", action="store_true")
    args = parser.parse_args()
    try:
        version = verify_inputs(args.root, check_installed=args.check_installed)
    except (OSError, ValueError, TypeError) as error:
        print("WINDOWS_X64_SECURE_PDF_PACKAGE_BLOCKED: " + str(error), file=sys.stderr)
        return 2
    print("WINDOWS_X64_SECURE_PDF_PACKAGE_VERSION_FLOOR_ONLY: " + version)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
