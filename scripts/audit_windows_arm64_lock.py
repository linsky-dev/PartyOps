"""Read-only ARM64 wheel availability audit for the frozen RC1 backend lock.

Runs under native Windows ARM64 Python. A compatible locked wheel only proves that
this exact dependency has a matching wheel; it never proves PartyOps can be
installed or used on Windows ARM64.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import platform
import sys
import tomllib
from pathlib import Path
from urllib.parse import unquote, urlparse

from packaging.markers import Marker, default_environment
from packaging.tags import Tag, sys_tags
from packaging.utils import canonicalize_name, parse_wheel_filename


def review_lock(raw: bytes, supported_tags: set[Tag], environment: dict[str, str]) -> dict:
    lock = tomllib.loads(raw.decode("utf-8"))
    packages = {
        canonicalize_name(pkg["name"]): pkg
        for pkg in lock.get("package", [])
    }
    if "partyops" not in packages:
        raise ValueError("frozen lock has no PartyOps root package")
    to_visit = ["partyops"]
    visited: set[str] = set()
    untracked: set[str] = set()
    while to_visit:
        name = canonicalize_name(to_visit.pop())
        if name in visited:
            continue
        if name not in packages:
            untracked.add(name)
            continue
        visited.add(name)
        for dep in packages[name].get("dependencies", []):
            marker = dep.get("marker")
            if marker and not Marker(marker).evaluate(environment=environment):
                continue
            to_visit.append(dep["name"])

    rows = []
    for name in sorted(visited):
        package = packages[name]
        if name == "partyops":
            continue
        compatible = []
        for wheel in package.get("wheels", []):
            filename = Path(unquote(urlparse(wheel["url"]).path)).name
            _, _, _, wheel_tags = parse_wheel_filename(filename)
            if wheel_tags.intersection(supported_tags):
                compatible.append(filename)
        rows.append(
            {
                "name": name,
                "version": package["version"],
                "compatible_locked_wheel": bool(compatible),
                "matching_wheels": sorted(compatible),
                "sdist_in_lock": isinstance(package.get("sdist"), dict),
                "note": (
                    "Pinned source compilation separately passed the native crypto test"
                    if name == "cryptography" and not compatible
                    else ""
                ),
            }
        )
    gaps = [
        item["name"]
        for item in rows
        if not item["compatible_locked_wheel"]
    ]
    if untracked:
        gaps.extend(sorted(untracked))
    return {
        "schema_version": 1,
        "status": "BUILD_ONLY",
        "wheel_availability": "BLOCKED" if gaps else "MATCHES_AVAILABLE",
        "app_installation_verified": False,
        "release_ready": False,
        "dependency_count": len(rows),
        "missing_locked_wheels": sorted(gaps),
        "untracked_dependencies": sorted(untracked),
        "dependencies": rows,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--lock", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    architecture = platform.machine().lower()
    if os.name != "nt" or architecture not in ("arm64", "aarch64"):
        parser.error("native Windows ARM64 Python is required; emulation is not accepted")
    raw = args.lock.read_bytes()
    report = review_lock(raw, set(sys_tags()), default_environment())
    report["lock_sha256"] = hashlib.sha256(raw).hexdigest()
    report["runner_os"] = platform.platform()
    report["machine"] = architecture
    report["python"] = sys.version.split()[0]
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(json.dumps({
        "wheel_availability": report["wheel_availability"],
        "missing_locked_wheels": report["missing_locked_wheels"],
        "dependency_count": report["dependency_count"],
        "evidence": str(args.output),
    }, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
