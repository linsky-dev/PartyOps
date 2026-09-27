"""Windows 包线与仿真身份回归；测试使用合成 PE/API，不代表真实 ARM 验收。"""
from __future__ import annotations

import hashlib
import io
import json
from pathlib import Path
from types import SimpleNamespace

import pytest

from app import platform_info, update_executor
from app import windows_runtime_identity as identity
from app.routers import updates


def make_bundle(tmp_path: Path, *, machine=0x014C, package="windows7", profile="legacy-core", architecture="x86"):
    executable = tmp_path / "PartyOps.exe"
    header = bytearray(64)
    header[:2] = b"MZ"
    header[60:64] = (64).to_bytes(4, "little")
    executable.write_bytes(header + b"PE\0\0" + machine.to_bytes(2, "little"))
    manifest = {
        "schema_version": 1, "product": "PartyOps", "platform": package,
        "architecture": architecture, "runtime_profile": profile,
        "files": [{"path": executable.name, "size": executable.stat().st_size,
                   "sha256": hashlib.sha256(executable.read_bytes()).hexdigest()}],
    }
    (tmp_path / "release-manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
    return executable, manifest


def write_manifest(executable, manifest):
    (executable.parent / "release-manifest.json").write_text(json.dumps(manifest), encoding="utf-8")


@pytest.mark.parametrize("process,native,version,machine,package,profile,kind,has_llm", [
    ("x86", "x86", "10.0.19045", 0x014C, "windows7", "legacy-core", "native", False),
    ("x86", "amd64", "10.0.19045", 0x014C, "windows7", "legacy-core", "wow64", False),
    ("x86", "arm64", "10.0.19045", 0x014C, "windows7", "legacy-core", "windows-x86-emulation", False),
    ("x86", "arm64", "10.0.26100", 0x014C, "windows7", "legacy-core", "windows-x86-emulation", False),
    ("amd64", "arm64", "10.0.26100", 0x8664, "windows", "full", "windows-x64-emulation", True),
    ("amd64", "amd64", "10.0.26100", 0x8664, "windows", "full", "native", True),
    ("amd64", "amd64", "6.1.7601", 0x8664, "windows7", "legacy-smart", "native", False),
])
def test_installed_profile_survives_new_os_and_emulation(
    monkeypatch, tmp_path, process, native, version, machine, package, profile, kind, has_llm,
):
    executable, _ = make_bundle(tmp_path, machine=machine, package=package, profile=profile, architecture=process)
    monkeypatch.setattr(identity, "query_machine_types", lambda _path: (process, native))
    result = identity.installed_identity(executable, version)
    assert result["package_identity_status"] == "verified"
    assert result["runtime_profile"] == profile
    assert result["architecture"] == result["process_architecture"] == result["binary_architecture"] == process
    assert result["os_architecture"] == native
    assert result["execution_kind"] == kind
    assert ("local_llm" in result["capabilities"]) is has_llm
    assert ("semantic_rerank" in result["capabilities"]) is (profile != "legacy-core")
    assert platform_info.update_platform_key({"platform_family": "windows", **result}) == package


@pytest.mark.parametrize("machine,process,native,version,package,profile,architecture,reason", [
    (0xAA64, "arm64", "arm64", "10.0.26100", "windows", "full", "arm64", "WINDOWS_PACKAGE_CHANNEL_UNSUPPORTED"),
    (0x8664, "amd64", "arm64", "10.0.19045", "windows", "full", "amd64", "WINDOWS_PROCESS_OS_COMBINATION_UNSUPPORTED"),
    (0x014C, "amd64", "amd64", "10.0.26100", "windows", "full", "amd64", "WINDOWS_PE_PROCESS_ISA_MISMATCH"),
    (0x014C, "x86", "x86", "10.0.19045", "windows7", "legacy-core", "amd64", "WINDOWS_PACKAGE_PROCESS_ISA_MISMATCH"),
    (0x014C, "x86", "x86", "10.0.19045", "windows7", "full", "x86", "WINDOWS_PACKAGE_CHANNEL_UNSUPPORTED"),
    (0x8664, "amd64", "amd64", "6.1.7601", "windows", "full", "amd64", "WINDOWS_PACKAGE_OS_VERSION_UNSUPPORTED"),
    (0x014C, "x86", "x86", "6.1.7600", "windows7", "legacy-core", "x86", "WINDOWS_PACKAGE_OS_VERSION_UNSUPPORTED"),
    (0x014C, "x86", "x86", "", "windows7", "legacy-core", "x86", "WINDOWS_OS_VERSION_UNPROVEN"),
])
def test_unknown_or_incompatible_identity_has_no_capabilities_or_update_channel(
    monkeypatch, tmp_path, machine, process, native, version, package, profile, architecture, reason,
):
    executable, _ = make_bundle(tmp_path, machine=machine, package=package, profile=profile, architecture=architecture)
    monkeypatch.setattr(identity, "query_machine_types", lambda _path: (process, native))
    result = identity.installed_identity(executable, version)
    assert result["package_identity_status"] == "unsupported"
    assert result["package_identity_reason"] == reason
    assert result["runtime_profile"] == "unsupported"
    assert result["capabilities"] == []
    assert platform_info.update_platform_key({"platform_family": "windows", **result}) == ""


@pytest.mark.parametrize("mutation,reason", [
    ("missing", "WINDOWS_PACKAGE_IDENTITY_UNPROVEN"),
    ("hash", "WINDOWS_CURRENT_EXECUTABLE_MANIFEST_MISMATCH"),
    ("size", "WINDOWS_CURRENT_EXECUTABLE_MANIFEST_MISMATCH"),
    ("path", "WINDOWS_CURRENT_EXECUTABLE_NOT_IN_MANIFEST"),
    ("duplicate_exe", "WINDOWS_CURRENT_EXECUTABLE_NOT_IN_MANIFEST"),
    ("schema_bool", "WINDOWS_RELEASE_MANIFEST_INVALID"),
    ("json_duplicate", "WINDOWS_RELEASE_MANIFEST_DUPLICATE_KEY"),
])
def test_manifest_must_bind_the_current_executable(monkeypatch, tmp_path, mutation, reason):
    executable, manifest = make_bundle(tmp_path)
    path = tmp_path / "release-manifest.json"
    if mutation == "missing":
        path.unlink()
    elif mutation == "json_duplicate":
        path.write_text('{"schema_version": 1, "schema_version": 1}', encoding="utf-8")
    else:
        if mutation == "hash":
            manifest["files"][0]["sha256"] = "f" * 64
        elif mutation == "size":
            manifest["files"][0]["size"] += 1
        elif mutation == "path":
            manifest["files"][0]["path"] = "_internal/PartyOps.exe"
        elif mutation == "duplicate_exe":
            manifest["files"].append(dict(manifest["files"][0]))
        elif mutation == "schema_bool":
            manifest["schema_version"] = True
        write_manifest(executable, manifest)
    monkeypatch.setattr(identity, "query_machine_types", lambda _path: ("x86", "x86"))
    result = identity.installed_identity(executable, "10.0.19045")
    assert result["package_identity_reason"] == reason
    assert result["capabilities"] == []


@pytest.mark.parametrize("machine,expected", [(0x014C, "x86"), (0x8664, "amd64"), (0xAA64, "arm64"), (0xA641, "arm64ec")])
def test_actual_pe_machine_is_read(tmp_path, machine, expected):
    executable, _ = make_bundle(tmp_path, machine=machine)
    assert identity.executable_architecture(executable) == expected


@pytest.mark.parametrize("content", [b"", b"MZ", b"!" * 70, b"MZ" + b"\0" * 68])
def test_invalid_pe_is_rejected(tmp_path, content):
    executable = tmp_path / "PartyOps.exe"
    executable.write_bytes(content)
    with pytest.raises(ValueError, match="WINDOWS_EXECUTABLE_PE_INVALID"):
        identity.executable_architecture(executable)


def test_unknown_machine_and_truncated_pe_are_rejected(tmp_path):
    executable, _ = make_bundle(tmp_path, machine=0xFFFF)
    with pytest.raises(ValueError, match="WINDOWS_EXECUTABLE_ISA_UNKNOWN"):
        identity.executable_architecture(executable)
    executable.write_bytes(executable.read_bytes()[:68])
    with pytest.raises(ValueError, match="WINDOWS_EXECUTABLE_PE_INVALID"):
        identity.executable_architecture(executable)


def test_manifest_link_and_oversized_manifest_are_rejected(monkeypatch, tmp_path):
    executable, _ = make_bundle(tmp_path)
    with monkeypatch.context() as patch:
        patch.setattr(Path, "is_symlink", lambda path: path.name == "release-manifest.json")
        with pytest.raises(ValueError, match="WINDOWS_RELEASE_MANIFEST_LINK_REJECTED"):
            identity.read_installed_manifest(executable)
    with monkeypatch.context() as patch:
        patch.setattr(Path, "open", lambda *_args, **_kwargs: io.BytesIO(b" " * (8 * 1024 * 1024 + 1)))
        with pytest.raises(ValueError, match="WINDOWS_RELEASE_MANIFEST_TOO_LARGE"):
            identity.read_installed_manifest(executable)


@pytest.mark.parametrize("process,native,expected", [
    (0, 0x8664, ("amd64", "amd64")),
    (0x014C, 0xAA64, ("x86", "arm64")),
    (0x8664, 0xAA64, ("amd64", "arm64")),
])
def test_windows_api_reports_process_and_native_separately(monkeypatch, tmp_path, process, native, expected):
    def query(_handle, process_ptr, native_ptr):
        process_ptr._obj.value = process
        native_ptr._obj.value = native
        return 1
    kernel = SimpleNamespace(GetCurrentProcess=lambda: 17, IsWow64Process2=query)
    monkeypatch.setattr(identity.ctypes, "WinDLL", lambda *_args, **_kwargs: kernel, raising=False)
    assert identity.query_machine_types(tmp_path / "unused.exe") == expected


@pytest.mark.parametrize("native_code,expected", [(0, "x86"), (9, "amd64")])
def test_win7_api_fallback_retains_pe_process_architecture(monkeypatch, tmp_path, native_code, expected):
    executable, _ = make_bundle(tmp_path)
    def query(pointer):
        pointer._obj.architecture = native_code
    kernel = SimpleNamespace(GetCurrentProcess=lambda: 17, GetNativeSystemInfo=query)
    monkeypatch.setattr(identity.ctypes, "WinDLL", lambda *_args, **_kwargs: kernel, raising=False)
    assert identity.query_machine_types(executable) == ("x86", expected)


def test_api_failure_does_not_fallback_to_ambiguous_platform_machine(monkeypatch, tmp_path):
    kernel = SimpleNamespace(GetCurrentProcess=lambda: 17, IsWow64Process2=lambda *_args: 0)
    monkeypatch.setattr(identity.ctypes, "WinDLL", lambda *_args, **_kwargs: kernel, raising=False)
    with pytest.raises(ValueError, match="WINDOWS_MACHINE_TYPES_QUERY_FAILED"):
        identity.query_machine_types(tmp_path / "unused.exe")


def test_unknown_api_machine_and_arm_fallback_are_not_guessed(monkeypatch, tmp_path):
    def query(_handle, process_ptr, native_ptr):
        process_ptr._obj.value = 0
        native_ptr._obj.value = 0xFFFF
        return 1
    kernel = SimpleNamespace(GetCurrentProcess=lambda: 17, IsWow64Process2=query)
    monkeypatch.setattr(identity.ctypes, "WinDLL", lambda *_args, **_kwargs: kernel, raising=False)
    with pytest.raises(ValueError, match="WINDOWS_NATIVE_ISA_UNPROVEN"):
        identity.query_machine_types(tmp_path / "unused.exe")
    def query_native(pointer):
        pointer._obj.architecture = 12
    kernel = SimpleNamespace(GetCurrentProcess=lambda: 17, GetNativeSystemInfo=query_native)
    with pytest.raises(ValueError, match="WINDOWS_NATIVE_ISA_UNPROVEN"):
        identity.query_machine_types(tmp_path / "unused.exe")


@pytest.mark.parametrize("version", ["unknown", "6.1.7601"])
def test_arm_emulation_requires_a_known_supported_windows_version(version):
    with pytest.raises(ValueError, match="WINDOWS_PROCESS_OS_COMBINATION_UNSUPPORTED"):
        identity.execution_kind("x86", "arm64", version)


def test_frozen_platform_keeps_real_os_and_installed_package_family(monkeypatch, tmp_path):
    executable, _ = make_bundle(tmp_path)
    monkeypatch.setattr(identity, "query_machine_types", lambda _path: ("x86", "arm64"))
    monkeypatch.setattr(platform_info, "sys", SimpleNamespace(platform="win32", frozen=True, executable=str(executable)))
    monkeypatch.setattr(platform_info.platform, "machine", lambda: "AMD64")
    monkeypatch.setattr(platform_info.platform, "win32_ver", lambda: ("10", "10.0.19045", "", ""))
    result = platform_info.detect_platform_info()
    assert result["distribution"] == "windows"
    assert result["distribution_version"] == "10"
    assert result["runtime_profile"] == "legacy-core"
    assert result["architecture"] == "x86"
    assert result["os_architecture"] == "arm64"
    assert result["execution_kind"] == "windows-x86-emulation"
    assert platform_info.update_platform_key(result) == "windows7"


def known_info(package="windows7", architecture="x86", profile="legacy-core"):
    return {"platform_family": "windows", "distribution": "windows", "package_format": "exe",
            "architecture": architecture, "process_architecture": architecture,
            "os_architecture": "arm64", "runtime_profile": profile,
            "package_platform": package, "package_identity_status": "verified"}


def test_executor_uses_process_isa_and_rejects_ambiguous_old_legacy_catalog(monkeypatch):
    info = known_info()
    monkeypatch.setattr(update_executor, "sys", SimpleNamespace(frozen=True))
    monkeypatch.setattr(update_executor, "os", SimpleNamespace(name="nt"))
    monkeypatch.setattr(update_executor, "detect_platform_info", lambda: info)
    monkeypatch.setattr(update_executor.platform, "machine", lambda: "AMD64")
    assert update_executor._architecture() == "x86"
    assert update_executor._manifest_platform_name({"format_version": 3}) == "windows7"
    with pytest.raises(RuntimeError, match="WINDOWS_UPDATE_CHANNEL_UNSUPPORTED"):
        update_executor._manifest_platform_name({"format_version": 2})
    info.update(package_platform="windows", architecture="amd64", process_architecture="amd64", runtime_profile="full")
    assert update_executor._architecture() == "amd64"
    assert update_executor._manifest_platform_name({"format_version": 2}) == "windows"
    info.update(architecture="arm64", process_architecture="arm64")
    with pytest.raises(RuntimeError, match="WINDOWS_UPDATE_CHANNEL_UNSUPPORTED"):
        update_executor._architecture()


@pytest.mark.parametrize("override", [
    {"architecture": "amd64"},
    {"runtime_profile": "full"},
    {"process_architecture": "arm64"},
    {"package_platform": "windows"},
    {"package_identity_status": "unsupported"},
])
def test_update_key_rejects_conflicting_package_identity(override):
    assert platform_info.update_platform_key({**known_info(), **override}) == ""


class Response(io.BytesIO):
    status = 200
    headers = {"Content-Encoding": ""}


def catalog_for(packages, format_version=3):
    return {"format": "partyops-update-channel", "format_version": format_version,
            "release": {"version": "9.0.0", "title": "测试新版本", "release_notes": ["合成更新包"],
                        "published_at": "2026-09-08T10:00:00+08:00", "platform_packages": packages},
            "signature": "synthetic"}


def record(name):
    return {"package_url": f"https://www.partyops.cn/releases/{name}.partyops-update",
            "package_size": 50, "package_sha256": "a" * 64}


def setup_catalog(monkeypatch, info, document):
    monkeypatch.setattr(updates, "get_settings", lambda: SimpleNamespace(
        update_catalog_url="https://www.partyops.cn/releases/update-v3.json", update_download_hosts="www.partyops.cn"))
    monkeypatch.setattr(updates, "_manifest_signature_valid", lambda _document: True)
    monkeypatch.setattr(updates, "_open_trusted_update_url", lambda _url: Response(json.dumps(document).encode()))
    monkeypatch.setattr(updates, "detect_platform_info", lambda: info)
    def forbidden_normalize():
        raise AssertionError("已知进程架构时不得另按宿主选择更新包")
    monkeypatch.setattr(updates, "normalize_architecture", forbidden_normalize)


def test_online_catalog_keeps_x86_legacy_channel_on_windows_arm(monkeypatch):
    document = catalog_for({"windows7": {"x86": record("legacy-x86")}, "windows": {"amd64": record("modern-amd64")}})
    setup_catalog(monkeypatch, known_info(), document)
    result = updates.fetch_online_update_catalog()
    assert result["target_available"] is True
    assert result["available"] is True
    assert result["package_url"].endswith("/legacy-x86.partyops-update")


@pytest.mark.parametrize("info,packages,format_version", [
    (known_info(), {"windows": {"amd64": record("wrong-amd64")}}, 3),
    (known_info(), {"windows7": {"amd64": record("wrong-legacy-amd64")}}, 3),
    (known_info(), {}, 1),
    (known_info("windows", "arm64", "full"), {"windows": {"arm64": record("nonexistent-native")}}, 3),
    ({**known_info(), "package_identity_status": "unsupported"}, {"windows7": {"x86": record("legacy")}}, 3),
    ({"platform_family": "windows", "architecture": "arm64", "runtime_profile": "unsupported"}, {}, 1),
])
def test_online_catalog_never_falls_back_to_another_channel(monkeypatch, info, packages, format_version):
    setup_catalog(monkeypatch, info, catalog_for(packages, format_version))
    result = updates.fetch_online_update_catalog()
    assert result["available"] is False
    assert result["target_available"] is False
    assert "package_url" not in result


def test_online_catalog_selects_real_x64_package_under_win11_emulation(monkeypatch):
    setup_catalog(monkeypatch, known_info("windows", "amd64", "full"),
                  catalog_for({"windows": {"amd64": record("modern-amd64"), "arm64": record("not-published")}}))
    result = updates.fetch_online_update_catalog()
    assert result["package_url"].endswith("/modern-amd64.partyops-update")
