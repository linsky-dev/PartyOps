param(
  [Parameter(Mandatory = $true)][string]$Installer,
  [Parameter(Mandatory = $true)][string]$ExpectedSha256,
  [Parameter(Mandatory = $true)][string]$OutputDirectory,
  [switch]$ConfirmEphemeralHostedRunner
)

# A real EXE installation, *not* real user-WPS or Win10/11 client acceptance.
# Never allow this destructive installer/uninstaller QA on user production PCs.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $ConfirmEphemeralHostedRunner -or
    $env:GITHUB_ACTIONS -ne 'true' -or
    $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or
    -not $env:RUNNER_TEMP -or
    -not [Environment]::Is64BitOperatingSystem -or
    [Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
  throw '[RC1_QA_EPHEMERAL_HOSTED_RUNNER_REQUIRED]'
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
  throw '[RC1_QA_ADMINISTRATOR_REQUIRED]'
}
if ([IO.DriveInfo]::new('C:\').DriveType -ne [IO.DriveType]::Fixed) {
  throw '[RC1_QA_SYSTEM_DRIVE_NOT_FIXED]'
}
$installerPath = (Resolve-Path -LiteralPath $Installer -ErrorAction Stop).Path
if ((Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne
    $ExpectedSha256.ToLowerInvariant()) {
  throw '[RC1_QA_INSTALLER_SOURCE_SHA_MISMATCH]'
}
if ([IO.Path]::GetFileName($installerPath) -ne
    'PartyOps_1.4.6-rc.1_windows_amd64_BUILD_ONLY_UNVERIFIED.exe') {
  throw '[RC1_QA_UNEXPECTED_INSTALLER_FILENAME]'
}
$signature = Get-AuthenticodeSignature -LiteralPath $installerPath
if ($signature.Status -ne 'NotSigned') {
  throw '[RC1_QA_CANDIDATE_SIGNATURE_STATUS_CHANGED] Unsigned test installer was expected.'
}
$qaId = [guid]::NewGuid().ToString('N')
$qaRoot = "C:\PartyOps-RC1-Smoke-$qaId"
$program = Join-Path $qaRoot 'Program Files 中文 空格'
$logs = [IO.Path]::GetFullPath($OutputDirectory)
$runTemp = [IO.Path]::GetFullPath($env:RUNNER_TEMP).TrimEnd('\') + '\'
if (-not $logs.StartsWith($runTemp, [StringComparison]::OrdinalIgnoreCase)) {
  throw '[RC1_QA_OUTPUT_NOT_IN_RUNNER_TEMP]'
}
if (Test-Path -LiteralPath $qaRoot) {
  throw '[RC1_QA_ALREADY_EXISTS]'
}
# A service-host install must not touch an existing machine's PartyOps service,
# database or previous app. The hosted image is discarded after the run.
foreach ($name in @('PartyOpsHost','PartyOpsUpdateService')) {
  if (Get-Service -Name $name -ErrorAction SilentlyContinue) {
    throw "[RC1_QA_UNEXPECTED_EXISTING_SERVICE] $name"
  }
}
$report = [ordered]@{
  schema_version = 1
  scope = 'WINDOWS_SERVER_HOSTED_ACTUAL_EXE_INSTALL_AND_UNINSTALL'
  original_installer_sha256 = $ExpectedSha256.ToLowerInvariant()
  output_source = 'same-run-exact-installer-sha256-verified-by-workflow'
  runner_os = [Environment]::OSVersion.VersionString
  run_id = $env:GITHUB_RUN_ID
  signed_release = $false
  current_version_real_wps_accepted = $false
  windows_10_11_client_accepted = $false
  native_upgrade_rollback_accepted = $false
  status = 'NOT_RUN'
  installed_exe_sha256 = $null
  installed_manifest_version = $null
  uninstall_passed = $false
  error = $null
}
New-Item -ItemType Directory -Path $logs -Force | Out-Null
$installLog = Join-Path $logs 'inno-actual-install.log'
$uninstallLog = Join-Path $logs 'inno-actual-uninstall.log'
function Invoke-QAInstaller {
  param([string]$FilePath, [string[]]$Arguments, [string]$Stage)
  # Start-Process -Wait waits for every descendant process; a normally
  # launched PartyOps background service can keep it waiting indefinitely.
  $process = Start-Process -FilePath $FilePath -ArgumentList $Arguments -PassThru
  try {
    if (-not $process.WaitForExit(900000)) {
      try { $process.Kill() } catch {}
      throw "[RC1_QA_INSTALLER_TIMEOUT] $Stage process did not finish within 15 minutes."
    }
    return $process.ExitCode
  } finally {
    $process.Dispose()
  }
}
try {
  $args = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',
    ('/DIR="' + $program + '"'),('/LOG="' + $installLog + '"'))
  $installExit = Invoke-QAInstaller -FilePath $installerPath -Arguments $args -Stage 'INSTALL'
  $report['install_exit_code'] = $installExit
  if ($installExit -ne 0) { throw "[RC1_QA_INSTALL_FAILED] Exit $installExit" }
  $exePath = Join-Path $program 'PartyOps.exe'
  $manifestPath = Join-Path $program 'release-manifest.json'
  $formatterHistory = Join-Path $program 'formatter-host/historical-wps-reference.json'
  foreach ($file in @($exePath,$manifestPath,$formatterHistory)) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
      throw "[RC1_QA_INSTALLED_FILE_MISSING] $([IO.Path]::GetFileName($file))"
    }
  }
  $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
  if ($manifest.version -ne '1.4.6-rc.1' -or
      $manifest.architecture -ne 'amd64' -or $manifest.platform -ne 'windows') {
    throw '[RC1_QA_INSTALLED_VERSION_ARCH_INVALID]'
  }
  $entry = @($manifest.files | Where-Object { $_.path -eq 'PartyOps.exe' })
  if ($entry.Count -ne 1) { throw '[RC1_QA_MAIN_BINARY_NOT_IN_MANIFEST]' }
  $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $exePath).Hash.ToLowerInvariant()
  if ($actual -ne $entry[0].sha256.ToLowerInvariant()) {
    throw '[RC1_QA_INSTALLED_BINARY_HASH_MISMATCH]'
  }
  $historical = Get-Content -Raw -LiteralPath $formatterHistory | ConvertFrom-Json
  if ($historical.status -ne 'HISTORICAL_WPS_REFERENCE_ONLY' -or
      $historical.current_version_native_wps_tested -or $historical.release_ready) {
    throw '[RC1_QA_INSTALLED_FAKE_WPS_ACCEPTANCE]'
  }
  $report['installed_exe_sha256'] = $actual
  $report['installed_manifest_version'] = $manifest.version
  $report['install_verified'] = $true
  # Running the actual installed desktop launcher catches missing
  # release-manifest.json and dependency mismatch that Inno file copy alone
  # does not detect. First-run background mode does not change user config.
  $launcher = Join-Path $program 'PartyOpsLauncher.exe'
  if (-not (Test-Path -LiteralPath $launcher -PathType Leaf)) {
    throw '[RC1_QA_INSTALLED_LAUNCHER_MISSING]'
  }
  $launcherProcess = Start-Process -FilePath $launcher -ArgumentList '--background' -PassThru
  try {
    if (-not $launcherProcess.WaitForExit(180000)) {
      try { $launcherProcess.Kill() } catch {}
      throw '[RC1_QA_LAUNCHER_RUNTIME_PREFLIGHT_TIMEOUT]'
    }
    $report['launcher_preflight_exit_code'] = $launcherProcess.ExitCode
    if ($launcherProcess.ExitCode -ne 0) {
      throw "[RC1_QA_LAUNCHER_RUNTIME_PREFLIGHT_FAILED] Exit $($launcherProcess.ExitCode)"
    }
    $report['launcher_release_manifest_preflight_passed'] = $true
  } finally { $launcherProcess.Dispose() }
  $uninstaller = Join-Path $program 'unins000.exe'
  if (-not (Test-Path -LiteralPath $uninstaller -PathType Leaf)) {
    throw '[RC1_QA_UNINSTALLER_MISSING]'
  }
  $uninstallArgs = @(
    '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + $uninstallLog + '"')
  )
  $uninstallExit = Invoke-QAInstaller -FilePath $uninstaller -Arguments $uninstallArgs -Stage 'UNINSTALL'
  $report['uninstall_exit_code'] = $uninstallExit
  if ($uninstallExit -ne 0 -or (Test-Path -LiteralPath $exePath)) {
    throw '[RC1_QA_UNINSTALL_FAILED_OR_BINARY_REMAINS]'
  }
  $report['uninstall_passed'] = $true
  $report['status'] = 'PASS_WINDOWS_SERVER_HOSTED_INSTALL_UNINSTALL_ONLY'
} catch {
  $report['status'] = 'FAILED'
  $report['error'] = [string]$_.Exception.Message
} finally {
  $report['verified_at_utc'] = [DateTime]::UtcNow.ToString('o')
  $reportPath = Join-Path $logs 'hosted-install-evidence.json'
  [IO.File]::WriteAllText($reportPath,
    ($report | ConvertTo-Json -Depth 8),
    (New-Object Text.UTF8Encoding($false)))
  Write-Host "QA_EVIDENCE=$reportPath"
  Write-Host "QA_STATUS=$($report.status)"
  if ($report.status -eq 'FAILED' -and (Test-Path -LiteralPath $installLog)) {
    Get-Content -LiteralPath $installLog -Tail 80 -ErrorAction SilentlyContinue | Write-Host
  }
}
if ($report.status -ne 'PASS_WINDOWS_SERVER_HOSTED_INSTALL_UNINSTALL_ONLY') {
  throw "[RC1_QA_INSTALLED_SMOKE_FAILED] $($report.error)"
}
