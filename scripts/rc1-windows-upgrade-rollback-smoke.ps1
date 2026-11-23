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
  param([string]$Path, [string]$LogPath, [string]$Stage,
    [switch]$UsePreviousAppDir)
  $args = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',
    ('/LOG="' + $LogPath + '"'))
  # Only the FIRST RC6 install sets a custom /DIR. Subsequent installers
  # must use previous Inno AppId for genuine same-directory upgrades.
  if (-not $UsePreviousAppDir) {
    $args += ('/DIR="' + $program + '"')
  }
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
function Test-ExistingBusinessDataCanaries {
  param([string]$Stage)
  $afterDataDir = [IO.File]::ReadAllText($dataMarkerPath).Trim()
  if ([string]::Compare($afterDataDir,$originalDataDir,$true) -ne 0) {
    throw "[RC1_UPGRADE_QA_DATA_DIRECTORY_MOVED] $Stage"
  }
  if ((Get-FileHash -LiteralPath $attachment -Algorithm SHA256).Hash.ToLowerInvariant() -ne
      $attachmentSha) {
    throw "[RC1_UPGRADE_QA_ATTACHMENT_CHANGED] $Stage"
  }
  if ((Get-FileHash -LiteralPath $sqliteCanary -Algorithm SHA256).Hash.ToLowerInvariant() -ne
      $sqliteSha) {
    throw "[RC1_UPGRADE_QA_SYNTHETIC_SQLITE_CHANGED] $Stage"
  }
  & python -c 'import sqlite3,sys; c=sqlite3.connect("file:"+sys.argv[1]+"?mode=ro",uri=True); assert c.execute("pragma quick_check").fetchone()[0]=="ok"; assert c.execute("select value from qa_probe").fetchone()[0]==sys.argv[2]; c.close()' $sqliteCanary $qaId
  if ($LASTEXITCODE -ne 0) {
    throw "[RC1_UPGRADE_QA_SQLITE_CONTENT_OR_INTEGRITY_FAILED] $Stage"
  }
}

$report = [ordered]@{
  schema_version = 1
  scope = 'HOSTED_WINDOWS_SERVER_RC6_TO_RC1_TO_RC6_BINARY_ROLLBACK_SYNTHETIC_ONLY'
  status = 'NOT_RUN'
  old_installer_sha256 = $OldSha256.ToLowerInvariant()
  new_installer_sha256 = $NewSha256.ToLowerInvariant()
  windows_10_11_real_client = $false
  native_wps_accepted = $false
  rc1_in_place_without_dir_override = $false
  rc6_rollback_without_dir_override = $false
  production_code_signing_accepted = $false
  database_schema_rollback_verified = $false
  synthetic_marker_preserved = $false
  previous_data_directory_preserved = $false
  synthetic_sqlite_and_attachment_preserved = $false
  real_partyops_schema_migrated = $false
  error = $null
}
$marker = $null
try {
  # The actual signed RC6 historical installer must install first.
  Invoke-Setup -Path $old -LogPath (Join-Path $logs 'rc6-initial.log') -Stage 'RC6_INITIAL'
  $oldExe = Read-Installed '1.4.5-rc.6'
  $report['rc6_exe_before_sha256'] = $oldExe
  # Real user scenario: the RC6 installation already chose its Business
  # Data location; RC1 must keep it, all attachment bytes and the existing
  # SQLite files in-place. This CANARY SQLite is not the PartyOps schema and
  # does not claim a 0026->0028 application database migration.
  $dataMarkerPath = Join-Path $env:ProgramData 'PartyOps/install-data-dir.txt'
  if (-not (Test-Path -LiteralPath $dataMarkerPath -PathType Leaf)) {
    throw '[RC1_UPGRADE_QA_RC6_DATA_MARKER_NOT_FOUND]'
  }
  $originalDataDir = [IO.File]::ReadAllText($dataMarkerPath).Trim()
  $expectedDataDir = Join-Path $env:ProgramData 'PartyOps-Data'
  if ([string]::Compare(
    [IO.Path]::GetFullPath($originalDataDir),
    [IO.Path]::GetFullPath($expectedDataDir), $true) -ne 0) {
    throw '[RC1_UPGRADE_QA_RC6_UNEXPECTED_BUSINESS_DATA_DIRECTORY]'
  }
  $attachmentDir = Join-Path $originalDataDir 'attachments'
  New-Item -ItemType Directory -Path $attachmentDir -Force | Out-Null
  $attachment = Join-Path $attachmentDir "qa-preserve-$qaId.txt"
  $sqliteCanary = Join-Path $originalDataDir "qa-preserve-$qaId.sqlite"
  [IO.File]::WriteAllBytes($attachment,
    [Text.Encoding]::UTF8.GetBytes("existing-user-attachment-$qaId"))
  $attachmentSha = (Get-FileHash -LiteralPath $attachment -Algorithm SHA256).Hash.ToLowerInvariant()
  & python -c 'import sqlite3,sys; c=sqlite3.connect(sys.argv[1]); c.execute("create table qa_probe (value text not null)"); c.execute("insert into qa_probe values (?)",(sys.argv[2],)); c.commit(); c.close()' $sqliteCanary $qaId
  if ($LASTEXITCODE -ne 0) { throw '[RC1_UPGRADE_QA_CREATE_SYNTHETIC_SQLITE_FAILED]' }
  $sqliteSha = (Get-FileHash -LiteralPath $sqliteCanary -Algorithm SHA256).Hash.ToLowerInvariant()
  Test-ExistingBusinessDataCanaries -Stage 'RC6_BASELINE'
  $report['existing_rc6_data_dir'] = $originalDataDir
  $report['existing_attachment_sha256'] = $attachmentSha
  $report['existing_synthetic_sqlite_sha256'] = $sqliteSha
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
  Invoke-Setup -Path $new -LogPath (Join-Path $logs 'rc1-upgrade.log') -Stage 'RC1_UPGRADE' -UsePreviousAppDir
  $newExe = Read-Installed '1.4.6-rc.1'
  if ($newExe -eq $oldExe -or -not (Test-Path -LiteralPath $marker -PathType Leaf) -or
      (Get-FileHash -LiteralPath $marker -Algorithm SHA256).Hash.ToLowerInvariant() -ne $markerSha) {
    throw '[RC1_UPGRADE_QA_UPGRADE_NOT_REPLACED_OR_MARKER_LOST]'
  }
  Test-ExistingBusinessDataCanaries -Stage 'RC1_UPGRADE'
  $report['rc1_exe_after_upgrade_sha256'] = $newExe
  $report['upgrade_passed'] = $true
  $report['rc1_in_place_without_dir_override'] = $true
  # Only program-binary rollback on a throwaway synthetic-data host.
  # No claim that a migrated production SQLite DB can be downgraded.
  Invoke-Setup -Path $old -LogPath (Join-Path $logs 'rc6-rollback.log') -Stage 'RC6_ROLLBACK' -UsePreviousAppDir
  $back = Read-Installed '1.4.5-rc.6'
  if ($back -ne $oldExe -or -not (Test-Path -LiteralPath $marker -PathType Leaf) -or
      (Get-FileHash -LiteralPath $marker -Algorithm SHA256).Hash.ToLowerInvariant() -ne $markerSha) {
    throw '[RC1_UPGRADE_QA_ROLLBACK_BINARY_OR_MARKER_CHANGED]'
  }
  Test-ExistingBusinessDataCanaries -Stage 'RC6_BINARY_ROLLBACK'
  $report['rc6_exe_after_rollback_sha256'] = $back
  $report['previous_data_directory_preserved'] = $true
  $report['synthetic_sqlite_and_attachment_preserved'] = $true
  $report['synthetic_marker_preserved'] = $true
  $report['rc6_rollback_without_dir_override'] = $true
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
