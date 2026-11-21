"""Source-only structure checks for RC1 candidate evidence (never extract archives).

These checks supplement, but do not replace, independently verified signatures,
native package installations, signed attestation and final human approval.
"""

from __future__ import annotations

import gzip
import hashlib
import json
import re
import subprocess
import tarfile
from pathlib import Path, PurePosixPath

ARCHIVE_NAME = "PartyOps-1.4.6-rc.1-source.tar.gz"
ARCHIVE_PREFIX = "PartyOps-1.4.6-rc.1"
REQUIRED_SOURCE_PATHS = frozenset({
    f"{ARCHIVE_PREFIX}/backend/app/__init__.py",
    f"{ARCHIVE_PREFIX}/backend/pyproject.toml",
    f"{ARCHIVE_PREFIX}/frontend/package.json",
    f"{ARCHIVE_PREFIX}/scripts/rc1_release_targets.py",
})


def validate_source_archive(root: Path, evidence: dict) -> None:
    """Reject placeholders, traversal, links, duplicates and wrong source roots."""
    if not isinstance(evidence, dict) or evidence.get("filename") != ARCHIVE_NAME:
        raise ValueError("source archive filename is not the frozen PartyOps source")
    root = root.resolve(strict=True)
    path = root / ARCHIVE_NAME
    if path.is_symlink() or not path.is_file() or not path.resolve().is_relative_to(root):
        raise ValueError("source archive path is missing or escapes evidence directory")
    if path.stat().st_size < 512:
        raise ValueError("source archive is too small")
    seen: set[str] = set()
    total_bytes = 0
    with tarfile.open(path, mode="r:gz") as source:
        for index, member in enumerate(source, start=1):
            parts = PurePosixPath(member.name)
            if (
                index > 100000
                or parts.is_absolute()
                or not parts.parts
                or parts.parts[0] != ARCHIVE_PREFIX
                or ".." in parts.parts
                or "\\" in member.name
                or member.name in seen
                or not (member.isfile() or member.isdir())
            ):
                raise ValueError("unsafe or non-PartyOps archive member")
            seen.add(member.name)
            if member.isfile():
                total_bytes += member.size
                if total_bytes > 2 * 1024 * 1024 * 1024:
                    raise ValueError("source archive expansion exceeds 2 GiB")
    if not REQUIRED_SOURCE_PATHS.issubset(seen):
        raise ValueError("essential PartyOps source paths are absent")


def validate_cyclonedx(path: Path) -> None:
    """Reject hash-matched placeholders that do not contain a CycloneDX SBOM."""
    data = json.loads(path.read_text(encoding="utf-8"))
    if (
        not isinstance(data, dict)
        or data.get("bomFormat") != "CycloneDX"
        or data.get("specVersion") != "1.6"
        or not isinstance(data.get("components"), list)
        or not data["components"]
    ):
        raise ValueError("CycloneDX 1.6 with nonempty components is required")


def verify_source_archive_git_identity(
    archive_path: Path,
    checkout_path: Path,
    expected_sha: str,
) -> None:
    """Compare uploaded uncompressed TAR bytes with a separate Git checkout.

    Independent GITHUB_TOKEN-authenticated checkout is done by the workflow.
    Checking exact tar stream prevents an unrelated-but-structurally-valid
    archive from masquerading as a frozen source commit. Does not sign code.
    """
    if not re.fullmatch(r"[0-9a-f]{40}", expected_sha):
        raise ValueError("candidate commit SHA must be 40 lowercase hex digits")
    try:
        checkout = checkout_path.resolve(strict=True)
        actual_sha = subprocess.check_output(
            ["git", "-C", str(checkout), "rev-parse", "--verify", "HEAD^{commit}"],
            stderr=subprocess.DEVNULL,
            timeout=30,
            text=True,
        ).strip()
    except (OSError, subprocess.SubprocessError) as exc:
        raise ValueError("independent source checkout cannot be verified") from exc
    if actual_sha != expected_sha:
        raise ValueError("independent checkout does not match candidate commit")

    command = [
        "git", "-C", str(checkout), "archive", "--format=tar",
        f"--prefix={ARCHIVE_PREFIX}/", expected_sha,
    ]
    try:
        with subprocess.Popen(
            command, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
        ) as process:
            assert process.stdout is not None
            expected_digest = hashlib.file_digest(process.stdout, "sha256").hexdigest()
            assert process.stderr is not None
            process.stderr.read(4096)
            if process.wait(timeout=60) != 0:
                raise ValueError("cannot generate tar for candidate commit")
        with gzip.open(archive_path, "rb") as uploaded:
            actual_digest = hashlib.file_digest(uploaded, "sha256").hexdigest()
    except (OSError, EOFError, subprocess.SubprocessError) as exc:
        raise ValueError("cannot hash original and uploaded tar streams") from exc
    if actual_digest != expected_digest:
        raise ValueError("uploaded source tar differs from immutable Git commit")
