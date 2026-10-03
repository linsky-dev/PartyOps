param(
    [Parameter(Mandatory=$true)][string]$VstoManifest,
    [string]$KeyName = 'partyops.documentformatter',
    [string]$FriendlyName = 'partyops公文排版助手',
    [string]$Description = 'Word/WPS 公文一键排版插件'
)
$ErrorActionPreference = 'Stop'
$manifest = (Resolve-Path $VstoManifest).Path
$key = "HKCU:\Software\Microsoft\Office\Word\Addins\$KeyName"
New-Item -Path $key -Force | Out-Null
New-ItemProperty -Path $key -Name FriendlyName -Value $FriendlyName -PropertyType String -Force | Out-Null
New-ItemProperty -Path $key -Name Description -Value $Description -PropertyType String -Force | Out-Null
New-ItemProperty -Path $key -Name LoadBehavior -Value 3 -PropertyType DWord -Force | Out-Null
$uri = (New-Object System.Uri($manifest)).AbsoluteUri + '|vstolocal'
New-ItemProperty -Path $key -Name Manifest -Value $uri -PropertyType String -Force | Out-Null
Write-Host "Registered Word VSTO add-in: $key" -ForegroundColor Green
Write-Host "Manifest=$uri"
