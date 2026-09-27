"""全功能测试与逐平台打包顺序门禁。"""

from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
GATE = ROOT / "scripts" / "verify-full-function-gate.py"


def _run(root: Path, mode: str, scope: str = "full") -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        [sys.executable, str(GATE), mode, "--root", str(root), "--scope", scope],
        check=False,
        capture_output=True,
        text=True,
        encoding="utf-8",
    )


def test_gate_requires_record_and_invalidates_after_source_change(tmp_path: Path) -> None:
    assert _run(tmp_path, "verify").returncode == 2

    source = tmp_path / "backend" / "app" / "sample.py"
    source.parent.mkdir(parents=True)
    source.write_text("VALUE = 1\n", encoding="utf-8")
    website = tmp_path / "website" / "src" / "site.js"
    website.parent.mkdir(parents=True)
    website.write_text("export const value = 1;\n", encoding="utf-8")
    assert _run(tmp_path, "record").returncode == 0
    assert _run(tmp_path, "verify").returncode == 0
    assert _run(tmp_path, "verify", "package").returncode == 0
    assert _run(tmp_path, "verify", "website").returncode == 0

    payload = json.loads(
        (tmp_path / ".release-gates" / "full-function-tests.json").read_text(encoding="utf-8")
    )
    assert payload["schema"] == 3
    assert payload["status"] == "passed"
    assert payload["timezone"] == "Asia/Shanghai"
    assert payload["fingerprint_canonicalization"] == "text-lf-v1"
    assert payload["tested_at"].endswith("+08:00")
    assert payload["source_fingerprints"]["package"]["file_count"] == 1
    assert payload["source_fingerprints"]["website"]["file_count"] == 1
    assert (
        "document-formatter-cross-platform-package-contract-manifest-hash-and-selftest-tests"
        in payload["scope"]
    )

    website.write_text("export const value = 2;\n", encoding="utf-8")
    stale = _run(tmp_path, "verify", "website")
    assert stale.returncode == 2
    assert "FULL_FUNCTION_GATE_STALE:website" in stale.stderr
    assert _run(tmp_path, "verify", "package").returncode == 0

    website.write_text("export const value = 1;\n", encoding="utf-8")
    source.write_text("VALUE = 2\n", encoding="utf-8")
    stale = _run(tmp_path, "verify", "package")
    assert stale.returncode == 2
    assert "FULL_FUNCTION_GATE_STALE:package" in stale.stderr
    assert _run(tmp_path, "verify", "website").returncode == 0


def test_package_gate_rejects_changed_legacy_wheel_lock(tmp_path: Path) -> None:
    """Win7依赖哈希变化也必须使原源码门禁失效。"""
    lock = tmp_path / "backend/legacy/requirements-windows7-amd64.lock"
    website = tmp_path / "website/src/site.js"
    lock.parent.mkdir(parents=True)
    website.parent.mkdir(parents=True)
    lock.write_text("python-calamine==0.3.2 --hash=sha256:first\n", encoding="utf-8")
    website.write_text("export const value = 1;\n", encoding="utf-8")
    assert _run(tmp_path, "record").returncode == 0
    assert _run(tmp_path, "verify", "package").returncode == 0
    lock.write_text("python-calamine==0.3.2+partyops.1 --hash=sha256:new\n", encoding="utf-8")
    assert _run(tmp_path, "verify", "package").returncode == 2
    assert _run(tmp_path, "verify", "website").returncode == 0


def test_gate_treats_text_line_endings_as_equivalent_but_not_binary_bytes(
    tmp_path: Path,
) -> None:
    source = tmp_path / "backend" / "app" / "sample.py"
    source.parent.mkdir(parents=True)
    source.write_bytes(b"VALUE = 1\r\nSECOND = 2\r\n")
    binary = tmp_path / "backend" / "app" / "fixture.png"
    binary.write_bytes(b"\x89PNG\r\n\x1a\nfixture\r\n")
    website = tmp_path / "website" / "src" / "site.js"
    website.parent.mkdir(parents=True)
    website.write_bytes(b"export const value = 1;\r\n")

    assert _run(tmp_path, "record").returncode == 0
    source.write_bytes(b"VALUE = 1\nSECOND = 2\n")
    website.write_bytes(b"export const value = 1;\n")
    assert _run(tmp_path, "verify", "full").returncode == 0

    binary.write_bytes(b"\x89PNG\r\n\x1a\nchanged\r\n")
    stale = _run(tmp_path, "verify", "package")
    assert stale.returncode == 2
    assert "FULL_FUNCTION_GATE_STALE:package" in stale.stderr


def test_package_scope_includes_dotnet48_source_evidence(tmp_path: Path) -> None:
    """随包前置运行时来源清单变化后必须让功能门禁失效。"""

    package = tmp_path / "backend" / "app" / "sample.py"
    website = tmp_path / "website" / "src" / "site.js"
    source = tmp_path / "vendor" / "windows" / "dotnet-framework-4.8" / "SOURCE.json"
    package.parent.mkdir(parents=True)
    website.parent.mkdir(parents=True)
    source.parent.mkdir(parents=True)
    package.write_text("VALUE = 1\n", encoding="utf-8")
    website.write_text("export const value = 1;\n", encoding="utf-8")
    source.write_text('{"sha256":"first"}\n', encoding="utf-8")

    assert _run(tmp_path, "record").returncode == 0
    assert _run(tmp_path, "verify", "package").returncode == 0
    source.write_text('{"sha256":"changed"}\n', encoding="utf-8")
    stale = _run(tmp_path, "verify", "package")
    assert stale.returncode == 2
    assert "FULL_FUNCTION_GATE_STALE:package" in stale.stderr


def test_every_platform_builder_verifies_full_function_gate() -> None:
    windows = (ROOT / "packaging" / "windows" / "build-windows.ps1").read_text(encoding="utf-8")
    linux = (ROOT / "packaging" / "linux" / "build-native.sh").read_text(encoding="utf-8")
    macos = (ROOT / "packaging" / "macos" / "build-pkg.sh").read_text(encoding="utf-8")
    release_tests = (ROOT / "scripts" / "test.ps1").read_text(encoding="utf-8")

    assert "verify-full-function-gate.py" in windows and "--scope package" in windows
    assert "verify-full-function-gate.py" in linux and "--scope package" in linux
    assert "verify-full-function-gate.py" in macos and "--scope package" in macos
    assert "verify-full-function-gate.py" in release_tests and " record --root " in release_tests
    assert "external" not in windows.lower()
    assert "PARTYOPS_OFFICE_RUNTIME_MODE" not in linux
    assert "PARTYOPS_LINUX_FORMATTER_RUNTIME" in (
        ROOT / "packaging" / "uos" / "build-portable.sh"
    ).read_text(encoding="utf-8")
    assert "PARTYOPS_MACOS_FORMATTER_RUNTIME" in macos
    uos = (ROOT / "packaging" / "uos" / "build-portable.sh").read_text(
        encoding="utf-8"
    )
    for builder in (uos, macos):
        assert "probe-wps-native-bridge.py" not in builder
        assert "--bridge-evidence" not in builder
        assert "word-vtable-map.json" in builder
        assert "LICENSE-MONO-RUNTIME.txt" in builder


def test_rpm_uses_audited_dependencies_instead_of_payload_autodetection() -> None:
    builder = (ROOT / "packaging" / "linux" / "build-native.sh").read_text(
        encoding="utf-8"
    )
    runtime_test = (ROOT / "scripts" / "test-native-package-runtime.sh").read_text(
        encoding="utf-8"
    )

    assert "AutoReqProv: no" in builder
    assert "Requires: glibc >= 2.17" in builder
    assert "rpm -qp --requires" in runtime_test
    assert "GLIBC_" in runtime_test
    assert "lib(Qt|KF|uno)" in runtime_test
