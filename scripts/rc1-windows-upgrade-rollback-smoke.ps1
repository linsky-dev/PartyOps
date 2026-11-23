param(
  [Parameter(Mandatory = $true)][string]$OldInstaller,
  [Parameter(Mandatory = $true)][string]$OldSha256,
  [Parameter(Mandatory = $true)][string]$NewInstaller,
  [Parameter(Mandatory = $true)][string]$NewSha256,
  [Parameter(Mandatory = $true)][string]$OutputDirectory,
  [switch]$ConfirmEphemeralHostedRunner
)

# Disposable HOSTED Windows Server program upgrade / rollback with synthetic
# file-retention evidence. It is NOT proof of DB downgrade safety or native WPS.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $ConfirmEphemeralHostedRunner -or $env:GITHUB_ACTIONS -ne 'true' -or
    $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or
    -not [Environment]::Is64BitOperatingSystem -or
    [Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT -or
    -not $env:RUNNER_TEMP) {
  throw '[RC1_UPGRADE_QA_DISPOSABLE_HOSTED_ONLY]'
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) -or
    [IO.DriveInfo]::new('C:\').DriveType -ne [IO.DriveType]::Fixed) {
  throw '[RC1_UPGRADE_QA_ADMIN_FIXED_SYSTEM_DISK_REQUIRED]'
}
$old = (Resolve-Path -LiteralPath $OldInstaller).Path
$new = (Resolve-Path -LiteralPath $NewInstaller).Path
if ((Get-FileHash -LiteralPath $old -Algorithm SHA256).Hash.ToLowerInvariant() -ne $OldSha256.ToLowerInvariant() -or
    (Get-FileHash -LiteralPath $new -Algorithm SHA256).Hash.ToLowerInvariant() -ne $NewSha256.ToLowerInvariant()) {
  throw '[RC1_UPGRADE_QA_INSTALLER_SHA_INVALID]'
}
if ([IO.Path]::GetFileName($new) -ne 'PartyOps_1.4.6-rc.1_windows_amd64_BUILD_ONLY_UNVERIFIED.exe' -or
    [IO.Path]::GetFileName($old) -ne 'PartyOps_1.4.5-rc.6_windows_amd64_startup-20261018.exe') {
  throw '[RC1_UPGRADE_QA_INSTALLER_IDENTITY_INVALID]'
}
$qaId = [guid]::NewGuid().ToString('N')
$program = "C:\PartyOps-RC1-Upgrade-$qaId\Program"
$logs = [IO.Path]::GetFullPath($OutputDirectory)
$tempRoot = [IO.Path]::GetFullPath($env:RUNNER_TEMP).TrimEnd('\') + '\'
if (-not $logs.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
    (Test-Path -LiteralPath (Split-Path -Parent $program))) {
  throw '[RC1_UPGRADE_QA_DIRECTORY_UNSAFE]'
}
foreach ($name in @('PartyOpsHost','PartyOpsUpdateService')) {
  if (Get-Service -Name $name -ErrorAction SilentlyContinue) {
    throw "[RC1_UPGRADE_QA_PREEXISTING_SERVICE] $name"
  }
}
New-Item -ItemType Directory -Path $logs -Force | Out-Null
function Invoke-Setup {
  param([string]$Path, [string]$LogPath, [string]$Stage)
  $args = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',
    ('/DIR="' + $program + '"'),('/LOG="' + $LogPath + '"'))
  $p = Start-Process -FilePath $Path -ArgumentList $args -PassThru
  try {
    if (-not $p.WaitForExit(900000)) {
      try { $p.Kill() } catch {}
      throw "[RC1_UPGRADE_QA_TIMEOUT] $Stage installer exceeded 15 min"
    }
    if ($p.ExitCode -ne 0) {
      throw "[RC1_UPGRADE_QA_INSTALL_FAILED] $Stage exited $($p.ExitCode)"
    }
  } finally { $p.Dispose() }
}
function Read-Installed {
  param([string]$Version)
  $manifestPath = Join-Path $program 'release-manifest.json'
  $exePath = Join-Path $program 'PartyOps.exe'
  if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf) -or
      -not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
    throw "[RC1_UPGRADE_QA_FILES_MISSING] $Version"
  }
  $m = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
  if ($m.version -ne $Version -or $m.architecture -ne 'amd64') {
    throw "[RC1_UPGRADE_QA_VERSION_MISMATCH] Expected $Version, got $($m.version)"
  }
  $entry = @($m.files | Where-Object { $_.path -eq 'PartyOps.exe' })
  if ($entry.Count -ne 1) { throw '[RC1_UPGRADE_QA_MAIN_EXE_MANIFEST_MISSING]' }
  $hash = (Get-FileHash -LiteralPath $exePath -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($hash -ne $entry[0].sha256.ToLowerInvariant()) {
    throw '[RC1_UPGRADE_QA_PROGRAM_HASH_CHANGED]'
  }
  return $hash
}
$report = [ordered]@{
  schema_version = 1
  scope = 'HOSTED_WINDOWS_SERVER_RC6_TO_RC1_TO_RC6_BINARY_ROLLBACK_SYNTHETIC_ONLY'
  status = 'NOT_RUN'
  old_installer_sha256 = $OldSha256.ToLowerInvariant()
  new_installer_sha256 = $NewSha256.ToLowerInvariant()
  windows_10_11_real_client = $false
  native_wps_accepted = $false
  production_code_signing_accepted = $false
  database_schema_rollback_verified = $false
  synthetic_marker_preserved = $false
  error = $null
}
$marker = $null
try {
  # The actual signed RC6 historical installer must install first.
  Invoke-Setup -Path $old -LogPath (Join-Path $logs 'rc6-initial.log') -Stage 'RC6_INITIAL'
  $oldExe = Read-Installed '1.4.5-rc.6'
  $report['rc6_exe_before_sha256'] = $oldExe
  # A synthetic *data* marker in ProgramData, not an injected program DLL.
  $dataRoot = Join-Path $env:ProgramData 'PartyOps'
  if (-not (Test-Path -LiteralPath $dataRoot -PathType Container)) {
    New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null
  }
  $marker = Join-Path $dataRoot ("ci-program-upgrade-only-$qaId.txt")
  $markerContent = [Text.Encoding]::UTF8.GetBytes("PartyOps synthetic QA file retention $qaId")
  [IO.File]::WriteAllBytes($marker,$markerContent)
  $markerSha = (Get-FileHash -LiteralPath $marker -Algorithm SHA256).Hash.ToLowerInvariant()
  $report['synthetic_marker_sha256'] = $markerSha
  Invoke-Setup -Path $new -LogPath (Join-Path $logs 'rc1-upgrade.log') -Stage 'RC1_UPGRADE'
  $newExe = Read-Installed '1.4.6-rc.1'
  if ($newExe -eq $oldExe -or -not (Test-Path -LiteralPath $marker -PathType Leaf) -or
      (Get-FileHash -LiteralPath $marker -Algorithm SHA256).Hash.ToLowerInvariant() -ne $markerSha) {
    throw '[RC1_UPGRADE_QA_UPGRADE_NOT_REPLACED_OR_MARKER_LOST]'
  }
  $report['rc1_exe_after_upgrade_sha256'] = $newExe
  $report['upgrade_passed'] = $true
  # Only program-binary rollback on a throwaway synthetic-data host.
  # No claim that a migrated production SQLite DB can be downgraded.
  Invoke-Setup -Path $old -LogPath (Join-Path $logs 'rc6-rollback.log') -Stage 'RC6_ROLLBACK'
  $back = Read-Installed '1.4.5-rc.6'
  if ($back -ne $oldExe -or -not (Test-Path -LiteralPath $marker -PathType Leaf) -or
      (Get-FileHash -LiteralPath $marker -Algorithm SHA256).Hash.ToLowerInvariant() -ne $markerSha) {
    throw '[RC1_UPGRADE_QA_ROLLBACK_BINARY_OR_MARKER_CHANGED]'
  }
  $report['rc6_exe_after_rollback_sha256'] = $back
  $report['synthetic_marker_preserved'] = $true
  $report['status'] = 'PASS_HOSTED_PROGRAM_UPGRADE_AND_ROLLBACK_ONLY'
} catch {
  $report['status'] = 'FAILED'
  $report['error'] = [string]$_.Exception.Message
} finally {
  $report['verified_at_utc'] = [DateTime]::UtcNow.ToString('o')
  [IO.File]::WriteAllText((Join-Path $logs 'hosted-upgrade-rollback-evidence.json'),
    ($report | ConvertTo-Json -Depth 8),
    [Text.UTF8Encoding]::new($false))
  Write-Host "RC1_QA_UPGRADE_ROLLBACK_STATUS=$($report.status)"
  if ($report.status -eq 'FAILED') {
    foreach ($name in @('rc6-initial.log','rc1-upgrade.log','rc6-rollback.log')) {
      $logPath = Join-Path $logs $name
      if (Test-Path -LiteralPath $logPath) {
        Write-Host "LAST_LOG_LINES=$name"
        Get-Content -LiteralPath $logPath -Tail 35 | Write-Host
      }
    }
  }
}
if ($report.status -ne 'PASS_HOSTED_PROGRAM_UPGRADE_AND_ROLLBACK_ONLY') {
  throw "[RC1_UPGRADE_QA_FAILED] $($report.error)"
}
