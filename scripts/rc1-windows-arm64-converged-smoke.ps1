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
    'PartyOps_1.4.6-rc.1_windows11_arm64_BUILD_ONLY_UNVERIFIED.exe') {
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
  scope = 'WINDOWS11_ARM64_HOSTED_ACTUAL_EXE_INSTALL_AND_UNINSTALL'
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
  $installTimer = [Diagnostics.Stopwatch]::StartNew()
  try {
    $installExit = Invoke-QAInstaller -FilePath $installerPath -Arguments $args -Stage 'INSTALL'
  } finally {
    $installTimer.Stop()
    $report['install_elapsed_seconds'] = [Math]::Round($installTimer.Elapsed.TotalSeconds, 2)
  }
  $report['install_exit_code'] = $installExit
  if ($installExit -ne 0) { throw "[RC1_QA_INSTALL_FAILED] Exit $installExit" }
  $exePath = Join-Path $program 'PartyOps.exe'
  $manifestPath = Join-Path $program 'release-manifest.json'
  $formatterHistory = Join-Path $program 'formatter-host/source-host.json'
  foreach ($file in @($exePath,$manifestPath,$formatterHistory)) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
      throw "[RC1_QA_INSTALLED_FILE_MISSING] $([IO.Path]::GetFileName($file))"
    }
  }
  $report['installed_manifest_size_bytes'] = (Get-Item -LiteralPath $manifestPath).Length
  $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
  if ($manifest.version -ne '1.4.6-rc.1' -or
      $manifest.architecture -ne 'arm64' -or $manifest.platform -ne 'windows' -or
      $manifest.source_commit -ne '740251bc84d7209a5effe24e6c6711274560fc76' -or
      $manifest.runtime_profile -ne 'arm64-qa') {
    throw '[RC1_QA_INSTALLED_VERSION_ARCH_INVALID]'
  }
  $entry = @($manifest.files | Where-Object { $_.path -eq 'PartyOps.exe' })
  if ($entry.Count -ne 1) { throw '[RC1_QA_MAIN_BINARY_NOT_IN_MANIFEST]' }
  $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $exePath).Hash.ToLowerInvariant()
  if ($actual -ne $entry[0].sha256.ToLowerInvariant()) {
    throw '[RC1_QA_INSTALLED_BINARY_HASH_MISMATCH]'
  }
  $formatterExe = Join-Path $program 'formatter-host/PartyOps.DocumentFormatter.Host.exe'
  if (-not (Test-Path -LiteralPath $formatterExe -PathType Leaf)) {
    throw '[RC1_ARM64_FORMATTER_HOST_MISSING]'
  }
  $formatterRecord = Get-Content -Raw -LiteralPath $formatterHistory | ConvertFrom-Json
  if ($formatterRecord.host_sha256 -ne (Get-FileHash $formatterExe).Hash.ToLowerInvariant()) {
    throw '[RC1_ARM64_FORMATTER_HOST_PROVENANCE_MISMATCH]'
  }
  # Check native ARM64 PE machine on all nine main application entrypoints via
  # manifest and installed binary. Do not treat .NET AnyCPU as native PE.
  $nativeCount = 0
  foreach ($row in $manifest.files) {
    if ($row.path -notmatch '^PartyOps[^/\\]*\.exe$') { continue }
    $p=Join-Path $program $row.path
    if ((Get-FileHash -LiteralPath $p).Hash.ToLowerInvariant() -ne $row.sha256) { throw "ARM64_INSTALLED_HASH_MISMATCH $($row.path)" }
    $stream=[IO.File]::OpenRead($p)
    try {
      $reader=[IO.BinaryReader]::new($stream)
      if ($reader.ReadUInt16() -ne 0x5A4D) { throw 'INVALID_PE' }
      $stream.Position=60; $offset=$reader.ReadInt32()
      if ($offset -lt 64 -or $offset -gt $stream.Length-6) { throw 'INVALID_PE_OFFSET' }
      $stream.Position=$offset
      if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0xAA64) { throw "ARM64_INSTALLED_NON_NATIVE $($row.path)" }
    } finally { $stream.Dispose() }
    $nativeCount++
  }
  if ($nativeCount -lt 9) { throw "ARM64_INSTALLED_NATIVE_ENTRYPOINTS_INCOMPLETE $nativeCount" }
  $report['native_entrypoints_verified']=$nativeCount
  $nativeInfo = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
  if ($nativeInfo -ne 'Arm64') { throw 'ARM64_HOST_REQUIRED' }
  $report['host_architecture']=$nativeInfo
  $startup=Start-Process -FilePath $exePath -ArgumentList '--startup-self-test' -RedirectStandardOutput (Join-Path $logs 'startup-selftest.txt') -RedirectStandardError (Join-Path $logs 'startup-selftest-error.txt') -PassThru
  try {
    if (-not $startup.WaitForExit(180000)) { $startup.Kill(); throw 'ARM64_INSTALLED_SELFTEST_TIMEOUT' }
    if ($startup.ExitCode -ne 0) { throw "ARM64_INSTALLED_SELFTEST_FAILED $($startup.ExitCode)" }
    $report['installed_startup_selftest_passed']=$true
  } finally { $startup.Dispose() }
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
  $launcherTimer = [Diagnostics.Stopwatch]::StartNew()
  $launcherProcess = Start-Process -FilePath $launcher -ArgumentList '--background' -PassThru
  try {
    if (-not $launcherProcess.WaitForExit(180000)) {
      try { $launcherProcess.Kill() } catch {}
      throw '[RC1_QA_LAUNCHER_RUNTIME_PREFLIGHT_TIMEOUT]'
    }
    $report['launcher_preflight_exit_code'] = $launcherProcess.ExitCode
    if ($launcherProcess.ExitCode -ne 0) {
      # Fail closed, but preserve a small, redacted reason rather than only an
      # opaque GUI process exit code. This runs on an ephemeral hosted runner.
      # Never upload arbitrary full user logs or configuration files.
      $diagnosticFiles = @(
        (Join-Path $env:LOCALAPPDATA 'PartyOps\launcher.log'),
        (Join-Path $env:TEMP 'PartyOps-launcher-emergency.log')
      )
      $diagnosticCodes = @()
      $diagnosticDetails = @()
      foreach ($diagnosticFile in $diagnosticFiles) {
        if (-not (Test-Path -LiteralPath $diagnosticFile -PathType Leaf)) { continue }
        $recentLines = @(Get-Content -LiteralPath $diagnosticFile -Tail 100 -ErrorAction SilentlyContinue)
        $diagnosticCodes += @(
          [regex]::Matches(($recentLines -join "`n"), '(?:RUNTIME|LAUNCHER|PERSONAL|HOST)_[A-Z0-9_]+') |
            ForEach-Object { $_.Value }
        )
        $diagnosticDetails += @(
          $recentLines |
            Where-Object { $_ -match '缺失=|哈希异常=|清单检查错误类型=|LAUNCHER_UNHANDLED_ERROR' } |
            Select-Object -Last 6 |
            ForEach-Object {
              $safeLine = $_.Replace($env:USERPROFILE, '<USERPROFILE>')
              $safeLine.Substring(0, [Math]::Min($safeLine.Length, 400))
            }
        )
      }
      $report['launcher_diagnostic_codes'] = @($diagnosticCodes | Select-Object -Unique)
      $report['launcher_diagnostic_details'] = @($diagnosticDetails | Select-Object -Last 12)
      Write-Host "RC1_QA_LAUNCHER_CODES=$($report['launcher_diagnostic_codes'] -join ',')"
      foreach ($detail in $report['launcher_diagnostic_details']) {
        Write-Host "RC1_QA_LAUNCHER_DETAIL=$detail"
      }
      throw "[RC1_QA_LAUNCHER_RUNTIME_PREFLIGHT_FAILED] Exit $($launcherProcess.ExitCode)"
    }
    $report['launcher_release_manifest_preflight_passed'] = $true
  } finally {
    $launcherTimer.Stop()
    $report['launcher_preflight_elapsed_seconds'] = [Math]::Round($launcherTimer.Elapsed.TotalSeconds, 2)
    $launcherProcess.Dispose()
  }
  $uninstaller = Join-Path $program 'unins000.exe'
  if (-not (Test-Path -LiteralPath $uninstaller -PathType Leaf)) {
    throw '[RC1_QA_UNINSTALLER_MISSING]'
  }
  $uninstallArgs = @(
    '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + $uninstallLog + '"')
  )
  $uninstallTimer = [Diagnostics.Stopwatch]::StartNew()
  try {
    $uninstallExit = Invoke-QAInstaller -FilePath $uninstaller -Arguments $uninstallArgs -Stage 'UNINSTALL'
  } finally {
    $uninstallTimer.Stop()
    $report['uninstall_elapsed_seconds'] = [Math]::Round($uninstallTimer.Elapsed.TotalSeconds, 2)
  }
  $report['uninstall_exit_code'] = $uninstallExit
  if ($uninstallExit -ne 0 -or (Test-Path -LiteralPath $exePath)) {
    throw '[RC1_QA_UNINSTALL_FAILED_OR_BINARY_REMAINS]'
  }
  $report['uninstall_passed'] = $true
  $report['status'] = 'PASS_WINDOWS11_ARM64_HOSTED_INSTALL_UNINSTALL_ONLY'
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
if ($report.status -ne 'PASS_WINDOWS11_ARM64_HOSTED_INSTALL_UNINSTALL_ONLY') {
  throw "[RC1_QA_INSTALLED_SMOKE_FAILED] $($report.error)"
}
