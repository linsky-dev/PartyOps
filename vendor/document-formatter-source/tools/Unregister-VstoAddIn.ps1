param([string]$KeyName = 'partyops.documentformatter')
$key = "HKCU:\Software\Microsoft\Office\Word\Addins\$KeyName"
if (Test-Path $key) { Remove-Item $key -Recurse -Force }
Write-Host "Removed $key"
