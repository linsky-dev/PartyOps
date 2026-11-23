"""Packaging-only pin tests: original WPS code and native WPS tests are untouched."""

from __future__ import annotations

import json
import os
from pathlib import Path

import pytest

from app import package_selftest as pkg


@pytest.fixture
def preview(tmp_path: Path, monkeypatch: pytest.MonkeyPatch):
    if os.name == "nt":
        pytest.skip("UOS legacy formatter preview is Linux native only")
    monkeypatch.setattr(pkg, "_source_formatter_architecture", lambda: "amd64")
    runtime = tmp_path / "PartyOps"
    folder = runtime / "formatter-host"
    folder.mkdir(parents=True)
    host = folder / "partyops-document-formatter-host"
    source = folder / "source-host.json"
    evidence = folder / "runtime-evidence.json"
    for p in (host, source, evidence):
        p.write_bytes(b"synthetic-test-only")

    old_files = {
        host.name: pkg._UOS_RC6_LEGACY_HOST,
        source.name: pkg._UOS_RC6_LEGACY_SOURCE_RECORD,
        evidence.name: pkg._UOS_RC6_LEGACY_EVIDENCE,
    }
    monkeypatch.setattr(pkg, "_sha256", lambda p: old_files.get(p.name, "0" * 64))
    head = "a" * 40
    metadata = {
        "schema_version": 1,
        "status": "BUILD_ONLY_UNVERIFIED",
        "version": "1.4.6-rc.1",
        "architecture": "amd64",
        "target_os": "UOS Desktop V20 1070",
        "source_sha": head,
        "historical_donor_deb_sha256": pkg._UOS_RC6_DONOR_DEB,
        "historical_wps_host_sha256": pkg._UOS_RC6_LEGACY_HOST,
        "historical_wps_source_record_sha256": pkg._UOS_RC6_LEGACY_SOURCE_RECORD,
        "historical_wps_evidence_sha256": pkg._UOS_RC6_LEGACY_EVIDENCE,
        "historical_wps_snapshot_sha256": pkg._UOS_RC6_LEGACY_SOURCE_SNAPSHOT,
        "historical_wps_host_unchanged": True,
        "wps_native_tests_rerun": False,
        "release_ready": False,
    }
    manifest = {
        "schema_version": 1,
        "product": "PartyOps",
        "version": "1.4.6-rc.1",
        "platform": "linux-deb",
        "architecture": "amd64",
        "runtime_profile": "uos-v20",
        "source_commit": head,
        "signed": False,
        "files": [
            {"path": f"formatter-host/{name}", "sha256": digest}
            for name, digest in old_files.items()
        ],
    }
    marker = runtime / "UOS-RC1-BUILD-ONLY.json"
    manifest_file = runtime / "release-manifest.json"
    record = {"source_snapshot_sha256": pkg._UOS_RC6_LEGACY_SOURCE_SNAPSHOT}
    def write() -> None:
        marker.write_text(json.dumps(metadata), encoding="utf-8")
        manifest_file.write_text(json.dumps(manifest), encoding="utf-8")
    write()
    return host, source, record, metadata, manifest, write


def test_exact_native_legacy_preview_hashes_are_allowed_for_amd64_only(preview):
    host, source, record, _, _, _ = preview
    assert pkg._allow_pinned_uos_rc6_formatter_preview(host, source, record)


@pytest.mark.parametrize(
    "container, key, value",
    [
        ("metadata", "historical_wps_host_sha256", "0" * 64),
        ("metadata", "historical_donor_deb_sha256", "0" * 64),
        ("metadata", "historical_wps_evidence_sha256", "0" * 64),
        ("metadata", "wps_native_tests_rerun", True),
        ("metadata", "release_ready", True),
        ("metadata", "status", "RELEASE_READY"),
        ("metadata", "source_sha", "b" * 40),
        ("manifest", "runtime_profile", "full"),
        ("manifest", "signed", True),
        ("manifest", "architecture", "arm64"),
        ("manifest", "source_commit", "b" * 40),
    ],
)
def test_preview_refuses_nonidentical_input(preview, container, key, value):
    host, source, record, metadata, manifest, write = preview
    (metadata if container == "metadata" else manifest)[key] = value
    write()
    assert not pkg._allow_pinned_uos_rc6_formatter_preview(host, source, record)


def test_missing_or_moved_legacy_evidence_is_not_accepted(preview):
    host, source, record, _, _, _ = preview
    (host.parent / "runtime-evidence.json").unlink()
    assert not pkg._allow_pinned_uos_rc6_formatter_preview(host, source, record)


def test_legacy_mode_never_accepts_the_wrong_wps_source_snapshot(preview):
    host, source, record, _, _, _ = preview
    record["source_snapshot_sha256"] = "0" * 64
    assert not pkg._allow_pinned_uos_rc6_formatter_preview(host, source, record)


def test_legacy_preview_refuses_symlinked_source_record(preview):
    host, source, record, _, _, _ = preview
    source.unlink()
    source.symlink_to(host)
    assert not pkg._allow_pinned_uos_rc6_formatter_preview(host, source, record)
