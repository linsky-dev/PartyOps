param(
  [Parameter(Mandatory = $true)][string]$Installer,
  [Parameter(Mandatory = $true)][string]$ExpectedSha256,
  [Parameter(Mandatory = $true)][string]$OutputDirectory,
  [switch]$ConfirmEphemeralHostedRunner
)

# Destructive, REAL setup/teardown on an EMPTY disposable GitHub Windows
# runner ONLY. Reproduces a previous PartyOps uninstall record pointing to
# a detached hard disk. Never invoke this on a user's actual device.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $ConfirmEphemeralHostedRunner -or
    $env:GITHUB_ACTIONS -ne 'true' -or
    $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or
    -not $env:RUNNER_TEMP -or
    [Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT -or
    -not [Environment]::Is64BitOperatingSystem) {
  throw '[DETACHED_DRIVE_QA_EPHEMERAL_WINDOWS_ONLY]'
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
  throw '[DETACHED_DRIVE_QA_ADMIN_REQUIRED]'
}
$exe = (Resolve-Path -LiteralPath $Installer).Path
$hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
if ($hash -ne $ExpectedSha256.ToLowerInvariant() -or
    [IO.Path]::GetFileName($exe) -ne
      'PartyOps_1.4.6-rc.1_windows_amd64_BUILD_ONLY_UNVERIFIED.exe') {
  throw '[DETACHED_DRIVE_QA_WRONG_INSTALLER_OR_SHA]'
}
$qaPath = [IO.Path]::GetFullPath($OutputDirectory)
$qaRoot = [IO.Path]::GetFullPath($env:RUNNER_TEMP).TrimEnd('\') + '\'
if (-not $qaPath.StartsWith($qaRoot,[StringComparison]::OrdinalIgnoreCase)) {
  throw '[DETACHED_DRIVE_QA_REPORT_OUTSIDE_RUNNER_TEMP]'
}
$registryKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{1C8EFC63-CAFC-46EF-A5E3-D3D119B5BB3A}_is1'
$markerDir = Join-Path $env:ProgramData 'PartyOps'
$marker = Join-Path $markerDir 'install-data-dir.txt'
$expectedProgramDir = Join-Path $env:ProgramFiles 'PartyOps'
if (Test-Path -LiteralPath $registryKey) {
  throw '[DETACHED_DRIVE_QA_PREEXISTING_UNINSTALL_KEY]'
}
if (Test-Path -LiteralPath $marker -PathType Leaf) {
  throw '[DETACHED_DRIVE_QA_PREEXISTING_USER_MARKER]'
}
if (Test-Path -LiteralPath $expectedProgramDir) {
  throw '[DETACHED_DRIVE_QA_PREEXISTING_PROGRAM]'
}
foreach ($name in @('PartyOpsHost','PartyOpsUpdateService')) {
  if (Get-Service -Name $name -ErrorAction SilentlyContinue) {
    throw "[DETACHED_DRIVE_QA_PREEXISTING_SERVICE] $name"
  }
}
$missingLetter = $null
foreach ($candidate in @('E','Z','Y','X','W','V','U','T')) {
  $root = $candidate + ':\'
  if ([IO.DriveInfo]::new($root).DriveType -eq [IO.DriveType]::NoRootDirectory) {
    $missingLetter = $candidate
    break
  }
}
if (-not $missingLetter) { throw '[DETACHED_DRIVE_QA_NO_DETACHED_DRIVE_AVAILABLE]' }
$missingRoot = $missingLetter + ':\'
$staleApp = $missingRoot + 'PartyOps1\PartyOps'
$staleData = $missingRoot + 'PartyOps1\user-data'
New-Item -ItemType Directory -Path $qaPath -Force | Out-Null
$report = [ordered]@{
  schema_version = 1
  scope = 'REAL_UNSIGNED_INNO_ON_EMPTY_HOSTED_WINDOWS_DETACHED_DRIVE_TEST'
  candidate_sha256 = $hash
  detached_drive = $missingRoot
  previous_stale_app_directory = $staleApp
  previous_stale_data_directory = $staleData
  actual_installed_program_directory = $null
  actual_written_data_directory = $null
  user_data_recovered_from_missing_disk = $false
  original_disk_modified = $false
  original_uninstall_performed = $false
  no_directory_override = $true
  explicit_missing_data_acknowledgement = $true
  install_passed = $false
  uninstall_passed = $false
  status = 'NOT_RUN'
  error = $null
}
try {
  New-Item -Path $registryKey -Force | Out-Null
  New-ItemProperty -Path $registryKey -Name 'InstallLocation' -PropertyType String -Value $staleApp -Force | Out-Null
  New-Item -ItemType Directory -Path $markerDir -Force | Out-Null
  [IO.File]::WriteAllText($marker,$staleData,[Text.UTF8Encoding]::new($false))
  if ([IO.DriveInfo]::new($missingRoot).DriveType -ne [IO.DriveType]::NoRootDirectory) {
    throw '[DETACHED_DRIVE_QA_MISSING_DISK_REAPPEARED]'
  }
  $log = Join-Path $qaPath 'detached-drive-actual-inno-install.log'
  $arguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',
    '/ALLOWMISSINGDATA=1',('/LOG="' + $log + '"'))
  # Intentionally omit /DIR: Inno must independently ignore the historic E:
  # record in both visible wizard and unattended upgrade/reinstall paths.
  $p = Start-Process -FilePath $exe -ArgumentList $arguments -PassThru
  try {
    if (-not $p.WaitForExit(900000)) {
      try { $p.Kill() } catch {}
      throw '[DETACHED_DRIVE_QA_SETUP_TIMEOUT]'
    }
    $report['setup_exit_code'] = $p.ExitCode
    if ($p.ExitCode -ne 0) { throw "[DETACHED_DRIVE_QA_SETUP_FAILED] Exit $($p.ExitCode)" }
  } finally { $p.Dispose() }
  $installed = Join-Path $expectedProgramDir 'PartyOps.exe'
  if (-not (Test-Path -LiteralPath $installed -PathType Leaf)) {
    throw '[DETACHED_DRIVE_QA_PROGRAM_NOT_ON_SYSTEM_FIXED_DISK]'
  }
  $report['actual_installed_program_directory'] = $expectedProgramDir
  $manifestFile = Join-Path $expectedProgramDir 'release-manifest.json'
  if (-not (Test-Path -LiteralPath $manifestFile -PathType Leaf)) {
    throw '[DETACHED_DRIVE_QA_MANIFEST_MISSING]'
  }
  $manifest = Get-Content -Raw -LiteralPath $manifestFile | ConvertFrom-Json
  $entry = @($manifest.files | Where-Object { $_.path -eq 'PartyOps.exe' })
  if ($manifest.version -ne '1.4.6-rc.1' -or $entry.Count -ne 1 -or
      (Get-FileHash -Algorithm SHA256 -LiteralPath $installed).Hash.ToLowerInvariant() -ne
        $entry[0].sha256.ToLowerInvariant()) {
    throw '[DETACHED_DRIVE_QA_PROGRAM_PROVENANCE_MISMATCH]'
  }
  $newData = [IO.File]::ReadAllText($marker).Trim()
  if ($newData -eq $staleData -or
      -not $newData.StartsWith((Join-Path $env:ProgramData 'PartyOps-Data'),[StringComparison]::OrdinalIgnoreCase)) {
    throw '[DETACHED_DRIVE_QA_STALE_BUSINESS_DATA_PATH_RETAINED]'
  }
  if ([IO.DriveInfo]::new([IO.Path]::GetPathRoot($newData)).DriveType -ne [IO.DriveType]::Fixed) {
    throw '[DETACHED_DRIVE_QA_REPLACEMENT_DATA_NOT_FIXED]'
  }
  $report['actual_written_data_directory'] = $newData
  if (Test-Path -LiteralPath $missingRoot) {
    throw '[DETACHED_DRIVE_QA_DETACHED_VOLUME_CREATED_BY_INSTALLER]'
  }
  $report['install_passed'] = $true
  $uninstaller = Join-Path $expectedProgramDir 'unins000.exe'
  if (-not (Test-Path -LiteralPath $uninstaller -PathType Leaf)) {
    throw '[DETACHED_DRIVE_QA_UNINSTALLER_MISSING]'
  }
  $q = Start-Process -FilePath $uninstaller -ArgumentList @(
    '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART'
  ) -PassThru
  try {
    if (-not $q.WaitForExit(900000)) {
      try { $q.Kill() } catch {}
      throw '[DETACHED_DRIVE_QA_UNINSTALL_TIMEOUT]'
    }
    $report['uninstall_exit_code'] = $q.ExitCode
    if ($q.ExitCode -ne 0 -or (Test-Path -LiteralPath $installed)) {
      throw '[DETACHED_DRIVE_QA_UNINSTALL_FAILED]'
    }
  } finally { $q.Dispose() }
  $report['uninstall_passed'] = $true
  $report['status'] = 'PASS_DETACHED_DISK_FALLBACK_REAL_INSTALL_UNINSTALL'
} catch {
  $report['status'] = 'FAILED'
  $report['error'] = [string]$_.Exception.Message
} finally {
  $report['checked_utc'] = [DateTime]::UtcNow.ToString('o')
  [IO.File]::WriteAllText((Join-Path $qaPath 'detached-drive-evidence.json'),
    ($report | ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
  Write-Host "DETACHED_DRIVE_QA_STATUS=$($report.status)"
  if ($report.status -eq 'FAILED') {
    $log = Join-Path $qaPath 'detached-drive-actual-inno-install.log'
    if (Test-Path -LiteralPath $log) {
      Get-Content -LiteralPath $log -Tail 65 | Write-Host
    }
  }
}
if ($report.status -ne 'PASS_DETACHED_DISK_FALLBACK_REAL_INSTALL_UNINSTALL') {
  throw "[DETACHED_DRIVE_QA_FAILED] $($report.error)"
}
