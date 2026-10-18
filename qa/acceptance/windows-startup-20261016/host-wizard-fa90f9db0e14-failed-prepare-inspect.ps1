$request='{"uuid": "f47cf265-72a2-4d51-9cf1-b1f5ae960cb2", "manifest": "adce91340993c4ab4f202dd3a926165ab2fd129236c90442ae4eae046c73204e", "wizard": "6806ec965293c44fcf8473459b480b9cbf5219ca1f0171b9f60bfd1263b8025d", "install": "C:\\PartyOps QA\\中文 程序", "host_data": "C:\\Users\\partyopsuser\\Documents\\PartyOps QA\\host-wizard-20261017\\主机 数据", "since": "2026-10-17T06:26:22.350916+00:00", "task": "PartyOps-QA-Host-Wizard-20261017"}'|ConvertFrom-Json

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

function Test-CimCreationInstant([DateTime]$ProcessStart, [string]$CimCreated) {
  [long]$actual=$ProcessStart.ToUniversalTime().Ticks
  [long]$expected=([DateTimeOffset]::Parse($CimCreated, [Globalization.CultureInfo]::InvariantCulture)).UtcDateTime.Ticks
  return (($actual - ($actual % 10)) -eq ($expected - ($expected % 10)))
}
$target='{"session_id": 1, "name": "PartyOpsWizard.exe", "pid": 5872, "installed_path_matches": true, "created": "2026-10-17T05:25:05.4063860Z", "owner_is_original_user": true}'|ConvertFrom-Json

if($pending -or $journal) {throw 'PENDING_TRANSACTION_REQUIRES_RECOVERY'}
$rows=@(Get-CimInstance Win32_Process -Filter "Name='PartyOpsWizard.exe'")
if($rows.Count -ne 1) {throw 'SINGLE_QA_HOST_WIZARD_REQUIRED'}
$p=$rows[0];$owner=Invoke-CimMethod -InputObject $p -MethodName GetOwner
if($p.ProcessId -ne $target.pid -or $p.ExecutablePath -ne $wizard -or $p.SessionId -ne $desktop[0].session_id -or $owner.ReturnValue -ne 0 -or $owner.User -ne 'partyopsuser' -or $owner.Domain -ne $env:COMPUTERNAME -or -not (Test-CimCreationInstant $p.CreationDate $target.created)) {throw 'BOUND_HOST_WIZARD_CHANGED'}
function Product-Processes {return (@(Get-CimInstance Win32_Process -Filter "Name='PartyOps.exe'"|Sort-Object ProcessId|ForEach-Object {
  ([string]$_.ProcessId)+'|'+$_.CreationDate.ToUniversalTime().ToString('o')
}) -join ';')}
$before=Product-Processes
$handle=Get-Process -Id $target.pid -ErrorAction Stop
if(-not (Test-CimCreationInstant $handle.StartTime $target.created)) {throw 'QA_WIZARD_HANDLE_CHANGED'}
$closed=$false;$terminated=$false
if($handle.MainWindowHandle -ne 0) {$closed=$handle.CloseMainWindow();if($closed){$null=$handle.WaitForExit(10000)}}
if(-not $handle.HasExited) {$handle.Kill();$terminated=$true;if(-not $handle.WaitForExit(10000)){throw 'QA_WIZARD_DID_NOT_EXIT'}}
if((Product-Processes) -cne $before) {throw 'PRODUCT_PROCESS_CHANGED_DURING_PREPARE'}
if(@(Get-CimInstance Win32_Process -Filter "Name='PartyOpsWizard.exe'").Count) {throw 'OTHER_WIZARD_APPEARED'}
$scheduler=New-Object -ComObject 'Schedule.Service';$scheduler.Connect()
$definition=$scheduler.NewTask(0)
$definition.RegistrationInfo.Description='PartyOps original Win10 QA host wizard only'
$definition.Principal.UserId=$env:COMPUTERNAME+'\partyopsuser'
$definition.Principal.LogonType=3
$definition.Principal.RunLevel=0
$definition.Settings.Enabled=$true;$definition.Settings.AllowDemandStart=$true
$definition.Settings.MultipleInstances=2
$action=$definition.Actions.Create(0)
$action.Path=$wizard;$action.WorkingDirectory=$request.install
$action.Arguments='--reconfigure --initial-role personal'
$folder=$scheduler.GetFolder('\')
$registered=$folder.RegisterTaskDefinition('PartyOps-QA-Restore-Personal-20261017',$definition,2,$definition.Principal.UserId,$null,3,$null)
$registered.Run($null)|Out-Null
@{old_qa_wizard_controlled_termination=$terminated;close_main_window_accepted=$closed;launch_requested=$true;task_runlevel=0;configuration_submitted=$false;uac_accepted=$false}|ConvertTo-Json -Compress
