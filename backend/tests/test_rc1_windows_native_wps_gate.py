"""Native Windows RC1 test launcher must never fabricate WPS or installation evidence."""

from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE = (ROOT / "scripts/rc1-windows-native-wps-gate.ps1").read_text(encoding="utf-8")

# Match executable invocations, not explanatory strings in the header comment.
# False positives here previously prevented the entire Windows backend suite
# from passing even though the gate itself refused unsupported environments.
NATIVE_TEST_CALL = "& (Join-Path $root 'scripts/test.ps1') -Scope Package"
VERIFY_CALL = "& $Python (Join-Path $root 'scripts/verify-full-function-gate.py') verify --root $root --scope package"
BUILD_CALL = "& (Join-Path $root 'packaging/windows/build-windows.ps1') -Python $Python"


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
    for call in (NATIVE_TEST_CALL, VERIFY_CALL, BUILD_CALL):
        assert SOURCE.count(call) == 1
    assert SOURCE.index(NATIVE_TEST_CALL) < SOURCE.index(VERIFY_CALL)
    assert SOURCE.index(VERIFY_CALL) < SOURCE.index(
        "report.native_wps_passed = $true"
    )
    assert SOURCE.index(VERIFY_CALL) < SOURCE.index(
        "report.native_full_function_passed = $true"
    )
    assert SOURCE.index("if ($Mode -eq 'BuildCandidate') {") < SOURCE.index(BUILD_CALL)
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
        NATIVE_TEST_CALL
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
