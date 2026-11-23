"""Windows x64 safe-PDF preview never weakens the normal installer path."""
from __future__ import annotations
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE = (ROOT / "packaging/windows/build-windows.ps1").read_text(encoding="utf-8")
INNO = (ROOT / "packaging/windows/PartyOps.iss").read_text(encoding="utf-8")


def test_preview_is_opt_in_x64_with_isolated_safe_pdf_lock() -> None:
    for marker in (
        "[switch]$BuildOnlyPreview",
        '[string]$PdfCandidateRoot = ""',
        "RC1_BUILD_ONLY_X64_PDF_ROOT_REQUIRED",
        "RC1_BUILD_ONLY_PDF_ROOT_INVALID",
        "rc1_windows_pdf_lock_delta.py",
        "rc1_windows_historical_wps_reference.py",
        "RC1_BUILD_ONLY_SECURE_PYMUPDF_21_REQUIRED",
        "VERSION_FLOOR_ONLY: 2",
        "Existing dependencies must remain unchanged",
        "RC1_BUILD_ONLY_PREVIOUS_WPS_REFERENCE_INVALID",
    ):
        assert marker in SOURCE
    assert SOURCE.index("rc1_windows_pdf_lock_delta.py") < SOURCE.index(
        "rc1_windows_historical_wps_reference.py"
    )


def test_default_packager_still_fail_closes_vulnerable_pdf() -> None:
    assert SOURCE.count("verify_windows_secure_pdf_package.py") == 1
    assert "& $Python $securePdfVerifier --root $repoRoot" in SOURCE
    assert "verify-full-function-gate.py" in SOURCE
    assert SOURCE.index("rc1_windows_historical_wps_reference.py") < SOURCE.index(
        "verify-full-function-gate.py"
    )
    assert SOURCE.count("--check-installed") == 1
    assert SOURCE.index("pip install -r") < SOURCE.index("--check-installed")
    assert "& $Python $securePdfVerifier --root $pdfPackageRoot --check-installed" in SOURCE
    assert "[WINDOWS_X64_PDF_BUILD_INPUTS_UNSAFE]" in SOURCE


def test_preview_reference_is_not_forged_current_wps() -> None:
    for marker in (
        "HISTORICAL_WPS_REFERENCE_ONLY",
        "current_version_native_wps_tested",
        "RC1_BUILD_ONLY_HISTORICAL_WPS_REFERENCE_MISSING",
        "RC1_BUILD_ONLY_HISTORICAL_WPS_REFERENCE_MISLABELED",
        "historical-wps-reference.json",
    ):
        assert marker in SOURCE
    assert SOURCE.index("if ($BuildOnlyPreview) {\n  # A hosted CI") < SOURCE.index(
        "verify-formatter-runtime-evidence.py"
    )


def test_preview_output_never_looks_like_official_release() -> None:
    assert "PartyOps_1.4.6-rc.1_windows_amd64_BUILD_ONLY_UNVERIFIED" in SOURCE
    assert "/DPartyOpsOutputBase=$installerBase" in SOURCE
    assert "#ifndef PartyOpsOutputBase" in INNO
    for marker in (
        "$candidate['scope'] = 'BUILD_ONLY_UNVERIFIED_NATIVE_WPS'",
        "$candidate['release_ready'] = $false",
        "$candidate['current_version_native_wps_tested'] = $false",
        "$candidate['full_function_gate_passed'] = $false",
        "$candidate['production_pdf_lock_unchanged'] = $true",
        "$candidate['embedded_pymupdf'] = '2.1'",
        "$candidate['historical_wps_reference_sha256']",
        "RC1_BUILD_ONLY_PYMUPDF_LICENSE_NOTICE_UNEXPECTED",
    ):
        assert marker in SOURCE


def test_normal_windows_builder_still_uses_original_gate_and_name() -> None:
    assert '"PartyOps_1.4.6-rc.1_windows_amd64"' in SOURCE
    assert "& $InnoCompiler $innoScript *> $innoLog" in SOURCE
    assert '  & $Python (Join-Path $repoRoot "scripts\\verify-full-function-gate.py") verify --root $repoRoot --scope package' in SOURCE
    assert "if ($BuildOnlyPreview) {\n    & $InnoCompiler" in SOURCE
