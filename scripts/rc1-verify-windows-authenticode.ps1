param(
  [Parameter(Mandatory = $true)][string]$Installer,
  [Parameter(Mandatory = $true)][string]$ExpectedSha256,
  [Parameter(Mandatory = $true)][string]$ExpectedPublisherThumbprint,
  [Parameter(Mandatory = $true)][string]$Evidence
)

# Verifies *external* trusted production Authenticode signing only.
# Never generates a key/certificate, changes trust stores, or signs a file.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
  throw '[RC1_AUTHENTICODE_WINDOWS_REQUIRED]'
}
$evidence = [IO.Path]::GetFullPath($Evidence)
$report = [ordered]@{
  schema_version = 1
  scope = 'WINDOWS_AUTHENTICODE_TRUSTED_PUBLISHER_RELEASE_SIGNATURE'
  status = 'BLOCKED'
  signed_release = $false
  expected_installer_sha256 = $ExpectedSha256.ToLowerInvariant()
  publisher_thumbprint_expected = $ExpectedPublisherThumbprint.ToUpperInvariant()
  actual_installer_sha256 = $null
  actual_signer_thumbprint = $null
  signer_subject = $null
  timestamp_subject = $null
  signature_status = $null
  error = $null
}
try {
  if ($ExpectedSha256 -notmatch '^[0-9a-fA-F]{64}$' -or
      $ExpectedPublisherThumbprint -notmatch '^[0-9a-fA-F]{40}$') {
    throw '[RC1_AUTHENTICODE_EXPECTED_HASH_OR_THUMBPRINT_INVALID]'
  }
  $path = (Resolve-Path -LiteralPath $Installer -ErrorAction Stop).Path
  if ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint) {
    throw '[RC1_AUTHENTICODE_LINKED_FILE_REFUSED]'
  }
  $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
  $report['actual_installer_sha256'] = $actual
  if ($actual -ne $ExpectedSha256.ToLowerInvariant()) {
    throw '[RC1_AUTHENTICODE_INSTALLER_HASH_CHANGED]'
  }
  $sig = Get-AuthenticodeSignature -LiteralPath $path
  $report['signature_status'] = [string]$sig.Status
  if ($sig.Status -ne 'Valid') {
    throw "[RC1_AUTHENTICODE_NOT_TRUSTED_OR_NOT_SIGNED] $($sig.Status)"
  }
  $cert = $sig.SignerCertificate
  if (-not $cert) { throw '[RC1_AUTHENTICODE_SIGNER_CERT_MISSING]' }
  $report['actual_signer_thumbprint'] = $cert.Thumbprint
  $report['signer_subject'] = $cert.Subject
  if ($cert.Thumbprint -ne $ExpectedPublisherThumbprint.ToUpperInvariant()) {
    throw '[RC1_AUTHENTICODE_PUBLISHER_CERT_MISMATCH]'
  }
  if ($cert.Subject -eq $cert.Issuer) {
    throw '[RC1_AUTHENTICODE_SELF_SIGNED_QA_CERT_DENIED]'
  }
  $codeSigning = $false
  foreach ($ext in $cert.Extensions) {
    if ($ext -is [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]) {
      foreach ($oid in $ext.EnhancedKeyUsages) {
        if ($oid.Value -eq '1.3.6.1.5.5.7.3.3') { $codeSigning = $true }
      }
    }
  }
  if (-not $codeSigning) { throw '[RC1_AUTHENTICODE_CODESIGN_EKU_MISSING]' }
  if (-not $sig.TimeStamperCertificate) {
    throw '[RC1_AUTHENTICODE_TRUSTED_TIMESTAMP_REQUIRED]'
  }
  $report['timestamp_subject'] = $sig.TimeStamperCertificate.Subject
  $report['status'] = 'PASS_TRUSTED_PUBLISHER_AUTHENTICODE_SIGNED'
  $report['signed_release'] = $true
} catch {
  $report['status'] = 'BLOCKED'
  $report['error'] = [string]$_.Exception.Message
} finally {
  $report['checked_at_utc'] = [DateTime]::UtcNow.ToString('o')
  $dir = [IO.Path]::GetDirectoryName($evidence)
  if (-not [IO.Directory]::Exists($dir)) { [IO.Directory]::CreateDirectory($dir) | Out-Null }
  [IO.File]::WriteAllText($evidence,
    ($report | ConvertTo-Json -Depth 6),
    [Text.UTF8Encoding]::new($false))
  Write-Host "PARTYOPS_AUTHENTICODE_STATUS=$($report.status)"
}
if (-not $report['signed_release']) {
  throw "[RC1_AUTHENTICODE_RELEASE_SIGNATURE_BLOCKED] $($report.error)"
}
