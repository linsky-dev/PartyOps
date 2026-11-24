"""Native ARM64 source candidate must not advertise an installed update channel."""
from app import platform_info


def test_arm64_source_candidate_has_runtime_probes_but_no_update_channel(monkeypatch):
    monkeypatch.setattr(platform_info.sys, "platform", "win32")
    monkeypatch.setattr(platform_info.sys, "frozen", False, raising=False)
    monkeypatch.setattr(platform_info.platform, "machine", lambda: "ARM64")
    monkeypatch.setattr(platform_info.platform, "win32_ver", lambda: ("11", "10.0.26100", "", ""))
    info = platform_info.detect_platform_info()
    assert info["runtime_profile"] == "arm64-source-candidate"
    assert info["architecture"] == "arm64"
    assert "semantic_rerank" in info["capabilities"]
    assert "local_llm" in info["capabilities"]
    assert platform_info.update_platform_key(info) == ""


def test_arm64_frozen_without_verified_package_remains_blocked(monkeypatch):
    monkeypatch.setattr(platform_info.sys, "platform", "win32")
    monkeypatch.setattr(platform_info.sys, "frozen", True, raising=False)
    monkeypatch.setattr(platform_info.platform, "machine", lambda: "ARM64")
    monkeypatch.setattr(platform_info.platform, "win32_ver", lambda: ("11", "10.0.26100", "", ""))
    monkeypatch.setattr(platform_info, "installed_identity", lambda *_: {
        "architecture": "arm64", "process_architecture": "arm64",
        "runtime_profile": "unsupported", "capabilities": [],
        "package_identity_status": "unsupported"})
    info = platform_info.detect_platform_info()
    assert not info["capabilities"]
    assert platform_info.update_platform_key(info) == ""
