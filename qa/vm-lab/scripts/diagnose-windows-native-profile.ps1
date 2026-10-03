# 仅用本轮专用普通账号读取 Windows profile/环境；不启动或配置 PartyOps，不写注册表。
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$RunDirectory,[switch]$InitializeBeforeLookup,[switch]$UseProfileEnvironment)
$ErrorActionPreference='Stop'
$tokens=$null;$errors=$null
$source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'windows-native-user.ps1'),[Text.Encoding]::UTF8)
$ast=[Management.Automation.Language.Parser]::ParseInput($source,[ref]$tokens,[ref]$errors)
if($errors.Count){throw ($errors|Out-String)}
foreach($node in $ast.FindAll({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst]},$false)){
  . ([scriptblock]::Create($node.Extent.Text))
}
$context=Get-NativeContext $RunDirectory
$private=Join-Path $context.state_directory 'private'
$record=Get-Content -LiteralPath (Join-Path $private 'ownership.json') -Raw -Encoding UTF8|ConvertFrom-Json
$user=Get-LocalUser -Name 'PartyOpsNativeQA'
Assert-NativeOwnership $user $record $context
Get-NativeMembership $record.sid
if($record.status -ne 'prepared' -or -not $user.Enabled){throw 'NATIVE_USER_NOT_PREPARED'}
$credential=Read-NativeCredential (Join-Path $private 'credential.clixml') $context $record.credential_sha256
$probeId='standard-user-probe-'+[guid]::NewGuid().ToString('N')
$output=Resolve-NativeChild $context.native_workspace (Join-Path $context.native_workspace ('probe-output\'+$probeId))
New-Item -ItemType Directory -Path $output|Out-Null
$report=Join-Path $context.run_directory ('os-profile-diagnostic-'+$probeId.Substring(20))
New-Item -ItemType Directory -Path $report|Out-Null
$child=@'
param([string]$ContextPath)
$ErrorActionPreference='Stop'
$env:PSModulePath=(Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\Modules')+';'+(Join-Path $env:ProgramFiles 'WindowsPowerShell\Modules')
$context=Get-Content -LiteralPath $ContextPath -Raw -Encoding UTF8|ConvertFrom-Json
$result=[ordered]@{scope='os-only-profile-diagnostic';generated_at=(Get-Date).ToString('o');status='failed'}
try{
  $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
  $principal=[Security.Principal.WindowsPrincipal]::new($identity)
  $result.sid=$identity.User.Value;$result.username=$identity.Name
  $result.session_id=[Diagnostics.Process]::GetCurrentProcess().SessionId
  $result.is_admin=$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
  if($result.sid -ne $context.sid -or $result.username -ine $context.name -or $result.is_admin -or $result.session_id -ne $context.session_id){throw 'OS_DIAGNOSTIC_IDENTITY_MISMATCH'}
  function Read-Folders {
    return @{profile=[Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile);local=[Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData);roaming=[Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)}
  }
  $registered=(Get-ItemProperty -LiteralPath ('HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\'+$identity.User.Value)).ProfileImagePath
  $volatile=Get-ItemProperty -LiteralPath 'HKCU:\Volatile Environment' -ErrorAction SilentlyContinue
  $rawKey=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders')
  $raw=@{}
  try{foreach($name in @('Local AppData','AppData','Personal')){$raw[$name]=$rawKey.GetValue($name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)}}finally{if($rawKey){$rawKey.Dispose()}}
  $result.registered_profile=$registered;$result.hkcu_volatile_profile=$volatile.USERPROFILE
  $result.hkcu_user_shell_folders_raw=$raw
  $result.environment_before=@{user_profile=$env:USERPROFILE;local_appdata=$env:LOCALAPPDATA;appdata=$env:APPDATA;username=$env:USERNAME;userdomain=$env:USERDOMAIN;homedrive=$env:HOMEDRIVE;homepath=$env:HOMEPATH}
  $result.initialized_before_known_folder_lookup=$context.initialize_before_lookup
  if(-not $context.initialize_before_lookup){$result.special_folders_before=Read-Folders}
  # 仅修正这个临时 OS 子进程的环境并再次观察，退出后环境随进程消失。
  $env:USERPROFILE=[Environment]::ExpandEnvironmentVariables($registered)
  $env:LOCALAPPDATA=Join-Path $env:USERPROFILE 'AppData\Local'
  $env:APPDATA=Join-Path $env:USERPROFILE 'AppData\Roaming'
  $result.special_folders_after=Read-Folders
  $result.status='observed'
}catch{$result.error=$_.Exception.Message}
[IO.File]::WriteAllText($context.result_path,($result|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
if($result.status -ne 'observed'){exit 1}
'@
$scriptPath=Join-Path $context.native_workspace ($probeId+'.ps1')
$contextPath=Join-Path $context.native_workspace ($probeId+'.json')
[IO.File]::WriteAllText($scriptPath,$child,[Text.UTF8Encoding]::new($false))
Write-NativeJson $contextPath @{sid=$record.sid;name=$credential.UserName;session_id=$context.session_id;initialize_before_lookup=[bool]$InitializeBeforeLookup;result_path=(Join-Path $output 'os-profile-result.json')}
$shell=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$launch=Get-NativeSecondaryLogonArguments $probeId $shell
$start=@{FilePath=$shell;ArgumentList=$launch.arguments;WorkingDirectory=$context.native_workspace;Credential=$credential;LoadUserProfile=$true;WindowStyle='Hidden';PassThru=$true;RedirectStandardOutput=(Join-Path $output 'stdout.log');RedirectStandardError=(Join-Path $output 'stderr.log')}
if($UseProfileEnvironment){
  $registered=(Get-ItemProperty -LiteralPath ('HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\'+$record.sid)).ProfileImagePath
  $registered=[Environment]::ExpandEnvironmentVariables($registered)
  $drive=[IO.Path]::GetPathRoot($registered)
  $start.Environment=@{USERPROFILE=$registered;LOCALAPPDATA=(Join-Path $registered 'AppData\Local');APPDATA=(Join-Path $registered 'AppData\Roaming');USERNAME=$record.username;USERDOMAIN=$context.machine_name;HOMEDRIVE=$drive.TrimEnd('\');HOMEPATH=$registered.Substring($drive.Length-1);TEMP=(Join-Path $context.native_workspace 'temp');TMP=(Join-Path $context.native_workspace 'temp')}
}
$process=Start-Process @start
if(-not $process.WaitForExit(30000)){throw 'OS_PROFILE_DIAGNOSTIC_TIMEOUT'}
$process.Refresh()
foreach($name in @('os-profile-result.json','stdout.log','stderr.log')){
  $path=Join-Path $output $name
  if(Test-Path -LiteralPath $path){Copy-Item -LiteralPath $path -Destination (Join-Path $report $name)}
}
Write-NativeJson (Join-Path $report 'execution.json') @{scope='os-only-profile-diagnostic';exit_code=$process.ExitCode;process_id=$process.Id;command_line_characters_including_nul=$launch.command_line_characters_including_nul;runtime_environment_passed=$false}
[pscustomobject]@{exit_code=$process.ExitCode;report_directory=$report}|ConvertTo-Json -Compress
Get-Content -LiteralPath (Join-Path $report 'os-profile-result.json') -Encoding UTF8
