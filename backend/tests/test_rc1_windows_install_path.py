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
    assert "UsePreviousAppDir=no" in iss
    assert "DefaultDirName={code:GetSuggestedProgramDir}" in iss
    assert "function FindExistingPartyOpsInstallDirectory" in iss
    assert "Software\\WOW6432Node\\Microsoft" in iss
    assert "PARTYOPS_UPGRADE_MULTIPLE_INSTALLATIONS" in iss
    assert "PARTYOPS_UPGRADE_USE_EXISTING_DIRECTORY" in iss
    assert "PARTYOPS_UPGRADE_PRESERVE_DATA_DIRECTORY" in iss
    assert "WizardForm.DirEdit.Text := PreviousAppInstallDir;" in iss
    assert "无需卸载旧版" in iss
    assert "PreviousBusinessDataDir := PreviousDataDir;" in iss
    assert "Result := ValidateUpgradeLocations;" in iss
    assert iss.index("Result := ValidateUpgradeLocations;", iss.index(
        "function PrepareToInstall")) < iss.index("Result := EnsureDotNet48(NeedsRestart);",
        iss.index("function PrepareToInstall"))


def test_interrupted_upgrade_recovers_previous_business_data_pointer_without_uninstall() -> None:
    iss = INSTALLER.read_text(encoding="utf-8-sig")
    start = iss.index("procedure BeginDataMarkerTransaction;")
    end = iss.index("procedure CommitPostInstallTransactions;", start)
    segment = iss[start:end]
    assert "if FileExists(DataMarkerPreviousPath) then" in segment
    assert "not DeleteFile(DataMarkerPath)" in segment
    assert "RenameFile(DataMarkerPreviousPath, DataMarkerPath)" in segment
    assert "DATA_MARKER_INTERRUPTED_RECOVERY_FAILED" in segment
    assert segment.index("RenameFile(DataMarkerPreviousPath, DataMarkerPath)") < segment.index(
        "DataMarkerHadPrevious := FileExists(DataMarkerPath)"
    )
    assert "DeleteFile(DataMarkerPreviousPath);" not in segment
    assert "RollbackDataMarker;" in iss
    assert "未完成的升级" in segment


def test_unsafe_legacy_acl_does_not_instruct_destructive_uninstall() -> None:
    content = VALIDATOR.read_text(encoding="utf-8-sig")
    assert "INSTALL_DIR_EXISTING_ACL_UNSAFE" in content
    assert "请先卸载旧版本" not in content
    assert "请保留旧版安装及数据" in content
    assert "Assert-SecureDirectoryAcl $fullPath -ForProgramDirectory" in content



def test_detached_disk_stale_uninstall_record_never_forces_original_path() -> None:
    """Removed E: is not a valid program installation; fall back dynamically."""
    source = INSTALLER.read_text(encoding="utf-8-sig")
    assert "function GetWindowsDriveType(RootPath: String): Integer;" in source
    assert "GetDriveTypeW@kernel32.dll stdcall" in source
    assert "function IsAvailableFixedDiskPath" in source
    assert "GetWindowsDriveType(RootPath) = 3" in source
    assert "DirExists(RootPath)" in source
    assert "not IsUsableExistingPartyOpsProgramDir(Win64Dir)" in source
    assert "not IsUsableExistingPartyOpsProgramDir(Win32Dir)" in source
    assert "function GetSuggestedProgramDir(Param: String): String;" in source
    assert "Result := ExpandConstant('{autopf}\\PartyOps');" in source
    assert "WizardForm.DirEdit.Text := ExpandConstant('{autopf}\\PartyOps');" in source
    assert "PreviousUnavailableAppInstallDir" in source
    assert "UsePreviousAppDir=no" in source
    assert "PartyOps.exe" in source
    assert "PARTYOPS_OLD_DATA_DISK_MISSING" in source
    assert "{param:ALLOWMISSINGDATA|0}" in source
    assert "not UnavailableDataAcknowledged" in source
    assert "请在安装向导确认选择新数据目录" in source
    assert "PreviousUnavailableBusinessDataDir := '';" in source
    assert "PreviousBusinessDataDir := PreviousDataDir" in source
    assert "PARTYOPS_UPGRADE_PRESERVE_DATA_DIRECTORY" in source


def test_missing_data_confirmation_is_required_for_silent_install() -> None:
    """Don't claim an offline business database was migrated automatically."""
    source = INSTALLER.read_text(encoding="utf-8-sig")
    prepare = source[source.index("function PrepareToInstall"): ]
    assert "PreviousUnavailableBusinessDataDir <> ''" in prepare
    assert "UnavailableDataAcknowledged or" in prepare
    assert "param:ALLOWMISSINGDATA|0" in prepare
    assert "无法从中恢复原账号、数据库、附件或证书" in source
    assert "旧数据不会被删除或迁移" in source
    assert "没有" not in source[source.index("function GetSuggestedProgramDir"):source.index("function ValidateUpgradeLocations")]



def test_installation_uses_one_full_acl_repair_then_a_readonly_post_extract_guard() -> None:
    """Avoid scanning and changing ACLs of 16k files twice without relaxing gates."""
    source = INSTALLER.read_text(encoding="utf-8-sig")
    pre = source.split("function ValidateAndSecureInstallDirectory: String;", 1)[1]
    pre = pre.split("function VerifyExtractedInstallDirectory: String;", 1)[0]
    post = source.split("function VerifyExtractedInstallDirectory: String;", 1)[1]
    post = post.split("function PrepareToInstall", 1)[0]
    assert " /reset /T /C /Q" in pre
    assert " /setintegritylevel (OI)(CI)H /T /C /Q" in pre
    assert " -VerifyTargetAcl" in pre
    assert " -VerifyTargetAcl" in post
    assert "if not ProgramDirectorySecuredBeforeExtraction then" in post
    assert "INSTALL_DIR_PAYLOAD_MISSING" in post
    assert "RunInstallPathValidator(PowerShell, Parameters, ResultCode)" in post
    assert " /reset /T /C /Q" not in post
    assert " /setintegritylevel" not in post
    assert "ProgramDirectorySecuredBeforeExtraction := True;" in source
    assert "ErrorMessage := VerifyExtractedInstallDirectory;" in source
    assert "RunRuntimeStartupSelfTest;" in source
    assert "StopServiceBeforeUpgrade(" in source

def test_interactive_navigation_never_extracts_or_runs_full_path_validator() -> None:
    source = INSTALLER.read_text(encoding="utf-8-sig")
    files = source.split("[Files]", 1)[1].split("[Icons]", 1)[0]
    assert files.index("validate-install-path.ps1") < files.index('Source: "{#BuildRoot}\\*"')
    assert files.index("ndp48-x86-x64-allos-enu.exe") < files.index('Source: "{#BuildRoot}\\*"')
    assert "dontcopy solidbreak" in files
    nav = source.split("function NextButtonClick", 1)[1].split("procedure CurPageChanged", 1)[0]
    for blocking in ("ExtractTemporaryFile", "RunInstallPathValidator(", "Exec(", "ExecAndLogOutput(", "ForceDirectories", "Sleep("):
        assert blocking not in nav, blocking
    assert "IsAvailableFixedDiskPath(AppDir)" in nav
    assert "PARTYOPS_INTERACTIVE_PREFLIGHT_DEFERRED" in nav
    assert "ValidateUpgradeLocations" in nav
    pre = source.split("function PrepareToInstall", 1)[1].split("procedure RemoveOwnedProtocol", 1)[0]
    assert pre.index("Result := ValidateAndSecureInstallDirectory;") < pre.index("HostServiceExistedBeforeInstall := ServiceExists")
    assert pre.index("Result := ValidateAndSecureInstallDirectory;") < pre.index("Result := StopServiceBeforeUpgrade")
    assert source.count("ExtractTemporaryFile('validate-install-path.ps1')") == 1
    assert source.count("EnsureInstallPathValidatorExtracted;") == 3
