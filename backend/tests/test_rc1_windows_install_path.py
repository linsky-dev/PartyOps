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
    assert "function NextButtonClick(CurPageID: Integer): Boolean;" in iss
    assert "if CurPageID <> wpSelectDir then" in iss
    assert "AppDir := WizardDirValue;" in iss
    assert "ReadInstallPathDiagnostic(DiagnosticFile, ResultCode)" in iss
    assert "请返回当前页面改选本机固定磁盘中的目录" in iss
    assert "Result := False;" in iss
    assert "function PrepareToInstall(var NeedsRestart: Boolean): String;" in iss
    assert "Result := ValidateAndSecureInstallDirectory;" in iss


def test_upgrade_finds_prior_install_in_32_and_64bit_registry_and_enforces_reuse() -> None:
    """A normal end user upgrades over RC6; no second install tree or uninstall."""
    iss = INSTALLER.read_text(encoding="utf-8-sig")
    assert "UsePreviousAppDir=yes" in iss
    assert "function FindExistingPartyOpsInstallDirectory" in iss
    assert "Software\\WOW6432Node\\Microsoft" in iss
    assert "PARTYOPS_UPGRADE_MULTIPLE_INSTALLATIONS" in iss
    assert "PARTYOPS_UPGRADE_USE_EXISTING_DIRECTORY" in iss
    assert "PARTYOPS_UPGRADE_PRESERVE_DATA_DIRECTORY" in iss
    assert "WizardForm.DirEdit.Text := PreviousAppInstallDir;" in iss
    assert "不需要先卸载旧版" in iss
    assert "PreviousBusinessDataDir := PreviousDataDir;" in iss
    assert "Result := ValidateUpgradeLocations;" in iss
    assert iss.index("Result := ValidateUpgradeLocations;", iss.index(
        "function PrepareToInstall")) < iss.index("Result := EnsureDotNet48(NeedsRestart);",
        iss.index("function PrepareToInstall"))
