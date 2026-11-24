"""Authenticode release check cannot confuse a QA self-signed signature with production."""
from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE = (ROOT / "scripts/rc1-verify-windows-authenticode.ps1").read_text(
    encoding="utf-8"
)


def test_release_signer_must_match_explicit_user_approved_thumbprint() -> None:
    for marker in (
        "ExpectedPublisherThumbprint",
        "RC1_AUTHENTICODE_EXPECTED_HASH_OR_THUMBPRINT_INVALID",
        "RC1_AUTHENTICODE_INSTALLER_HASH_CHANGED",
        "Get-AuthenticodeSignature",
        "if ($sig.Status -ne 'Valid')",
        "RC1_AUTHENTICODE_PUBLISHER_CERT_MISMATCH",
    ):
        assert marker in SOURCE
    assert "New-SelfSignedCertificate" not in SOURCE
    assert "Set-AuthenticodeSignature" not in SOURCE


def test_release_signature_needs_trusted_ca_eku_and_timestamp() -> None:
    assert "RC1_AUTHENTICODE_SELF_SIGNED_QA_CERT_DENIED" in SOURCE
    assert "RC1_AUTHENTICODE_CODESIGN_EKU_MISSING" in SOURCE
    assert "1.3.6.1.5.5.7.3.3" in SOURCE
    assert "RC1_AUTHENTICODE_TRUSTED_TIMESTAMP_REQUIRED" in SOURCE
    assert "$report['signed_release'] = $true" in SOURCE
    assert "RC1_AUTHENTICODE_RELEASE_SIGNATURE_BLOCKED" in SOURCE
    assert "RC1_AUTHENTICODE_LINKED_FILE_REFUSED" in SOURCE


def test_rejection_report_never_claims_a_release() -> None:
    assert "status = 'BLOCKED'" in SOURCE
    assert "signed_release = $false" in SOURCE
    assert "$report['status'] = 'BLOCKED'" in SOURCE
    assert "($report | ConvertTo-Json" in SOURCE
