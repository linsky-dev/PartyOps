"""Only UOS packaging preflight regressions; no WPS or unrelated product tests."""

from __future__ import annotations

import importlib.util
import json
import platform
import shutil
import subprocess
from pathlib import Path
from unittest.mock import patch

import pytest

ROOT = Path(__file__).resolve().parents[2]
PREFLIGHT_PATH = ROOT / "scripts" / "rc1_uos_preflight.py"
spec = importlib.util.spec_from_file_location("partyops_uos_preflight", PREFLIGHT_PATH)
assert spec and spec.loader
preflight = importlib.util.module_from_spec(spec)
spec.loader.exec_module(preflight)


def _arch() -> str:
    machine = platform.machine().lower()
    if machine == "x86_64":
        return "amd64"
    if machine == "aarch64":
        return "arm64"
    pytest.skip(f"UOS source contract native CPU runner not supported: {machine}")


def test_uos_source_gate_does_not_claim_native_uos_install_or_wps() -> None:
    report = preflight.inspect(ROOT, _arch())
    assert report["checks_passed"] is True
    assert report["status"] == "PASS_SOURCE_CONTRACT_ONLY"
    assert report["check_type"] == "HOSTED_NATIVE_CPU_SOURCE_ONLY"
    assert report["native_uos_install"] == "NOT_RUN"
    assert report["native_uos_wps_gold_samples"] == "NOT_RUN"
    assert report["full_offline_binary_build"] == "NOT_RUN"
    assert report["authentic_uos_deb_artifact"] is False
    assert report["release_ready"] is False
    assert len(report["source_commit"]) == 40


def test_uos_source_gate_rejects_vendor_checksum_in_place_mutation(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    paths = (
        "packaging/uos/build-portable.sh",
        "packaging/linux/build-native.sh",
        "packaging/uos/one-click-install.sh",
        "packaging/linux/post-install-transaction.sh",
        "scripts/test-native-package-runtime.sh",
        "packaging/uos/desktop-launcher.sh",
        "scripts/verify-version-consistency.py",
    )
    for path in paths:
        target = tmp_path / path
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(ROOT / path, target)
    portable = tmp_path / paths[0]
    portable.write_text(
        portable.read_text(encoding="utf-8").replace(
            "sed 's/\\r$//' SHA256SUMS | sha256sum -c -",
            "sed -i 's/\\r$//' \"$ROOT/vendor/SHA256SUMS\"",
        ),
        encoding="utf-8",
    )
    with patch.object(preflight.subprocess, "check_output", return_value="a" * 40):
        report = preflight.inspect(tmp_path, _arch())
    assert report["checks"]["no_vendor_checksum_mutation"] is False
    assert report["checks_passed"] is False
    assert report["release_ready"] is False


def test_crlf_supplier_checksum_is_validated_without_rewriting_input(
    tmp_path: Path,
) -> None:
    checksum = tmp_path / "SHA256SUMS"
    payload = tmp_path / "payload.txt"
    payload.write_bytes(b"partyops-uos-packaging-test\n")
    actual = subprocess.check_output(
        ["sha256sum", "payload.txt"], cwd=tmp_path, text=True
    ).strip()
    checksum.write_bytes((actual + "\r\n").encode("ascii"))
    original = checksum.read_bytes()
    result = subprocess.run(
        ["bash", "-c", "sed 's/\\r$//' SHA256SUMS | sha256sum -c -"],
        cwd=tmp_path,
        text=True,
        capture_output=True,
        check=False,
    )
    assert result.returncode == 0, result.stderr
    assert "payload.txt: OK" in result.stdout
    assert checksum.read_bytes() == original


def test_preflight_rejects_invalid_architecture() -> None:
    with pytest.raises(ValueError, match="Unsupported UOS target architecture"):
        preflight.check_architecture("loong64")
