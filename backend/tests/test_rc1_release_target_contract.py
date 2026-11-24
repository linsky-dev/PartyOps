"""Guard RC1 release evidence: ten artifacts, twelve independent native scenarios."""

from __future__ import annotations

import runpy
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MATRIX = runpy.run_path(str(ROOT / "scripts" / "rc1_release_targets.py"))
INSTALLERS = MATRIX["INSTALLER_VALIDATION_TARGETS"]
NATIVE = MATRIX["expected_native_targets"]()


def test_ten_installer_targets_keep_all_twelve_native_acceptance_scenarios():
    assert len(INSTALLERS) == 10
    assert set(INSTALLERS) == {
        "windows-x64", "windows-7-x64", "windows-7-x86",
        "deb-amd64", "deb-arm64", "deb-loong64",
        "rpm-x86_64", "rpm-aarch64", "macos-intel", "macos-apple-silicon",
    }
    assert len(NATIVE) == 12
    assert len(set(NATIVE)) == len(NATIVE)
    assert NATIVE["windows-10-x64"] == NATIVE["windows-11-x64"] == "windows-x64"
    assert NATIVE["uos-deb-arm64"] == NATIVE["kylin-deb-arm64"] == "deb-arm64"
    assert set(NATIVE.values()) == set(INSTALLERS)


def test_manual_candidate_and_release_gate_share_identity_and_fail_closed():
    candidate = (ROOT / ".github/workflows/cloud-package-candidate.yml").read_text(encoding="utf-8")
    gate = (ROOT / ".github/workflows/cloud-release-gate.yml").read_text(encoding="utf-8")
    assert "from scripts.rc1_release_targets import" in candidate
    assert "from scripts.rc1_release_targets import" in gate
    assert '"native_validation_targets"' in candidate
    assert 'manifest.get("native_validation_targets", [])' in gate
    assert 'installer target matrix is incomplete or contains unknown targets' in gate
    assert 'native target matrix is incomplete or contains duplicate targets' in gate
    assert 'native test evidence is missing' in gate
    assert '"status": "NOT_RUN"' in candidate
    assert '"release_gate": "BLOCKED"' in candidate
    assert '"wps_native_acceptance": "NOT_RUN"' in candidate
    assert '"wps_native_acceptance"' in gate
    assert "READY_FOR_FINAL_HUMAN_RELEASE_APPROVAL" in gate
