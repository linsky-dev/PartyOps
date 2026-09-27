# 本机验收前保全已有业务；不备份过期安装包，不触碰其他项目。
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$RunDirectory)
$ErrorActionPreference='Stop'
$reportRoot=[IO.Path]::GetFullPath('D:\PartyOps-VM-Lab\reports\win11-x64-native').TrimEnd('\')
$destination=[IO.Path]::GetFullPath($RunDirectory).TrimEnd('\')
if(-not $destination.StartsWith($reportRoot+'\',[StringComparison]::OrdinalIgnoreCase)) {throw 'NATIVE_REPORT_PATH_OUTSIDE_LAB'}
if(Test-Path -LiteralPath $destination) {throw 'NATIVE_REPORT_ALREADY_EXISTS'}
$running=@(Get-CimInstance Win32_Process | Where-Object {$_.Name -match '^PartyOps.*\.exe$'})
if($running.Count) {throw 'PARTYOPS_MUST_BE_STOPPED_BEFORE_BACKUP'}
$services=@(Get-CimInstance Win32_Service | Where-Object {$_.Name -in @('PartyOpsHost','PartyOpsUpdateService')})
if(@($services|Where-Object {$_.State -ne 'Stopped'}).Count) {throw 'PARTYOPS_SERVICES_MUST_BE_STOPPED'}
$sources=@(
  @{label='personal-config';path='C:\Users\Administrator\AppData\Local\PartyOps'},
  @{label='system-config';path='C:\ProgramData\PartyOps'},
  @{label='existing-business';path='D:\PartyOps QA 数据\Win11 Explorer 首次配置'}
)
$nativeAccount=$null
$nativeUser=Get-LocalUser -Name 'PartyOpsNativeQA' -ErrorAction SilentlyContinue
if($nativeUser){
  # 原专用账号继续保留原 SID、密码与归属；新 run 只保全已经登记的旧配置和业务。
  $ownerPath='D:\PartyOps-VM-Lab\state\native\PartyOpsNativeQA\private\ownership.json'
  $owner=Get-Content -LiteralPath $ownerPath -Raw -Encoding UTF8|ConvertFrom-Json
  $controller=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
  $hardware=(Get-CimInstance Win32_ComputerSystemProduct).UUID
  $oldRun=[IO.Path]::GetFullPath($owner.run_directory).TrimEnd('\')
  $workspaceRoot='E:\codex\PartyOps\.partyops-vm-lab\native\win11-x64'
  $workspace=Join-Path $workspaceRoot ([IO.Path]::GetFileName($oldRun)+'\PartyOpsNativeQA')
  $ownedData=Join-Path $workspace ('中文 空格业务数据-'+$owner.ownership_id)
  if($owner.schema_version -ne 1 -or $owner.purpose -ne 'PartyOps native standard-user validation' -or
     $owner.status -ne 'prepared' -or $owner.username -ne 'PartyOpsNativeQA' -or
     $nativeUser.SID.Value -ne $owner.sid -or $nativeUser.Description -ne ('PartyOps QA '+$owner.ownership_id) -or
     $owner.machine_name -ne $env:COMPUTERNAME -or $owner.hardware_uuid -ne $hardware -or $owner.controller_sid -ne $controller -or
     [IO.Path]::GetDirectoryName($oldRun) -ne $reportRoot -or $owner.native_workspace -ne $workspace -or $owner.data_directory -ne $ownedData){
    throw 'NATIVE_EXISTING_STANDARD_ACCOUNT_OWNERSHIP_MISMATCH'
  }
  $profile=(Get-ItemProperty -LiteralPath ('HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\'+$owner.sid)).ProfileImagePath
  $profile=[Environment]::ExpandEnvironmentVariables($profile)
  $config=Join-Path $profile 'AppData\Local\PartyOps'
  $personal=Join-Path $config 'personal.env'
  if(Test-Path -LiteralPath $personal){
    $configured=@(Get-Content -LiteralPath $personal -Encoding UTF8|Where-Object {$_ -match '^PARTYOPS_DATA_DIR='})
    if($configured.Count -ne 1){throw 'NATIVE_EXISTING_STANDARD_DATA_CONFIG_INVALID'}
    $raw=($configured[0] -split '=',2)[1].Trim()
    if(($raw.StartsWith("'") -and $raw.EndsWith("'")) -or ($raw.StartsWith('"') -and $raw.EndsWith('"'))){$raw=$raw.Substring(1,$raw.Length-2)}
    if([IO.Path]::GetFullPath($raw).TrimEnd('\') -ne $ownedData){throw 'NATIVE_EXISTING_STANDARD_DATA_OUTSIDE_OWNERSHIP'}
  }
  $sources+=@(@{label='owned-standard-user-config';path=$config},@{label='owned-standard-user-business';path=$ownedData})
  $nativeAccount=@{username=$owner.username;sid=$owner.sid;ownership_id=$owner.ownership_id;original_run=$oldRun;
    data_directory=$ownedData;ownership_sha256=(Get-FileHash -LiteralPath $ownerPath -Algorithm SHA256).Hash.ToLowerInvariant();
    account_changed=$false;credential_changed=$false;new_run_account_authorized=$false}
}
# 递归前核对每个节点，目录联接不进入备份，也不允许产生不完整备份。
function Assert-NoReparse([string]$Root) {
  $entry=Get-Item -LiteralPath $Root -Force
  if($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) {throw 'NATIVE_BACKUP_REPARSE_POINT'}
  if($entry.PSIsContainer) {foreach($child in Get-ChildItem -LiteralPath $Root -Force){Assert-NoReparse $child.FullName}}
}
foreach($source in $sources){if(Test-Path -LiteralPath $source.path){Assert-NoReparse $source.path}}
$backupBytes=0L
foreach($source in $sources){
  if(Test-Path -LiteralPath $source.path){
    foreach($file in Get-ChildItem -LiteralPath $source.path -File -Force -Recurse){$backupBytes+=$file.Length}
  }
}
if((Get-PSDrive -Name D).Free - $backupBytes -lt 20GB){throw 'NATIVE_BACKUP_D_DRIVE_RESERVE_REQUIRED'}
New-Item -ItemType Directory -Path $destination | Out-Null
$private=Join-Path $destination 'protected.local'
New-Item -ItemType Directory -Path $private | Out-Null
& icacls.exe $private /inheritance:r /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F' | Out-Null
if($LASTEXITCODE -ne 0){throw 'NATIVE_BACKUP_ACL_FAILED'}
$records=@()
foreach($source in $sources){
  if(-not(Test-Path -LiteralPath $source.path)){continue}
  $backup=Join-Path $private $source.label
  Copy-Item -LiteralPath $source.path -Destination $backup -Recurse
  $files=@()
  foreach($file in Get-ChildItem -LiteralPath $source.path -File -Force -Recurse){
    $relative=$file.FullName.Substring($source.path.Length).TrimStart('\')
    $copy=Join-Path $backup $relative
    $digest=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if((Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash.ToLowerInvariant() -ne $digest){throw 'NATIVE_BACKUP_HASH_MISMATCH'}
    $files+=@{relative_path=$relative;bytes=$file.Length;sha256=$digest}
  }
  $records+=@{source=$source.path;backup=$backup;files=$files;verified=$true}
}
$os=Get-CimInstance Win32_OperatingSystem
$record=[ordered]@{
  schema_version=1;at=(Get-Date).ToString('o');target='win11-x64-native';environment_type='native-host';
  os=@{caption=$os.Caption;version=$os.Version;architecture=$os.OSArchitecture;boot_id=$os.LastBootUpTime.ToUniversalTime().ToString('o')};
  protected=$records;services_before=@($services|Select-Object Name,State,StartMode,PathName);
  existing_native_account=$nativeAccount;
  existing_install='E:\PartyOps1\PartyOps';
  test_scope='用户授权的Win11本机验收；同版本候选替换不是旧版本升级或干净系统安装';
  rollback='仅在确认需要恢复时，将protected.local内已校验的配置/业务恢复至记录的原路径；当前候选安装器可重装。旧安装包不另存。';
  runtime_environment_passed=$false
}
$output=Join-Path $destination 'protection.json'
[IO.File]::WriteAllText($output,($record|ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($false))
[pscustomobject]@{protection=$output;protected_roots=$records.Count;verified=$true;runtime_environment_passed=$false}|ConvertTo-Json -Compress
