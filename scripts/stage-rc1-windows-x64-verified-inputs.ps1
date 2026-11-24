param(
  [Parameter(Mandatory = $true)][string]$RepoRoot,
  [Parameter(Mandatory = $true)][string]$LibreOfficeMsi,
  [Parameter(Mandatory = $true)][string]$Rc6Installer,
  [Parameter(Mandatory = $true)][string]$Rc6ReleaseMetadata,
  [Parameter(Mandatory = $true)][string]$InnoExtractorDirectory,
  [string]$Python = 'python'
)

# Only stages byte-verified build inputs. No installer or release approval.
# Full-function/WPS/security/upgrade/rollback gates cannot be bypassed here.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = (Resolve-Path -LiteralPath $RepoRoot).Path
$workspace = Join-Path $root 'artifacts/windows-rc1-input-staging'
$vendorWindows = Join-Path $root 'vendor/windows'
$stageOffice = Join-Path $vendorWindows 'libreoffice-headless-amd64'
$stageAi = Join-Path $vendorWindows 'local-ai/llama-b10331'

function Assert-FileSha256([string]$Path, [string]$Sha) {
  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    throw "[WINDOWS_INPUT_FILE_MISSING] $Path"
  }
  $item = Get-Item -LiteralPath $Path
  if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
    throw "[WINDOWS_INPUT_FILE_LINK_REFUSED] $Path"
  }
  $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($actual -ne $Sha.ToLowerInvariant()) {
    throw "[WINDOWS_INPUT_SHA256_MISMATCH] $Path"
  }
}

if ((Test-Path -LiteralPath $workspace) -or
    (Test-Path -LiteralPath $stageOffice) -or
    (Test-Path -LiteralPath $stageAi)) {
  throw '[WINDOWS_INPUT_STAGE_NOT_FRESH] Existing data cannot be silently overwritten.'
}
foreach ($entry in @($root, $vendorWindows)) {
  if ((Get-Item -LiteralPath $entry).Attributes -band [IO.FileAttributes]::ReparsePoint) {
    throw '[WINDOWS_INPUT_WORKSPACE_LINK_REFUSED]'
  }
}
New-Item -ItemType Directory -Path $workspace -Force | Out-Null

# Official Document Foundation Windows x64 MSI, exact bytes and signer.
$msiSha = '480fecf759fc6393bd614fbf43753eb95a8198fc1a065d41b727e3b664cd1e59'
$exeSha = '6c55b6200376b5b6cf75971f3985606c6c0886460d7c83caa39a1ccf531142f1'
$comSha = '07d7c9f90881a0b97211926ab801630a0eabc4b23df0ebd831a55327d09df3a8'
$signer = '6B5DA216489DE6C1D7DB498ED672B7ED0F599513'
Assert-FileSha256 $LibreOfficeMsi $msiSha
$signature = Get-AuthenticodeSignature -LiteralPath $LibreOfficeMsi
if ($signature.Status -ne 'Valid' -or
    $signature.SignerCertificate.Thumbprint -ne $signer) {
  throw '[WINDOWS_INPUT_OFFICE_MSI_SIGNATURE_INVALID]'
}
$officeExtraction = Join-Path $workspace 'office-extraction'
New-Item -ItemType Directory -Path $officeExtraction -Force | Out-Null
$log = Join-Path $workspace 'office-extraction.log'
$msiArguments = @('/a', ('"' + $LibreOfficeMsi + '"'), '/qn', '/norestart',
                 ('TARGETDIR="' + $officeExtraction + '"'), '/l*v', ('"' + $log + '"'))
$msiResult = Start-Process -FilePath 'msiexec.exe' -ArgumentList $msiArguments -Wait -PassThru
if ($msiResult.ExitCode -ne 0) {
  throw "[WINDOWS_INPUT_OFFICE_EXTRACTION_FAILED] $($msiResult.ExitCode)"
}
$officeExecutables = @(Get-ChildItem -LiteralPath $officeExtraction -Recurse -File -Filter 'soffice.exe' |
  Where-Object { $_.FullName -match '[\\/]program[\\/]soffice\.exe$' })
if ($officeExecutables.Count -ne 1) {
  throw '[WINDOWS_INPUT_OFFICE_EXECUTABLE_AMBIGUOUS]'
}
$officeOrigin = Split-Path -Parent (Split-Path -Parent $officeExecutables[0].FullName)
$originCli = Join-Path $officeOrigin 'program/soffice.com'
Assert-FileSha256 $officeExecutables[0].FullName $exeSha
Assert-FileSha256 $originCli $comSha
foreach ($entry in @($officeExecutables[0].FullName, $originCli)) {
  $checked = Get-AuthenticodeSignature -LiteralPath $entry
  if ($checked.Status -ne 'Valid' -or
      $checked.SignerCertificate.Thumbprint -ne $signer) {
    throw '[WINDOWS_INPUT_OFFICE_BINARY_SIGNATURE_INVALID]'
  }
}
if (@(Get-ChildItem -LiteralPath $officeOrigin -Recurse -Force |
      Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) {
  throw '[WINDOWS_INPUT_OFFICE_REPARSE_POINT]'
}
$licenseFiles = @(Get-ChildItem -LiteralPath $officeOrigin -Recurse -File |
  Where-Object { $_.Name -match '^(license|copying|notice)([.\-]|$)' })
if (-not $licenseFiles.Count) { throw '[WINDOWS_INPUT_OFFICE_LICENSES_MISSING]' }
Copy-Item -LiteralPath $officeOrigin -Destination $stageOffice -Recurse
Assert-FileSha256 (Join-Path $stageOffice 'program/soffice.exe') $exeSha
Assert-FileSha256 (Join-Path $stageOffice 'program/soffice.com') $comSha
$licenseRoot = Join-Path $stageOffice 'licenses'
New-Item -ItemType Directory -Path $licenseRoot -Force | Out-Null
foreach ($original in $licenseFiles) {
  $relative = $original.FullName.Substring($officeOrigin.Length).TrimStart('\','/')
  $dest = Join-Path $licenseRoot $relative
  New-Item -ItemType Directory -Path (Split-Path -Parent $dest) -Force | Out-Null
  Copy-Item -LiteralPath $original.FullName -Destination $dest -Force
}
[ordered]@{
  component = 'LibreOffice Windows x64 headless runtime'
  version = '26.2.7.3'
  platform = 'windows'
  architecture = 'amd64'
  msi_sha256 = $msiSha
  soffice_sha256 = $exeSha
  soffice_cli_sha256 = $comSha
  authenticode = [ordered]@{
    status = 'Valid'
    certificate_thumbprint = $signer
    subject = $signature.SignerCertificate.Subject
  }
  actual_wps_validation = 'NOT_RUN'
  release_ready = $false
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $stageOffice 'SOURCE.json') -Encoding utf8

# Run the existing official fixed-hash SQLite and .NET preparers unchanged.
$pythonExecutable = (& $Python -c 'import sys; print(sys.executable)').Trim()
if ($LASTEXITCODE -ne 0 -or -not $pythonExecutable) {
  throw '[WINDOWS_INPUT_PYTHON_NOT_FOUND]'
}
& (Join-Path $root 'scripts/prepare-windows-build.ps1') -Python $pythonExecutable
if ($LASTEXITCODE -ne 0) { throw '[WINDOWS_INPUT_SQLITE_VERIFY_FAILED]' }
. (Join-Path $root 'packaging/windows/prepare-dotnet48-runtime.ps1')
$dotnetDestination = Join-Path $workspace 'dotnet48/prerequisites'
Add-VerifiedPartyOpsDotNet48Prerequisite -RepoRoot $root -Destination $dotnetDestination

Assert-FileSha256 $Rc6Installer 'e3e2ea45ccd38acff059b5713826a78f998a56ccef75341633f67bd4e17e57da'
Assert-FileSha256 $Rc6ReleaseMetadata '9e314cbfa1c5d604ad0f24dd8525b0afcda4f511f1cd8d4571114be1061298ee'
# Existing OCR tool verifies each source file against the frozen 62-file manifest.
& (Join-Path $root 'packaging/windows/recover-ocr-from-public-installer.ps1') -RepoRoot $root -InstallerPath $Rc6Installer -ExtractorDirectory $InnoExtractorDirectory
if ($LASTEXITCODE -ne 0) { throw '[WINDOWS_INPUT_OCR_RECOVERY_FAILED]' }
. (Join-Path $root 'packaging/windows/prepare-ocr-runtime.ps1')
$env:PARTYOPS_LEGACY_ARCH = ''
Expand-VerifiedPartyOpsOcrRuntime -RepoRoot $root -Destination (Join-Path $workspace 'ocr')

# Original 449MB installer contains the embedded app file-hash manifest.
$extractor = Join-Path $InnoExtractorDirectory 'innoextract.exe'
$extracted = Join-Path $workspace 'rc6-extracted'
New-Item -ItemType Directory -Path $extracted -Force | Out-Null
& $extractor --extract --output-dir $extracted $Rc6Installer | Out-Null
if ($LASTEXITCODE -ne 0) { throw '[WINDOWS_INPUT_RC6_EXTRACT_FAILED]' }
$appFolder = Join-Path $extracted 'app'
$verifiedAi = Join-Path $workspace 'verified-localai'
$aiEvidencePath = Join-Path $workspace 'localai-evidence.json'
& $pythonExecutable (Join-Path $root 'scripts/rc1_windows_recover_local_ai.py') --repo-root $root --installer $Rc6Installer --release-metadata $Rc6ReleaseMetadata --manifest (Join-Path $appFolder 'release-manifest.json') --source $appFolder --destination $verifiedAi --evidence $aiEvidencePath
if ($LASTEXITCODE -ne 0) { throw '[WINDOWS_INPUT_LOCALAI_FILE_VERIFICATION_FAILED]' }
New-Item -ItemType Directory -Path (Split-Path -Parent $stageAi) -Force | Out-Null
Copy-Item -LiteralPath $verifiedAi -Destination $stageAi -Recurse
$aiEvidence = Get-Content -LiteralPath $aiEvidencePath -Raw | ConvertFrom-Json
foreach ($item in $aiEvidence.files.PSObject.Properties) {
  Assert-FileSha256 (Join-Path $stageAi $item.Name) ([string]$item.Value.sha256)
}
$server = Join-Path $stageAi 'llama-server.exe'
$proc = Start-Process -FilePath $server -ArgumentList @('--version') -PassThru -NoNewWindow
try {
  if (-not $proc.WaitForExit(60000) -or $proc.ExitCode -ne 0) {
    throw '[WINDOWS_INPUT_LOCALAI_NATIVE_START_FAILED]'
  }
} finally {
  if (-not $proc.HasExited) {
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
  }
}

$evidence = [ordered]@{
  schema_version = 1
  scope = 'WINDOWS_X64_VERIFIED_RUNTIME_INPUT_CLOSURE_BUILD_ONLY'
  source_commit = $env:GITHUB_SHA
  sqlite = '3.53.4_DLL_NATIVE_VERIFIED'
  dotnet48 = 'OFFICIAL_SIGNED_FIXED_HASH_VERIFIED'
  libreoffice = '26.2.7.3_OFFICIAL_SIGNED_NATIVE_VERIFIED'
  ocr = '62_ORIGINAL_FILES_NATIVE_CHINESE_ENGLISH_PASS'
  local_ai = 'ORIGINAL_FILE_HASHES_AND_NATIVE_SERVER_PASS'
  local_ai_file_count = [int]$aiEvidence.file_count
  production_pdf_security_review = 'BLOCKED'
  real_wps_golden_and_six_features = 'NOT_RUN'
  windows_10_11_install_upgrade_rollback = 'NOT_RUN'
  installer_built = $false
  signed = $false
  release_ready = $false
}
$evidenceFile = Join-Path $root 'artifacts/windows-x64-input-closure-evidence.json'
$evidence | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $evidenceFile -Encoding utf8
Write-Output 'PARTYOPS_RC1_WINDOWS_X64_RUNTIME_INPUTS_STAGED_AND_VERIFIED_BUILD_ONLY'
Write-Output $evidenceFile
