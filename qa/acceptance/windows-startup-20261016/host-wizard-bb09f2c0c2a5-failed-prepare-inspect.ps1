$request='{"uuid": "f47cf265-72a2-4d51-9cf1-b1f5ae960cb2", "manifest": "adce91340993c4ab4f202dd3a926165ab2fd129236c90442ae4eae046c73204e", "wizard": "6806ec965293c44fcf8473459b480b9cbf5219ca1f0171b9f60bfd1263b8025d", "install": "C:\\PartyOps QA\\中文 程序", "host_data": "C:\\PartyOps-QA-Host-20261017\\主机 数据", "since": "2026-10-17T05:25:01.002945+00:00", "task": "PartyOps-QA-Host-Wizard-20261017"}'|ConvertFrom-Json

$ErrorActionPreference='Stop'
$marker=Get-Content -LiteralPath 'C:\ProgramData\PartyOps-VM-Lab\identity.json' -Raw|ConvertFrom-Json
if($marker.uuid -ne $request.uuid -or $marker.purpose -ne 'disposable-qa' -or (Get-CimInstance Win32_ComputerSystemProduct).UUID -ne $request.uuid) {throw 'QA_GUEST_IDENTITY_REQUIRED'}
$wizard=Join-Path $request.install 'PartyOpsWizard.exe'
if((Get-FileHash -LiteralPath (Join-Path $request.install 'release-manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant() -ne $request.manifest -or (Get-FileHash -LiteralPath $wizard -Algorithm SHA256).Hash.ToLowerInvariant() -ne $request.wizard) {throw 'CANDIDATE_WIZARD_CHANGED'}
$account=Get-LocalUser -Name 'partyopsuser'
$sid=$account.SID.Value
if(-not $account.Enabled -or @(Get-LocalGroupMember -SID 'S-1-5-32-544'|Where-Object {$_.SID.Value -eq $sid}).Count) {throw 'ORIGINAL_STANDARD_ACCOUNT_REQUIRED'}
$desktop=@(Get-CimInstance Win32_Process -Filter "Name='explorer.exe'"|ForEach-Object {
  $owner=Invoke-CimMethod -InputObject $_ -MethodName GetOwner
  if($owner.ReturnValue -eq 0 -and $owner.User -eq 'partyopsuser' -and $owner.Domain -eq $env:COMPUTERNAME) {
    @{pid=[int]$_.ProcessId;session_id=[int]$_.SessionId}
  }
})
if($desktop.Count -ne 1 -or $desktop[0].session_id -eq 0) {throw 'ORIGINAL_INTERACTIVE_DESKTOP_REQUIRED'}
$userRoot='C:\Users\partyopsuser\AppData\Local\PartyOps'
$control='C:\ProgramData\PartyOps'
$pending=Test-Path -LiteralPath (Join-Path $control 'host-switch-pending.json')
$journal=Test-Path -LiteralPath (Join-Path $userRoot 'host-config-journal.json')

$products=@(Get-CimInstance Win32_Process|Where-Object {$_.Name -in @('PartyOps.exe','PartyOpsWizard.exe')}|ForEach-Object {
  $owner=Invoke-CimMethod -InputObject $_ -MethodName GetOwner
  @{name=$_.Name;pid=[int]$_.ProcessId;installed_path_matches=($_.ExecutablePath -eq (Join-Path $request.install $_.Name));owner_is_original_user=($owner.ReturnValue -eq 0 -and $owner.User -eq 'partyopsuser' -and $owner.Domain -eq $env:COMPUTERNAME);session_id=[int]$_.SessionId;created=$_.CreationDate.ToUniversalTime().ToString('o')}
})
$wizardPids=@($products|Where-Object {$_.name -eq 'PartyOpsWizard.exe'}|ForEach-Object {$_.pid})
$productPids=@($products|ForEach-Object {$_.pid})
$listeners=@(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue|Where-Object {$_.OwningProcess -in $productPids -or $_.LocalPort -in @(18765,18775)}|ForEach-Object {
  @{port=[int]$_.LocalPort;pid=[int]$_.OwningProcess;loopback=($_.LocalAddress -in @('127.0.0.1','::1'));owned_by_wizard=($_.OwningProcess -in $wizardPids)}
})
function Public-Mode([string]$path) {
  if(-not(Test-Path -LiteralPath $path -PathType Leaf)) {return 'missing'}
  $value=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json
  if($value.mode -in @('personal','host','client')) {return [string]$value.mode}
  return 'unknown'
}
$statusFile=Join-Path $request.host_data 'logs\partyops-host-status.json'
$hostStatus=@{present=$false}
if(Test-Path -LiteralPath $statusFile -PathType Leaf) {
  $value=Get-Content -LiteralPath $statusFile -Raw|ConvertFrom-Json
  $hostStatus=@{present=$true;stage='unknown';code='';pid=$null;exit_code=$null}
  if([string]$value.stage -match '^[a-z_]{1,40}$') {$hostStatus.stage=[string]$value.stage}
  if([string]$value.code -match '^[A-Z][A-Z0-9_]{0,63}$') {$hostStatus.code=[string]$value.code}
  if($value.pid -is [int] -or $value.pid -is [long]) {$hostStatus.pid=$value.pid}
  if($value.exit_code -is [int] -or $value.exit_code -is [long]) {$hostStatus.exit_code=$value.exit_code}
}
$outcomes=@(if(Test-Path -LiteralPath $control) {
  Get-ChildItem -LiteralPath $control -Filter 'host-config-outcome-*.json' -File|Where-Object {$_.LastWriteTimeUtc -ge [DateTimeOffset]::Parse($request.since).UtcDateTime}|ForEach-Object {
    $value=Get-Content -LiteralPath $_.FullName -Raw|ConvertFrom-Json
    if($value.format_version -eq 1 -and $value.outcome -in @('commit','rollback')) {[string]$value.outcome}
  }
})
@{username='partyopsuser';administrator=$false;session_id=$desktop[0].session_id;processes=$products;listeners=$listeners;wizard_exists=($wizardPids.Count -gt 0);user_mode=(Public-Mode (Join-Path $userRoot 'mode.json'));system_mode=(Public-Mode (Join-Path $control 'mode.json'));pending_mode_switch=$pending;user_journal_present=$journal;qa_host_data_marker_present=(Test-Path -LiteralPath (Join-Path $request.host_data '.partyops-data-root.json'));fixed_qa_host_status=$hostStatus;public_outcomes_since_launch=$outcomes;uac_acceptance='not-verified';host_configuration_acceptance='not-verified'}|ConvertTo-Json -Depth 7 -Compress
