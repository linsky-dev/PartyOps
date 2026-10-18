# 仅在原 QA 虚拟机运行；三份旧安装器已在界面退出，核对身份后处理残留。
$ErrorActionPreference='Stop'
$expected='089eeb02fb7da42a683a4d9c7fcecf155b7b326fcd8b8281cbd0b7acd3d9783a'
$installer='C:\Users\Public\Desktop\WPS_Setup_x64_28488.exe'
if((Get-CimInstance Win32_ComputerSystemProduct).UUID -ne 'f47cf265-72a2-4d51-9cf1-b1f5ae960cb2'){throw 'WRONG_GUEST'}
if((Get-FileHash -LiteralPath $installer).Hash.ToLowerInvariant() -ne $expected){throw 'WRONG_INSTALLER'}
if((Get-AuthenticodeSignature -LiteralPath $installer).Status -ne 'Valid'){throw 'INVALID_VENDOR_SIGNATURE'}
$bound=@{2424=1792214059282L;5540=1792214076575L;2764=1792214078558L}
$processes=@(Get-CimInstance Win32_Process -Filter "Name='WPS_Setup_x64_28488.exe'")
foreach($p in $processes){
 if(-not $bound.ContainsKey([int]$p.ProcessId)){throw 'OTHER_INSTALLER_PRESENT'}
 if([Math]::Abs(([DateTimeOffset]$p.CreationDate).ToUnixTimeMilliseconds()-$bound[[int]$p.ProcessId]) -gt 2){throw 'INSTALLER_IDENTITY_CHANGED'}
 if($p.ExecutablePath -notin @($installer,'C:\ProgramData\kingsoft\20261017_131420\WPS_Setup_x64_28488.exe')){throw 'OTHER_INSTALLER_PATH'}
 $processHash=if($p.ExecutablePath -eq $installer){$expected}else{'5e574529af84aa0d5a7916e1ee4fec00a3ede12ac213781b79b5318ced464bd8'}
 if((Get-FileHash -LiteralPath $p.ExecutablePath).Hash.ToLowerInvariant() -ne $processHash -or (Get-AuthenticodeSignature -LiteralPath $p.ExecutablePath).Status -ne 'Valid'){throw 'INSTALLER_HASH_CHANGED'}
}
$keys=@('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*')
if(@(Get-ItemProperty -Path $keys -ErrorAction SilentlyContinue|Where-Object {$_.DisplayName -match 'WPS'}).Count){throw 'WPS_ALREADY_INSTALLED'}
$receipt='C:\ProgramData\PartyOps-VM-Lab\wps-silent-20261017.json'
if(-not(Test-Path -LiteralPath $receipt) -or (Get-Content -LiteralPath $receipt -Raw|ConvertFrom-Json).status -ne 'starting'){throw 'EXPECTED_PRELAUNCH_INTERRUPTION_REQUIRED'}
@{status='starting';installer_sha256=$expected;argument='/S';license_approved=$true}|ConvertTo-Json|Set-Content -LiteralPath $receipt -Encoding UTF8
foreach($p in $processes){Stop-Process -Id $p.ProcessId -Force -ErrorAction Stop}
if(@(Get-CimInstance Win32_Process -Filter "Name='WPS_Setup_x64_28488.exe'").Count){throw 'PREVIOUS_INSTALLER_STILL_PRESENT'}
@{status='launching';installer_sha256=$expected}|ConvertTo-Json|Set-Content -LiteralPath $receipt -Encoding UTF8
$p=Start-Process -FilePath $installer -ArgumentList '/S' -WindowStyle Hidden -PassThru -Wait
$result=@{status='installer-exited';exit_code=$p.ExitCode;installer_sha256=$expected;argument='/S';license_approved=$true;installed=@(Get-ItemProperty -Path $keys -ErrorAction SilentlyContinue|Where-Object {$_.DisplayName -match 'WPS'}|Select-Object DisplayName,DisplayVersion,InstallLocation)}
$result|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $receipt -Encoding UTF8
$result|ConvertTo-Json -Depth 5 -Compress