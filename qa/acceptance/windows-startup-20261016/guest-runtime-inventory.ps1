$ErrorActionPreference='Stop'
[Console]::OutputEncoding=New-Object System.Text.UTF8Encoding($false)
$identity=Get-Content -LiteralPath 'C:\ProgramData\PartyOps-VM-Lab\identity.json' -Raw | ConvertFrom-Json
if($identity.uuid -ne 'f47cf265-72a2-4d51-9cf1-b1f5ae960cb2' -or $identity.purpose -ne 'disposable-qa'){throw 'QA_GUEST_IDENTITY_MISMATCH'}
$root='C:\PartyOps QA\中文 程序'
$internal=Join-Path $root '_internal'
$directories=@(Get-ChildItem -LiteralPath $root -Directory | Select-Object Name)
$internalDirectories=@(Get-ChildItem -LiteralPath $internal -Directory | Select-Object Name)
$manifest=Get-Content -LiteralPath (Join-Path $root 'release-manifest.json') -Raw | ConvertFrom-Json
$source=Get-Content -LiteralPath (Join-Path $root 'SOURCE.json') -Raw | ConvertFrom-Json
@{directories=$directories;internal_directories=$internalDirectories;manifest_keys=@($manifest.PSObject.Properties.Name);source_keys=@($source.PSObject.Properties.Name)} | ConvertTo-Json -Depth 5 -Compress
