$ErrorActionPreference='Stop'
[Console]::OutputEncoding=New-Object System.Text.UTF8Encoding($false)
$identity=Get-Content -LiteralPath 'C:\ProgramData\PartyOps-VM-Lab\identity.json' -Raw | ConvertFrom-Json
if($identity.uuid -ne 'f47cf265-72a2-4d51-9cf1-b1f5ae960cb2' -or $identity.purpose -ne 'disposable-qa'){throw 'QA_GUEST_IDENTITY_MISMATCH'}
function Probe-Bind([string]$address,[int]$port) {
  $listener=New-Object Net.Sockets.TcpListener([Net.IPAddress]::Parse($address),$port)
  $listener.Server.ExclusiveAddressUse=$true
  try {$listener.Start(); return @{result='bound'}}
  catch {$errorObject=$_.Exception; while($errorObject.InnerException){$errorObject=$errorObject.InnerException}; return @{result='failed';native_error=$errorObject.NativeErrorCode;exception_type=$errorObject.GetType().FullName}}
  finally {$listener.Stop()}
}
# 文档保留地址不发送网络请求；仅验证本机 bind 错误分类。
$notAvailable=Probe-Bind '192.0.2.254' 18766
$held=New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback,0)
$held.Server.ExclusiveAddressUse=$true
try {$held.Start();$port=$held.LocalEndpoint.Port;$inUse=Probe-Bind '127.0.0.1' $port}
finally {$held.Stop()}
$released=Probe-Bind '127.0.0.1' $port
$python=@(Get-Command python.exe,python3.exe -ErrorAction SilentlyContinue | Select-Object Source)
@{scope='Windows 10 socket 基线；非候选安装包验收';unavailable_address=$notAvailable;occupied_port=$inUse;after_release=$released;python=$python} | ConvertTo-Json -Depth 5 -Compress
