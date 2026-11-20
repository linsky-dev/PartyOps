"""Exercise the actual release-gate inline Python against isolated synthetic evidence."""

from __future__ import annotations

import hashlib
import json
import os
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE_SHA = "a" * 40


def _inline_gate_script() -> str:
    workflow = (ROOT / ".github/workflows/cloud-release-gate.yml").read_text(encoding="utf-8")
    section = workflow.split("      - name: Evaluate the fail-closed release gate", 1)[1]
    payload = section.split("          python - <<'PY'\n", 1)[1].split("\n          PY", 1)[0]
    return "\n".join(line[10:] for line in payload.splitlines()) + "\n"


def _stored_file(folder: Path, name: str, data: bytes) -> dict:
    file = folder / name
    file.write_bytes(data)
    return {"filename": name, "size_bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()}


def _fake_manifest(folder: Path) -> dict:
    folder.mkdir(parents=True)
    package_mapping = {
        "windows-x64": ("windows-10-x64", "windows-11-x64"),
        "windows-7-x64": ("windows-7-x64",),
        "windows-7-x86": ("windows-7-x86",),
        "deb-amd64": ("uos-deb-x64",),
        "deb-arm64": ("uos-deb-arm64", "kylin-deb-arm64"),
        "deb-loong64": ("deepin-deb-loong64",),
        "rpm-x86_64": ("openeuler-rpm-x86_64",),
        "rpm-aarch64": ("openeuler-rpm-aarch64",),
        "macos-intel": ("macos-intel",),
        "macos-apple-silicon": ("macos-apple-silicon",),
    }
    installer_rows = [
        {
            "target": package, "status": "PASS",
            "artifacts": [_stored_file(folder, f"{package}.txt", package.encode())],
        }
        for package in package_mapping
    ]
    native_rows = [
        {
            "target": system, "installer_target": package, "status": "PASS",
            "artifacts": [_stored_file(folder, f"native-{system}.txt", system.encode())],
        }
        for package, systems in package_mapping.items() for system in systems
    ]
    py = _stored_file(folder, "sbom-python.cdx.json", b"{}")
    front = _stored_file(folder, "sbom-frontend.cdx.json", b"{}")
    return {
        "version": "1.4.6-rc.1",
        "source_commit": SOURCE_SHA,
        "source_commit_verified": True,
        "source_archive": _stored_file(folder, "source.tar.gz", b"synthetic-source"),
        "installer_targets": installer_rows,
        "native_validation_targets": native_rows,
        "native_installation_acceptance": "PASS",
        "wps_native_acceptance": "PASS",
        "upgrade_and_rollback_acceptance": "PASS",
        "signature_status": "PASS",
        "security_review_status": "PASS",
        "website_preview_status": "PASS",
        "sbom": {"python": py["sha256"], "frontend": front["sha256"]},
    }


def _evaluate(tmp_path: Path, manifest: dict) -> dict:
    evidence = tmp_path / "artifacts" / "candidate"
    (evidence / "candidate-evidence.json").write_text(
        json.dumps(manifest, ensure_ascii=False), encoding="utf-8"
    )
    env = os.environ.copy()
    env.update({
        "SOURCE_SHA": SOURCE_SHA,
        "GITHUB_OUTPUT": str(tmp_path / "github-output.txt"),
        "PYTHONPATH": str(ROOT),
    })
    result = subprocess.run(
        [sys.executable, "-c", _inline_gate_script()],
        cwd=tmp_path,
        env=env,
        capture_output=True,
        text=True,
        timeout=15,
        check=False,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    return json.loads((tmp_path / "release-gate-result.json").read_text(encoding="utf-8"))


def test_gate_requires_all_ten_installers_and_twelve_native_scenarios(tmp_path: Path):
    manifest = _fake_manifest(tmp_path / "artifacts" / "candidate")
    verdict = _evaluate(tmp_path, manifest)
    assert verdict["verdict"] == "READY_FOR_FINAL_HUMAN_RELEASE_APPROVAL"


def test_gate_blocks_missing_native_system_even_if_all_ten_packages_pass(tmp_path: Path):
    manifest = _fake_manifest(tmp_path / "artifacts" / "candidate")
    manifest["native_validation_targets"].pop()
    verdict = _evaluate(tmp_path, manifest)
    assert verdict["verdict"] == "BLOCKED"
    assert any("native target matrix" in error for error in verdict["blockers"])


def test_gate_blocks_forged_native_evidence_and_wrong_installer(tmp_path: Path):
    manifest = _fake_manifest(tmp_path / "artifacts" / "candidate")
    target = manifest["native_validation_targets"][0]
    target["installer_target"] = "deb-loong64"
    target["artifacts"][0]["sha256"] = "0" * 64
    verdict = _evaluate(tmp_path, manifest)
    assert verdict["verdict"] == "BLOCKED"
    assert any("wrong installer" in error for error in verdict["blockers"])
    assert any("SHA-256 mismatch" in error for error in verdict["blockers"])


def test_gate_blocks_any_missing_signature_or_wps_acceptance(tmp_path: Path):
    manifest = _fake_manifest(tmp_path / "artifacts" / "candidate")
    manifest["signature_status"] = "NOT_RUN"
    manifest["wps_native_acceptance"] = "NOT_RUN"
    verdict = _evaluate(tmp_path, manifest)
    assert verdict["verdict"] == "BLOCKED"
    assert any("signature_status" in error for error in verdict["blockers"])
    assert any("wps_native_acceptance" in error for error in verdict["blockers"])
