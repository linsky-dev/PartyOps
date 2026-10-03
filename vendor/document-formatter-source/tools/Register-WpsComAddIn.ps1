param(
    [ValidateSet('Diagnose', 'Install', 'Uninstall')]
    [string]$Action = 'Diagnose',
    [string]$AssemblyPath = (Join-Path $PSScriptRoot 'application\PartyOps.DocumentFormatter.WpsShim.dll'),
    [ValidateSet('Auto', 'x86', 'x64')]
    [string]$Platform = 'Auto'
)

$ErrorActionPreference = 'Stop'

# WPS 为 32 位程序，但某些系统将 Office/WPS 注册表视图分开保存。两种视图都写入，
# 可以避免用户在 64 位 PowerShell 中安装后，32 位 WPS 看不到加载项。
$registryViews = @(
    [Microsoft.Win32.RegistryView]::Registry32,
    [Microsoft.Win32.RegistryView]::Registry64
)
$classId = '7843E826-447C-484C-BB7E-EAA85A5BCC3F'
$legacyClassIds = @(
    'A2A70F17-2C03-4B8A-8D20-3A7A0B2A7C76',
    '4E0D899B-5A7C-4806-BC60-BBD4D08D6942'
)
$legacyRecordIds = @('107198D8-2F8A-3FC4-9801-F243F019FDC5', '1DCD4019-B46A-357F-BCF9-D46546AA700D')
$progId = 'PartyOps.DocumentFormatter.WpsAddIn'
$legacyProgIds = @('PartyOps.DocumentFormatter.WpsShim')
$className = 'PartyOps.DocumentFormatter.Wps.WpsComAddInEntryPoint'
$assemblyFileName = 'PartyOps.DocumentFormatter.WpsShim.dll'
$wordAddInSubKey = 'Software\Microsoft\Office\Word\Addins\' + $progId
$classSubKey = 'Software\Classes\CLSID\{' + $classId + '}'
$classSubKey32 = 'Software\Classes\WOW6432Node\CLSID\{' + $classId + '}'
$progSubKey = 'Software\Classes\' + $progId
$wpsWhitelistSubKeys = @(
    'Software\Kingsoft\Office\WPS\AddinsWL',
    'Software\Kingsoft\Office\6.0\wps\AddinsWL'
)

function Get-RegistryViewValue([Microsoft.Win32.RegistryView]$View, [string]$SubKey, [string]$Name) {
    $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, $View)
    try {
        $key = $baseKey.OpenSubKey($SubKey, $false)
        if ($null -eq $key) { return $null }
        try { return $key.GetValue($Name, $null) }
        finally { $key.Dispose() }
    }
    finally { $baseKey.Dispose() }
}

function Get-MachineRegistryViewValue([Microsoft.Win32.RegistryView]$View, [string]$SubKey, [string]$Name) {
    $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, $View)
    try {
        $key = $baseKey.OpenSubKey($SubKey, $false)
        if ($null -eq $key) { return $null }
        try { return $key.GetValue($Name, $null) }
        finally { $key.Dispose() }
    }
    finally { $baseKey.Dispose() }
}

function Get-ClassSubKey([Microsoft.Win32.RegistryView]$View) {
    # RegistryView 已负责 WOW64 重定向；机器级 RegAsm 键始终从标准 CLSID 路径读取。
    return $classSubKey
}

function Set-RegistryViewValue([Microsoft.Win32.RegistryView]$View, [string]$SubKey, [string]$Name, [object]$Value, [Microsoft.Win32.RegistryValueKind]$Kind) {
    $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, $View)
    try {
        $key = $baseKey.CreateSubKey($SubKey, $true)
        if ($null -eq $key) { throw "无法创建当前用户注册表键：$SubKey ($View)" }
        try { $key.SetValue($Name, $Value, $Kind) }
        finally { $key.Dispose() }
    }
    finally { $baseKey.Dispose() }
}

function Remove-RegistryViewTree([Microsoft.Win32.RegistryView]$View, [string]$SubKey) {
    $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, $View)
    try { $baseKey.DeleteSubKeyTree($SubKey, $false) }
    finally { $baseKey.Dispose() }
}

function Remove-MachineRegistryViewTree([Microsoft.Win32.RegistryView]$View, [string]$SubKey) {
    $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, $View)
    try { $baseKey.DeleteSubKeyTree($SubKey, $false) }
    catch [System.UnauthorizedAccessException] {
        throw "无法清理机器级旧 COM 注册：$SubKey ($View)。请以管理员身份重新运行安装脚本。"
    }
    finally { $baseKey.Dispose() }
}

function Remove-RegistryViewValue([Microsoft.Win32.RegistryView]$View, [string]$SubKey, [string]$Name) {
    $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, $View)
    try {
        $key = $baseKey.OpenSubKey($SubKey, $true)
        if ($null -eq $key) { return }
        try { $key.DeleteValue($Name, $false) }
        finally { $key.Dispose() }
    }
    finally { $baseKey.Dispose() }
}

function Get-AssemblyMetadata([string]$Path) {
    if (!(Test-Path -LiteralPath $Path)) {
        throw "WPS COM 加载桥程序集不存在：$Path"
    }
    $resolved = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $Path).Path)
    $name = [Reflection.AssemblyName]::GetAssemblyName($resolved)
    $tokenBytes = $name.GetPublicKeyToken()
    $token = if ($null -eq $tokenBytes -or $tokenBytes.Length -eq 0) { 'null' } else { -join ($tokenBytes | ForEach-Object { $_.ToString('x2') }) }
    $bytes = [IO.File]::ReadAllBytes($resolved)
    if ($bytes.Length -lt 64) { throw "WPS COM 加载桥不是有效 PE 文件：$resolved" }
    $peOffset = [BitConverter]::ToInt32($bytes, 0x3c)
    if ($peOffset -lt 0 -or $peOffset + 6 -gt $bytes.Length) { throw "WPS COM 加载桥 PE 头无效：$resolved" }
    $machine = [BitConverter]::ToUInt16($bytes, $peOffset + 4)
    $detectedPlatform = if ($machine -eq 0x8664) { 'x64' } elseif ($machine -eq 0x014c) { 'x86' } else { throw ('不支持的 WPS COM 加载桥 PE 架构：0x{0:X4}' -f $machine) }
    [pscustomobject]@{
        Path = $resolved
        CodeBase = ([Uri]$resolved).AbsoluteUri
        AssemblyFullName = $name.FullName
        AssemblyName = $name.Name
        Version = $name.Version.ToString()
        PublicKeyToken = $token
        Platform = $detectedPlatform
    }
}

function Get-RegAsmPath([string]$TargetPlatform) {
    $frameworkDirectory = if ($TargetPlatform -eq 'x64') { 'Framework64' } else { 'Framework' }
    $path = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::Windows)) "Microsoft.NET\$frameworkDirectory\v4.0.30319\RegAsm.exe"
    if (!(Test-Path -LiteralPath $path)) { throw ".NET Framework RegAsm 不存在：$path" }
    return $path
}

function Invoke-RegAsm([string]$TargetPlatform, [string]$Mode, [string]$Path) {
    $regAsm = Get-RegAsmPath $TargetPlatform
    $arguments = if ($Mode -eq 'Register') { @($Path, '/codebase', '/nologo') } else { @($Path, '/unregister', '/nologo') }
    $output = & $regAsm @arguments 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "RegAsm $Mode 失败（请以管理员身份运行与宿主位数匹配的安装包）：$($output.Trim())"
    }
    if (![string]::IsNullOrWhiteSpace($output)) { Write-Verbose $output.Trim() }
    return $regAsm
}

function Get-ViewSnapshot([Microsoft.Win32.RegistryView]$View) {
    $viewClassSubKey = Get-ClassSubKey $View
    $classKey = Get-MachineRegistryViewValue $View $viewClassSubKey ''
    $inproc = $viewClassSubKey + '\InprocServer32'
    $addin = $wordAddInSubKey
    [pscustomobject]@{
        View = $View.ToString()
        ClassDefault = [string]$classKey
        InprocServer32 = [string](Get-MachineRegistryViewValue $View $inproc '')
        InprocClass = [string](Get-MachineRegistryViewValue $View $inproc 'Class')
        InprocAssembly = [string](Get-MachineRegistryViewValue $View $inproc 'Assembly')
        InprocRuntimeVersion = [string](Get-MachineRegistryViewValue $View $inproc 'RuntimeVersion')
        InprocCodeBase = [string](Get-MachineRegistryViewValue $View $inproc 'CodeBase')
        ProgIdClassId = [string](Get-MachineRegistryViewValue $View ($progSubKey + '\CLSID') '')
        WordLoadBehavior = [int](Get-RegistryViewValue $View $addin 'LoadBehavior')
        WordManifest = [string](Get-RegistryViewValue $View $addin 'Manifest')
        WpsWhitelist = [string](Get-RegistryViewValue $View $wpsWhitelistSubKeys[0] $progId)
        WpsLegacyWhitelist = [string](Get-RegistryViewValue $View $wpsWhitelistSubKeys[1] $progId)
    }
}

if ($Action -eq 'Diagnose') {
    $metadata = $null
    if (Test-Path -LiteralPath $AssemblyPath) { $metadata = Get-AssemblyMetadata $AssemblyPath }
    [pscustomobject]@{
        Action = $Action
        ProgId = $progId
        ClassId = $classId
        Assembly = $metadata
        RegAsmPath = if ($null -ne $metadata) { Get-RegAsmPath $metadata.Platform } else { $null }
        RegistryViews = @($registryViews | ForEach-Object { Get-ViewSnapshot $_ })
    } | ConvertTo-Json -Depth 8
    return
}

if ($Action -eq 'Uninstall') {
    if (Test-Path -LiteralPath $AssemblyPath) {
        $metadata = Get-AssemblyMetadata $AssemblyPath
        $effectivePlatform = if ($Platform -eq 'Auto') { $metadata.Platform } else { $Platform }
        if ($effectivePlatform -ne $metadata.Platform) { throw "指定平台与程序集不一致：指定=$effectivePlatform，程序集=$($metadata.Platform)" }
        Invoke-RegAsm $effectivePlatform 'Unregister' $metadata.Path | Out-Null
    }
    foreach ($view in $registryViews) {
        Remove-RegistryViewTree $view $wordAddInSubKey
        Remove-RegistryViewTree $view (Get-ClassSubKey $view)
        Remove-RegistryViewTree $view $progSubKey
        Remove-MachineRegistryViewTree $view (Get-ClassSubKey $view)
        Remove-MachineRegistryViewTree $view $progSubKey
        foreach ($recordId in $legacyRecordIds) {
            Remove-RegistryViewTree $view ('Software\Classes\Record\{' + $recordId + '}')
            Remove-MachineRegistryViewTree $view ('Software\Classes\Record\{' + $recordId + '}')
        }
        foreach ($legacyClassId in $legacyClassIds) {
            Remove-RegistryViewTree $view ('Software\Classes\CLSID\{' + $legacyClassId + '}')
            Remove-RegistryViewTree $view ('Software\Classes\WOW6432Node\CLSID\{' + $legacyClassId + '}')
            Remove-MachineRegistryViewTree $view ('Software\Classes\CLSID\{' + $legacyClassId + '}')
        }
        foreach ($legacyProgId in $legacyProgIds) {
            Remove-RegistryViewTree $view ('Software\Microsoft\Office\Word\Addins\' + $legacyProgId)
            Remove-RegistryViewTree $view ('Software\Classes\' + $legacyProgId)
            Remove-MachineRegistryViewTree $view ('Software\Classes\' + $legacyProgId)
        }
        foreach ($subKey in $wpsWhitelistSubKeys) {
            Remove-RegistryViewValue $view $subKey $progId
            foreach ($legacyProgId in $legacyProgIds) { Remove-RegistryViewValue $view $subKey $legacyProgId }
        }
    }
    Write-Host "已移除 WPS 原生 COM 加载桥：$progId（RegAsm 类注册及当前用户 WPS/Office 注册）。" -ForegroundColor Green
    return
}

$metadata = Get-AssemblyMetadata $AssemblyPath
$effectivePlatform = if ($Platform -eq 'Auto') { $metadata.Platform } else { $Platform }
if ($effectivePlatform -ne $metadata.Platform) { throw "指定平台与程序集不一致：指定=$effectivePlatform，程序集=$($metadata.Platform)" }
$targetView = if ($effectivePlatform -eq 'x64') { [Microsoft.Win32.RegistryView]::Registry64 } else { [Microsoft.Win32.RegistryView]::Registry32 }
$assemblyQualifiedName = $metadata.AssemblyFullName
$inproc = $classSubKey + '\InprocServer32'
$inprocVersion = $inproc + '\' + $metadata.Version
$codeBase = $metadata.CodeBase

# CLR 托管 COM 类必须由 .NET Framework RegAsm 建立完整机器级映射。此前的精简 HKCU
# 映射虽然可解析 ProgID，却会在 CoCreateInstance 阶段以 0x80070002 失败。
# 先删除旧的每用户类映射，避免它覆盖 RegAsm 的 HKLM 注册。
foreach ($view in $registryViews) {
    Remove-RegistryViewTree $view $classSubKey
    Remove-RegistryViewTree $view $classSubKey32
    Remove-RegistryViewTree $view $progSubKey
    foreach ($recordId in $legacyRecordIds) {
        Remove-RegistryViewTree $view ('Software\Classes\Record\{' + $recordId + '}')
        Remove-MachineRegistryViewTree $view ('Software\Classes\Record\{' + $recordId + '}')
    }
    foreach ($legacyClassId in $legacyClassIds) {
        Remove-RegistryViewTree $view ('Software\Classes\CLSID\{' + $legacyClassId + '}')
        Remove-RegistryViewTree $view ('Software\Classes\WOW6432Node\CLSID\{' + $legacyClassId + '}')
        Remove-MachineRegistryViewTree $view ('Software\Classes\CLSID\{' + $legacyClassId + '}')
    }
    foreach ($legacyProgId in $legacyProgIds) {
        Remove-RegistryViewTree $view ('Software\Microsoft\Office\Word\Addins\' + $legacyProgId)
        Remove-RegistryViewTree $view ('Software\Classes\' + $legacyProgId)
        Remove-MachineRegistryViewTree $view ('Software\Classes\' + $legacyProgId)
        foreach ($subKey in $wpsWhitelistSubKeys) { Remove-RegistryViewValue $view $subKey $legacyProgId }
    }
    Set-RegistryViewValue $view $wordAddInSubKey 'Description' 'partyops公文排版助手 WPS 原生 COM 加载桥' ([Microsoft.Win32.RegistryValueKind]::String)
    Set-RegistryViewValue $view $wordAddInSubKey 'FriendlyName' 'partyops公文排版助手' ([Microsoft.Win32.RegistryValueKind]::String)
    Set-RegistryViewValue $view $wordAddInSubKey 'LoadBehavior' 3 ([Microsoft.Win32.RegistryValueKind]::DWord)
    Set-RegistryViewValue $view $wordAddInSubKey 'CommandLineSafe' 0 ([Microsoft.Win32.RegistryValueKind]::DWord)

    foreach ($subKey in $wpsWhitelistSubKeys) {
        # AddinsWL 以值名表示允许的 ProgID。空字符串是 WPS 原生 COM 加载项的白名单标记，
        # 与 VSTO 的 manifest|vstolocal 值分开，避免再次触发 VSTO 加载器阻塞。
        Set-RegistryViewValue $view $subKey $progId '' ([Microsoft.Win32.RegistryValueKind]::String)
    }
}
$regAsmPath = Invoke-RegAsm $effectivePlatform 'Register' $metadata.Path

$snapshots = @(Get-ViewSnapshot $targetView)
$complete = $true
foreach ($snapshot in $snapshots) {
    $complete = $complete -and
        $snapshot.InprocServer32 -eq 'mscoree.dll' -and
        $snapshot.InprocClass -eq $className -and
        $snapshot.InprocCodeBase -eq $codeBase -and
        $snapshot.ProgIdClassId -eq ('{' + $classId + '}') -and
        $snapshot.WordLoadBehavior -eq 3 -and
        $snapshot.WpsWhitelist -eq ''
}
if (!$complete) { throw 'WPS 原生 COM 加载桥注册后校验失败。' }
Write-Host "WPS 原生 COM 加载桥已注册：$progId" -ForegroundColor Green
Write-Host "CLSID={$classId}; Platform=$effectivePlatform; RegAsm=$regAsmPath; CodeBase=$codeBase"
