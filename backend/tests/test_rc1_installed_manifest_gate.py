"""Real Windows installer must reject missing/mismatched manifest before success."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path

import pytest

from app import __version__, setup_wizard, startup_selftest
from app.windows_runtime_identity import read_installed_manifest


def _installed(tmp_path: Path) -> Path:
    exe = tmp_path / "PartyOps.exe"
    exe.write_bytes(b"the installed app bytes")
    manifest = {
        "schema_version": 1,
        "product": "PartyOps",
        "version": __version__,
        "files": [{
            "path": "PartyOps.exe",
            "size": exe.stat().st_size,
            "sha256": hashlib.sha256(exe.read_bytes()).hexdigest(),
        }],
    }
    (tmp_path / "release-manifest.json").write_text(
        json.dumps(manifest), encoding="utf-8"
    )
    return exe


def _pretend_target_windows(monkeypatch: pytest.MonkeyPatch, exe: Path) -> None:
    monkeypatch.setattr(startup_selftest.sys, "platform", "win32")
    monkeypatch.setattr(startup_selftest.sys, "frozen", True, raising=False)
    monkeypatch.setattr(startup_selftest.sys, "executable", str(exe))


def test_frozen_installer_rejects_missing_or_corrupt_manifest(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    exe = _installed(tmp_path)
    _pretend_target_windows(monkeypatch, exe)
    startup_selftest._verify_frozen_installed_manifest(tmp_path)
    (tmp_path / "release-manifest.json").unlink()
    with pytest.raises(RuntimeError, match="RUNTIME_DEPENDENCY_MISSING"):
        startup_selftest._verify_frozen_installed_manifest(tmp_path)
    (tmp_path / "release-manifest.json").write_bytes(b"not-json")
    with pytest.raises(RuntimeError, match="RUNTIME_DEPENDENCY_MISSING"):
        startup_selftest._verify_frozen_installed_manifest(tmp_path)


def test_frozen_installer_rejects_executable_tamper(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    exe = _installed(tmp_path)
    _pretend_target_windows(monkeypatch, exe)
    exe.write_bytes(b"tampered installed EXE")
    with pytest.raises(RuntimeError, match="RUNTIME_DEPENDENCY_MISSING"):
        startup_selftest._verify_frozen_installed_manifest(tmp_path)


def test_frozen_installer_rejects_wrong_version(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    exe = _installed(tmp_path)
    _pretend_target_windows(monkeypatch, exe)
    manifest_path = tmp_path / "release-manifest.json"
    record = json.loads(manifest_path.read_text())
    record["version"] = "9.9.9"
    manifest_path.write_text(json.dumps(record))
    with pytest.raises(RuntimeError, match="RUNTIME_PACKAGE_MISMATCH"):
        startup_selftest._verify_frozen_installed_manifest(tmp_path)


def test_dev_python_does_not_require_frozen_install_manifest(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.setattr(startup_selftest.sys, "frozen", False, raising=False)
    startup_selftest._verify_frozen_installed_manifest(tmp_path)



def test_large_release_manifest_is_not_misreported_as_missing(
    tmp_path: Path,
) -> None:
    """Real onedir distributions can legitimately have >4 MiB of file inventory.

    The launcher preflight must use the same bounded 8 MiB budget as the
    verified executable identity reader. The other dependency checks remain
    fail-closed when required binaries are not actually on disk.
    """

    import sys

    exe = _installed(tmp_path)
    manifest_path = tmp_path / "release-manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    manifest.update(
        platform="windows",
        architecture="amd64",
        runtime_profile="full",
        description="x" * (5 * 1024 * 1024),
    )
    manifest["files"].append({
        "path": f"_internal/python{sys.version_info.major}{sys.version_info.minor}.dll",
        "size": 1,
        "sha256": "0" * 64,
    })
    manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
    assert 4 * 1024 * 1024 < manifest_path.stat().st_size < 8 * 1024 * 1024
    assert read_installed_manifest(exe)["product"] == "PartyOps"
    assert setup_wizard._marked_runtime_file_is_trusted(tmp_path, exe)

    with pytest.raises(setup_wizard.HostStartupError) as failure:
        setup_wizard._preflight_windows_runtime_dependencies(
            exe, force=True, windows_version=(10, 0)
        )
    assert "缺失或无效清单=" not in failure.value.detail
    assert "缺失=" in failure.value.detail

    # The same ceiling remains strict: enormous inventory must be rejected.
    manifest_path.write_bytes(b" " * (8 * 1024 * 1024 + 1))
    with pytest.raises(ValueError, match="WINDOWS_RELEASE_MANIFEST_TOO_LARGE"):
        read_installed_manifest(exe)


def test_installer_requires_manifest_before_service_install_and_desktop_launch() -> None:
    root = Path(__file__).resolve().parents[2]
    iss = (root / "packaging/windows/PartyOps.iss").read_text(encoding="utf-8-sig")
    post_check = iss.split("function VerifyExtractedInstallDirectory", 1)[1]
    assert "release-manifest.json" in post_check.split("function PrepareToInstall", 1)[0]
    assert "RunRuntimeStartupSelfTest;" in iss
    # Compare actual calls within the post-install callback, not an earlier
    # procedure declaration elsewhere in the PascalScript source.
    post_install = iss.split("procedure CurStepChanged(CurStep: TSetupStep);", 1)[1]
    assert post_install.index("ErrorMessage := VerifyExtractedInstallDirectory;") < (
        post_install.index("RunRuntimeStartupSelfTest;")
    )
    assert post_install.index("RunRuntimeStartupSelfTest;") < (
        post_install.index("ProtectSystemControlDirectories;")
    )
    startup = (root / "backend/app/startup_selftest.py").read_text(encoding="utf-8")
    assert "_verify_frozen_installed_manifest(runtime)" in startup
    assert "read_installed_manifest(installed_executable)" in startup
    launcher = (root / "packaging/windows/windows_launcher.py").read_text(
        encoding="utf-8"
    )
    assert "_preflight_windows_runtime_dependencies(runtime / \"PartyOps.exe\")" in launcher
    assert "release-manifest.json" in (
        root / "backend/app/setup_wizard.py"
    ).read_text(encoding="utf-8")
