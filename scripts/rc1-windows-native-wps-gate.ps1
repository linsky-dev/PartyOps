<#
RC1 Windows 10/11 x64 native WPS acceptance and installer-candidate entrypoint.
READ-ONLY by default. Real acceptance delegates to the ORIGINAL scripts/test.ps1;
the original packaging/windows/build-windows.ps1 remains the only builder.

Run on a dedicated, interactive Windows 10/11 x64 WPS test machine, not on a
production PartyOps installation or a headless cloud runner. Neither test
records nor candidate package represent permission to publish.
#>
[CmdletBinding()]
param(
  [ValidateSet('Preflight', 'NativeTests', 'BuildCandidate')]
  [string]$Mode = 'Preflight',
  [Parameter(Mandatory = $true)]
  [ValidatePattern('^[a-fA-F0-9]{40}$')]
  [string]$ExpectedSourceSha,
  [string]$RepoRoot = '',
  [string]$Python = 'python',
  [switch]$ConfirmDedicatedTestMachine,
  [switch]$ConfirmInteractiveWpsAcceptance
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $RepoRoot) {
  $RepoRoot = Join-Path $PSScriptRoot '..'
}
$root = (Resolve-Path -LiteralPath $RepoRoot).Path
$blocked = [System.Collections.Generic.List[string]]::new()
$source = ''
try {
  $source = (& git -C $root rev-parse HEAD).Trim()
  if ($LASTEXITCODE -ne 0) { throw 'git source lookup failed' }
} catch {
  [void]$blocked.Add('SOURCE_GIT_IDENTITY_UNAVAILABLE')
}
if ($source -ne $ExpectedSourceSha.ToLowerInvariant()) {
  [void]$blocked.Add('SOURCE_COMMIT_NOT_APPROVED')
}
# Five existing, Git-tracked WPS receipt files are intentionally rewritten
# by scripts/test.ps1. Allow ONLY unstaged modifications to these exact output
# paths when rerunning NativeTests -> BuildCandidate on the same clean source.
# Staged edits, additions, deletions, renames and every code change still block.
$generatedEvidenceChanges = @()
try {
  $modified = @(& git -C $root status --porcelain --untracked-files=no)
  $gitStatusExit = $LASTEXITCODE
  $allowedGeneratedEvidence = @(
    '.release-gates/formatter-parity-x64.json',
    '.release-gates/formatter-parity-x86.json',
    '.release-gates/formatter-features-e2e-x64.json',
    '.release-gates/formatter-features-e2e-x86.json',
    '.release-gates/full-function-tests.json'
  )
  $unsafeChanges = @(
    foreach ($line in $modified) {
      if ($line.Length -ge 4 -and $line.Substring(0, 2) -ceq ' M' -and
          $line.Substring(3) -cin $allowedGeneratedEvidence) {
        $generatedEvidenceChanges += $line.Substring(3)
      } else {
        $line
      }
    }
  )
  if ($gitStatusExit -ne 0 -or $unsafeChanges.Count) {
    [void]$blocked.Add('SOURCE_TRACKED_WORKTREE_NOT_CLEAN')
  }
} catch {
  [void]$blocked.Add('SOURCE_WORKTREE_INSPECTION_FAILED')
}
if (-not [Environment]::Is64BitOperatingSystem) {
  [void]$blocked.Add('WINDOWS_X64_REQUIRED')
}
# A 64-bit OS can still be ARM64. The existing amd64 installer and WPS
# acceptance scope must never run under Windows-on-ARM x64 emulation.
$osArchitecture = ''
try {
  $osArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
  if ($osArchitecture -ne 'X64') {
    [void]$blocked.Add('WINDOWS_AMD64_ARCH_REQUIRED')
  }
} catch {
  [void]$blocked.Add('WINDOWS_ARCH_IDENTITY_UNAVAILABLE')
}
$windowsCaption = ''
$osBuild = 0
try {
  $os = Get-CimInstance Win32_OperatingSystem
  $windowsCaption = [string]$os.Caption
  $osBuild = [int]$os.BuildNumber
  if ($windowsCaption -notmatch 'Windows (10|11)' -or $os.ProductType -ne 1) {
    [void]$blocked.Add('WINDOWS_10_11_CLIENT_REQUIRED')
  }
} catch {
  [void]$blocked.Add('WINDOWS_CLIENT_IDENTITY_UNAVAILABLE')
}
try {
  if ([System.Diagnostics.Process]::GetCurrentProcess().SessionId -eq 0) {
    [void]$blocked.Add('INTERACTIVE_WPS_USER_SESSION_REQUIRED')
  }
} catch {
  [void]$blocked.Add('INTERACTIVE_SESSION_UNAVAILABLE')
}
$wpsRegistered = $false
$wpsRegistryRoots = @(
  'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*',
  'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*',
  'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
)
foreach ($path in $wpsRegistryRoots) {
  $items = @(Get-ItemProperty -Path $path -ErrorAction SilentlyContinue |
    Where-Object {
      $displayName = $_.PSObject.Properties['DisplayName']
      $null -ne $displayName -and [string]$displayName.Value -match '^WPS Office(\s|$|\()'
    })
  if ($items.Count) { $wpsRegistered = $true; break }
}
if (-not $wpsRegistered) { [void]$blocked.Add('WPS_OFFICE_CURRENT_USER_OR_MACHINE_INSTALL_NOT_FOUND') }

$version = ''
$pythonCommand = 'import sys; from pathlib import Path; sys.path.insert(0,sys.argv[1]); from rc1_release_security import assert_safe_pymupdf_lock; print(assert_safe_pymupdf_lock(Path(sys.argv[2])))'
try {
  $version = (& $Python -c $pythonCommand (Join-Path $root 'scripts') (Join-Path $root 'backend/uv.lock') 2>$null) | Select-Object -Last 1
  if ($LASTEXITCODE -ne 0) { $version = '' }
} catch { $version = '' }
if (-not $version) { [void]$blocked.Add('SECURE_PYMUPDF_PRODUCTION_LOCK_REQUIRED') }

$runtimePaths = @(
  'backend/.venv/Scripts/python.exe',
  'vendor/windows/libreoffice-headless-amd64/program/soffice.com',
  'vendor/windows/libreoffice-headless-amd64/SOURCE.json',
  'vendor/windows/sqlite-3.53.4/runtime/sqlite3.dll',
  'vendor/windows/local-ai/llama-b10331/llama-server.exe',
  'vendor/windows/ocr/tesseract-5.5.3-windows-amd64-recovered',
  'vendor/windows/dotnet-framework-4.8/ndp48-x86-x64-allos-enu.exe'
)
$missingRuntime = @($runtimePaths | Where-Object {
  -not (Test-Path -LiteralPath (Join-Path $root $_))
})
if ($missingRuntime.Count) { [void]$blocked.Add('STAGED_WINDOWS_BUILD_INPUTS_MISSING') }
# Independent WPS-only QA does not need the SQLite, local-AI, OCR, .NET, or
# Corepack inputs needed to build an installer. It still needs the real WPS
# user desktop, the original formatter source and real PDF goldens.
$nativeWpsPaths = @(
  'backend/.venv/Scripts/python.exe',
  'vendor/windows/libreoffice-headless-amd64/program/soffice.com',
  'vendor/windows/libreoffice-headless-amd64/SOURCE.json'
)
$missingNativeWps = @($nativeWpsPaths | Where-Object {
  -not (Test-Path -LiteralPath (Join-Path $root $_))
})
if (-not (Get-Command corepack -ErrorAction SilentlyContinue)) {
  [void]$blocked.Add('NODE_COREPACK_MISSING')
}
$report = [ordered]@{
  schema_version = 1
  product = 'PartyOps'
  target = 'windows-10-11-x64-interactive-wps'
  mode = $Mode
  source_commit = $source
  expected_source_commit = $ExpectedSourceSha.ToLowerInvariant()
  os_caption = $windowsCaption
  os_build = $osBuild
  os_architecture = $osArchitecture
  wps_registered = $wpsRegistered
  allowed_generated_evidence_changes = @($generatedEvidenceChanges)
  production_pymupdf_secured = [bool]$version
  python_secure_pdf_version = [string]$version
  missing_runtime_paths = $missingRuntime
  missing_native_wps_paths = $missingNativeWps
  blockers = @($blocked.ToArray())
  native_wps_passed = $false
  wps_verified_evidence = @()
  native_full_function_passed = $false
  candidate_installer_built = $false
  signed = $false
  native_install_upgrade_rollback_passed = $false
  release_approved = $false
  release_ready = $false
  status = 'BLOCKED'
}
# Native WPS acceptance may proceed before the unrelated global PyMuPDF
# cross-platform security lock is updated. A candidate EXE may NOT.
$testBlockers = @($blocked.ToArray() | Where-Object {
  $_ -ne 'SECURE_PYMUPDF_PRODUCTION_LOCK_REQUIRED'
})
if ($Mode -eq 'NativeTests') {
  # WPS-only validation does not grant package/full approval.
  $testBlockers = @($testBlockers | Where-Object {
    $_ -ne 'STAGED_WINDOWS_BUILD_INPUTS_MISSING' -and $_ -ne 'NODE_COREPACK_MISSING'
  })
  if ($missingNativeWps.Count) { $testBlockers += 'NATIVE_WPS_TEST_INPUTS_MISSING' }
}
if ($Mode -eq 'Preflight') {
  $report | ConvertTo-Json -Depth 8
  if ($blocked.Count) { exit 2 }
  exit 0
}
if (-not $ConfirmDedicatedTestMachine -or -not $ConfirmInteractiveWpsAcceptance) {
  throw '[NATIVE_WPS_CONSENT_REQUIRED] Explicit dedicated-machine and interactive-WPS switches are required.'
}
if ($testBlockers.Count) {
  $report | ConvertTo-Json -Depth 8
  throw '[NATIVE_WPS_ENVIRONMENT_BLOCKED] Native acceptance requires the original WPS test environment and staged inputs.'
}
if ($Mode -eq 'BuildCandidate' -and $blocked.Count) {
  $report | ConvertTo-Json -Depth 8
  throw '[WINDOWS_SECURE_RELEASE_DEPENDENCY_BLOCKED] No EXE may be made from the vulnerable baseline.'
}
$evidenceRoot = Join-Path $root 'artifacts'
$runDirectory = Join-Path $evidenceRoot ('rc1-native-wps-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runDirectory -ErrorAction Stop | Out-Null
$receipt = Join-Path $runDirectory 'native-wps-candidate.json'
function Save-Receipt {
  [IO.File]::WriteAllText(
    $receipt,
    ($report | ConvertTo-Json -Depth 8) + [Environment]::NewLine,
    [Text.UTF8Encoding]::new($false)
  )
}
# Confirm BOTH original x64 and x86 WPS runs wrote fresh evidence
# bound to the relocated host and canonical three-page golden. This invokes
# the EXISTING Windows packaging verifier before any native PASS marker.
function Assert-NativeWpsEvidence {
  param([Parameter(Mandatory = $true)][datetime]$StartedUtc)

  $verified = @()
  foreach ($architecture in @('x64', 'x86')) {
    $runtime = Join-Path $root ('.release-gates\formatter-relocated-' + $architecture)
    $parity = Join-Path $root ('.release-gates\formatter-parity-' + $architecture + '.json')
    $features = Join-Path $root ('.release-gates\formatter-features-e2e-' + $architecture + '.json')
    foreach ($file in @($parity, $features)) {
      if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "[NATIVE_WPS_EVIDENCE_MISSING] $architecture evidence missing: $file"
      }
      if ((Get-Item -LiteralPath $file).LastWriteTimeUtc -lt $StartedUtc.AddSeconds(-2)) {
        throw "[NATIVE_WPS_EVIDENCE_STALE] $architecture evidence not from this run: $file"
      }
    }
    $verifiedPath = Join-Path $runDirectory ('verified-formatter-' + $architecture + '.json')
    $arguments = @(
      '--root', $root,
      '--runtime', $runtime,
      '--platform', 'windows',
      '--architecture', $architecture,
      '--parity-evidence', $parity,
      '--features-evidence', $features,
      '--output', $verifiedPath
    )
    & $Python (Join-Path $root 'scripts/verify-formatter-runtime-evidence.py') @arguments
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $verifiedPath -PathType Leaf)) {
      throw "[NATIVE_WPS_EVIDENCE_VERIFICATION_FAILED] $architecture"
    }
    $result = Get-Content -Raw -LiteralPath $verifiedPath | ConvertFrom-Json
    if ($result.status -ne 'passed' -or $result.platform -ne 'windows' -or
        $result.architecture -ne $architecture -or $result.provider -ne 'wps' -or
        $result.rendered_pages -ne 3 -or $result.feature_cases -ne 10) {
      throw "[NATIVE_WPS_EVIDENCE_CONTRACT_INVALID] $architecture"
    }
    $verified += [ordered]@{
      architecture = $architecture
      host_sha256 = [string]$result.host_sha256
      parity_evidence_sha256 = [string]$result.parity_evidence_sha256
      feature_evidence_sha256 = [string]$result.feature_evidence_sha256
      verified_record_sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $verifiedPath).Hash.ToLowerInvariant()
    }
  }
  $report.wps_verified_evidence = $verified
}

$wpsRunStartedUtc = [DateTime]::UtcNow
try {
  if ($Mode -eq 'NativeTests') {
    # Actual WPS goldens + original six-function E2E only. Never issue a
    # full-function receipt, start PyInstaller, or claim a release-ready EXE.
    & (Join-Path $root 'scripts/test.ps1') -Scope WpsOnly
    if ($LASTEXITCODE -ne 0) { throw '[WINDOWS_ORIGINAL_WPS_TESTS_FAILED]' }
    Assert-NativeWpsEvidence -StartedUtc $wpsRunStartedUtc
    $report.native_wps_passed = $true
    $report.status = 'PASS_NATIVE_WPS_ONLY'
  } else {
    # BuildCandidate requires the complete source/security audit and verified
    # package receipt. Independent website release remains separately gated.
    & (Join-Path $root 'scripts/test.ps1') -Scope Package
    if ($LASTEXITCODE -ne 0) { throw '[WINDOWS_ORIGINAL_FULL_FUNCTION_TESTS_FAILED]' }
    & $Python (Join-Path $root 'scripts/verify-full-function-gate.py') verify --root $root --scope package
    if ($LASTEXITCODE -ne 0) { throw '[WINDOWS_FULL_FUNCTION_RECORD_UNVERIFIED]' }
    Assert-NativeWpsEvidence -StartedUtc $wpsRunStartedUtc
    $report.native_wps_passed = $true
    $report.native_full_function_passed = $true
    $report.status = 'PASS_NATIVE_TESTS_ONLY'
  }
  if ($Mode -eq 'BuildCandidate') {
    # Unmodified original packaging command and ALL its native WPS guards.
    & (Join-Path $root 'packaging/windows/build-windows.ps1') -Python $Python
    if ($LASTEXITCODE -ne 0) { throw '[WINDOWS_ORIGINAL_PACKAGER_FAILED]' }
    $installer = Join-Path $evidenceRoot 'PartyOps_1.4.6-rc.1_windows_amd64.exe'
    if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) {
      throw '[WINDOWS_CANDIDATE_INSTALLER_MISSING]'
    }
    $bytes = [IO.File]::ReadAllBytes($installer)
    if ($bytes.Length -lt 1MB -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) {
      throw '[WINDOWS_CANDIDATE_INSTALLER_NOT_PE]'
    }
    $report.installer_path = $installer
    $report.installer_size_bytes = [int64]$bytes.Length
    $report.installer_sha256 = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
    $report.candidate_installer_built = $true
    $report.status = 'BUILD_ONLY_NEEDS_NATIVE_INSTALL_SIGNATURE_AND_RELEASE'
  }
} catch {
  $report.status = 'BLOCKED'
  $report.error_code = if ($_.Exception.Message -match '^\[[A-Z_]+\]') {
    $Matches[0]
  } else {
    'ORIGINAL_SCRIPT_ERROR'
  }
  Save-Receipt
  throw
}
Save-Receipt
Write-Output ($report | ConvertTo-Json -Depth 8)
