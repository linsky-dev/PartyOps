from __future__ import annotations

import hashlib
import json
import sys
from pathlib import Path

import pytest

SCRIPT_DIR = Path(__file__).resolve().parents[1] / "scripts"
sys.path.insert(0, str(SCRIPT_DIR))
from prepare_win10_x86_browser import (  # noqa: E402
    CHECKSUM_URL,
    DOWNLOAD_URL,
    EXPECTED_UUID,
    validate_guest_baseline,
    validate_install_result,
    verify_source,
)


def test_source_evidence_matches_hash_size_and_official_urls(tmp_path):
    source = tmp_path / "Firefox-ESR-115.40.0esr-win32-zh-CN.exe"
    payload = b"offline source validation fixture"
    source.write_bytes(payload)
    digest = hashlib.sha256(payload).hexdigest()
    metadata = tmp_path / (source.stem + ".source.json")
    metadata.write_text(json.dumps({
        "download_url": DOWNLOAD_URL,
        "official_sha256sum_url": CHECKSUM_URL,
        "sha256": digest,
        "bytes": len(payload),
    }), encoding="utf-8")

    result = verify_source(source, metadata, expected_sha256=digest, expected_bytes=len(payload))

    assert result["sha256"] == digest
    assert result["bytes"] == len(payload)


def test_source_evidence_rejects_non_official_url(tmp_path):
    source = tmp_path / "Firefox-ESR-115.40.0esr-win32-zh-CN.exe"
    source.write_bytes(b"fixture")
    metadata = tmp_path / (source.stem + ".source.json")
    metadata.write_text(json.dumps({
        "download_url": "https://example.invalid/firefox.exe",
        "official_sha256sum_url": CHECKSUM_URL,
        "sha256": "0" * 64,
        "bytes": source.stat().st_size,
    }), encoding="utf-8")

    with pytest.raises(RuntimeError, match="BROWSER_OFFICIAL_SOURCE_EVIDENCE_MISMATCH"):
        verify_source(source, metadata)


def test_guest_baseline_requires_bound_clean_win10_x86_identity():
    state = {
        "uuid": EXPECTED_UUID,
        "snapshots": ["clean-original-5e853139"],
        "clean_baseline": {
            "name": "clean-original-5e853139",
            "cold_boot_verified": True,
            "identity_sha256": "a" * 64,
        },
    }
    spec = {"os": "windows", "os_release": "10", "arch": "i686"}
    identity = {
        "os": "windows", "os_build": "19045", "arch": "i686",
        "hardware_uuid": EXPECTED_UUID, "installed_package": False,
    }

    validate_guest_baseline(state, spec, identity)

    identity["installed_package"] = True
    with pytest.raises(RuntimeError, match="WIN10_X86_BASELINE_IDENTITY_MISMATCH"):
        validate_guest_baseline(state, spec, identity)


def test_installed_browser_requires_pe32_x86_and_exact_esr_version():
    valid = {
        "status": "complete", "exit_code": 0, "installed": True,
        "version": "115.40.0", "pe_machine": 0x14C,
        "installer_sha256": "ab895b98fcc78217639b3fe61add19c272e8f632b5afb6ec77a237dfdb0df08f",
    }
    validate_install_result(valid)

    valid["pe_machine"] = 0x8664
    with pytest.raises(RuntimeError, match="BROWSER_INSTALL_OR_PE32_VERSION_VERIFICATION_FAILED"):
        validate_install_result(valid)
