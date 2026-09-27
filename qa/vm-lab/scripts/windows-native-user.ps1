# 本机专用标准用户及安装版启动诊断；不设置自动登录、不重启、不声称 GUI 验收通过。
[CmdletBinding()]
param(
  [Parameter(Mandatory=$true)][ValidateSet('Prepare','PrepareRevalidation','RunProbe','LaunchWizard','LaunchPersonal','RunConfiguredPersonalProbe')][string]$Action,
  [Parameter(Mandatory=$true)][string]$RunDirectory,
  [ValidateSet('Retain','Fresh')][string]$DataMode='Retain',
  [ValidatePattern('^[a-fA-F0-9]{64}$')][string]$RecoverCredentialSha256
)
$ErrorActionPreference='Stop'

function Write-NativeJson([string]$Path, $Value) {
  [IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 15),[Text.UTF8Encoding]::new($false))
}

function Write-NewNativeJson([string]$Path,$Value) {
  $stream=[IO.File]::Open($Path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
  try{$bytes=[Text.UTF8Encoding]::new($false).GetBytes(($Value|ConvertTo-Json -Depth 15));$stream.Write($bytes,0,$bytes.Length)}finally{$stream.Dispose()}
}

function Resolve-NativeChild([string]$Root,[string]$Candidate) {
  $parent=[IO.Path]::GetFullPath($Root).TrimEnd('\')
  $path=[IO.Path]::GetFullPath($Candidate).TrimEnd('\')
  if(-not $path.StartsWith($parent+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'NATIVE_PATH_OUTSIDE_LAB'}
  $cursor=$path
  while($cursor){
    if(Test-Path -LiteralPath $cursor){
      if((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'NATIVE_REPARSE_POINT_REJECTED'}
    }
    $next=[IO.Path]::GetDirectoryName($cursor)
    if($next -eq $cursor){break};$cursor=$next
  }
  return $path
}

function Set-NativeDirectoryAcl([string]$Path,[string]$UserSid='', [string]$Rights='ReadAndExecute') {
  $acl=[Security.AccessControl.DirectorySecurity]::new()
  $acl.SetAccessRuleProtection($true,$false)
  foreach($sid in @('S-1-5-32-544','S-1-5-18')){
    $rule=[Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($sid),
      [Security.AccessControl.FileSystemRights]::FullControl,'ContainerInherit,ObjectInherit','None','Allow')
    $acl.AddAccessRule($rule)
  }
  if($UserSid){
    $rule=[Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($UserSid),
      [Security.AccessControl.FileSystemRights]$Rights,'ContainerInherit,ObjectInherit','None','Allow')
    $acl.AddAccessRule($rule)
  }
  Set-Acl -LiteralPath $Path -AclObject $acl
}

function Assert-NativeRecordContext($Record,$Context) {
  if(-not $Record -or $Record.schema_version -ne 1 -or $Record.purpose -ne 'PartyOps native standard-user validation' -or
      $Record.username -ne 'PartyOpsNativeQA' -or $Record.run_directory -ne $Context.run_directory -or
      $Record.machine_name -ne $Context.machine_name -or $Record.hardware_uuid -ne $Context.hardware_uuid -or
      $Record.controller_sid -ne $Context.controller_sid -or $Record.native_workspace -ne $Context.native_workspace){
    throw 'NATIVE_USER_NOT_OWNED_BY_THIS_RUN'
  }
  if($Record.data_directory -ne (Join-Path $Context.native_workspace ('中文 空格业务数据-'+$Record.ownership_id))) {throw 'NATIVE_OWNED_DATA_DIRECTORY_CHANGED'}
}

function Assert-NativeOwnership($User,$Record,$Context) {
  Assert-NativeRecordContext $Record $Context
  if(-not $User -or -not $Record.sid -or $User.SID.Value -ne $Record.sid -or
      $User.Description -ne ('PartyOps QA '+$Record.ownership_id)) {throw 'NATIVE_OWNED_USER_IDENTITY_CHANGED'}
}

function Assert-NativeMembership([string]$Sid,[string[]]$UserMembers,[string[]]$AdministratorMembers) {
  if($AdministratorMembers -contains $Sid){throw 'NATIVE_STANDARD_USER_IS_ADMINISTRATOR'}
  if($UserMembers -notcontains $Sid){throw 'NATIVE_STANDARD_USER_NOT_IN_USERS'}
}

function Get-NativeMembership([string]$Sid) {
  $users=@(Get-LocalGroupMember -SID ([Security.Principal.SecurityIdentifier]::new('S-1-5-32-545'))|ForEach-Object {$_.SID.Value})
  $admins=@(Get-LocalGroupMember -SID ([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))|ForEach-Object {$_.SID.Value})
  Assert-NativeMembership $Sid $users $admins
}

function Get-NativeContext([string]$Directory) {
  if(-not (Get-Command Start-Process).Parameters.ContainsKey('Environment')){throw 'NATIVE_CONTROLLER_REQUIRES_POWERSHELL7_ENVIRONMENT'}
  $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
  if(-not ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'NATIVE_USER_CONTROLLER_REQUIRES_ADMINISTRATOR'
  }
  $os=Get-CimInstance Win32_OperatingSystem
  if($os.Caption -notmatch 'Windows 11' -or -not [Environment]::Is64BitProcess){throw 'NATIVE_WINDOWS11_X64_REQUIRED'}
  $run=Resolve-NativeChild 'D:\PartyOps-VM-Lab\reports\win11-x64-native' $Directory
  $name=[IO.Path]::GetFileName($run)
  if($name -notmatch '^[a-z0-9][a-z0-9_-]{0,79}$' -or [IO.Path]::GetDirectoryName($run) -ne 'D:\PartyOps-VM-Lab\reports\win11-x64-native'){throw 'NATIVE_RUN_ID_INVALID'}
  $install=Get-Content -LiteralPath (Join-Path $run 'install-result.json') -Raw -Encoding UTF8|ConvertFrom-Json
  $app='E:\PartyOps1\PartyOps\PartyOps.exe'
  if($install.target -ne 'win11-x64-native' -or $install.environment_type -ne 'native-host' -or
      $install.installer_exit_code -ne 0 -or $install.package.id -ne 'windows_amd64' -or
      $install.package.provenance_status -ne 'verified' -or $install.install_dir -ne 'E:\PartyOps1\PartyOps') {
    throw 'NATIVE_VERIFIED_INSTALL_RESULT_REQUIRED'
  }
  Resolve-NativeChild 'E:\PartyOps1\PartyOps' $app|Out-Null
  $digest=(Get-FileHash -LiteralPath $app -Algorithm SHA256).Hash.ToLowerInvariant()
  if($digest -ne $install.installed_executable_sha256){throw 'NATIVE_INSTALLED_EXECUTABLE_CHANGED'}
  $wizard='E:\PartyOps1\PartyOps\PartyOpsWizard.exe'
  Resolve-NativeChild 'E:\PartyOps1\PartyOps' $wizard|Out-Null
  $manifest=Get-Content -LiteralPath 'E:\PartyOps1\PartyOps\release-manifest.json' -Raw -Encoding UTF8|ConvertFrom-Json
  $wizardEntry=@($manifest.files|Where-Object {$_.path -eq 'PartyOpsWizard.exe'})
  $wizardHash=(Get-FileHash -LiteralPath $wizard -Algorithm SHA256).Hash.ToLowerInvariant()
  if($manifest.version -ne $install.package.version -or $wizardEntry.Count -ne 1 -or $wizardEntry[0].sha256 -ne $wizardHash){throw 'NATIVE_WIZARD_MANIFEST_MISMATCH'}
  $launcher='E:\PartyOps1\PartyOps\PartyOpsLauncher.exe'
  Resolve-NativeChild 'E:\PartyOps1\PartyOps' $launcher|Out-Null
  $launcherEntry=@($manifest.files|Where-Object {$_.path -eq 'PartyOpsLauncher.exe'})
  $launcherHash=(Get-FileHash -LiteralPath $launcher -Algorithm SHA256).Hash.ToLowerInvariant()
  if($launcherEntry.Count -ne 1 -or $launcherEntry[0].sha256 -ne $launcherHash){throw 'NATIVE_LAUNCHER_MANIFEST_MISMATCH'}
  $binding=$null;$bindingHash=$null;$protectionHash=$null
  if(Test-Path -LiteralPath (Join-Path $run 'install-binding.json')){
    $repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
    $verified=@(& (Join-Path $repo 'backend\.venv\Scripts\python.exe') (Join-Path $PSScriptRoot 'prepare-native-candidate.py') --run-directory $run --verify-only)
    if($LASTEXITCODE -ne 0){throw 'NATIVE_CURRENT_BUILD_BINDING_REJECTED'}
    $binding=($verified -join "`n")|ConvertFrom-Json
    $bindingHash=(Get-FileHash -LiteralPath (Join-Path $run 'install-binding.json') -Algorithm SHA256).Hash.ToLowerInvariant()
    $protectionHash=(Get-FileHash -LiteralPath (Join-Path $run 'protection.json') -Algorithm SHA256).Hash.ToLowerInvariant()
    if($install.status -eq 'failed' -or $install.install_binding_sha256 -ne $bindingHash -or
       $install.package.sha256 -ne $binding.package.sha256 -or $install.package.source_fingerprint -ne $binding.source_fingerprint -or
       $digest -ne $binding.windows_payload.executables.'PartyOps.exe'.sha256 -or
       $wizardHash -ne $binding.windows_payload.executables.'PartyOpsWizard.exe'.sha256 -or
       (Get-FileHash -LiteralPath (Join-Path (Split-Path $app) 'release-manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant() -ne $binding.windows_payload.manifest.sha256){throw 'NATIVE_CURRENT_INSTALL_PAYLOAD_MISMATCH'}
  }
  $workspace=Resolve-NativeChild 'E:\codex\PartyOps\.partyops-vm-lab\native\win11-x64' (
    Join-Path 'E:\codex\PartyOps\.partyops-vm-lab\native\win11-x64' ($name+'\PartyOpsNativeQA'))
  $state=Resolve-NativeChild 'D:\PartyOps-VM-Lab\state\native' 'D:\PartyOps-VM-Lab\state\native\PartyOpsNativeQA'
  $session=[Diagnostics.Process]::GetCurrentProcess().SessionId
  if($session -le 0){throw 'NATIVE_INTERACTIVE_SESSION_REQUIRED'}
  return [pscustomobject]@{run_directory=$run;native_workspace=$workspace;state_directory=$state;
    machine_name=$env:COMPUTERNAME;hardware_uuid=(Get-CimInstance Win32_ComputerSystemProduct).UUID;
    controller_sid=$identity.User.Value;session_id=$session;app_path=$app;app_sha256=$digest;
    wizard_path=$wizard;wizard_sha256=$wizardHash;launcher_path=$launcher;launcher_sha256=$launcherHash;
    build_binding=$binding;install_binding_sha256=$bindingHash;protection_sha256=$protectionHash;
    install_result_sha256=(Get-FileHash -LiteralPath (Join-Path $run 'install-result.json') -Algorithm SHA256).Hash.ToLowerInvariant()}
}

function Assert-NativeCredentialRecovery($Context,[string]$ExpectedSha256) {
  # 仅接受操作员确认过哈希的这一次失败遗留；不清理、改写或认领任何同名账号。
  $private=Join-Path $Context.state_directory 'private'
  $credentialPath=Join-Path $private 'credential.clixml'
  if($ExpectedSha256 -notmatch '^[a-fA-F0-9]{64}$'){throw 'NATIVE_UNREGISTERED_STATE_DIRECTORY'}
  $top=@(Get-ChildItem -LiteralPath $Context.state_directory -Force)
  $leaves=@(Get-ChildItem -LiteralPath $private -Force)
  if($top.Count -ne 1 -or $top[0].Name -ne 'private' -or -not $top[0].PSIsContainer -or
      $leaves.Count -ne 1 -or $leaves[0].Name -ne 'credential.clixml' -or $leaves[0].PSIsContainer){throw 'NATIVE_RECOVERY_UNEXPECTED_FILES'}
  foreach($path in @($Context.state_directory,$private,$credentialPath)){
    Resolve-NativeChild 'D:\PartyOps-VM-Lab\state\native' $path|Out-Null
    $acl=Get-Acl -LiteralPath $path
    if(-not $acl.AreAccessRulesProtected -and $path -ne $credentialPath){throw 'NATIVE_RECOVERY_ACL_NOT_PRIVATE'}
    foreach($rule in $acl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])){
      if($rule.IdentityReference.Value -notin @('S-1-5-32-544','S-1-5-18')){throw 'NATIVE_RECOVERY_ACL_NOT_PRIVATE'}
    }
  }
  if((Get-FileHash -LiteralPath $credentialPath -Algorithm SHA256).Hash -ne $ExpectedSha256){throw 'NATIVE_RECOVERY_CREDENTIAL_HASH_MISMATCH'}
}

function Read-NativeCredential([string]$Path,$Context,[string]$ExpectedSha256) {
  if(-not(Test-Path -LiteralPath $Path)){throw 'NATIVE_USER_NOT_READY'}
  if($ExpectedSha256 -and (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $ExpectedSha256){throw 'NATIVE_CREATION_CREDENTIAL_CHANGED'}
  $credential=Import-Clixml -LiteralPath $Path
  if($credential -isnot [pscredential] -or $credential.UserName -ne ($Context.machine_name+'\PartyOpsNativeQA')){throw 'NATIVE_CREDENTIAL_IDENTITY_MISMATCH'}
  return $credential
}

function Prepare-NativeUser($Context,[string]$RecoverySha256='') {
  $private=Join-Path $Context.state_directory 'private'
  $owner=Join-Path $private 'ownership.json'
  $credentialPath=Join-Path $private 'credential.clixml'
  $user=Get-LocalUser -Name 'PartyOpsNativeQA' -ErrorAction SilentlyContinue
  $record=$null
  if(Test-Path -LiteralPath $owner){$record=Get-Content -LiteralPath $owner -Raw -Encoding UTF8|ConvertFrom-Json}
  # 在任何账号或目录修改前，先拒绝无本轮登记的同名账户。
  if(($user -or $record) -and $record.status -ne 'creation-intent'){
    Assert-NativeOwnership $user $record $Context
    if(-not $user.Enabled -or -not(Test-Path -LiteralPath $credentialPath)){throw 'NATIVE_USER_NOT_READY'}
    if($record.status -eq 'prepared'){Get-NativeMembership $record.sid;return $record}
    if($record.status -ne 'preparing'){throw 'NATIVE_USER_NOT_READY'}
  }
  if(-not $record){
    if(Test-Path -LiteralPath $Context.state_directory){
      Assert-NativeCredentialRecovery $Context $RecoverySha256
      $credential=Read-NativeCredential $credentialPath $Context $RecoverySha256
    }else{
      if($RecoverySha256){throw 'NATIVE_RECOVERY_STATE_MISSING'}
      New-Item -ItemType Directory -Path $Context.state_directory -Force|Out-Null
      Set-NativeDirectoryAcl $Context.state_directory
      New-Item -ItemType Directory -Path $private|Out-Null
      Set-NativeDirectoryAcl $private
      $bytes=New-Object byte[] 36
      $random=[Security.Cryptography.RandomNumberGenerator]::Create()
      try{$random.GetBytes($bytes)}finally{$random.Dispose()}
      $password=[Security.SecureString]::new()
      foreach($character in ('Aa1!'+[Convert]::ToBase64String($bytes)).ToCharArray()){$password.AppendChar($character)}
      $password.MakeReadOnly()
      [Array]::Clear($bytes,0,$bytes.Length)
      $credential=[pscredential]::new(($Context.machine_name+'\PartyOpsNativeQA'),$password)
      # Export-Clixml 对 SecureString 使用当前控制账号的 DPAPI；不序列化明文密码。
      $credential|Export-Clixml -LiteralPath $credentialPath
    }
    $ownershipId=[guid]::NewGuid().ToString('N')
    $record=[pscustomobject]@{schema_version=1;purpose='PartyOps native standard-user validation';
      ownership_id=$ownershipId;username='PartyOpsNativeQA';sid=$null;
      run_directory=$Context.run_directory;native_workspace=$Context.native_workspace;
      data_directory=(Join-Path $Context.native_workspace ('中文 空格业务数据-'+$ownershipId));
      machine_name=$Context.machine_name;hardware_uuid=$Context.hardware_uuid;controller_sid=$Context.controller_sid;
      credential_sha256=(Get-FileHash -LiteralPath $credentialPath -Algorithm SHA256).Hash.ToLowerInvariant();
      recovered_credential_sha256=$RecoverySha256;prepared_at=(Get-Date).ToString('o');status='creation-intent'}
    # 创建账号前持久登记上下文和随机归属标识；中断后只续跑这一已登记意图。
    Write-NativeJson $owner $record
  }
  if($record.status -eq 'creation-intent'){
    Assert-NativeRecordContext $record $Context
    if($record.ownership_id -notmatch '^[a-f0-9]{32}$' -or $record.credential_sha256 -notmatch '^[a-f0-9]{64}$'){throw 'NATIVE_CREATION_INTENT_INVALID'}
    $credential=Read-NativeCredential $credentialPath $Context $record.credential_sha256
    $description='PartyOps QA '+$record.ownership_id
    # Windows New-LocalUser 的 Description 上限为 48 字符；当前固定为 44。
    if($description.Length -gt 48){throw 'NATIVE_DESCRIPTION_TOO_LONG'}
    if($user){
      if($user.Description -ne $description -or -not $user.Enabled){throw 'NATIVE_OWNED_USER_IDENTITY_CHANGED'}
    }else{$user=New-LocalUser -Name 'PartyOpsNativeQA' -Password $credential.Password -Description $description}
    $record.sid=$user.SID.Value;$record.status='preparing';Write-NativeJson $owner $record
  }
  $admins=@(Get-LocalGroupMember -SID ([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))|ForEach-Object {$_.SID.Value})
  if($admins -contains $record.sid){throw 'NATIVE_STANDARD_USER_IS_ADMINISTRATOR'}
  $members=@(Get-LocalGroupMember -SID ([Security.Principal.SecurityIdentifier]::new('S-1-5-32-545'))|ForEach-Object {$_.SID.Value})
  if($members -notcontains $record.sid){Add-LocalGroupMember -SID ([Security.Principal.SecurityIdentifier]::new('S-1-5-32-545')) -Member $user}
  Get-NativeMembership $record.sid
  New-Item -ItemType Directory -Path $Context.native_workspace -Force|Out-Null
  Set-NativeDirectoryAcl $Context.native_workspace $record.sid
  foreach($name in @('temp',('中文 空格业务数据-'+$record.ownership_id),'probe-output')){
    $directory=Join-Path $Context.native_workspace $name
    New-Item -ItemType Directory -Path $directory -Force|Out-Null
    Set-NativeDirectoryAcl $directory $record.sid 'Modify'
  }
  $record.status='prepared';Write-NativeJson $owner $record
  return $record
}

function Get-NativeOriginalOwnership($Context) {
  $path=Resolve-NativeChild $Context.state_directory (Join-Path $Context.state_directory 'private\ownership.json')
  $owner=Get-Content -LiteralPath $path -Raw -Encoding UTF8|ConvertFrom-Json
  $oldRun=Resolve-NativeChild 'D:\PartyOps-VM-Lab\reports\win11-x64-native' $owner.run_directory
  if([IO.Path]::GetDirectoryName($oldRun) -ne 'D:\PartyOps-VM-Lab\reports\win11-x64-native' -or
     [IO.Path]::GetFileName($oldRun) -notmatch '^[a-z0-9][a-z0-9_-]{0,79}$' -or
     $owner.ownership_id -notmatch '^[a-f0-9]{32}$' -or $owner.credential_sha256 -notmatch '^[a-f0-9]{64}$'){throw 'NATIVE_ORIGINAL_OWNERSHIP_INVALID'}
  $oldWorkspace=Join-Path 'E:\codex\PartyOps\.partyops-vm-lab\native\win11-x64' ([IO.Path]::GetFileName($oldRun)+'\PartyOpsNativeQA')
  $oldContext=@{run_directory=$oldRun;native_workspace=$oldWorkspace;machine_name=$Context.machine_name;
    hardware_uuid=$Context.hardware_uuid;controller_sid=$Context.controller_sid}
  $user=Get-LocalUser -Name 'PartyOpsNativeQA' -ErrorAction SilentlyContinue
  Assert-NativeOwnership $user $owner $oldContext
  if(-not $user.Enabled -or $owner.status -ne 'prepared'){throw 'NATIVE_USER_NOT_PREPARED'}
  Get-NativeMembership $owner.sid
  Resolve-NativeChild $oldWorkspace $owner.data_directory|Out-Null
  $credential=Resolve-NativeChild $Context.state_directory (Join-Path $Context.state_directory 'private\credential.clixml')
  $null=Read-NativeCredential $credential $Context $owner.credential_sha256
  return @{record=$owner;path=$path;sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}
}

function Assert-NativeRevalidationProtection($Context,$Original,[switch]$VerifySource) {
  $path=Join-Path $Context.run_directory 'protection.json'
  if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $Context.protection_sha256){throw 'NATIVE_REVALIDATION_PROTECTION_CHANGED'}
  $protection=Get-Content -LiteralPath $path -Raw -Encoding UTF8|ConvertFrom-Json
  $owner=$Original.record;$record=$protection.existing_native_account
  if($protection.target -ne 'win11-x64-native' -or $protection.environment_type -ne 'native-host' -or
     $record.username -ne $owner.username -or $record.sid -ne $owner.sid -or $record.ownership_id -ne $owner.ownership_id -or
     $record.ownership_sha256 -ne $Original.sha256 -or $record.original_run -ne $owner.run_directory -or
     $record.data_directory -ne $owner.data_directory -or $record.account_changed -ne $false -or $record.credential_changed -ne $false){throw 'NATIVE_REVALIDATION_BACKUP_OWNERSHIP_MISMATCH'}
  $profile=Get-NativeProfileEnvironment $Context $owner
  foreach($source in @((Join-Path $profile.LOCALAPPDATA 'PartyOps'),$owner.data_directory)){
    Resolve-NativeChild ([IO.Path]::GetDirectoryName($source)) $source|Out-Null
    $roots=@($protection.protected|Where-Object {$_.source -eq $source -and $_.verified -eq $true})
    if($roots.Count -ne 1){throw 'NATIVE_REVALIDATION_BACKUP_REQUIRED'}
    $backup=Resolve-NativeChild (Join-Path $Context.run_directory 'protected.local') $roots[0].backup
    $expected=@{}
    foreach($file in $roots[0].files){
      if($expected.ContainsKey($file.relative_path)){throw 'NATIVE_REVALIDATION_BACKUP_DUPLICATE'}
      $expected[$file.relative_path]=$file
      $copy=Resolve-NativeChild $backup (Join-Path $backup $file.relative_path)
      if((Get-Item -LiteralPath $copy).Length -ne $file.bytes -or
         (Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256){throw 'NATIVE_REVALIDATION_BACKUP_CHANGED'}
      if($VerifySource){
        $actual=Resolve-NativeChild $source (Join-Path $source $file.relative_path)
        if((Get-Item -LiteralPath $actual).Length -ne $file.bytes -or
           (Get-FileHash -LiteralPath $actual -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256){throw 'NATIVE_REVALIDATION_SOURCE_CHANGED_AFTER_BACKUP'}
      }
    }
    if($VerifySource){
      # 逐目录拒绝链接，避免递归先沿目录联接进入未登记数据。
      $pending=[Collections.Generic.Stack[string]]::new();$pending.Push($source);$count=0
      while($pending.Count){foreach($item in Get-ChildItem -LiteralPath ($pending.Pop()) -Force){
        if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'NATIVE_REVALIDATION_SOURCE_REPARSE'}
        if($item.PSIsContainer){$pending.Push($item.FullName)}else{
          $relative=$item.FullName.Substring($source.Length).TrimStart('\')
          if(-not $expected.ContainsKey($relative)){throw 'NATIVE_REVALIDATION_UNBACKED_SOURCE_FILE'};$count++
        }
      }}
      if($count -ne $expected.Count){throw 'NATIVE_REVALIDATION_SOURCE_FILE_COUNT_CHANGED'}
    }
  }
}

function Assert-NativeRevalidationSession($Session,$Context,$Original) {
  $owner=$Original.record
  if(-not $Context.build_binding -or -not $Context.install_binding_sha256 -or -not $Context.protection_sha256){throw 'NATIVE_REVALIDATION_TRUSTED_INSTALL_REQUIRED'}
  if(-not $Session -or $Session.schema_version -ne 2 -or $Session.purpose -ne 'PartyOps native standard-user revalidation' -or
     $Session.status -ne 'prepared' -or $Session.runtime_environment_passed -ne $false -or
     $Session.username -ne $owner.username -or $Session.sid -ne $owner.sid -or $Session.ownership_id -ne $owner.ownership_id -or
     $Session.run_directory -ne $Context.run_directory -or $Session.native_workspace -ne $Context.native_workspace -or
     $Session.machine_name -ne $Context.machine_name -or $Session.hardware_uuid -ne $Context.hardware_uuid -or
     $Session.controller_sid -ne $Context.controller_sid -or $Session.original_ownership_path -ne $Original.path -or
     $Session.original_ownership_sha256 -ne $Original.sha256 -or $Session.original_run -ne $owner.run_directory -or
     $Session.original_data_directory -ne $owner.data_directory -or $Session.original_run -eq $Session.run_directory -or
     $Session.install_result_sha256 -ne $Context.install_result_sha256 -or $Session.install_binding_sha256 -ne $Context.install_binding_sha256 -or
     $Session.protection_sha256 -ne $Context.protection_sha256 -or $Session.source_fingerprint -ne $Context.build_binding.source_fingerprint -or
     $Session.package_sha256 -ne $Context.build_binding.package.sha256 -or $Session.app_sha256 -ne $Context.app_sha256 -or
     $Session.wizard_sha256 -ne $Context.wizard_sha256 -or $Session.account_changed -ne $false -or $Session.credential_changed -ne $false){throw 'NATIVE_REVALIDATION_SESSION_BINDING_MISMATCH'}
  $expected=$(if($Session.data_mode -eq 'Retain'){$owner.data_directory}elseif($Session.data_mode -eq 'Fresh'){
    Join-Path $Context.native_workspace ('中文 空格业务数据-'+$owner.ownership_id)
  }else{throw 'NATIVE_REVALIDATION_DATA_MODE_INVALID'})
  if($Session.data_directory -ne $expected){throw 'NATIVE_REVALIDATION_DATA_DIRECTORY_MISMATCH'}
}

function Read-NativeRevalidationSession($Context) {
  $path=Resolve-NativeChild $Context.run_directory (Join-Path $Context.run_directory 'native-user-session.json')
  $session=Get-Content -LiteralPath $path -Raw -Encoding UTF8|ConvertFrom-Json
  $original=Get-NativeOriginalOwnership $Context
  Assert-NativeRevalidationSession $session $Context $original
  Assert-NativeRevalidationProtection $Context $original
  $session|Add-Member NoteProperty credential_sha256 $original.record.credential_sha256
  $session|Add-Member NoteProperty session_sha256 ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant())
  return $session
}

function Prepare-NativeRevalidation($Context,[string]$Mode='Retain') {
  if(-not $Context.build_binding){throw 'NATIVE_REVALIDATION_TRUSTED_INSTALL_REQUIRED'}
  if($Mode -notin @('Retain','Fresh')){throw 'NATIVE_REVALIDATION_DATA_MODE_INVALID'}
  $sessionPath=Join-Path $Context.run_directory 'native-user-session.json'
  if(Test-Path -LiteralPath $sessionPath){
    $session=Read-NativeRevalidationSession $Context
    if($session.data_mode -ne $Mode){throw 'NATIVE_REVALIDATION_DATA_MODE_CHANGED_USE_NEW_RUN'}
    return $session
  }
  $original=Get-NativeOriginalOwnership $Context;$owner=$original.record
  if($owner.run_directory -eq $Context.run_directory){throw 'NATIVE_REVALIDATION_REQUIRES_NEW_RUN'}
  foreach($process in @(Get-CimInstance Win32_Process|Where-Object {$_.Name -match '^PartyOps.*\.exe$'})){
    $processOwner=Invoke-CimMethod -InputObject $process -MethodName GetOwnerSid
    if($processOwner.ReturnValue -ne 0){throw 'NATIVE_REVALIDATION_PROCESS_OWNER_UNAVAILABLE'}
    if($processOwner.Sid -eq $owner.sid){throw 'NATIVE_REVALIDATION_OWNED_PARTYOPS_MUST_BE_STOPPED'}
  }
  if(@(Get-CimInstance Win32_Service|Where-Object {$_.Name -in @('PartyOpsHost','PartyOpsUpdateService') -and $_.State -ne 'Stopped'}).Count){throw 'NATIVE_REVALIDATION_PARTYOPS_SERVICES_MUST_BE_STOPPED'}
  Assert-NativeRevalidationProtection $Context $original -VerifySource
  if(Test-Path -LiteralPath $Context.native_workspace){throw 'NATIVE_REVALIDATION_WORKSPACE_ALREADY_EXISTS'}
  if((Get-PSDrive -Name E).Free -lt 30GB){throw 'NATIVE_REVALIDATION_E_DRIVE_RESERVE_REQUIRED'}
  $data=$(if($Mode -eq 'Retain'){$owner.data_directory}else{Join-Path $Context.native_workspace ('中文 空格业务数据-'+$owner.ownership_id)})
  $session=[pscustomobject]@{schema_version=2;purpose='PartyOps native standard-user revalidation';status='prepared';
    prepared_at=(Get-Date).ToString('o');username=$owner.username;sid=$owner.sid;ownership_id=$owner.ownership_id;
    run_directory=$Context.run_directory;native_workspace=$Context.native_workspace;data_directory=$data;data_mode=$Mode;
    original_run=$owner.run_directory;original_data_directory=$owner.data_directory;original_ownership_path=$original.path;original_ownership_sha256=$original.sha256;
    machine_name=$Context.machine_name;hardware_uuid=$Context.hardware_uuid;controller_sid=$Context.controller_sid;
    install_result_sha256=$Context.install_result_sha256;install_binding_sha256=$Context.install_binding_sha256;protection_sha256=$Context.protection_sha256;
    source_fingerprint=$Context.build_binding.source_fingerprint;package_sha256=$Context.build_binding.package.sha256;
    app_sha256=$Context.app_sha256;wizard_sha256=$Context.wizard_sha256;account_changed=$false;credential_changed=$false;runtime_environment_passed=$false}
  Assert-NativeRevalidationSession $session $Context $original
  # 新目录只保存本轮执行脚本与输出；原目录 ACL、配置和业务不修改。
  New-Item -ItemType Directory -Path $Context.native_workspace|Out-Null
  Set-NativeDirectoryAcl $Context.native_workspace $owner.sid
  $names=@('temp','probe-output')
  if($Mode -eq 'Fresh'){$names+=('中文 空格业务数据-'+$owner.ownership_id)}
  foreach($name in $names){$path=Join-Path $Context.native_workspace $name;New-Item -ItemType Directory -Path $path|Out-Null;Set-NativeDirectoryAcl $path $owner.sid 'Modify'}
  Write-NewNativeJson $sessionPath $session
  return Read-NativeRevalidationSession $Context
}

function Get-NativeProbeScript {
  return @'
param([Parameter(Mandatory=$true)][string]$ContextPath)
$ErrorActionPreference='Stop'
# 二次登录子进程只采用系统 Windows PowerShell 模块，避免继承控制器的 PowerShell 7 模块路径。
$env:PSModulePath=(Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\Modules')+';'+(Join-Path $env:ProgramFiles 'WindowsPowerShell\Modules')
$context=Get-Content -LiteralPath $ContextPath -Raw -Encoding UTF8|ConvertFrom-Json
function Save-Result($Value){[IO.File]::WriteAllText($context.result_path,($Value|ConvertTo-Json -Depth 10),[Text.UTF8Encoding]::new($false))}
function Start-NativeInstalledProcess([string]$FilePath,[string]$Arguments,[string]$WorkingDirectory,[string]$StdoutPath,[string]$StderrPath) {
  $started=Start-Process -FilePath $FilePath -ArgumentList $Arguments -WorkingDirectory $WorkingDirectory -WindowStyle Hidden -PassThru -RedirectStandardOutput $StdoutPath -RedirectStandardError $StderrPath
  # Windows PowerShell 5 的 PassThru Process 需在进程存活时取得 Handle，才能在退出后保留真实退出状态。
  $handle=$started.Handle
  if($handle -eq [IntPtr]::Zero){throw 'NATIVE_APP_PROCESS_HANDLE_UNAVAILABLE'}
  return $started
}
function Start-NativeRetainedPersonal($Context,[string]$PersonalPath,[string]$Sid,[int]$Session) {
  if($Context.data_mode -ne 'Retain' -or -not $Context.native_user_session_sha256){throw 'NATIVE_RETAIN_SESSION_REQUIRED'}
  $lines=@(Get-Content -LiteralPath $PersonalPath -Encoding UTF8|Where-Object {$_ -match '^PARTYOPS_PORT='})
  if($lines.Count -ne 1 -or $lines[0] -notmatch '^PARTYOPS_PORT=(\d{4,5})$'){throw 'NATIVE_RETAIN_PORT_INVALID'}
  $port=[int]$matches[1]
  if($port -lt 1024 -or $port -gt 65534){throw 'NATIVE_RETAIN_PORT_INVALID'}
  # 新轮次必须实际创建进程，不能复用旧 marker/PID 或向已占用端口发送凭据。
  if(@(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue).Count){throw 'NATIVE_RETAIN_PORT_ALREADY_IN_USE'}
  foreach($existing in @(Get-CimInstance Win32_Process -Filter "Name='PartyOps.exe'")){
    $owner=Invoke-CimMethod -InputObject $existing -MethodName GetOwnerSid
    if($owner.ReturnValue -ne 0){throw 'NATIVE_RETAIN_EXISTING_OWNER_UNAVAILABLE'}
    if($owner.Sid -eq $Sid){throw 'NATIVE_RETAIN_OLD_RUNTIME_STILL_RUNNING'}
  }
  if((Get-FileHash -LiteralPath $Context.launcher_path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $Context.launcher_sha256){throw 'NATIVE_CHILD_LAUNCHER_HASH_MISMATCH'}
  $startedAt=[DateTime]::UtcNow
  $launcher=Start-NativeInstalledProcess $Context.launcher_path '--background' $Context.data_directory $Context.stdout_path $Context.stderr_path
  if(-not $launcher.WaitForExit(180000)){throw 'NATIVE_RETAIN_LAUNCHER_TIMEOUT'}
  $launcher.Refresh()
  if($null -eq $launcher.ExitCode){throw 'NATIVE_RETAIN_LAUNCHER_EXIT_UNAVAILABLE'}
  if($launcher.ExitCode -ne 0){throw ('NATIVE_RETAIN_LAUNCHER_FAILED_'+$launcher.ExitCode)}
  $markerPath=Join-Path $Context.data_directory '.partyops-personal-process.json'
  $marker=Get-Content -LiteralPath $markerPath -Raw -Encoding UTF8|ConvertFrom-Json
  if($marker.format_version -ne 1 -or $marker.executable -ine $Context.app_path -or $marker.pid -le 0){throw 'NATIVE_RETAIN_PROCESS_MARKER_INVALID'}
  $listeners=@(Get-NetTCPConnection -LocalPort $port -State Listen)
  if($listeners.Count -ne 1 -or $listeners[0].LocalAddress -ne '127.0.0.1' -or $listeners[0].OwningProcess -ne $marker.pid){throw 'NATIVE_RETAIN_LISTENER_MISMATCH'}
  $actual=Get-CimInstance Win32_Process -Filter ('ProcessId='+$marker.pid)
  $owner=Invoke-CimMethod -InputObject $actual -MethodName GetOwnerSid
  if($owner.ReturnValue -ne 0 -or $owner.Sid -ne $Sid -or $actual.SessionId -ne $Session -or
     $actual.ExecutablePath -ine $Context.app_path -or $actual.CreationDate.ToUniversalTime() -lt $startedAt){throw 'NATIVE_RETAIN_RUNTIME_IDENTITY_MISMATCH'}
  $hash=(Get-FileHash -LiteralPath $actual.ExecutablePath -Algorithm SHA256).Hash.ToLowerInvariant()
  if($hash -ne $Context.app_sha256){throw 'NATIVE_RETAIN_RUNTIME_HASH_MISMATCH'}
  return @{pid=[int]$actual.ProcessId;owner_sid=$owner.Sid;session_id=[int]$actual.SessionId;
    executable_path=$actual.ExecutablePath;sha256=$hash;created_at=$actual.CreationDate.ToUniversalTime().ToString('o');
    port=$port;address='127.0.0.1';launcher_exit_code=[int]$launcher.ExitCode;started_at=$startedAt.ToString('o')}
}
$result=[ordered]@{schema_version=1;generated_at=(Get-Date).ToString('o');status='failed';runtime_environment_passed=$false;
  target='win11-x64-native';environment_type='native-host';scope='standard-token-installed-startup-diagnostic';
  run_directory=$context.run_directory;native_workspace=$context.native_workspace;app_sha256=$context.app_sha256;
  native_user_session_sha256=$context.native_user_session_sha256;data_mode=$context.data_mode;
  gui_verified=$false;first_configuration_verified=$false}
try{
  $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
  $principal=[Security.Principal.WindowsPrincipal]::new($identity)
  $session=[Diagnostics.Process]::GetCurrentProcess().SessionId
  # 即使后续 profile 校验失败，也保留已经实际观察到的令牌与环境，避免被外层缺字段掩盖。
  $result.username=$identity.Name;$result.sid=$identity.User.Value;$result.session_id=$session
  $result.is_admin=$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
  $result.environment_before=@{user_profile=$env:USERPROFILE;local_appdata=$env:LOCALAPPDATA;appdata=$env:APPDATA;username=$env:USERNAME}
  if($identity.User.Value -ne $context.sid -or $identity.Name -ine ($context.machine_name+'\PartyOpsNativeQA')){throw 'NATIVE_CHILD_SID_MISMATCH'}
  if($result.is_admin -or @($identity.Groups|ForEach-Object {$_.Value}) -contains 'S-1-5-32-544'){throw 'NATIVE_CHILD_ADMINISTRATOR_TOKEN'}
  if($session -le 0 -or $session -ne $context.session_id){throw 'NATIVE_CHILD_SESSION_MISMATCH'}
  $registered=(Get-ItemProperty -LiteralPath ('HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\'+$identity.User.Value)).ProfileImagePath
  $registered=[Environment]::ExpandEnvironmentVariables($registered)
  $profile=[Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
  $local=[Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
  $roaming=[Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)
  $result.registered_profile=$registered;$result.roaming_appdata=$roaming
  $result.user_profile=$profile;$result.local_appdata=$local;$result.is_admin=$false
  if(-not $profile -or $profile -ine $registered -or $local -ine (Join-Path $profile 'AppData\Local') -or
      $roaming -ine (Join-Path $profile 'AppData\Roaming') -or $env:USERPROFILE -ine $profile -or
      $env:LOCALAPPDATA -ine $local -or $env:APPDATA -ine $roaming -or $env:USERNAME -ine 'PartyOpsNativeQA'){throw 'NATIVE_CHILD_PROFILE_MISMATCH'}
  # 环境必须在 Windows PowerShell 初始化前正确传入；进程内晚改变量不能消除 Known Folder 缓存。
  $result.environment_initialized_before_process_start=$true
  $env:TEMP=Join-Path $context.native_workspace 'temp';$env:TMP=$env:TEMP
  $env:PARTYOPS_DATA_DIR=$context.data_directory
  $env:PYTHONUTF8='1'
  if((Get-FileHash -LiteralPath $context.app_path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $context.app_sha256){throw 'NATIVE_CHILD_APP_HASH_MISMATCH'}
  $executable=$context.app_path;$arguments='--startup-desktop-user-self-test'
  if($context.action -in @('RunConfiguredPersonalProbe','LaunchPersonal')){
    $modePath=Join-Path $local 'PartyOps\mode.json'
    $mode=Get-Content -LiteralPath $modePath -Raw -Encoding UTF8|ConvertFrom-Json
    $personalPath=Join-Path $local 'PartyOps\personal.env'
    if($mode.mode -ne 'personal' -or $mode.config_path -ine $personalPath){throw 'NATIVE_CONFIGURED_PERSONAL_MODE_REQUIRED'}
    $personalFile=Get-Item -LiteralPath $personalPath
    if($personalFile.Length -gt 65536 -or ($personalFile.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'NATIVE_PERSONAL_CONFIG_FILE_INVALID'}
    # 验证配置自身指定本轮数据目录；不得让继承的环境变量替代缺失/异位的配置。
    $dataLines=@(Get-Content -LiteralPath $personalPath -Encoding UTF8|Where-Object {$_ -match '^PARTYOPS_DATA_DIR='})
    if($dataLines.Count -ne 1){throw 'NATIVE_PERSONAL_CONFIG_DATA_BINDING_MISSING'}
    $dataValue=$dataLines[0].Substring('PARTYOPS_DATA_DIR='.Length).Trim()
    $expected=$context.data_directory
    if($dataValue -cne $expected -and $dataValue -cne ("'"+$expected+"'") -and $dataValue -cne ('"'+$expected+'"')){throw 'NATIVE_PERSONAL_CONFIG_DATA_BINDING_MISMATCH'}
    $result.configured_personal=@{mode_path=$modePath;config_path=$personalPath;data_directory=$expected;
      config_sha256=(Get-FileHash -LiteralPath $personalPath -Algorithm SHA256).Hash.ToLowerInvariant()}
    $arguments='--startup-configured-personal-permission-self-test'
    $result.scope='standard-token-configured-personal-permission-diagnostic'
  }
  if($context.action -eq 'LaunchWizard'){
    $config=Join-Path $local 'PartyOps'
    $urlFile=Join-Path $config 'wizard.url'
    if((Test-Path -LiteralPath (Join-Path $config 'mode.json')) -or (Test-Path -LiteralPath $urlFile)){throw 'NATIVE_USER_ALREADY_CONFIGURED_OR_WIZARD_EXISTS'}
    $executable=$context.wizard_path;$arguments='--no-browser --initial-role personal'
    if((Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant() -ne $context.wizard_sha256){throw 'NATIVE_CHILD_WIZARD_HASH_MISMATCH'}
  }
  if($context.action -eq 'LaunchPersonal'){
    $result.runtime=Start-NativeRetainedPersonal $context $personalPath $identity.User.Value $session
    $result.scope='standard-token-retained-personal-runtime-ready';$result.exit_code=$null
  }else{
  $process=Start-NativeInstalledProcess $executable $arguments $env:PARTYOPS_DATA_DIR $context.stdout_path $context.stderr_path
  $actual=Get-CimInstance Win32_Process -Filter ('ProcessId='+$process.Id)
  $owner=Invoke-CimMethod -InputObject $actual -MethodName GetOwnerSid
  if($owner.ReturnValue -ne 0 -or $owner.Sid -ne $context.sid -or $actual.SessionId -ne $session -or $actual.ExecutablePath -ine $executable){throw 'NATIVE_APP_PROCESS_OWNER_MISMATCH'}
  $result.process=@{pid=$process.Id;owner_sid=$owner.Sid;session_id=$actual.SessionId;executable_path=$actual.ExecutablePath}
  if($context.action -eq 'LaunchWizard'){
    $deadline=[DateTime]::UtcNow.AddSeconds(120)
    while(-not(Test-Path -LiteralPath $urlFile)){
      if($process.HasExited){throw 'NATIVE_WIZARD_EXITED_BEFORE_READY'}
      if([DateTime]::UtcNow -gt $deadline){throw 'NATIVE_WIZARD_READY_TIMEOUT'}
      Start-Sleep -Milliseconds 250
    }
    $url=[IO.File]::ReadAllText($urlFile,[Text.Encoding]::UTF8).Trim()
    $uri=[uri]$url
    if($uri.Scheme -ne 'http' -or $uri.Host -ne '127.0.0.1' -or $uri.Port -le 0 -or $uri.Query -or $uri.Fragment -or $uri.AbsolutePath -ne '/') {throw 'NATIVE_WIZARD_URL_INVALID'}
    $listeners=@(Get-NetTCPConnection -LocalPort $uri.Port -State Listen|Where-Object {$_.OwningProcess -eq $process.Id})
    if(-not $listeners.Count){throw 'NATIVE_WIZARD_LISTENER_OWNER_MISMATCH'}
    $result.wizard=@{pid=$process.Id;owner_sid=$owner.Sid;session_id=$actual.SessionId;executable_path=$actual.ExecutablePath;
      sha256=$context.wizard_sha256;url_file=$urlFile;url=$url;created_at=$actual.CreationDate.ToUniversalTime().ToString('o')}
    $result.scope='standard-token-personal-wizard-ready';$result.exit_code=$null
  }else{
    if(-not $process.WaitForExit(180000)){throw 'NATIVE_APP_SELFTEST_TIMEOUT'}
    $process.Refresh();$result.exit_code=$process.ExitCode
    if($null -eq $result.exit_code){throw 'NATIVE_APP_EXIT_CODE_UNAVAILABLE'}
    $lines=@(Get-Content -LiteralPath $context.stdout_path -Encoding UTF8|Where-Object {$_.TrimStart().StartsWith('{')})
    if(-not $lines.Count){throw 'NATIVE_APP_SELFTEST_JSON_MISSING'}
    $result.selftest=$lines[-1]|ConvertFrom-Json
    if($result.exit_code -ne 0 -or $result.selftest.passed -ne $true){throw 'NATIVE_APP_SELFTEST_FAILED'}
    if($context.action -eq 'RunConfiguredPersonalProbe'){
      if($result.selftest.mode -ne 'configured-personal-permission' -or $result.selftest.checked -ne $true -or $result.selftest.data_dir_writable -ne $true){throw 'NATIVE_PERSONAL_PERMISSION_NOT_CHECKED'}
      if((Get-FileHash -LiteralPath $result.configured_personal.config_path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $result.configured_personal.config_sha256){throw 'NATIVE_PERSONAL_CONFIG_CHANGED_DURING_PROBE'}
    }elseif($result.selftest.desktop_user -ne $true){throw 'NATIVE_APP_SELFTEST_FAILED'}
  }
  }
  $result.status='passed'
}catch{$result.error=$_.Exception.Message}
Save-Result $result
if($result.status -ne 'passed'){exit 1}
# 二次登录的父进程退出会卸载 profile；向导/业务测试期间保留此专用进程。
# 仅控制器可写根目录中的释放标记；释放前应先正常关闭该测试账号的向导与业务实例。
if($context.action -in @('LaunchWizard','LaunchPersonal')){
  while(-not(Test-Path -LiteralPath $context.profile_release_path)){Start-Sleep -Milliseconds 500}
}
'@
}

function Get-NativeSecondaryLogonArguments([string]$ProbeId,[string]$ShellPath) {
  if($ProbeId -notmatch '^standard-user-probe-[a-f0-9]{32}$'){throw 'NATIVE_PROBE_ID_INVALID'}
  # CreateProcessWithLogonW 的总命令行上限为 1024 字符。只编码读取器，正文留在管理员可写的工作目录。
  # .NET 相对路径使用 Start-Process 显式传入的工作目录；ContextPath 先转换为绝对路径，保全中文。
  $command="& ([scriptblock]::Create([IO.File]::ReadAllText('"+$ProbeId+".ps1',[Text.Encoding]::UTF8))) -ContextPath ([IO.Path]::GetFullPath('"+$ProbeId+".json'))"
  $encoded=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
  $arguments='-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand '+$encoded
  $length=('"'+$ShellPath+'" '+$arguments).Length+1
  if($length -gt 1024){throw 'NATIVE_SECONDARY_LOGON_COMMAND_TOO_LONG'}
  return [pscustomobject]@{arguments=$arguments;command_line_characters_including_nul=$length;command_line_limit=1024}
}

function Get-NativeProfileEnvironment($Context,$Record) {
  $key='HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\'+$Record.sid
  if(-not(Test-Path -LiteralPath $key)){throw 'NATIVE_PROFILE_REGISTRATION_REQUIRED'}
  $profile=(Get-ItemProperty -LiteralPath $key).ProfileImagePath
  $profile=[Environment]::ExpandEnvironmentVariables($profile)
  if(-not [IO.Path]::IsPathRooted($profile) -or -not(Test-Path -LiteralPath $profile)){throw 'NATIVE_REGISTERED_PROFILE_MISSING'}
  $drive=[IO.Path]::GetPathRoot($profile)
  return @{USERPROFILE=$profile;LOCALAPPDATA=(Join-Path $profile 'AppData\Local');APPDATA=(Join-Path $profile 'AppData\Roaming');
    USERNAME=$Record.username;USERDOMAIN=$Context.machine_name;HOMEDRIVE=$drive.TrimEnd('\');HOMEPATH=$profile.Substring($drive.Length-1);
    TEMP=(Join-Path $Context.native_workspace 'temp');TMP=(Join-Path $Context.native_workspace 'temp')}
}

function Assert-NativeProbeReceipt($Result,$Record,$Context) {
  if($Result.sid -ne $Record.sid -or $Result.session_id -ne $Context.session_id -or $Result.app_sha256 -ne $Context.app_sha256){
    $detail=$(if($Result.error){'; child_error='+$Result.error}else{''})
    throw ('NATIVE_PROBE_RECEIPT_IDENTITY_MISMATCH'+$detail)
  }
}

function Invoke-NativeUserProbe($Context,[string]$Mode='RunProbe') {
  $private=Join-Path $Context.state_directory 'private'
  $sessionPath=Join-Path $Context.run_directory 'native-user-session.json'
  if(Test-Path -LiteralPath $sessionPath){$record=Read-NativeRevalidationSession $Context}else{
    $record=Get-Content -LiteralPath (Join-Path $private 'ownership.json') -Raw -Encoding UTF8|ConvertFrom-Json
    $user=Get-LocalUser -Name 'PartyOpsNativeQA' -ErrorAction SilentlyContinue
    Assert-NativeOwnership $user $record $Context
    Get-NativeMembership $record.sid
    if(-not $user.Enabled -or $record.status -ne 'prepared'){throw 'NATIVE_USER_NOT_PREPARED'}
  }
  $receiptName=$(if($Mode -eq 'LaunchWizard'){'standard-user-wizard.json'}elseif($Mode -eq 'LaunchPersonal'){'standard-user-runtime.json'}elseif($Mode -eq 'RunConfiguredPersonalProbe'){'standard-user-personal-permission.json'}else{'standard-user-latest.json'})
  if($record.session_sha256 -and (Test-Path -LiteralPath (Join-Path $Context.run_directory $receiptName))){throw 'NATIVE_REVALIDATION_PROBE_EVIDENCE_EXISTS'}
  if($Mode -in @('LaunchWizard','LaunchPersonal')){
    $probe=Get-Content -LiteralPath (Join-Path $Context.run_directory 'standard-user-latest.json') -Raw -Encoding UTF8|ConvertFrom-Json
    if($probe.status -ne 'passed' -or $probe.sid -ne $record.sid -or $probe.is_admin -ne $false -or
        $probe.install_result_sha256 -ne $Context.install_result_sha256 -or $probe.app_sha256 -ne $Context.app_sha256){throw 'NATIVE_STANDARD_USER_PROBE_REQUIRED'}
  }
  $credential=Read-NativeCredential (Join-Path $private 'credential.clixml') $Context $record.credential_sha256
  if($credential -isnot [pscredential] -or $credential.UserName -ine ($Context.machine_name+'\PartyOpsNativeQA')){throw 'NATIVE_CREDENTIAL_ACCOUNT_MISMATCH'}
  # 全新账号首次二次登录才会由 Windows 建立 profile。先运行只读 OS 探针，不触碰产品。
  if(-not(Test-Path -LiteralPath ('HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\'+$record.sid))){
    & (Join-Path $PSScriptRoot 'diagnose-windows-native-profile.ps1') -RunDirectory $Context.run_directory|Out-Null
  }
  $profileEnvironment=Get-NativeProfileEnvironment $Context $record
  $probeId='standard-user-probe-'+[guid]::NewGuid().ToString('N')
  $output=Resolve-NativeChild $Context.native_workspace (Join-Path $Context.native_workspace ('probe-output\'+$probeId))
  New-Item -ItemType Directory -Path $output|Out-Null
  $report=Join-Path $Context.run_directory $probeId
  New-Item -ItemType Directory -Path $report|Out-Null
  $scriptPath=Join-Path $Context.native_workspace ($probeId+'.ps1')
  $contextPath=Join-Path $Context.native_workspace ($probeId+'.json')
  $payload=[ordered]@{run_directory=$Context.run_directory;native_workspace=$Context.native_workspace;
    data_directory=$record.data_directory;action=$Mode;wizard_path=$Context.wizard_path;wizard_sha256=$Context.wizard_sha256;
    launcher_path=$Context.launcher_path;launcher_sha256=$Context.launcher_sha256;
    native_user_session_sha256=$record.session_sha256;data_mode=$record.data_mode;
    profile_release_path=(Join-Path $Context.native_workspace ($probeId+'.release-profile'));
    sid=$record.sid;machine_name=$Context.machine_name;session_id=$Context.session_id;
    app_path=$Context.app_path;app_sha256=$Context.app_sha256;result_path=(Join-Path $output 'result.json');
    stdout_path=(Join-Path $output 'app.stdout.log');stderr_path=(Join-Path $output 'app.stderr.log')}
  Write-NativeJson $contextPath $payload
  [IO.File]::WriteAllText($scriptPath,(Get-NativeProbeScript),[Text.UTF8Encoding]::new($false))
  $shell=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
  $launch=Get-NativeSecondaryLogonArguments $probeId $shell
  Write-NativeJson (Join-Path $report 'launch-request.json') ([ordered]@{
    generated_at=(Get-Date).ToString('o');action=$Mode;script_path=$scriptPath;context_path=$contextPath;
    script_sha256=(Get-FileHash -LiteralPath $scriptPath -Algorithm SHA256).Hash.ToLowerInvariant();
    working_directory=$Context.native_workspace;shell_path=$shell;profile_environment=$profileEnvironment;
    command_line_characters_including_nul=$launch.command_line_characters_including_nul;command_line_limit=$launch.command_line_limit})
  try{
    $process=Start-Process -FilePath $shell -ArgumentList $launch.arguments -WorkingDirectory $Context.native_workspace -Credential $credential -LoadUserProfile -Environment $profileEnvironment -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $output 'controller.stdout.log') -RedirectStandardError (Join-Path $output 'controller.stderr.log')
    $secondaryHandle=$process.Handle
    if($secondaryHandle -eq [IntPtr]::Zero){throw 'NATIVE_SECONDARY_LOGON_HANDLE_UNAVAILABLE'}
  }catch{
    Write-NativeJson (Join-Path $report 'launch-failure.json') ([ordered]@{generated_at=(Get-Date).ToString('o');
      status='failed';stage='secondary-logon-start';error=$_.Exception.Message;runtime_environment_passed=$false})
    throw
  }
  $actual=Get-CimInstance Win32_Process -Filter ('ProcessId='+$process.Id)
  $owner=Invoke-CimMethod -InputObject $actual -MethodName GetOwnerSid
  if($owner.ReturnValue -ne 0 -or $owner.Sid -ne $record.sid -or $actual.SessionId -ne $Context.session_id){throw 'NATIVE_SECONDARY_LOGON_TOKEN_MISMATCH'}
  if($Mode -in @('LaunchWizard','LaunchPersonal')){
    $deadline=[DateTime]::UtcNow.AddSeconds(150)
    while(-not(Test-Path -LiteralPath $payload.result_path)){
      if($process.HasExited){throw 'NATIVE_WIZARD_PROFILE_HOLDER_EXITED'}
      if([DateTime]::UtcNow -gt $deadline){throw 'NATIVE_WIZARD_PROFILE_HOLDER_TIMEOUT'}
      Start-Sleep -Milliseconds 250
    }
  }else{
    if(-not $process.WaitForExit(240000)){throw 'NATIVE_SECONDARY_LOGON_PROBE_TIMEOUT'}
  }
  $process.Refresh()
  foreach($name in @('result.json','app.stdout.log','app.stderr.log','controller.stdout.log','controller.stderr.log')){
    $path=Resolve-NativeChild $output (Join-Path $output $name)
    if(Test-Path -LiteralPath $path){Copy-Item -LiteralPath $path -Destination (Join-Path $report $name)}
  }
  $result=Get-Content -LiteralPath (Join-Path $report 'result.json') -Raw -Encoding UTF8|ConvertFrom-Json
  Assert-NativeProbeReceipt $result $record $Context
  if($record.session_sha256){
    $verifiedSession=Read-NativeRevalidationSession $Context
    if($verifiedSession.session_sha256 -ne $record.session_sha256 -or $result.native_user_session_sha256 -ne $record.session_sha256){throw 'NATIVE_REVALIDATION_SESSION_CHANGED_DURING_PROBE'}
  }
  $receipt=[ordered]@{schema_version=1;generated_at=(Get-Date).ToString('o');run_directory=$Context.run_directory;
    native_workspace=$Context.native_workspace;data_directory=$record.data_directory;username=$record.username;sid=$record.sid;session_id=$Context.session_id;
    machine_name=$Context.machine_name;hardware_uuid=$Context.hardware_uuid;
    user_profile=$result.user_profile;local_appdata=$result.local_appdata;is_admin=$result.is_admin;
    secondary_logon_pid=$process.Id;secondary_logon_owner_sid=$owner.Sid;secondary_logon_exit_code=$(if($process.HasExited){$process.ExitCode}else{$null});
    app_sha256=$Context.app_sha256;install_result_sha256=$Context.install_result_sha256;
    native_user_session_sha256=$record.session_sha256;data_mode=$record.data_mode;
    status=$result.status;error=$result.error;report_directory=$report;result_sha256=(Get-FileHash -LiteralPath (Join-Path $report 'result.json') -Algorithm SHA256).Hash.ToLowerInvariant();
    gui_verified=$false;first_configuration_verified=$false;runtime_environment_passed=$false}
  Write-NativeJson (Join-Path $report 'identity-receipt.json') $receipt
  if($Mode -eq 'RunConfiguredPersonalProbe'){
    $receipt.configured_personal=$result.configured_personal
    $receipt.selftest=$result.selftest
    Write-NativeJson (Join-Path $report 'identity-receipt.json') $receipt
    Write-NativeJson (Join-Path $Context.run_directory 'standard-user-personal-permission.json') $receipt
  }elseif($Mode -in @('LaunchWizard','LaunchPersonal')){
    if($Mode -eq 'LaunchWizard'){$receipt.wizard=$result.wizard}else{$receipt.runtime=$result.runtime}
    $receipt.profile_holder=@{pid=$process.Id;owner_sid=$owner.Sid;session_id=$Context.session_id;release_file=$payload.profile_release_path}
    Write-NativeJson (Join-Path $report 'identity-receipt.json') $receipt
    Write-NativeJson (Join-Path $Context.run_directory $receiptName) $receipt
  }else{Write-NativeJson (Join-Path $Context.run_directory 'standard-user-latest.json') $receipt}
  [pscustomobject]@{status=$result.status;sid=$record.sid;session_id=$Context.session_id;report_directory=$report;runtime_environment_passed=$false}|ConvertTo-Json -Compress
  if(($Mode -notin @('LaunchWizard','LaunchPersonal') -and $process.ExitCode -ne 0) -or $result.status -ne 'passed'){
    throw ('NATIVE_STANDARD_USER_PROBE_FAILED; child_error='+$result.error)
  }
}

$context=Get-NativeContext $RunDirectory
if($Action -eq 'Prepare'){
  $record=Prepare-NativeUser $context $RecoverCredentialSha256
  [pscustomobject]@{status=$record.status;username=$record.username;sid=$record.sid;native_workspace=$record.native_workspace;runtime_environment_passed=$false}|ConvertTo-Json -Compress
}elseif($Action -eq 'PrepareRevalidation'){
  if($RecoverCredentialSha256){throw 'NATIVE_REVALIDATION_DOES_NOT_RECOVER_OR_REWRITE_CREDENTIAL'}
  $record=Prepare-NativeRevalidation $context $DataMode
  [pscustomobject]@{status=$record.status;username=$record.username;sid=$record.sid;data_mode=$record.data_mode;
    data_directory=$record.data_directory;native_workspace=$record.native_workspace;session_sha256=$record.session_sha256;
    account_changed=$false;credential_changed=$false;runtime_environment_passed=$false}|ConvertTo-Json -Compress
}else{Invoke-NativeUserProbe $context $Action}
