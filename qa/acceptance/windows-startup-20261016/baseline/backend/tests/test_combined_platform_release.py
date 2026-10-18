"""验证合并后的真实模块同时保留 Windows 包线与 Loong64 Linux 选包约束。"""
import hashlib
import json
from types import SimpleNamespace

import pytest

from app import platform_info, update_executor, windows_runtime_identity
from app.routers import updates


@pytest.mark.parametrize("alias", ["loongarch64", "loong64"])
def test_frozen_linux_loong64_uses_deb_without_windows_identity(alias, monkeypatch, tmp_path):
    executable = tmp_path / "partyops"
    header = bytearray(64)
    header[:6] = b"\x7fELF\x02\x01"
    header[18:20] = (258).to_bytes(2, "little")
    header[48:52] = (3).to_bytes(4, "little")
    executable.write_bytes(header)
    (tmp_path / "release-manifest.json").write_text(json.dumps({
        "schema_version": 1, "product": "PartyOps", "platform": "linux-deb",
        "architecture": "loong64", "runtime_profile": "core",
        "files": [{"path": "partyops", "size": len(header), "sha256": hashlib.sha256(header).hexdigest()}],
    }), encoding="utf-8")
    monkeypatch.setattr(platform_info, "sys", SimpleNamespace(platform="linux", frozen=True, executable=str(executable)))
    monkeypatch.setattr(platform_info.platform, "machine", lambda: alias)
    monkeypatch.setattr(platform_info, "read_os_release", lambda path: {"ID": "deepin", "VERSION_ID": "25"})
    monkeypatch.setattr(platform_info, "installed_identity", lambda *_args: pytest.fail("Linux 不走 Windows 身份"))
    info = platform_info.detect_platform_info()
    monkeypatch.setattr(update_executor, "os", SimpleNamespace(name="posix"))
    monkeypatch.setattr(update_executor, "sys", SimpleNamespace(platform="linux", frozen=True))
    monkeypatch.setattr(update_executor, "detect_platform_info", lambda: info)
    assert info["architecture"] == update_executor._architecture() == "loong64"
    assert info["runtime_profile"] == "core"
    assert info["distribution"] == "deepin"
    assert update_executor._manifest_platform_name({"format_version": 4}) == "linux-deb"
    assert updates.V4_PLATFORM_ARTIFACTS["linux-deb"]["loong64"] == "_linux_loong64.deb"
    assert "loong64" not in updates.V3_PLATFORM_ARTIFACTS["linux-deb"]
    assert "loong64" not in updates.V4_PLATFORM_ARTIFACTS["linux-rpm"]


@pytest.mark.parametrize("process,package,profile,machine,channel", [
    ("x86", "windows7", "legacy-core", 0x014C, "windows7"),
    ("amd64", "windows", "full", 0x8664, "windows"),
])
def test_frozen_windows_emulation_keeps_package_channel_after_loong_merge(tmp_path, monkeypatch, process, package, profile, machine, channel):
    executable = tmp_path / "PartyOps.exe"
    header = bytearray(64)
    header[:2], header[60:64] = b"MZ", (64).to_bytes(4, "little")
    executable.write_bytes(header + b"PE\0\0" + machine.to_bytes(2, "little"))
    manifest = {"schema_version": 1, "product": "PartyOps", "platform": package, "architecture": process,
                "runtime_profile": profile, "files": [{"path": "PartyOps.exe", "size": executable.stat().st_size,
                "sha256": hashlib.sha256(executable.read_bytes()).hexdigest()}]}
    (tmp_path / "release-manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
    runtime_sys = SimpleNamespace(platform="win32", frozen=True, executable=str(executable))
    monkeypatch.setattr(platform_info, "sys", runtime_sys)
    monkeypatch.setattr(platform_info.platform, "win32_ver", lambda: ("11", "10.0.26200", "", ""))
    monkeypatch.setattr(windows_runtime_identity, "query_machine_types", lambda _path: (process, "arm64"))
    monkeypatch.setattr(update_executor, "sys", runtime_sys)
    monkeypatch.setattr(update_executor, "os", SimpleNamespace(name="nt"))
    info = platform_info.detect_platform_info()
    assert info["package_identity_status"] == "verified"
    assert info["architecture"] == update_executor._architecture() == process
    assert info["os_architecture"] == "arm64"
    assert info["execution_kind"] == ("windows-x86-emulation" if process == "x86" else "windows-x64-emulation")
    assert update_executor._manifest_platform_name({"format_version": 4}) == channel
    if profile == "legacy-core":
        assert "local_llm" not in info["capabilities"]
        with pytest.raises(RuntimeError, match="兼容包发布线"):
            update_executor._manifest_platform_name({"format_version": 2})


def test_unknown_frozen_windows_identity_still_has_no_update_channel(monkeypatch):
    monkeypatch.setattr(update_executor, "os", SimpleNamespace(name="nt"))
    monkeypatch.setattr(update_executor, "sys", SimpleNamespace(platform="win32", frozen=True))
    monkeypatch.setattr(update_executor, "detect_platform_info", lambda: {"platform_family": "windows", "package_identity_status": "unsupported"})
    with pytest.raises(RuntimeError, match="WINDOWS_UPDATE_CHANNEL_UNSUPPORTED"):
        update_executor._architecture()
