"""Pure tests for the separate ARM64 runtime dependency availability report."""

from __future__ import annotations

import runpy
from pathlib import Path

from packaging.tags import Tag

ROOT = Path(__file__).resolve().parents[2]
review_lock = runpy.run_path(str(ROOT / "scripts" / "audit_windows_arm64_lock.py"))["review_lock"]
ENV = {
    "implementation_name": "cpython", "implementation_version": "3.12.0",
    "os_name": "nt", "platform_machine": "ARM64", "platform_release": "11",
    "platform_system": "Windows", "platform_version": "11",
    "python_full_version": "3.12.10", "platform_python_implementation": "CPython",
    "python_version": "3.12", "sys_platform": "win32", "extra": "",
}
TAGS = {Tag("cp312", "cp312", "win_arm64"), Tag("cp39", "abi3", "win_arm64"), Tag("py3", "none", "any")}


def test_missing_runtime_wheel_is_not_treated_as_installed_or_release_ready() -> None:
    data = b"""
[[package]]
name = "partyops"
version = "1.4.6rc1"
dependencies = [{ name = "onnxruntime" }, { name = "cryptography" }]
[[package]]
name = "onnxruntime"
version = "1.22.1"
wheels = [{ url = "https://example.invalid/onnxruntime-1.22.1-cp312-cp312-win_amd64.whl" }]
[[package]]
name = "cryptography"
version = "50.0.0"
sdist = { url = "https://example.invalid/cryptography-50.0.0.tar.gz" }
"""
    result = review_lock(data, TAGS, ENV)
    assert result["wheel_availability"] == "BLOCKED"
    assert result["missing_locked_wheels"] == ["cryptography", "onnxruntime"]
    assert result["app_installation_verified"] is False
    assert result["release_ready"] is False


def test_matching_native_and_universal_wheels_and_markers_are_respected() -> None:
    data = b"""
[[package]]
name = "partyops"
version = "1.4.6rc1"
dependencies = [
  { name = "argon2-cffi-bindings" },
  { name = "pure-package" },
  { name = "linux-only", marker = "sys_platform == 'linux'" },
]
[[package]]
name = "argon2-cffi-bindings"
version = "25.1.0"
wheels = [{ url = "https://example.invalid/argon2_cffi_bindings-25.1.0-cp39-abi3-win_arm64.whl" }]
[[package]]
name = "pure-package"
version = "1.0"
wheels = [{ url = "https://example.invalid/pure_package-1.0-py3-none-any.whl" }]
"""
    result = review_lock(data, TAGS, ENV)
    assert result["wheel_availability"] == "MATCHES_AVAILABLE"
    assert result["dependency_count"] == 2
    assert not result["missing_locked_wheels"]
    assert result["app_installation_verified"] is False


def test_missing_dependency_is_a_blocker_not_a_silent_skip() -> None:
    raw = b'[[package]]\nname = "partyops"\nversion = "1"\ndependencies = [{name = "missing"}]\n'
    result = review_lock(raw, TAGS, ENV)
    assert result["wheel_availability"] == "BLOCKED"
    assert result["untracked_dependencies"] == ["missing"]


def test_real_lock_still_requires_more_than_the_crypto_source_build() -> None:
    raw = (ROOT / "backend" / "uv.lock").read_bytes()
    result = review_lock(raw, TAGS, ENV)
    assert result["wheel_availability"] == "BLOCKED"
    for known_gap in ("onnxruntime", "numpy", "pymupdf", "tokenizers"):
        assert known_gap in result["missing_locked_wheels"]
    assert result["app_installation_verified"] is False
