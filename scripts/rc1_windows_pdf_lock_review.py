"""Generate an isolated secure PyMuPDF 2.1 lock candidate, not a production edit.

Windows x64 native smoke decides only technical compatibility. Licensing,
native WPS, other OS architectures, and signed installer validation are separate.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import tomllib
from pathlib import Path


PIN_OLD = "PyMuPDF==1.26.0"
PIN_NEW = "PyMuPDF==2.1"
TRACKED = ("pyproject.toml", "uv.lock", "requirements.txt")


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def locked_version(path: Path) -> str:
    with path.open("rb") as stream:
        packages = tomllib.load(stream)["package"]
    matches = [
        x for x in packages
        if isinstance(x, dict) and str(x.get("name", "")).lower() == "pymupdf"
    ]
    if len(matches) != 1 or not isinstance(matches[0].get("version"), str):
        raise ValueError("expected exactly one PyMuPDF locked version")
    return matches[0]["version"]


def prepare(root: Path, candidate: Path) -> dict:
    source = root / "backend"
    if candidate.exists():
        raise ValueError("isolation directory already exists")
    if locked_version(source / "uv.lock") != "1.26.0":
        raise ValueError("production PyMuPDF baseline changed, review the new source first")
    candidate.mkdir(parents=True)
    hashes = {name: digest(source / name) for name in TRACKED}
    for name in TRACKED:
        original = (source / name).read_text(encoding="utf-8")
        if name != "uv.lock":
            if original.count(PIN_OLD) != 1:
                raise ValueError(f"unexpected {name} PyMuPDF dependency pin")
            original = original.replace(PIN_OLD, PIN_NEW)
        (candidate / name).write_text(original, encoding="utf-8")
    baseline = {
        "production_input_hashes": hashes,
        "expected_candidate_version": "2.1",
        "baseline_pymupdf": "1.26.0",
    }
    (candidate / "baseline.json").write_text(
        json.dumps(baseline, indent=2) + "\n", encoding="utf-8"
    )
    return baseline


def finalize(root: Path, candidate: Path, evidence: Path) -> dict:
    baseline = json.loads((candidate / "baseline.json").read_text(encoding="utf-8"))
    hashes = baseline["production_input_hashes"]
    source = root / "backend"
    if any(digest(source / name) != sha for name, sha in hashes.items()):
        raise ValueError("production dependency files changed during isolated candidate resolution")
    if locked_version(source / "uv.lock") != "1.26.0":
        raise ValueError("production lock unexpectedly changed")
    if locked_version(candidate / "uv.lock") != "2.1":
        raise ValueError("candidate uv.lock did not resolve PyMuPDF 2.1")
    if PIN_NEW not in (candidate / "pyproject.toml").read_text(encoding="utf-8"):
        raise ValueError("candidate pyproject does not pin 2.1")
    if PIN_NEW not in (candidate / "requirements.txt").read_text(encoding="utf-8"):
        raise ValueError("candidate requirements do not pin 2.1")
    result = {
        "schema_version": 1,
        "scope": "WINDOWS_X64_SECURE_PYMUPDF_2_1_ISOLATED_LOCK_REVIEW_ONLY",
        "source_commit": os.environ.get("GITHUB_SHA", ""),
        "candidate_pymupdf": "2.1",
        "baseline_pymupdf": "1.26.0",
        "production_files_unchanged": True,
        "candidate_lock_sha256": digest(candidate / "uv.lock"),
        "candidate_project_sha256": digest(candidate / "pyproject.toml"),
        "real_windows_10_11_installer_built": False,
        "real_wps_native_tested": False,
        "licensing_review_complete": False,
        "native_arm64_or_win7_supported": False,
        "production_upgrade_approved": False,
        "release_ready": False,
        "status": "BUILD_ONLY",
    }
    evidence.parent.mkdir(parents=True, exist_ok=True)
    evidence.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return result


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=("prepare", "finalize"))
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--candidate", type=Path, required=True)
    parser.add_argument("--evidence", type=Path, default=Path("artifacts/windows-pdf-lock-evidence.json"))
    args = parser.parse_args()
    root = args.root.resolve(strict=True)
    candidate = args.candidate.absolute()
    if not candidate.is_relative_to(root / "artifacts") or candidate.is_symlink():
        parser.error("candidate must be a new, isolated directory under repository artifacts/")
    try:
        if args.mode == "prepare":
            payload = prepare(root, candidate)
        else:
            payload = finalize(root, candidate, args.evidence)
    except (OSError, ValueError, KeyError, TypeError) as exc:
        print(f"WINDOWS_PDF_LOCK_REVIEW_BLOCKED: {exc}")
        return 2
    print(json.dumps(payload, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
