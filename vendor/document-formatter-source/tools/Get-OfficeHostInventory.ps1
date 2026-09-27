$ErrorActionPreference = 'Stop'

function Get-PeArchitecture([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or !(Test-Path -LiteralPath $Path)) {
        return 'unknown'
    }
    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    $reader = [System.IO.BinaryReader]::new($stream)
    try {
        if ($reader.ReadUInt16() -ne 0x5A4D) { return 'unknown' }
        $stream.Position = 0x3C
        $peOffset = $reader.ReadInt32()
        if ($peOffset -lt 0 -or $peOffset -gt ($stream.Length - 6)) { return 'unknown' }
        $stream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x00004550) { return 'unknown' }
        [uint16]$machine = $reader.ReadUInt16()
        if ($machine -eq [uint16]0x014C) { return 'x86' }
        if ($machine -eq [uint16]0x8664) { return 'x64' }
        if ($machine -eq [uint16]0xAA64) { return 'arm64' }
        return 'unknown'
    }
    finally {
        $reader.Dispose()
        $stream.Dispose()
    }
}

function Get-ExecutableFromCommand([string]$Command) {
    if ([string]::IsNullOrWhiteSpace($Command)) { return '' }
    $match = [regex]::Match($Command.Trim(), '^(?:"(?<quoted>[^"]+\.exe)"|(?<plain>.+?\.exe))(?:\s|$)', 'IgnoreCase')
    if (!$match.Success) { return '' }
    if ($match.Groups['quoted'].Success) { return $match.Groups['quoted'].Value }
    return $match.Groups['plain'].Value.Trim()
}

function Get-DefaultRegistryValue([string]$Path) {
    if (!(Test-Path -LiteralPath $Path)) { return '' }
    return [string](Get-ItemProperty -LiteralPath $Path -ErrorAction SilentlyContinue).'(default)'
}

$wordCandidates = [System.Collections.Generic.List[string]]::new()
function Add-WordCandidate([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return }
    $candidate = Get-ExecutableFromCommand $Path
    if ([string]::IsNullOrWhiteSpace($candidate)) { $candidate = $Path.Trim().Trim('"') }
    if ((Split-Path -Leaf $candidate) -ieq 'WINWORD.EXE' -and (Test-Path -LiteralPath $candidate)) {
        $resolved = [System.IO.Path]::GetFullPath($candidate)
        if (!$wordCandidates.Contains($resolved)) { $wordCandidates.Add($resolved) }
    }
}

foreach ($key in @(
    'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\WINWORD.EXE',
    'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\WINWORD.EXE',
    'Registry::HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\WINWORD.EXE'
)) {
    Add-WordCandidate (Get-DefaultRegistryValue $key)
}

$command = Get-Command WINWORD.EXE -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -ne $command) { Add-WordCandidate $command.Source }

foreach ($key in @(
    'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Office\ClickToRun\Configuration',
    'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Office\ClickToRun\Configuration'
)) {
    if (Test-Path -LiteralPath $key) {
        $configuration = Get-ItemProperty -LiteralPath $key
        Add-WordCandidate (Join-Path ([string]$configuration.InstallationPath) 'root\Office16\WINWORD.EXE')
    }
}

foreach ($version in @('11.0', '12.0', '14.0', '15.0', '16.0')) {
    foreach ($base in @(
        'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Office',
        'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Office',
        'Registry::HKEY_CURRENT_USER\SOFTWARE\Microsoft\Office'
    )) {
        $key = "$base\$version\Word\InstallRoot"
        if (Test-Path -LiteralPath $key) {
            $installRoot = [string](Get-ItemProperty -LiteralPath $key).Path
            Add-WordCandidate (Join-Path $installRoot 'WINWORD.EXE')
        }
    }
}

foreach ($drive in (Get-PSDrive -PSProvider FileSystem | Where-Object { $_.Name.Length -eq 1 })) {
    foreach ($relative in @(
        'Program Files\Microsoft Office\root\Office16\WINWORD.EXE',
        'Program Files (x86)\Microsoft Office\root\Office16\WINWORD.EXE',
        'Program Files\Microsoft Office\Office16\WINWORD.EXE',
        'Program Files (x86)\Microsoft Office\Office16\WINWORD.EXE',
        'Program Files\Microsoft Office\Office12\WINWORD.EXE',
        'Program Files (x86)\Microsoft Office\Office12\WINWORD.EXE'
    )) {
        Add-WordCandidate (Join-Path $drive.Root $relative)
    }
}

$wordClsid = '{000209FF-0000-0000-C000-000000000046}'
$wordComServers = @(
    (Get-DefaultRegistryValue "Registry::HKEY_CLASSES_ROOT\CLSID\$wordClsid\LocalServer32"),
    (Get-DefaultRegistryValue "Registry::HKEY_CLASSES_ROOT\Wow6432Node\CLSID\$wordClsid\LocalServer32")
) | Where-Object { ![string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique
foreach ($server in $wordComServers) { Add-WordCandidate $server }

$wpsClsid = '{000209FF-0000-4B30-A977-D214852036FF}'
$wpsComServers = @(
    (Get-DefaultRegistryValue "Registry::HKEY_CLASSES_ROOT\CLSID\$wpsClsid\LocalServer32"),
    (Get-DefaultRegistryValue "Registry::HKEY_CLASSES_ROOT\Wow6432Node\CLSID\$wpsClsid\LocalServer32")
) | Where-Object { ![string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique
$wpsCandidates = @(
    $wpsComServers | ForEach-Object { Get-ExecutableFromCommand $_ } | Where-Object {
        ![string]::IsNullOrWhiteSpace($_) -and (Split-Path -Leaf $_) -ieq 'wps.exe' -and (Test-Path -LiteralPath $_)
    } | ForEach-Object { [System.IO.Path]::GetFullPath($_) } | Select-Object -Unique
)

$wordComRedirectedToWps = @($wordComServers | Where-Object {
    (Split-Path -Leaf (Get-ExecutableFromCommand $_)) -ieq 'wps.exe'
}).Count -gt 0
$officeRegistrationRemnants = @(
    'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Office\11.0\Registration',
    'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Office\12.0\Registration',
    'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Office\12.0\Registration',
    'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Office\14.0\Registration',
    'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Office\15.0\Registration',
    'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Office\16.0\Registration'
) | Where-Object { Test-Path -LiteralPath $_ }

[pscustomobject]@{
    WordInstalled = $wordCandidates.Count -gt 0
    WordPaths = @($wordCandidates)
    WordArchitecture = if ($wordCandidates.Count -gt 0) { Get-PeArchitecture $wordCandidates[0] } else { 'not-installed' }
    WordComServers = @($wordComServers)
    WordComRedirectedToWps = $wordComRedirectedToWps
    WpsInstalled = $wpsCandidates.Count -gt 0
    WpsPaths = @($wpsCandidates)
    WpsArchitecture = if ($wpsCandidates.Count -gt 0) { Get-PeArchitecture $wpsCandidates[0] } else { 'not-installed' }
    WpsComServers = @($wpsComServers)
    OfficeRegistrationRemnants = @($officeRegistrationRemnants)
}
