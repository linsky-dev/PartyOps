"""真实自检函数的 Mac15/11 边界回归；合成资源不代表原生 WPS 验收。"""
import hashlib

import pytest

from app import package_selftest as package


def record_for(tmp_path, architecture, minimum):
    host = tmp_path / "partyops-document-formatter-host"
    host.write_bytes(b"synthetic host for offline validator only")
    plugin = tmp_path / "wps-formatter-plugin"
    plugin.mkdir()
    resources = {}
    for name in package.MAC_PLUGIN_FILES:
        content = ("mock fixed resource " + name).encode("utf-8")
        (plugin / name).write_bytes(content)
        resources[name] = hashlib.sha256(content).hexdigest()
    (tmp_path / "LICENSE-MONO-RUNTIME.txt").write_text("mock nonempty runtime license", encoding="utf-8")
    record = {"schema": 3, "platform": "macos", "architecture": architecture,
        "adapter": package.MAC_OBJECT_ADAPTER, "source_project": "PartyOps.DocumentFormatter.AddIn",
        "source_snapshot_sha256": package.MAC_OBJECT_SOURCE_SHA256, "source_snapshot_files": 898,
        "rules_sha256": package.MAC_OBJECT_RULES_SHA256, "features": package.MAC_FEATURES,
        "host_sha256": package._sha256(host), "timezone": "Asia/Shanghai", "self_contained": True,
        "minimum_macos": minimum, "acceptance_profile": "mac-object-limited-candidate", "limitations": package.MAC_LIMITATIONS,
        "feature_validation": {name: "pending-target-package-validation" for name in package.MAC_FEATURES},
        "plugin_resources_sha256": resources, "native_bundle_runtime": "Mono JIT compiler version 6.14.1",
        "managed_host_sha256": "a" * 64, "resource_catalog_source_sha256": "b" * 64,
        "built_at": "2026-10-09T12:00:00+08:00"}
    return host, record


@pytest.mark.parametrize("architecture,minimum", [("arm64", "15.0"), ("x86_64", "11.0")])
def test_mac_record_accepts_exact_architecture_baseline(tmp_path, monkeypatch, architecture, minimum):
    monkeypatch.setattr(package, "_source_formatter_architecture", lambda: architecture)
    host, record = record_for(tmp_path, architecture, minimum)
    assert package._validate_mac_object_record(host, record) is record


@pytest.mark.parametrize("architecture,minimum", [("arm64", "11.0"), ("arm64", "14.0"), ("arm64", "16.0"),
    ("x86_64", "15.0"), ("x86_64", "11"), ("arm64", None), ("x86_64", None)])
def test_mac_record_rejects_missing_or_wrong_baseline(tmp_path, monkeypatch, architecture, minimum):
    monkeypatch.setattr(package, "_source_formatter_architecture", lambda: architecture)
    host, record = record_for(tmp_path, architecture, minimum)
    if minimum is None:
        record.pop("minimum_macos")
    with pytest.raises(RuntimeError, match="来源清单无效"):
        package._validate_mac_object_record(host, record)


@pytest.mark.parametrize("architecture", ["amd64", "x64", "unknown", ""])
def test_mac_record_rejects_unsupported_architecture(tmp_path, monkeypatch, architecture):
    monkeypatch.setattr(package, "_source_formatter_architecture", lambda: architecture)
    host, record = record_for(tmp_path, architecture, "11.0")
    with pytest.raises(RuntimeError, match="目标架构无效"):
        package._validate_mac_object_record(host, record)


def test_mac_record_baseline_does_not_bypass_resource_binding(tmp_path, monkeypatch):
    monkeypatch.setattr(package, "_source_formatter_architecture", lambda: "arm64")
    host, record = record_for(tmp_path, "arm64", "15.0")
    (tmp_path / "wps-formatter-plugin/main.js").write_bytes(b"tampered resource")
    with pytest.raises(RuntimeError, match="资源摘要不匹配"):
        package._validate_mac_object_record(host, record)
