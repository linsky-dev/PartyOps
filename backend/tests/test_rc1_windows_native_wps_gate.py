"""Native Windows RC1 test launcher must never fabricate WPS or installation evidence."""

from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE = (ROOT / "scripts/rc1-windows-native-wps-gate.ps1").read_text(encoding="utf-8")


def test_native_wps_launcher_is_opt_in_and_platform_bound() -> None:
    for value in (
        "ValidateSet('Preflight', 'NativeTests', 'BuildCandidate')",
        "ConfirmDedicatedTestMachine",
        "ConfirmInteractiveWpsAcceptance",
        "WINDOWS_10_11_CLIENT_REQUIRED",
        "WINDOWS_AMD64_ARCH_REQUIRED",
        "WINDOWS_ARCH_IDENTITY_UNAVAILABLE",
        "INTERACTIVE_WPS_USER_SESSION_REQUIRED",
        "WPS_OFFICE_CURRENT_USER_OR_MACHINE_INSTALL_NOT_FOUND",
        "SOURCE_COMMIT_NOT_APPROVED",
        "SOURCE_TRACKED_WORKTREE_NOT_CLEAN",
        "SECURE_PYMUPDF_PRODUCTION_LOCK_REQUIRED",
    ):
        assert value in SOURCE


def test_only_original_native_wps_tests_can_record_real_approval() -> None:
    for call in (
        "scripts/test.ps1",
        "scripts/verify-full-function-gate.py",
        "packaging/windows/build-windows.ps1",
    ):
        assert call in SOURCE
    assert SOURCE.index("scripts/test.ps1") < SOURCE.index(
        "report.native_wps_passed = $true"
    )
    assert SOURCE.index("scripts/verify-full-function-gate.py") < SOURCE.index(
        "report.native_full_function_passed = $true"
    )
    assert SOURCE.index("SECURE_PYMUPDF_PRODUCTION_LOCK_REQUIRED") < SOURCE.index(
        "if ($Mode -eq 'BuildCandidate' -and $blocked.Count)"
    )


def test_candidate_is_never_mislabeled_release_ready_or_signed() -> None:
    for marker in (
        "release_ready = $false",
        "signed = $false",
        "native_install_upgrade_rollback_passed = $false",
        "release_approved = $false",
        "BUILD_ONLY_NEEDS_NATIVE_INSTALL_SIGNATURE_AND_RELEASE",
    ):
        assert marker in SOURCE
    assert "bypass" not in SOURCE.lower()


def test_preflight_returns_blocked_without_starting_native_wps() -> None:
    assert SOURCE.index("if ($Mode -eq 'Preflight')") < SOURCE.index(
        "scripts/test.ps1"
    )
    assert "exit 2" in SOURCE
    assert "Test-Path -LiteralPath" in SOURCE


def test_x64_native_gate_checks_os_arch_not_just_64bit_process() -> None:
    # Windows ARM64 also reports Is64BitOperatingSystem=True. Never treat it
    # as equivalent to the genuine Windows x64 client release target.
    assert "RuntimeInformation]::OSArchitecture" in SOURCE
    assert "if ($osArchitecture -ne 'X64')" in SOURCE
    assert "os_architecture = $osArchitecture" in SOURCE
    assert SOURCE.index("WINDOWS_AMD64_ARCH_REQUIRED") < SOURCE.index(
        "if ($Mode -eq 'Preflight')"
    )
