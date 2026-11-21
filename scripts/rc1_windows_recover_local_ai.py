"""Recover only byte-verified historical RC6 llama.cpp runtime inputs.

The historical signed/unsigned installer is a *source*, never a substitute
for RC1 signatures, dependency/AI model validation, native WPS or packaging.
The reference release manifest and installer SHA must independently match
their frozen GitHub release assets before files can be copied.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
from pathlib import Path

EXPECTED_INSTALLER_SHA = "e3e2ea45ccd38acff059b5713826a78f998a56ccef75341633f67bd4e17e57da"
EXPECTED_MANIFEST_SHA = "9e314cbfa1c5d604ad0f24dd8525b0afcda4f511f1cd8d4571114be1061298ee"
REQUIRED = frozenset({
    "llama-server.exe",
    "llama-server-impl.dll",
    "llama-common.dll",
    "llama.dll",
    "ggml.dll",
    "LICENSE",
    "SOURCE.json",
})
EXTRA_NATIVE = re.compile(r"^(?:ggml|llama)[a-z0-9_.-]*\.(?:dll|exe)$", re.I)


def sha256(file: Path) -> str:
    digest = hashlib.sha256()
    with file.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def validate_files(source: Path, entries: list[dict]) -> dict[str, dict]:
    if not source.is_dir() or source.is_symlink():
        raise ValueError("historical extracted app/ directory is absent or linked")
    records = {}
    for entry in entries:
        if not isinstance(entry, dict):
            raise ValueError("invalid reference release record")
        name = entry.get("path")
        if isinstance(name, str) and "/" not in name and "\\" not in name:
            if name in records:
                raise ValueError("duplicate root release record")
            records[name] = entry

    found = {
        path.name: path for path in source.iterdir()
        if path.name in REQUIRED or EXTRA_NATIVE.fullmatch(path.name)
    }
    if REQUIRED - set(found):
        raise ValueError(f"historical RC6 local AI input absent: {sorted(REQUIRED-set(found))}")
    verified = {}
    for name, path in sorted(found.items()):
        if not path.is_file() or path.is_symlink():
            raise ValueError(f"non-regular or linked local AI payload: {name}")
        stat = path.stat()
        expected = records.get(name)
        if (
            not isinstance(expected, dict)
            or expected.get("size") != stat.st_size
            or expected.get("sha256") != sha256(path)
            or stat.st_size <= 0
        ):
            raise ValueError(f"RC6 release manifest hash/size mismatch: {name}")
        verified[name] = {
            "sha256": expected["sha256"],
            "size": stat.st_size,
        }
    if b"b10331" not in (source / "SOURCE.json").read_bytes().lower():
        raise ValueError("historical local AI source record is not llama-b10331")
    return verified


def recover(root: Path, installer: Path, manifest_path: Path, source: Path, destination: Path) -> dict:
    root = root.resolve(strict=True)
    if not destination.absolute().is_relative_to(root / "artifacts"):
        raise ValueError("local AI output must remain inside repository artifacts/")
    if destination.exists() or destination.is_symlink():
        raise ValueError("refusing to overwrite existing local AI candidate")
    if sha256(installer) != EXPECTED_INSTALLER_SHA:
        raise ValueError("published RC6 installer bytes differ")
    if sha256(manifest_path) != EXPECTED_MANIFEST_SHA:
        raise ValueError("published RC6 reference manifest bytes differ")
    manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
    if any(manifest.get(k) != v for k, v in (
        ("version", "1.4.5-rc.6"),
        ("platform", "windows"),
        ("architecture", "amd64"),
    )):
        observed = {key: manifest.get(key) for key in
                    ("schema_version", "product", "version", "release_tag",
                     "platform", "architecture", "runtime_profile")}
        raise ValueError(f"reference release manifest identity mismatch: {observed}")
    files = manifest.get("files")
    if not isinstance(files, list) or not files:
        raise ValueError("reference release manifest has no files")
    verified = validate_files(source, files)
    destination.mkdir(parents=True)
    for name, reference in verified.items():
        path = destination / name
        shutil.copyfile(source / name, path)
        if sha256(path) != reference["sha256"]:
            raise ValueError(f"post-copy local AI hash mismatch: {name}")
    return {
        "schema_version": 1,
        "status": "PASS_VERIFIED_HISTORICAL_RC6_LOCAL_AI_FILES",
        "scope": "WINDOWS_X64_LOCAL_AI_INPUT_RECOVERY_BUILD_ONLY",
        "source_commit": os.environ.get("GITHUB_SHA", ""),
        "release_installer_sha256": EXPECTED_INSTALLER_SHA,
        "release_manifest_sha256": EXPECTED_MANIFEST_SHA,
        "file_count": len(verified),
        "files": verified,
        "rc1_signed_installer": False,
        "native_windows_10_11_install": False,
        "real_wps_accepted": False,
        "security_and_licenses_approved": False,
        "release_ready": False,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo-root", type=Path, required=True)
    parser.add_argument("--installer", type=Path, required=True)
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--destination", type=Path, required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    args = parser.parse_args()
    try:
        payload = recover(
            args.repo_root, args.installer, args.manifest, args.source, args.destination
        )
        args.evidence.parent.mkdir(parents=True, exist_ok=True)
        args.evidence.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        print(f"PARTYOPS_RC6_LOCALAI_VERIFIED_FOR_WINDOWS_PACKAGING: {payload['file_count']} files")
    except (OSError, ValueError, KeyError, TypeError) as error:
        print(f"RC1_LOCALAI_RECOVERY_BLOCKED: {error}")
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
