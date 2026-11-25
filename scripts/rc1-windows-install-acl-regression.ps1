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
    # Independently construct an exact protected DACL with SDDL. icacls
    # additive grants are the bug under test, not a trustworthy fixture.
    $fixture=Get-Acl -LiteralPath $dir
    $fixture.SetSecurityDescriptorSddlForm('D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1200a9;;;BU)',[Security.AccessControl.AccessControlSections]::Access)
    Set-Acl -LiteralPath $dir -AclObject $fixture
    $rules=(Get-Acl -LiteralPath $dir).GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])
    if (@($rules | Where-Object { $_.IdentityReference.Value -notin @('S-1-5-18','S-1-5-32-544','S-1-5-32-545') }).Count) { throw 'FIXTURE_DACL_NOT_EXACT' }
    if ($name -ne 'safe-existing') {
      & icacls.exe $dir /grant '*S-1-5-11:(OI)(CI)M' /Q | Out-Null
      if ($LASTEXITCODE) { throw 'SEED_EXPLICIT_ACE_FAILED' }
    }
    if ($name -ne 'empty') { Set-Content -LiteralPath (Join-Path $dir 'PartyOps.exe') -Value 'synthetic test only' }
    $before=(Get-Acl -LiteralPath $dir).Sddl
    Write-Host "FIXTURE $name $before"
    $authRules=@((Get-Acl -LiteralPath $dir).GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier]) | Where-Object { $_.IdentityReference.Value -eq 'S-1-5-11' })
    if (($name -eq 'safe-existing' -and $authRules.Count -ne 0) -or ($name -ne 'safe-existing' -and $authRules.Count -eq 0)) { throw "FIXTURE_EXPECTED_ACE_WRONG $name" }
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
