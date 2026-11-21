"""Read-only Windows installer-input audit for RC1.

This diagnostic deliberately cannot bless, build, or sign an installer.
An existing file is never proof of its signature, provenance, or native WPS result.
All actual release gates in packaging/windows/build-windows*.ps1 remain authoritative.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import platform
import subprocess
from datetime import datetime, timezone
from pathlib import Path

from rc1_release_security import assert_safe_pymupdf_lock


# Items are anchored to the prerequisites of build-windows.ps1.  Presence is
# informational; separate Authenticode, hash, license and WPS checks still apply.
MODERN_INPUTS = {
    "office_executable": "vendor/windows/libreoffice-headless-amd64/program/soffice.exe",
    "office_cli": "vendor/windows/libreoffice-headless-amd64/program/soffice.com",
    "office_source": "vendor/windows/libreoffice-headless-amd64/SOURCE.json",
    "office_licenses": "vendor/windows/libreoffice-headless-amd64/licenses",
    "sqlite_runtime": "vendor/windows/sqlite-3.53.4/runtime/sqlite3.dll",
    "local_ai": "vendor/windows/local-ai/llama-b10331/llama-server.exe",
    "local_ai_source": "vendor/windows/local-ai/llama-b10331/SOURCE.json",
    "dotnet_source": "vendor/windows/dotnet-framework-4.8/SOURCE.json",
    "dotnet_installer": "vendor/windows/dotnet-framework-4.8/ndp48-x86-x64-allos-enu.exe",
    "formatter_parity": ".release-gates/formatter-parity-x64.json",
    "formatter_e2e": ".release-gates/formatter-features-e2e-x64.json",
    "ocr_archive": "vendor/windows/ocr/tesseract-5.5.3-windows-amd64.zip",
    "ocr_recovered": "vendor/windows/ocr/tesseract-5.5.3-windows-amd64-recovered",
}

LEGACY_INPUTS = {
    "windows7-amd64": {
        "lock": "backend/legacy/requirements-windows7-amd64.lock",
        "runtime_archive": "vendor/windows/ocr/tesseract-5.5.2-windows7-amd64.zip",
        "sqlite": "vendor/windows/sqlite-3.53.4/runtime/sqlite3.dll",
        "ucrt": "vendor/windows/ucrt-10.0.19041.0-x64/SOURCE.json",
        "vc142": "vendor/windows/vc142-14.29.30157-x64/SOURCE.json",
    },
    "windows7-x86": {
        "lock": "backend/legacy/requirements-windows7-x86.lock",
        "runtime_archive": "vendor/windows/ocr/tesseract-5.5.2-windows7-x86.zip",
        "sqlite": "vendor/windows/sqlite-3.53.4-x86/runtime/sqlite3.dll",
        "ucrt": "vendor/windows/ucrt-10.0.19041.0-x86/SOURCE.json",
        "vc142": "vendor/windows/vc142-14.29.30157-x86/SOURCE.json",
    },
}


def inspect_inputs(root: Path, mapping: dict[str, str]) -> dict[str, dict[str, str]]:
    items = {}
    for key, relative in mapping.items():
        path = root / relative
        present = path.is_file() or path.is_dir()
        # No file is validated solely by being present.
        items[key] = {
            "path": relative,
            "state": "PRESENT_UNVERIFIED" if present else "MISSING",
        }
    return items


def current_security_lock(root: Path) -> dict[str, str]:
    try:
        version = assert_safe_pymupdf_lock(root / "backend/uv.lock")
    except (OSError, ValueError) as error:
        return {"state": "BLOCKED", "reason": str(error)}
    return {
        "state": "VERSION_FLOOR_ONLY",
        "version": version,
        "reason": "CVE floor does not certify platform wheels, licenses or behavior",
    }


def full_function_gate(root: Path) -> dict[str, str]:
    # Compare the actual build-scoped fingerprint.  Do not create or refresh
    # evidence without running the original full-function suite + native WPS.
    from importlib.util import module_from_spec, spec_from_file_location

    path = root / "scripts/verify-full-function-gate.py"
    if not path.is_file():
        return {"state": "BLOCKED", "reason": "full-function verifier absent"}
    spec = spec_from_file_location("rc1_full_function_gate_readonly", path)
    if spec is None or spec.loader is None:
        return {"state": "BLOCKED", "reason": "full-function verifier unreadable"}
    module = module_from_spec(spec)
    try:
        spec.loader.exec_module(module)
        digest, count = module.source_fingerprint(root, "package")
        gate = root / ".release-gates/full-function-tests.json"
        if not gate.is_file():
            return {"state": "BLOCKED", "reason": "full-function evidence absent", "fingerprint": digest}
        evidence = json.loads(gate.read_text(encoding="utf-8"))
        recorded = evidence.get("source_fingerprints", {}).get("package", {})
        valid = (
            evidence.get("schema") == 3
            and evidence.get("status") == "passed"
            and evidence.get("fingerprint_canonicalization") == module.FINGERPRINT_CANONICALIZATION
            and recorded == {"sha256": digest, "file_count": count}
        )
        return {
            "state": "CURRENT_RECORD_UNVERIFIED_NATIVE" if valid else "BLOCKED",
            "reason": "presence/fingerprint alone does not independently verify full native WPS evidence"
            if valid else "source fingerprint differs from full-function record",
            "fingerprint": digest,
        }
    except (OSError, ValueError, TypeError, KeyError, AttributeError) as error:
        return {"state": "BLOCKED", "reason": f"full-function fingerprint failed: {type(error).__name__}"}


def audit(root: Path, source_sha: str) -> dict:
    root = root.resolve(strict=True)
    inputs = inspect_inputs(root, MODERN_INPUTS)
    legacy = {target: inspect_inputs(root, mapping) for target, mapping in LEGACY_INPUTS.items()}
    ocr_possible = (
        inputs["ocr_archive"]["state"] == "PRESENT_UNVERIFIED"
        or inputs["ocr_recovered"]["state"] == "PRESENT_UNVERIFIED"
    )
    lock = current_security_lock(root)
    gate = full_function_gate(root)
    blockers = []
    if lock["state"] == "BLOCKED":
        blockers.append("SECURE_PYMUPDF_PRODUCTION_LOCK_REQUIRED")
    if gate["state"] != "CURRENT_RECORD_UNVERIFIED_NATIVE":
        blockers.append("FULL_FUNCTION_GATE_STALE_OR_MISSING")
    for key in ("office_executable", "office_cli", "office_source", "office_licenses",
                "sqlite_runtime", "local_ai", "local_ai_source", "dotnet_source",
                "dotnet_installer", "formatter_parity", "formatter_e2e"):
        if inputs[key]["state"] == "MISSING":
            blockers.append("WINDOWS_X64_INPUT_MISSING:" + key)
    if not ocr_possible:
        blockers.append("WINDOWS_X64_OCR_SOURCE_MISSING")
    # Even if every prerequisite exists, this read-only report cannot certify
    # their signatures, real native WPS tests, or a Windows 10/11 installation.
    blockers.extend([
        "WINDOWS_X64_INPUT_SIGNATURE_LICENSE_AND_HASH_AUDIT_REQUIRED",
        "WINDOWS_X64_REAL_WPS_GOLDEN_ACCEPTANCE_REQUIRED",
        "WINDOWS_10_11_NATIVE_INSTALL_UPGRADE_ROLLBACK_REQUIRED",
        "WINDOWS_7_NATIVE_WHEELHOUSE_SECURITY_BACKPORT_AND_INSTALL_REQUIRED",
        "WINDOWS_INSTALLER_BUILD_SIGNING_AND_RELEASE_APPROVAL_NOT_RUN",
    ])
    return {
        "schema_version": 1,
        "product": "PartyOps",
        "release": "1.4.6-rc.1",
        "source_commit": source_sha,
        "created_at_utc": datetime.now(timezone.utc).isoformat(),
        "runner": {"os": platform.system(), "architecture": platform.machine()},
        "scope": "WINDOWS_INSTALLER_PREREQUISITE_DIAGNOSTIC_ONLY",
        "status": "BLOCKED",
        "installer_generated": False,
        "signed": False,
        "release_ready": False,
        "modern_x64": {
            "windows_10_11": "NOT_RUN_NATIVE_INSTALL",
            "inputs": inputs,
            "production_pymupdf": lock,
            "full_function_gate": gate,
        },
        "legacy": {
            target: {
                "inputs": items,
                "security_backport_wheelhouse": "NOT_VERIFIED",
                "native_install": "NOT_RUN",
            }
            for target, items in legacy.items()
        },
        "blockers": blockers,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--output", type=Path, default=Path("artifacts/windows-rc1-preflight.json"))
    args = parser.parse_args()
    try:
        root = args.root.resolve(strict=True)
        source_sha = subprocess.check_output(
            ["git", "-C", str(root), "rev-parse", "HEAD"],
            text=True, stderr=subprocess.DEVNULL, timeout=30
        ).strip()
        if os.environ.get("GITHUB_SHA") and source_sha != os.environ["GITHUB_SHA"]:
            raise RuntimeError("checkout differs from exact CI source SHA")
        result = audit(root, source_sha)
        output = args.output.resolve()
        if output == root or (output.exists() and output.is_symlink()):
            raise RuntimeError("invalid output location")
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        print("WINDOWS_RC1_PREFLIGHT_DIAGNOSTIC_ONLY: BLOCKED")
        for blocker in result["blockers"]:
            print("BLOCKED: " + blocker)
        print("Evidence: " + str(output))
    except (OSError, subprocess.SubprocessError, RuntimeError) as error:
        print("WINDOWS_RC1_PREFLIGHT_ERROR: " + str(error))
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
