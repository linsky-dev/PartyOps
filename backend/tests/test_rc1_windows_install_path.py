"""Installer disk validation must reject nonlocal paths, report true reasons and preserve ACL gates."""
from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
VALIDATOR = ROOT / "packaging/windows/validate-install-path.ps1"
INSTALLER = ROOT / "packaging/windows/PartyOps.iss"


def test_installer_validator_ps51_bom_and_exactly_one_file() -> None:
    raw = VALIDATOR.read_bytes()
    assert raw[:3] == b"\xef\xbb\xbf"
    assert b"\x00" not in raw[:128]
    assert 'INSTALL_DIR_NOT_FIXED' in raw.decode("utf-8-sig")


def test_network_relative_root_and_removable_are_refused() -> None:
    source = VALIDATOR.read_text(encoding="utf-8-sig")
    assert "^[A-Za-z]:\\\\ " not in source
    assert "^[A-Za-z]:\\\\" in source
    assert 'if ($Path -match' in source and 'INSTALL_DIR_NOT_FIXED' in source
    assert 'INSTALL_DIR_INVALID' in source
    assert '$drive.DriveType -ne [IO.DriveType]::Fixed' in source
    assert 'Windows 识别的磁盘类型：$type' in source
    assert 'New-Object IO.DriveInfo($root)' in source


def test_protections_are_kept_for_privileged_installer_files() -> None:
    source = VALIDATOR.read_text(encoding="utf-8-sig")
    for phrase in (
        'INSTALL_DIR_ROOT_DENIED',
        'INSTALL_DIR_REPARSE_POINT',
        'INSTALL_DIR_EXISTING_ACL_UNSAFE',
        'INSTALL_DIR_NOT_PARTYOPS',
        'Assert-SecureDirectoryAcl $fullPath -ForProgramDirectory',
        'Stop-InstallPathValidation',
    ):
        assert phrase in source
    iss = INSTALLER.read_text(encoding="utf-8-sig")
    assert "RunInstallPathValidator(PowerShell, Parameters, ResultCode)" in iss
    assert "VerifyTargetAcl" in iss
    assert "icacls.exe" in iss
