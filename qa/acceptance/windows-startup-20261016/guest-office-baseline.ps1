$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)
$identity = Get-Content -LiteralPath 'C:\ProgramData\PartyOps-VM-Lab\identity.json' -Raw | ConvertFrom-Json
if ($identity.uuid -ne 'f47cf265-72a2-4d51-9cf1-b1f5ae960cb2' -or $identity.purpose -ne 'disposable-qa') { throw 'QA_GUEST_REQUIRED' }
$apps = @(Get-ItemProperty 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*','HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -match 'WPS|LibreOffice' } | Select-Object DisplayName,DisplayVersion,InstallLocation)
$desktopProfile = Get-CimInstance Win32_UserProfile | Where-Object { $_.LocalPath -eq 'C:\Users\partyopsuser' }
if ($desktopProfile -and $desktopProfile.Loaded) {
  $userUninstall = 'Registry::HKEY_USERS\' + $desktopProfile.SID + '\Software\Microsoft\Windows\CurrentVersion\Uninstall\*'
  $apps += @(Get-ItemProperty $userUninstall -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -match 'WPS|LibreOffice' } | Select-Object DisplayName,DisplayVersion,InstallLocation)
}
$wpsPaths = @('C:\Program Files\Kingsoft\WPS Office','C:\Program Files (x86)\Kingsoft\WPS Office','C:\Users\partyopsuser\AppData\Local\Kingsoft\WPS Office')
$wpsFiles = @()
foreach ($wpsPath in $wpsPaths) {
  if (Test-Path -LiteralPath $wpsPath) {
    $wpsFiles += @(Get-ChildItem -LiteralPath $wpsPath -Filter wps.exe -Recurse -File | ForEach-Object { @{path=$_.FullName;version=$_.VersionInfo.FileVersion} })
  }
}
$fonts = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts'
$com = @()
foreach ($progId in @('Word.Application','KWPS.Application','WPS.Application')) {
  $key = 'Registry::HKEY_CLASSES_ROOT\' + $progId + '\CLSID'
  $com += @{prog_id=$progId;registered=(Test-Path -LiteralPath $key)}
}
$names = @($fonts.PSObject.Properties.Name | Where-Object { $_ -notmatch '^PS' })
[ordered]@{
  guest_uuid = $identity.uuid
  time_utc = [DateTime]::UtcNow.ToString('o')
  office_apps = $apps
  wps_executables = $wpsFiles
  office_com_registration = $com
  machine_font_count = $names.Count
  document_font_names = @($names | Where-Object { $_ -match '仿宋|小标宋|楷体|黑体|宋体|Times New Roman|FangSong|KaiTi|SimHei|SimSun' })
  scope = '只读安装身份和系统字体登记；未打开文档，未验证新安装包排版'
} | ConvertTo-Json -Depth 4 -Compress
