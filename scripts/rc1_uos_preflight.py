#!/usr/bin/env python3
"""Fail-closed, source-only preflight for PartyOps 1.4.6-rc.1 UOS candidates.

This is not a native UOS installer or an assertion that WPS worked on UOS.
The native glibc 2.17, wheelhouse, WPS and packaging gates stay in the
existing build-portable.sh and build-native.sh implementations.
"""

from __future__ import annotations

import argparse
import json
import platform
import re
import subprocess
from pathlib import Path

VERSION = "1.4.6-rc.1"
PACKAGE_VERSION = "1.4.6~rc.1"


def check_architecture(architecture: str) -> None:
    expected = {"amd64": "x86_64", "arm64": "aarch64"}
    if architecture not in expected:
        raise ValueError(f"Unsupported UOS target architecture: {architecture}")
    actual = platform.machine().lower()
    if actual != expected[architecture]:
        raise ValueError(
            f"Source checks must run on the matching native CPU: "
            f"target={architecture}, current={actual}"
        )


def inspect(root: Path, architecture: str) -> dict[str, object]:
    check_architecture(architecture)
    source_commit = subprocess.check_output(
        ["git", "-C", str(root), "rev-parse", "HEAD"], text=True
    ).strip()
    if re.fullmatch(r"[0-9a-f]{40}", source_commit) is None:
        raise ValueError("Cannot verify the exact source commit")

    paths = {
        "portable": "packaging/uos/build-portable.sh",
        "native": "packaging/linux/build-native.sh",
        "installer": "packaging/uos/one-click-install.sh",
        "transaction": "packaging/linux/post-install-transaction.sh",
        "runtime_smoke": "scripts/test-native-package-runtime.sh",
        "desktop": "packaging/uos/desktop-launcher.sh",
        "version": "scripts/verify-version-consistency.py",
    }
    source = {
        key: (root / path).read_text(encoding="utf-8")
        for key, path in paths.items()
    }
    checks = {
        "version_pinned": (
            'APP_VERSION="1.4.6-rc.1"' in source["portable"]
            and 'DEB_VERSION="1.4.6~rc.1"' in source["native"]
            and 'VERSION="${PARTYOPS_VERSION:-1.4.6-rc.1}"' in source["installer"]
            and 'PACKAGE_VERSION="${PARTYOPS_PACKAGE_VERSION:-1.4.6~rc.1}"'
            in source["installer"]
        ),
        "native_release_gate_intact": (
            "verify-full-function-gate.py" in source["native"]
            and "verify-version-consistency.py" in source["native"]
        ),
        "full_runtime_requires_native_wps_evidence": (
            "PARTYOPS_FORMATTER_EVIDENCE_DIR" in source["portable"]
            and "validate-source-formatter-runtime.py" in source["portable"]
            and "verify-formatter-runtime-evidence.py" in source["portable"]
            and "FORMATTER_RUNTIME_MISSING" in source["portable"]
        ),
        "no_vendor_checksum_mutation": (
            "sed 's/\\r$//' SHA256SUMS | sha256sum -c -" in source["portable"]
            and "sed -i 's/\\r$//' " not in source["portable"]
        ),
        "installer_requires_hash_and_preserves_data": (
            "verify_deb" in source["installer"]
            and "sha256sum -c" in source["installer"]
            and "apt-get install --reinstall" in source["installer"]
            and "/opt/partyops/install-desktop-shortcut.sh" in source["installer"]
        ),
        "installer_has_native_version_and_arch_checks": (
            "dpkg-deb -f" in source["installer"]
            and "dpkg-query" in source["installer"]
            and "PACKAGE_ARCH" in source["installer"]
        ),
        "post_install_is_observable_and_fail_closed": (
            "post-install-selftest.sh" in source["transaction"]
            and "partyops-install-verify.service" in source["transaction"]
            and "PACKAGE_VERIFY_SERVICE_START_FAILED" in source["transaction"]
        ),
        "frozen_package_smoke_kept": (
            "--package-self-test" in source["runtime_smoke"]
            and "--runtime-layout-self-test" in source["runtime_smoke"]
            and "RUNTIME_NOT_EXECUTED" in source["runtime_smoke"]
        ),
        "desktop_launch_kept": (
            "RUNTIME_VERSION_MISMATCH" in source["desktop"]
            and "startup-diagnostic.txt" in source["desktop"]
        ),
    }

    # Missing build inputs are reported, never silently assumed present.
    inputs = {
        "wheels": root / "vendor" / "wheels" / architecture,
        "ocr": root / "vendor" / "ocr" / architecture / "tesseract-runtime.tar.gz",
        "local_ai": root / "vendor" / "local-ai" / architecture / "llama-runtime.tar.gz",
        "office": root / "vendor" / "linux" / f"libreoffice-headless-{architecture}",
        "vendor_hashes": root / "vendor" / "SHA256SUMS",
        "formatter_host": root / "artifacts" / "uos-native-formatter-host" / architecture,
    }
    found = {name: item.exists() for name, item in inputs.items()}
    return {
        "schema_version": 1,
        "product": "PartyOps",
        "version": VERSION,
        "deb_version": PACKAGE_VERSION,
        "source_commit": source_commit,
        "target_os": "UOS V20",
        "target_architecture": architecture,
        "host_cpu": platform.machine(),
        "check_type": "HOSTED_NATIVE_CPU_SOURCE_ONLY",
        "checks": checks,
        "checks_passed": all(checks.values()),
        "available_local_inputs": found,
        "native_uos_install": "NOT_RUN",
        "native_uos_wps_gold_samples": "NOT_RUN",
        "full_offline_binary_build": "NOT_RUN",
        "authentic_uos_deb_artifact": False,
        "release_ready": False,
        "status": "PASS_SOURCE_CONTRACT_ONLY" if all(checks.values()) else "FAIL",
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--architecture", choices=("amd64", "arm64"), required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        result = inspect(args.root.resolve(), args.architecture)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(
            json.dumps(result, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
        )
        for key, passed in result["checks"].items():
            print(f"UOS_SOURCE_CHECK {key}={'PASS' if passed else 'FAIL'}")
        print(f"UOS_SOURCE_RESULT={result['status']}")
        return 0 if result["checks_passed"] else 2
    except (OSError, subprocess.CalledProcessError, ValueError) as exc:
        print(f"UOS_SOURCE_RESULT=FAIL {type(exc).__name__}: {exc}")
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
