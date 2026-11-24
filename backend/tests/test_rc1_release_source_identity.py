"""RC1 source archive identity tests with a real isolated Git repository."""

from __future__ import annotations

import gzip
import importlib.util
import io
import os
import subprocess
import tarfile
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "rc1_release_evidence", ROOT / "scripts/rc1_release_evidence.py"
)
assert SPEC is not None and SPEC.loader is not None
evidence = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(evidence)


def _git(checkout: Path, *args: str) -> bytes:
    return subprocess.check_output(
        ["git", *args], cwd=checkout, stderr=subprocess.DEVNULL, timeout=30
    )


def _source_fixture(tmp_path: Path) -> tuple[Path, Path, str]:
    checkout = tmp_path / "source-verify"
    checkout.mkdir()
    _git(checkout, "init", "-q")
    _git(checkout, "config", "user.name", "PartyOps test fixture")
    _git(checkout, "config", "user.email", "test@example.invalid")
    paths = {
        "backend/app/__init__.py": "synthetic app",
        "backend/pyproject.toml": "synthetic python config",
        "frontend/package.json": '{"name":"test"}',
        "scripts/rc1_release_targets.py": os.urandom(2048).hex(),
    }
    for filename, data in paths.items():
        path = checkout / filename
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(data, encoding="utf-8")
    _git(checkout, "add", ".")
    _git(checkout, "commit", "-qm", "Fake release tree for test only")
    sha = _git(checkout, "rev-parse", "HEAD").decode("ascii").strip()
    tar = _git(
        checkout, "archive", "--format=tar",
        "--prefix=PartyOps-1.4.6-rc.1/", sha,
    )
    archive = tmp_path / evidence.ARCHIVE_NAME
    archive.write_bytes(gzip.compress(tar, mtime=0))
    return checkout, archive, sha


def test_immutable_archive_accepts_byte_exact_source_commit(tmp_path: Path) -> None:
    checkout, archive, sha = _source_fixture(tmp_path)
    evidence.validate_source_archive(tmp_path, {"filename": archive.name})
    evidence.verify_source_archive_git_identity(archive, checkout, sha)


def test_immutable_archive_blocks_modified_contents_with_valid_tar_tree(
    tmp_path: Path,
) -> None:
    checkout, archive, sha = _source_fixture(tmp_path)
    updated_tar = io.BytesIO()
    with tarfile.open(archive, "r:gz") as source:
        with tarfile.open(fileobj=updated_tar, mode="w") as forged:
            for member in source:
                if not member.isfile():
                    forged.addfile(member)
                    continue
                extracted = source.extractfile(member)
                assert extracted is not None
                payload = extracted.read()
                if member.name.endswith("frontend/package.json"):
                    payload = payload.replace(b"test", b"evil")
                member.size = len(payload)
                forged.addfile(member, io.BytesIO(payload))
    archive.write_bytes(gzip.compress(updated_tar.getvalue(), mtime=0))
    evidence.validate_source_archive(tmp_path, {"filename": archive.name})
    with pytest.raises(ValueError, match="differs from immutable Git commit"):
        evidence.verify_source_archive_git_identity(archive, checkout, sha)


def test_immutable_archive_blocks_mismatched_or_invalid_source_sha(tmp_path: Path) -> None:
    checkout, archive, sha = _source_fixture(tmp_path)
    assert sha != "f" * 40
    with pytest.raises(ValueError, match="checkout does not match"):
        evidence.verify_source_archive_git_identity(archive, checkout, "f" * 40)
    with pytest.raises(ValueError, match="40 lowercase hex digits"):
        evidence.verify_source_archive_git_identity(archive, checkout, "../not-a-sha")


def test_release_workflow_checks_out_immutable_sha_without_saved_credentials() -> None:
    text = (ROOT / ".github/workflows/cloud-release-gate.yml").read_text(
        encoding="utf-8"
    )
    check = text.split(
        "      - name: Independently check out immutable candidate Git commit", 1
    )[1].split("      - name: Evaluate the fail-closed release gate", 1)[0]
    assert "ref: ${{ inputs.source_sha }}" in check
    assert "path: source-verify" in check
    assert "persist-credentials: false" in check
    assert "verify_source_archive_git_identity(" in check
    assert "PARTYOPS_RC1_IMMUTABLE_GIT_ARCHIVE_IDENTITY_PASS" in check
