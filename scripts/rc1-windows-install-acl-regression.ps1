param([switch]$ConfirmEphemeralHostedRunner)
$ErrorActionPreference='Stop'
if (-not $ConfirmEphemeralHostedRunner -or $env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') { throw 'HOSTED_ONLY' }
$root=Join-Path $env:RUNNER_TEMP ('PartyOps-acl-'+[guid]::NewGuid().ToString('N'))
$validator=(Resolve-Path 'packaging/windows/validate-install-path.ps1').Path
$ps=Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
function Invoke-Validator([string]$Target,[string[]]$More) {
  & $ps -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $validator -Path $Target @More | Out-Host
  return $LASTEXITCODE
}
try {
  foreach ($name in @('empty','unsafe-existing','safe-existing')) {
    $dir=Join-Path $root $name
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    & icacls.exe $dir /setowner '*S-1-5-32-544' /Q | Out-Null
    if ($LASTEXITCODE) { throw 'SET_OWNER_FAILED' }
    & icacls.exe $dir /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-32-545:(OI)(CI)RX' /Q | Out-Null
    if ($LASTEXITCODE) { throw 'SET_BASE_ACL_FAILED' }
    if ($name -ne 'safe-existing') {
      & icacls.exe $dir /grant '*S-1-5-11:(OI)(CI)M' /Q | Out-Null
      if ($LASTEXITCODE) { throw 'SEED_EXPLICIT_ACE_FAILED' }
    }
    if ($name -ne 'empty') { Set-Content -LiteralPath (Join-Path $dir 'PartyOps.exe') -Value 'synthetic test only' }
    $before=(Get-Acl -LiteralPath $dir).Sddl
    $rc=Invoke-Validator $dir @('-NormalizeTargetAcl')
    if ($name -eq 'unsafe-existing') {
      if ($rc -ne 6 -or (Get-Acl -LiteralPath $dir).Sddl -ne $before) { throw 'UNSAFE_OLD_DIR_NOT_REJECTED_WITHOUT_MUTATION' }
    } else {
      if ($rc -ne 0 -or (Invoke-Validator $dir @('-VerifyTargetAcl')) -ne 0) { throw "ACL_NORMALIZE_FAILED $name" }
      $actual=(Get-Acl -LiteralPath $dir).GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])
      if (@($actual | Where-Object { $_.IdentityReference.Value -eq 'S-1-5-11' }).Count) { throw 'UNTRUSTED_ACE_RETAINED' }
    }
  }
  Write-Host 'ACL_REGRESSION_PASS: empty repaired, unsafe old rejected, safe old retained'
} finally {
  Remove-Item -LiteralPath $root -Force -Recurse -ErrorAction SilentlyContinue
}
