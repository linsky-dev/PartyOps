"""Exercise the actual release-gate inline Python against isolated synthetic evidence."""

from __future__ import annotations

import hashlib
import io
import json
import os
import subprocess
import sys
import tarfile
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


def _synthetic_installer(target: str) -> tuple[str, bytes]:
    """Header-shaped fake package for parser tests ONLY, not real installers."""
    extension = (
        ".exe" if target.startswith("windows") else
        ".deb" if target.startswith("deb") else
        ".rpm" if target.startswith("rpm") else ".pkg"
    )
    data = bytearray(4096)
    if extension == ".exe":
        data[:2] = b"MZ"
        data[0x3c:0x40] = (128).to_bytes(4, "little")
        data[128:132] = b"PE\x00\x00"
        machine = 0x14c if target == "windows-7-x86" else 0x8664
        data[132:134] = machine.to_bytes(2, "little")
    elif extension == ".deb":
        data[:8] = b"!<arch>\n"
    elif extension == ".rpm":
        data[:4] = b"\xed\xab\xee\xdb"
    else:
        data[:4] = b"xar!"
        data[4:6] = (28).to_bytes(2, "big")
    encoded = target.encode("ascii")
    data[-len(encoded):] = encoded
    return f"PartyOps_1.4.6-rc.1_{target}{extension}", bytes(data)


def _expected_native_arch(target: str) -> str:
    return {
        "windows-10-x64": "amd64", "windows-11-x64": "amd64",
        "windows-7-x64": "amd64", "windows-7-x86": "x86",
        "uos-deb-x64": "amd64", "uos-deb-arm64": "aarch64",
        "kylin-deb-arm64": "aarch64", "deepin-deb-loong64": "loong64",
        "openeuler-rpm-x86_64": "x86_64",
        "openeuler-rpm-aarch64": "aarch64",
        "macos-intel": "x86_64", "macos-apple-silicon": "arm64",
    }[target]


def _fake_source_tar(*, malicious_member: str | None = None, omit: str | None = None) -> bytes:
    """Isolated tar parser fixture, NOT an authentic source release."""
    output = io.BytesIO()
    names = [
        "backend/app/__init__.py", "backend/pyproject.toml",
        "frontend/package.json", "scripts/rc1_release_targets.py",
    ]
    if malicious_member:
        names.append(malicious_member)
    payload = b"".join(hashlib.sha256(i.to_bytes(4, "big")).digest() for i in range(128))
    with tarfile.open(fileobj=output, mode="w:gz") as stream:
        for name in names:
            if name == omit:
                continue
            item = tarfile.TarInfo(
                name if name.startswith("../") else f"PartyOps-1.4.6-rc.1/{name}"
            )
            item.size = len(payload)
            stream.addfile(item, io.BytesIO(payload))
    return output.getvalue()


def _fake_sbom(component: str) -> bytes:
    return json.dumps({
        "bomFormat": "CycloneDX", "specVersion": "1.6", "version": 1,
        "components": [{"type": "library", "name": component, "version": "0"}],
    }).encode("utf-8")


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
            "artifacts": [_stored_file(folder, *_synthetic_installer(package))],
        }
        for package in package_mapping
    ]
    hashes = {row["target"]: row["artifacts"][0]["sha256"] for row in installer_rows}
    native_rows = [
        {
            "target": system, "installer_target": package, "status": "PASS",
            "native_architecture": _expected_native_arch(system),
            "installed_package_sha256": hashes[package],
            "artifacts": [_stored_file(folder, f"native-{system}.json", system.encode())],
        }
        for package, systems in package_mapping.items() for system in systems
    ]
    py = _stored_file(folder, "sbom-python.cdx.json", _fake_sbom("python"))
    front = _stored_file(folder, "sbom-frontend.cdx.json", _fake_sbom("frontend"))
    return {
        "version": "1.4.6-rc.1",
        "source_commit": SOURCE_SHA,
        "source_commit_verified": True,
        "source_archive": _stored_file(
            folder, "PartyOps-1.4.6-rc.1-source.tar.gz", _fake_source_tar()
        ),
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


def _evaluate(tmp_path: Path, manifest: dict, *, pymupdf_version: str = "2.1.0") -> dict:
    evidence = tmp_path / "artifacts" / "candidate"
    (evidence / "candidate-evidence.json").write_text(
        json.dumps(manifest, ensure_ascii=False), encoding="utf-8"
    )
    synthetic_lock = tmp_path / "source-verify" / "backend" / "uv.lock"
    synthetic_lock.parent.mkdir(parents=True, exist_ok=True)
    synthetic_lock.write_text(
        f'[[package]]\nname = "pymupdf"\nversion = "{pymupdf_version}"\n',
        encoding="utf-8",
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


def test_gate_rejects_plain_text_placeholder_even_with_matching_hash(tmp_path: Path):
    folder = tmp_path / "artifacts" / "candidate"
    manifest = _fake_manifest(folder)
    row = manifest["installer_targets"][0]
    row["artifacts"] = [_stored_file(folder, "placeholder.txt", b"not an installer")]
    manifest["native_validation_targets"][0]["installed_package_sha256"] = "0" * 64
    verdict = _evaluate(tmp_path, manifest)
    assert verdict["verdict"] == "BLOCKED"
    assert any("exactly one real installer" in b for b in verdict["blockers"])
    assert any("native tested package SHA" in b for b in verdict["blockers"])


def test_gate_rejects_wrong_pe_architecture_and_truncated_package(tmp_path: Path):
    folder = tmp_path / "artifacts" / "candidate"
    manifest = _fake_manifest(folder)
    wrong = manifest["installer_targets"][1]
    filename = wrong["artifacts"][0]["filename"]
    content = bytearray((folder / filename).read_bytes())
    content[132:134] = (0x14c).to_bytes(2, "little")  # x86, not x64
    wrong["artifacts"] = [_stored_file(folder, filename, bytes(content))]
    short = manifest["installer_targets"][2]
    shortfile = short["artifacts"][0]["filename"]
    short["artifacts"] = [_stored_file(folder, shortfile, b"MZ" + b"\0" * 10)]
    verdict = _evaluate(tmp_path, manifest)
    assert verdict["verdict"] == "BLOCKED"
    assert sum("installer format or architecture invalid" in b for b in verdict["blockers"]) >= 2


def test_gate_rejects_native_wrong_architecture_with_valid_package_hash(tmp_path: Path):
    manifest = _fake_manifest(tmp_path / "artifacts" / "candidate")
    row = next(x for x in manifest["native_validation_targets"] if x["target"] == "kylin-deb-arm64")
    row["native_architecture"] = "amd64"
    verdict = _evaluate(tmp_path, manifest)
    assert verdict["verdict"] == "BLOCKED"
    assert any("native architecture does not match target" in b for b in verdict["blockers"])


def test_gate_rejects_reused_executable_across_distinct_installer_targets(tmp_path: Path):
    folder = tmp_path / "artifacts" / "candidate"
    manifest = _fake_manifest(folder)
    source, target = manifest["installer_targets"][:2]
    same_bytes = (folder / source["artifacts"][0]["filename"]).read_bytes()
    target["artifacts"] = [_stored_file(folder, target["artifacts"][0]["filename"], same_bytes)]
    verdict = _evaluate(tmp_path, manifest)
    assert verdict["verdict"] == "BLOCKED"
    assert any("share identical file hash" in b for b in verdict["blockers"])


def test_gate_rejects_hash_matched_text_disguised_as_source_tar(tmp_path: Path):
    folder = tmp_path / "artifacts" / "candidate"
    manifest = _fake_manifest(folder)
    manifest["source_archive"] = _stored_file(
        folder, "PartyOps-1.4.6-rc.1-source.tar.gz", b"not a tar archive" * 100
    )
    result = _evaluate(tmp_path, manifest)
    assert result["verdict"] == "BLOCKED"
    assert any("source archive structure invalid" in x for x in result["blockers"])


def test_gate_rejects_tar_archive_with_parent_path_member(tmp_path: Path):
    folder = tmp_path / "artifacts" / "candidate"
    manifest = _fake_manifest(folder)
    manifest["source_archive"] = _stored_file(
        folder, "PartyOps-1.4.6-rc.1-source.tar.gz",
        _fake_source_tar(malicious_member="../out-of-scope"),
    )
    result = _evaluate(tmp_path, manifest)
    assert result["verdict"] == "BLOCKED"
    assert any("source archive structure invalid" in x for x in result["blockers"])


def test_gate_rejects_source_archive_missing_essential_code(tmp_path: Path):
    folder = tmp_path / "artifacts" / "candidate"
    manifest = _fake_manifest(folder)
    manifest["source_archive"] = _stored_file(
        folder, "PartyOps-1.4.6-rc.1-source.tar.gz",
        _fake_source_tar(omit="backend/pyproject.toml"),
    )
    result = _evaluate(tmp_path, manifest)
    assert result["verdict"] == "BLOCKED"
    assert any("source archive structure invalid" in x for x in result["blockers"])


def test_gate_rejects_hash_matched_empty_sbom_json(tmp_path: Path):
    folder = tmp_path / "artifacts" / "candidate"
    manifest = _fake_manifest(folder)
    manifest["sbom"]["python"] = _stored_file(
        folder, "sbom-python.cdx.json", b"{}"
    )["sha256"]
    result = _evaluate(tmp_path, manifest)
    assert result["verdict"] == "BLOCKED"
    assert any("SBOM CycloneDX contents invalid" in x for x in result["blockers"])


def test_release_gate_blocks_known_vulnerable_pymupdf_locks(tmp_path: Path):
    manifest = _fake_manifest(tmp_path / "artifacts" / "candidate")
    for version in ("1.26.0", "1.28.2"):
        verdict = _evaluate(tmp_path, manifest, pymupdf_version=version)
        assert verdict["verdict"] == "BLOCKED"
        assert any("PyMuPDF security floor blocked" in x for x in verdict["blockers"])
        assert any("CVE-2026-120752" in x for x in verdict["blockers"])


def test_release_gate_requires_pymupdf_lock_with_parseable_version(tmp_path: Path):
    manifest = _fake_manifest(tmp_path / "artifacts" / "candidate")
    verdict = _evaluate(tmp_path, manifest, pymupdf_version="unverified")
    assert verdict["verdict"] == "BLOCKED"
    assert any("PyMuPDF security floor blocked" in x for x in verdict["blockers"])

def test_release_gate_accepts_official_two_part_pymupdf_versions(tmp_path: Path):
    """PyMuPDF publishes 2.0 / 2.1, not only 2.0.0 / 2.1.0."""
    for version in ("2.0", "2.1", "2.1.0"):
        manifest = _fake_manifest(tmp_path / "artifacts" / "candidate")
        verdict = _evaluate(tmp_path, manifest, pymupdf_version=version)
        assert verdict["verdict"] == "READY_FOR_FINAL_HUMAN_RELEASE_APPROVAL", (version, verdict)


def test_release_gate_rejects_unreviewed_pymupdf_version_variants(tmp_path: Path):
    """Never silently accept vulnerable 1.x or prerelease/local builds."""
    for version in ("1.28", "2.0rc1", "2.1.post1", "2.1+local", "2.1.0.1"):
        manifest = _fake_manifest(tmp_path / "artifacts" / "candidate")
        verdict = _evaluate(tmp_path, manifest, pymupdf_version=version)
        assert verdict["verdict"] == "BLOCKED", (version, verdict)
        assert any("PyMuPDF security floor blocked" in blocker for blocker in verdict["blockers"])
