# 只读复核原 QA Guest；不读取凭据或业务内容，不启动、安装或重启程序。
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=New-Object System.Text.UTF8Encoding($false)
$identity=Get-Content -LiteralPath 'C:\ProgramData\PartyOps-VM-Lab\identity.json' -Raw | ConvertFrom-Json
if($identity.uuid -ne 'f47cf265-72a2-4d51-9cf1-b1f5ae960cb2' -or $identity.purpose -ne 'disposable-qa'){throw 'QA_GUEST_IDENTITY_MISMATCH'}
$os=Get-CimInstance Win32_OperatingSystem
$root='C:\PartyOps QA\中文 程序'
$manifest=Join-Path $root 'release-manifest.json'
$processes=@(Get-Process -Name 'PartyOps*' -ErrorAction SilentlyContinue | Select-Object Id,ProcessName,Path)
$installers=@(Get-Process -Name '*setup*','PartyOps_1.4.5-rc.6_windows_amd64*' -ErrorAction SilentlyContinue | Select-Object Id,ProcessName)
$services=@(Get-Service -Name 'PartyOps*' -ErrorAction SilentlyContinue | ForEach-Object {@{name=$_.Name;status=$_.Status.ToString()}})
$report=@{checked_at_utc=[DateTime]::UtcNow.ToString('o');guest_uuid=$identity.uuid;purpose=$identity.purpose;os=$os.Caption;version=$os.Version;architecture=$os.OSArchitecture;last_boot_utc=$os.LastBootUpTime.ToUniversalTime().ToString('o');manifest_sha256=(Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash.ToLowerInvariant();exe_sha256=(Get-FileHash -LiteralPath (Join-Path $root 'PartyOps.exe') -Algorithm SHA256).Hash.ToLowerInvariant();processes=$processes;installer_processes=$installers;services=$services}
$report | ConvertTo-Json -Depth 5 -Compress
