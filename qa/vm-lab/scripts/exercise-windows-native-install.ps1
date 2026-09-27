# 对用户授权的本机现有安装执行当前候选替换，明确不计作干净系统或旧版升级。
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$RunDirectory)
$ErrorActionPreference='Stop'
function Test-NativeInstalledCandidate($Registration,$Result,$Candidate,$Binding,[string]$InstallDirectory) {
  try {
    return ($Result.installer_exit_code -eq 0 -and $Registration.DisplayVersion -eq $Candidate.version -and
      $Registration.InstallLocation -and [IO.Path]::GetFullPath($Registration.InstallLocation).TrimEnd('\') -eq $InstallDirectory -and
      $Result.installed_executable_sha256 -eq $Binding.windows_payload.executables.'PartyOps.exe'.sha256 -and
      $Result.installed_manifest_sha256 -eq $Binding.windows_payload.manifest.sha256)
  } catch {return $false}
}
$run=[IO.Path]::GetFullPath($RunDirectory).TrimEnd('\')
if(-not $run.StartsWith('D:\PartyOps-VM-Lab\reports\win11-x64-native\',[StringComparison]::OrdinalIgnoreCase)){throw 'NATIVE_RUN_OUTSIDE_LAB'}
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$controller=Join-Path $repo 'backend\.venv\Scripts\python.exe'
$verifier=Join-Path $PSScriptRoot 'prepare-native-candidate.py'
# 期望载荷来自真实构建结束时封存的清单；不能用安装后的自身哈希追认。
$verified=@(& $controller $verifier --run-directory $run --verify-only)
if($LASTEXITCODE -ne 0){throw 'NATIVE_PREINSTALL_BUILD_BINDING_REJECTED'}
$binding=($verified -join "`n")|ConvertFrom-Json
foreach($name in @('install-start.json','install-result.json','install.log')){
  if(Test-Path -LiteralPath (Join-Path $run $name)){throw 'NATIVE_INSTALL_EVIDENCE_EXISTS_USE_NEW_RUN'}
}
$protection=Get-Content -LiteralPath (Join-Path $run 'protection.json') -Raw | ConvertFrom-Json
$candidate=$binding.package
if($protection.target -ne 'win11-x64-native' -or $candidate.provenance_status -ne 'verified' -or $candidate.id -ne 'windows_amd64'){throw 'NATIVE_CONTEXT_NOT_VERIFIED'}
foreach($root in $protection.protected){
  if($root.verified -ne $true){throw 'NATIVE_BACKUP_INCOMPLETE'}
  foreach($file in $root.files){
    $backup=Join-Path $root.backup $file.relative_path
    if((Get-FileHash -LiteralPath $backup -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256){throw 'NATIVE_BACKUP_CHANGED'}
  }
}
$package=[IO.Path]::GetFullPath($candidate.path)
if([IO.Path]::GetDirectoryName($package) -ne 'E:\codex\PartyOps\artifacts' -or
   [IO.Path]::GetFileName($package) -ne ('PartyOps_'+$candidate.version+'_windows_amd64.exe')){throw 'NATIVE_INSTALLER_PATH_UNEXPECTED'}
if((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant() -ne $candidate.sha256){throw 'NATIVE_INSTALLER_HASH_CHANGED'}
$os=Get-CimInstance Win32_OperatingSystem
if($os.Caption -notmatch 'Windows 11' -or -not [Environment]::Is64BitOperatingSystem){throw 'NATIVE_WINDOWS11_X64_REQUIRED'}
if(@(Get-CimInstance Win32_Process|Where-Object {$_.Name -match '^PartyOps.*\.exe$'}).Count){throw 'PARTYOPS_ALREADY_RUNNING'}
$install=[IO.Path]::GetFullPath($protection.existing_install)
if($install -ne 'E:\PartyOps1\PartyOps'){throw 'NATIVE_INSTALL_DIRECTORY_CHANGED'}
$log=Join-Path $run 'install.log'
if(Test-Path -LiteralPath $log){throw 'NATIVE_INSTALL_LOG_ALREADY_EXISTS_INSPECT_BEFORE_RETRY'}
$temp=Join-Path 'E:\codex\PartyOps\.partyops-vm-lab\native\win11-x64' ([IO.Path]::GetFileName($run)+'\installer-temp')
New-Item -ItemType Directory -Path $temp -Force | Out-Null
$before=[ordered]@{at=(Get-Date).ToString('o');target='win11-x64-native';environment_type='native-host';package=$candidate;install_dir=$install;boot_id=$os.LastBootUpTime.ToUniversalTime().ToString('o');scope='current-candidate-replacement';runtime_environment_passed=$false;
  install_binding_sha256=(Get-FileHash -LiteralPath (Join-Path $run 'install-binding.json') -Algorithm SHA256).Hash.ToLowerInvariant();expected_payload=$binding.windows_payload}
$before|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $run 'install-start.json') -Encoding utf8NoBOM
$previousTemp=$env:TEMP
$previousTmp=$env:TMP
try {
  $env:TEMP=$temp;$env:TMP=$temp
  $arguments='/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /DIR="'+$install+'" /LOG="'+$log+'"'
  $process=Start-Process -FilePath $package -ArgumentList $arguments -WindowStyle Hidden -PassThru
  [pscustomobject]@{installer_pid=$process.Id;log=$log}|ConvertTo-Json -Compress
  $process.WaitForExit()
  $exitCode=$process.ExitCode
} finally {$env:TEMP=$previousTemp;$env:TMP=$previousTmp}
$app=Join-Path $install 'PartyOps.exe'
$manifest=Join-Path $install 'release-manifest.json'
$registration=Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{1C8EFC63-CAFC-46EF-A5E3-D3D119B5BB3A}_is1' -ErrorAction SilentlyContinue
$result=[ordered]@{
  at=(Get-Date).ToString('o');target='win11-x64-native';environment_type='native-host';status='partial';
  scope=$before.scope;package=$candidate;installer_exit_code=$exitCode;install_dir=$install;
  installed_executable_sha256=if(Test-Path -LiteralPath $app){(Get-FileHash -LiteralPath $app -Algorithm SHA256).Hash.ToLowerInvariant()}else{$null};
  installed_manifest_sha256=if(Test-Path -LiteralPath $manifest){(Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash.ToLowerInvariant()}else{$null};
  install_binding_sha256=$before.install_binding_sha256;expected_payload=$binding.windows_payload;
  registered_version=$registration.DisplayVersion;registered_path=$registration.InstallLocation;
  services=@(Get-CimInstance Win32_Service|Where-Object {$_.Name -in @('PartyOpsHost','PartyOpsUpdateService')}|Select-Object Name,State,StartMode,PathName);
  install_log_sha256=(Get-FileHash -LiteralPath $log -Algorithm SHA256).Hash.ToLowerInvariant();
  standard_user_first_start_verified=$false;runtime_environment_passed=$false;
  limitations=@('不是干净系统安装','不是较旧发行版覆盖升级','尚未执行完整功能、普通用户桌面和真实宿主重启')
}
if(-not(Test-NativeInstalledCandidate $registration $result $candidate $binding $install)){$result.status='failed'}
$result|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $run 'install-result.json') -Encoding utf8NoBOM
[pscustomobject]$result|Select-Object target,installer_exit_code,installed_executable_sha256,registered_version,status,runtime_environment_passed|ConvertTo-Json -Compress
if($exitCode -ne 0){exit $exitCode}
if($result.status -eq 'failed'){throw 'NATIVE_INSTALLED_CANDIDATE_MISMATCH'}
