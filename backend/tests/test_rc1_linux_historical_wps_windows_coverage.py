"""平台中立的 WPS 历史字节来源门禁测试；不运行也不修改 WPS。

让 Windows 全量发布测试能够检查 Linux BUILD_ONLY 包的拒绝分支。
模拟的只是平台检测和文件 SHA 输入，不宣称 Linux 原生安装或 WPS 验收。
"""

from __future__ import annotations

import json
from pathlib import Path
from types import SimpleNamespace

import pytest

from app import package_selftest as pkg

HEAD = "a" * 40


def _simulate_linux(monkeypatch, architecture: str) -> None:
    # 仅替换 package_selftest 模块引用，不改 Python 全局 os.name/sys.platform。
    monkeypatch.setattr(pkg, "os", SimpleNamespace(name="posix"))
    monkeypatch.setattr(pkg, "sys", SimpleNamespace(platform="linux"))
    monkeypatch.setattr(pkg, "_source_formatter_architecture", lambda: architecture)


def _fixture(tmp_path: Path, monkeypatch, kind: str, arch: str):
    _simulate_linux(monkeypatch, arch)
    root = tmp_path / "PartyOps"
    fmt = root / "formatter-host"
    fmt.mkdir(parents=True)
    host = fmt / "partyops-document-formatter-host"
    source = fmt / "source-host.json"
    evidence = fmt / "runtime-evidence.json"
    for file in (host, source, evidence):
        file.write_bytes(b"only-synthetic-data")
    snapshot = pkg._UOS_RC6_LEGACY_SOURCE_SNAPSHOT
    record = {"source_snapshot_sha256": snapshot}
    pin = None if kind == "uos" else pkg._PINNED_RC6_LINUX_PREVIEWS[(kind, arch)]
    hashes = {
        host.name: pkg._UOS_RC6_LEGACY_HOST if pin is None else pin["host_sha"],
        source.name: pkg._UOS_RC6_LEGACY_SOURCE_RECORD if pin is None else pin["source_sha"],
        evidence.name: pkg._UOS_RC6_LEGACY_EVIDENCE if pin is None else pin["evidence_sha"],
    }
    monkeypatch.setattr(pkg, "_sha256", lambda p: hashes.get(p.name, "0" * 64))
    release = {
        "schema_version": 1,
        "product": "PartyOps",
        "version": "1.4.6-rc.1",
        "platform": "linux-deb" if kind == "uos" else kind,
        "architecture": arch,
        "runtime_profile": "uos-v20",
        "source_commit": HEAD,
        "signed": False,
        "files": [{"path": f"formatter-host/{name}", "sha256": digest}
                  for name, digest in hashes.items()],
    }
    if kind == "uos":
        receipt = {
            "schema_version": 1,
            "status": "BUILD_ONLY_UNVERIFIED",
            "version": "1.4.6-rc.1",
            "architecture": arch,
            "target_os": "UOS Desktop V20 1070",
            "source_sha": HEAD,
            "historical_donor_deb_sha256": pkg._UOS_RC6_DONOR_DEB,
            "historical_wps_host_sha256": hashes[host.name],
            "historical_wps_source_record_sha256": hashes[source.name],
            "historical_wps_evidence_sha256": hashes[evidence.name],
            "historical_wps_snapshot_sha256": snapshot,
            "historical_wps_host_unchanged": True,
            "wps_native_tests_rerun": False,
            "release_ready": False,
        }
        receipt_file = root / "UOS-RC1-BUILD-ONLY.json"
        verifier = pkg._allow_pinned_uos_rc6_formatter_preview
    else:
        receipt = {
            "schema_version": 1,
            "status": "BUILD_ONLY_UNVERIFIED",
            "version": "1.4.6-rc.1",
            "architecture": arch,
            "target_os": pin["target_os"],
            "source_sha": HEAD,
            "original_package_sha256": pin["donor_sha"],
            "historical_wps_host_sha256": hashes[host.name],
            "historical_wps_source_record_sha256": hashes[source.name],
            "historical_wps_evidence_sha256": hashes[evidence.name],
            "historical_wps_snapshot_sha256": snapshot,
            "historical_wps_host_unchanged": True,
            "wps_native_tests_rerun": False,
            "release_ready": False,
        }
        receipt_file = root / "RC1-LINUX-BUILD-ONLY.json"
        verifier = pkg._allow_pinned_rc6_linux_preview

    def save():
        (root / "release-manifest.json").write_text(json.dumps(release), encoding="utf-8")
        receipt_file.write_text(json.dumps(receipt), encoding="utf-8")
    save()
    return SimpleNamespace(
        root=root, host=host, source=source, evidence=evidence, record=record,
        receipt=receipt, release=release, receipt_file=receipt_file, hashes=hashes,
        save=save, verify=lambda: verifier(host, source, record),
    )


CASES = (
    "valid", "foreign_os", "foreign_platform", "wrong_arch", "wrong_snapshot",
    "missing_host", "missing_receipt", "invalid_json", "release_not_object",
    "receipt_not_object", "missing_sha", "bad_sha", "bad_receipt",
    "bad_manifest", "files_not_array", "bad_file_hash", "bad_host_hash",
    "bad_source_hash", "bad_evidence_hash",
)


@pytest.mark.parametrize("case", CASES)
def test_uos_amd64_legacy_byte_identity_rejection_on_all_hosts(
    tmp_path: Path, monkeypatch, case: str
) -> None:
    data = _fixture(tmp_path, monkeypatch, "uos", "amd64")
    if case == "foreign_os":
        monkeypatch.setattr(pkg, "os", SimpleNamespace(name="nt"))
    elif case == "foreign_platform":
        monkeypatch.setattr(pkg, "sys", SimpleNamespace(platform="darwin"))
    elif case == "wrong_arch":
        monkeypatch.setattr(pkg, "_source_formatter_architecture", lambda: "arm64")
    elif case == "wrong_snapshot":
        data.record["source_snapshot_sha256"] = "0" * 64
    elif case == "missing_host":
        data.host.unlink()
    elif case == "missing_receipt":
        data.receipt_file.unlink()
    elif case == "invalid_json":
        data.receipt_file.write_text("{bad-json", encoding="utf-8")
    elif case == "release_not_object":
        (data.root / "release-manifest.json").write_text("[]", encoding="utf-8")
    elif case == "receipt_not_object":
        data.receipt_file.write_text("[]", encoding="utf-8")
    elif case == "missing_sha":
        data.release["source_commit"] = 1
        data.save()
    elif case == "bad_sha":
        data.release["source_commit"] = "0"
        data.save()
    elif case == "bad_receipt":
        data.receipt["status"] = "PASS"
        data.save()
    elif case == "bad_manifest":
        data.release["platform"] = "linux-rpm"
        data.save()
    elif case == "files_not_array":
        data.release["files"] = {}
        data.save()
    elif case == "bad_file_hash":
        data.release["files"][0]["sha256"] = "f" * 64
        data.save()
    elif case == "bad_host_hash":
        data.hashes[data.host.name] = "f" * 64
    elif case == "bad_source_hash":
        data.hashes[data.source.name] = "f" * 64
    elif case == "bad_evidence_hash":
        data.hashes[data.evidence.name] = "f" * 64
    assert data.verify() is (case == "valid")


@pytest.mark.parametrize("kind,arch", (
    ("linux-deb", "arm64"),
    ("linux-rpm", "amd64"),
    ("linux-rpm", "arm64"),
))
@pytest.mark.parametrize("case", CASES)
def test_other_linux_rc6_legacy_byte_identity_rejection_on_all_hosts(
    tmp_path: Path, monkeypatch, kind: str, arch: str, case: str
) -> None:
    data = _fixture(tmp_path, monkeypatch, kind, arch)
    if case == "foreign_os":
        monkeypatch.setattr(pkg, "os", SimpleNamespace(name="nt"))
    elif case == "foreign_platform":
        monkeypatch.setattr(pkg, "sys", SimpleNamespace(platform="darwin"))
    elif case == "wrong_arch":
        monkeypatch.setattr(pkg, "_source_formatter_architecture", lambda: "not-native")
    elif case == "wrong_snapshot":
        data.record["source_snapshot_sha256"] = "0" * 64
    elif case == "missing_host":
        data.host.unlink()
    elif case == "missing_receipt":
        data.receipt_file.unlink()
    elif case == "invalid_json":
        data.receipt_file.write_text("{bad-json", encoding="utf-8")
    elif case == "release_not_object":
        (data.root / "release-manifest.json").write_text("[]", encoding="utf-8")
    elif case == "receipt_not_object":
        data.receipt_file.write_text("[]", encoding="utf-8")
    elif case == "missing_sha":
        data.release["source_commit"] = 1
        data.save()
    elif case == "bad_sha":
        data.release["source_commit"] = "0"
        data.save()
    elif case == "bad_receipt":
        data.receipt["status"] = "PASS"
        data.save()
    elif case == "bad_manifest":
        data.release["signed"] = True
        data.save()
    elif case == "files_not_array":
        data.release["files"] = {}
        data.save()
    elif case == "bad_file_hash":
        data.release["files"][0]["sha256"] = "f" * 64
        data.save()
    elif case == "bad_host_hash":
        data.hashes[data.host.name] = "f" * 64
    elif case == "bad_source_hash":
        data.hashes[data.source.name] = "f" * 64
    elif case == "bad_evidence_hash":
        data.hashes[data.evidence.name] = "f" * 64
    assert data.verify() is (case == "valid")
