"""Read-only PyMuPDF 2.1 *binary wheel* coverage for PartyOps release targets.

Installed success on Windows x64 is NOT proof the same lock installs on Windows
ARM64, Windows 7, Intel Mac or LoongArch. A source tarball is not a tested
binary. No code/production lock/source is ever edited by this check.
"""

from __future__ import annotations

import argparse
import json
import re
import tomllib
from pathlib import Path

# Native installer targets are intentionally distinct from build runner labels.
TARGETS = {
    "windows-10-x64": "win_amd64",
    "windows-11-x64": "win_amd64",
    "windows-11-arm64": "win_arm64",
    "macos-intel": "macosx_x86_64",
    "macos-apple-silicon": "macosx_arm64",
    "uos-deb-x64": "manylinux_x86_64",
    "uos-deb-arm64": "manylinux_aarch64",
    "kylin-deb-arm64": "manylinux_aarch64",
    "deepin-deb-loong64": "manylinux_loong64",
    "openeuler-rpm-x86_64": "manylinux_x86_64",
    "openeuler-rpm-aarch64": "manylinux_aarch64",
    "windows-7-x64": "legacy_python38",
    "windows-7-x86": "legacy_python38",
}


def wheel_matches(filename: str, target_tag: str) -> bool:
    # PyMuPDF has a compiled extension: a pure-Python 'any' wheel must not
    # count toward a native binary acceptance claim.
    if not filename.startswith("pymupdf-2.1-cp311-abi3-"):
        return False
    if target_tag.startswith("macosx_"):
        arch = target_tag.removeprefix("macosx_")
        return re.search(r"-macosx_[\d_]+_" + re.escape(arch) + r"\.whl$", filename) is not None
    if target_tag.startswith("manylinux_"):
        arch = target_tag.removeprefix("manylinux_")
        return re.search(r"-manylinux_[\d_]+_" + re.escape(arch) + r"\.whl$", filename) is not None
    return filename.endswith("-" + target_tag + ".whl")


def inspect(lock_path: Path) -> dict:
    with lock_path.open("rb") as source:
        document = tomllib.load(source)
    packages = document.get("package")
    if not isinstance(packages, list):
        raise ValueError("candidate has no uv package records")
    matches = [
        entry for entry in packages
        if isinstance(entry, dict) and entry.get("name") == "pymupdf"
    ]
    if len(matches) != 1 or matches[0].get("version") != "2.1":
        raise ValueError("exactly one official PyMuPDF==2.1 record required")
    entries = matches[0].get("wheels")
    if not isinstance(entries, list) or not entries:
        raise ValueError("native PyMuPDF wheel inventory not locked")
    wheels = []
    for row in entries:
        if not isinstance(row, dict) or not isinstance(row.get("url"), str):
            raise ValueError("unverifiable candidate wheel record")
        filename = row["url"].split("?", 1)[0].rsplit("/", 1)[-1]
        if not filename.endswith(".whl"):
            raise ValueError("unexpected binary artifact in wheel inventory")
        if filename in wheels:
            raise ValueError("duplicate native wheel record")
        wheels.append(filename)

    results = {}
    for name, tag in TARGETS.items():
        if tag == "legacy_python38":
            status, reason = "BLOCKED", "PYTHON_3_8_BELOW_OFFICIAL_CP311_ABI3_FLOOR"
            matched = []
        else:
            matched = [filename for filename in wheels if wheel_matches(filename, tag)]
            status = "WHEEL_PRESENT_UNVERIFIED_NATIVE" if matched else "BLOCKED"
            reason = (
                "BINARY_WHEEL_EXISTS_BUT_RUNTIME_NOT_VERIFIED"
                if matched else "NO_OFFICIAL_PYMUPDF_2_1_NATIVE_WHEEL"
            )
        results[name] = {
            "status": status,
            "reason": reason,
            "wheel_filenames": matched,
            "actual_installation": "NOT_RUN",
            "real_native_business_validation": "NOT_RUN",
        }
    for target in ("windows-10-x64", "windows-11-x64"):
        if results[target]["status"] != "WHEEL_PRESENT_UNVERIFIED_NATIVE":
            raise ValueError("Windows x64 official PyMuPDF 2.1 binary wheel absent")
    return {
        "schema_version": 1,
        "scope": "PYMUPDF_2_1_OFFICIAL_LOCKED_NATIVE_WHEEL_COVERAGE_BUILD_ONLY",
        "dependency": "pymupdf",
        "locked_version": "2.1",
        "locked_wheel_filenames": sorted(wheels),
        "targets": results,
        "requires_source_build_review": sorted(
            name for name, item in results.items()
            if item["status"] == "BLOCKED"
        ),
        "windows_x64_wheel_available": True,
        "all_platform_binary_compatibility_proven": False,
        "production_lock_changed": False,
        "installer_built": False,
        "release_ready": False,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--lock", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        payload = inspect(args.lock)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
        print("PARTYOPS_WINDOWS_X64_PYMUPDF_2_1_WHEEL_AVAILABLE_OTHER_TARGETS_NOT_ASSUMED")
    except (OSError, ValueError, TypeError, KeyError) as exc:
        print(f"PARTYOPS_PYMUPDF_NATIVE_WHEEL_MATRIX_BLOCKED: {exc}")
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
