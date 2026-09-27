param(
    [ValidateSet('Diagnose', 'Install', 'Uninstall')]
    [string]$Action = 'Diagnose',
    [ValidateSet('Auto', 'Word', 'Wps')]
    [string]$HostTarget = 'Auto',
    [switch]$TrustPublisher,
    [switch]$TrustManifest,
    [switch]$LaunchWord,
    [switch]$LaunchWps
)

$ErrorActionPreference = 'Stop'
$applicationDirectory = Join-Path $PSScriptRoot 'application'
$manifestPath = Join-Path $applicationDirectory 'PartyOps.DocumentFormatter.AddIn.vsto'
$wpsShimScript = Join-Path $PSScriptRoot 'Register-WpsComAddIn.ps1'
$wpsShimAssemblyPath = Join-Path $applicationDirectory 'PartyOps.DocumentFormatter.WpsShim.dll'
$certificatePath = Join-Path $PSScriptRoot 'publisher.cer'
$summaryPath = Join-Path $PSScriptRoot 'publish-summary.json'
$addInKey = 'HKCU:\Software\Microsoft\Office\Word\Addins\partyops.documentformatter'
$inclusionRoot = 'HKCU:\Software\Microsoft\VSTO\Security\Inclusion'
$wpsWhitelistKeys = @(
    'HKCU:\Software\Kingsoft\Office\WPS\AddinsWL',
    'HKCU:\Software\Kingsoft\Office\6.0\wps\AddinsWL'
)
$manifestUrl = ([Uri]$manifestPath).AbsoluteUri
$wpsManifestValue = $manifestPath + '|vstolocal'
$registryViews = @(
    [Microsoft.Win32.RegistryView]::Registry32,
    [Microsoft.Win32.RegistryView]::Registry64
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

function Remove-RegistryViewValueIfEqual([Microsoft.Win32.RegistryView]$View, [string]$SubKey, [string]$Name, [string]$Expected) {
    $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, $View)
    try {
        $key = $baseKey.OpenSubKey($SubKey, $true)
        if ($null -eq $key) { return }
        try {
            $current = [string]$key.GetValue($Name, $null)
            if ([string]::Equals($current, $Expected, [StringComparison]::OrdinalIgnoreCase)) {
                $key.DeleteValue($Name, $false)
            }
        }
        finally { $key.Dispose() }
    }
    finally { $baseKey.Dispose() }
}

function Get-ManifestInclusionEntries([string]$Url) {
    if (!(Test-Path -LiteralPath $inclusionRoot)) { return @() }
    return @(Get-ChildItem -LiteralPath $inclusionRoot -ErrorAction SilentlyContinue | Where-Object {
        $entry = Get-ItemProperty -LiteralPath $_.PSPath -ErrorAction SilentlyContinue
        $null -ne $entry -and [string]::Equals([string]$entry.Url, $Url, [StringComparison]::OrdinalIgnoreCase)
    })
}

function Get-RsaPublicKeyXml([System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate) {
    $rsa = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPublicKey($Certificate)
    if ($null -eq $rsa) { throw '发布者证书不包含 RSA 公钥，无法建立 VSTO Inclusion 信任。' }
    try {
        $parameters = $rsa.ExportParameters($false)
        $modulus = [Convert]::ToBase64String($parameters.Modulus)
        $exponent = [Convert]::ToBase64String($parameters.Exponent)
        return "<RSAKeyValue><Modulus>$modulus</Modulus><Exponent>$exponent</Exponent></RSAKeyValue>"
    }
    finally {
        $rsa.Dispose()
    }
}

& (Join-Path $PSScriptRoot 'Verify-VstoPackage.ps1') -ApplicationDirectory $applicationDirectory

$inventoryScript = Join-Path $PSScriptRoot 'Get-OfficeHostInventory.ps1'
if (!(Test-Path -LiteralPath $inventoryScript)) {
    throw "Office 主机检测脚本不存在：$inventoryScript"
}
$officeInventory = & $inventoryScript
$wordCandidates = @($officeInventory.WordPaths)
$wpsCandidates = @($officeInventory.WpsPaths)
$packageSummary = if (Test-Path -LiteralPath $summaryPath) { Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json } else { $null }
$packagePlatform = if ($null -ne $packageSummary) { [string]$packageSummary.platform } else { 'unknown' }
$wordArchitecture = [string]$officeInventory.WordArchitecture
$wpsArchitecture = [string]$officeInventory.WpsArchitecture

if ($Action -eq 'Diagnose') {
    $wpsShimDiagnosis = if (Test-Path -LiteralPath $wpsShimScript) {
        try { (& $wpsShimScript -Action Diagnose | Out-String).Trim() }
        catch { 'WPS 原生 COM 加载桥诊断失败：' + $_.Exception.Message }
    } else { '当前发布包未包含 WPS 原生 COM 加载桥脚本。' }
    [pscustomobject]@{
        WordInstalled = $wordCandidates.Count -gt 0
        WordPath = $wordCandidates -join '; '
		WordArchitecture = $wordArchitecture
		WordComServer = @($officeInventory.WordComServers) -join '; '
		WordComRedirectedToWps = [bool]$officeInventory.WordComRedirectedToWps
		WpsInstalled = [bool]$officeInventory.WpsInstalled
		WpsPath = $wpsCandidates -join '; '
		WpsArchitecture = $wpsArchitecture
		WpsComServer = @($officeInventory.WpsComServers) -join '; '
		OfficeRegistrationRemnants = @($officeInventory.OfficeRegistrationRemnants) -join '; '
		PackagePlatform = $packagePlatform
		WordPlatformCompatible = $wordArchitecture -eq 'not-installed' -or $wordArchitecture -eq 'unknown' -or $packagePlatform -eq 'unknown' -or $wordArchitecture -eq $packagePlatform
		WpsPlatformCompatible = $wpsArchitecture -eq 'not-installed' -or $wpsArchitecture -eq 'unknown' -or $packagePlatform -eq 'unknown' -or $wpsArchitecture -eq $packagePlatform
		RequestedHostTarget = $HostTarget
        AddInRegistered = Test-Path -LiteralPath $addInKey
        Manifest = $manifestPath
        PublisherCertificate = $certificatePath
		ManifestInclusionTrusted = @(Get-ManifestInclusionEntries $manifestUrl).Count -gt 0
		WpsWhitelistValues = @($registryViews | ForEach-Object {
			$view = $_
			@(
				[string](Get-RegistryViewValue $view 'Software\Kingsoft\Office\WPS\AddinsWL' 'partyops.documentformatter')
				[string](Get-RegistryViewValue $view 'Software\Kingsoft\Office\6.0\wps\AddinsWL' 'partyops.documentformatter')
			)
		}) -join '; '
		WpsComShimAssembly = $wpsShimAssemblyPath
		WpsComShimDiagnosis = $wpsShimDiagnosis
		RegistryViewValues = @($registryViews | ForEach-Object {
			$view = $_
			[pscustomobject]@{
				View = $view.ToString()
				Manifest = [string](Get-RegistryViewValue $view 'Software\Microsoft\Office\Word\Addins\partyops.documentformatter' 'Manifest')
				WpsWhitelist = [string](Get-RegistryViewValue $view 'Software\Kingsoft\Office\WPS\AddinsWL' 'partyops.documentformatter')
				WpsLegacyWhitelist = [string](Get-RegistryViewValue $view 'Software\Kingsoft\Office\6.0\wps\AddinsWL' 'partyops.documentformatter')
			}
		}) | ConvertTo-Json -Compress
    } | Format-List
    return
}

if ($Action -eq 'Uninstall') {
    if (Test-Path -LiteralPath $wpsShimScript) {
        & $wpsShimScript -Action Uninstall | Out-Null
    }
    foreach ($view in $registryViews) {
        Remove-RegistryViewTree $view 'Software\Microsoft\Office\Word\Addins\partyops.documentformatter'
    }
    foreach ($entry in @(Get-ManifestInclusionEntries $manifestUrl)) {
        Remove-Item -LiteralPath $entry.PSPath -Recurse -Force
    }
    foreach ($view in $registryViews) {
        Remove-RegistryViewValue $view 'Software\Kingsoft\Office\WPS\AddinsWL' 'partyops.documentformatter'
        Remove-RegistryViewValue $view 'Software\Kingsoft\Office\6.0\wps\AddinsWL' 'partyops.documentformatter'
    }
    Write-Host '已移除当前用户的partyops公文排版助手加载项注册及本包精确 VSTO Inclusion 条目。发布文件与证书未删除。' -ForegroundColor Green
    return
}

$effectiveHostTarget = if ($HostTarget -eq 'Auto') { 'Word' } else { $HostTarget }
$hostCandidates = if ($effectiveHostTarget -eq 'Wps') { $wpsCandidates } else { $wordCandidates }
$hostArchitecture = if ($effectiveHostTarget -eq 'Wps') { $wpsArchitecture } else { $wordArchitecture }
$hostDisplayName = if ($effectiveHostTarget -eq 'Wps') { 'WPS Writer' } else { 'Microsoft Word' }
if ($hostCandidates.Count -eq 0) {
    if ($effectiveHostTarget -eq 'Wps') {
        throw '未检测到真实 wps.exe。请先修复或安装 WPS Office 文字组件。'
    }
    throw '未检测到真实 WINWORD.EXE。若 Word.Application 被 WPS 接管，不能将 WPS COM 服务误判为 Microsoft Word；如需显式验证 WPS，请使用 -HostTarget Wps。'
}
if ($hostArchitecture -ne 'unknown' -and $packagePlatform -ne 'unknown' -and $hostArchitecture -ne $packagePlatform) {
	throw "安装包位数与 $hostDisplayName 不匹配：安装包=$packagePlatform，宿主=$hostArchitecture。请改用与宿主位数一致的发布包。"
}
if ($effectiveHostTarget -eq 'Wps') {
    Write-Warning 'WPS 兼容安装将同时保留 Word VSTO 注册并安装 WPS 原生 COM 加载桥。请先保存所有文档，并在本机完成 Ribbon/COM 功能验收。'
}
if ($TrustPublisher) {
    if (!(Test-Path -LiteralPath $certificatePath)) {
        throw "发布者证书不存在：$certificatePath"
    }
    $certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($certificatePath)
    foreach ($storeName in @('Root', 'TrustedPublisher')) {
        $store = [System.Security.Cryptography.X509Certificates.X509Store]::new($storeName, 'CurrentUser')
        try {
            $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
            $store.Add($certificate)
        }
        finally {
            $store.Dispose()
        }
    }
    Write-Warning "已将发布者证书加入当前用户的受信任根和受信任发布者：$($certificate.Thumbprint)"
}
if ($TrustManifest) {
    if (!(Test-Path -LiteralPath $certificatePath)) {
        throw "发布者证书不存在：$certificatePath"
    }
    $certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($certificatePath)
    $publicKeyXml = Get-RsaPublicKeyXml $certificate
    $entries = @(Get-ManifestInclusionEntries $manifestUrl)
    $inclusionKey = if ($entries.Count -gt 0) { $entries[0].PSPath } else {
        (New-Item -Path (Join-Path $inclusionRoot ([guid]::NewGuid().ToString())) -Force).PSPath
    }
    New-ItemProperty -LiteralPath $inclusionKey -Name 'Url' -Value $manifestUrl -PropertyType String -Force | Out-Null
    New-ItemProperty -LiteralPath $inclusionKey -Name 'PublicKey' -Value $publicKeyXml -PropertyType String -Force | Out-Null
    Write-Warning "已建立仅绑定当前清单 URL 与签名公钥的用户级 VSTO Inclusion 信任：$manifestUrl"
}

$manifestUri = $manifestUrl + '|vstolocal'
foreach ($view in $registryViews) {
    $wordAddInSubKey = 'Software\Microsoft\Office\Word\Addins\partyops.documentformatter'
    Set-RegistryViewValue $view $wordAddInSubKey 'Description' 'partyops公文排版助手' ([Microsoft.Win32.RegistryValueKind]::String)
    Set-RegistryViewValue $view $wordAddInSubKey 'FriendlyName' 'partyops公文排版助手' ([Microsoft.Win32.RegistryValueKind]::String)
    Set-RegistryViewValue $view $wordAddInSubKey 'LoadBehavior' 3 ([Microsoft.Win32.RegistryValueKind]::DWord)
    Set-RegistryViewValue $view $wordAddInSubKey 'Manifest' $manifestUri ([Microsoft.Win32.RegistryValueKind]::String)
}
if ($effectiveHostTarget -eq 'Wps') {
	    if (!(Test-Path -LiteralPath $wpsShimScript) -or !(Test-Path -LiteralPath $wpsShimAssemblyPath)) {
	        throw "当前发布包缺少 WPS 原生 COM 加载桥：$wpsShimAssemblyPath"
	    }
	    # 精确删除本插件的旧 VSTO 白名单值，避免 WPS 同时走 VSTO 与原生 COM 两条加载链。
	    # Word 的 VSTO 注册仍保留，真实 Word 安装后不受影响。
	    foreach ($view in $registryViews) {
	        Remove-RegistryViewValue $view 'Software\Kingsoft\Office\WPS\AddinsWL' 'partyops.documentformatter'
	        Remove-RegistryViewValue $view 'Software\Kingsoft\Office\6.0\wps\AddinsWL' 'partyops.documentformatter'
	    }
	    $shimPlatform = if ($packagePlatform -in @('x86', 'x64')) { $packagePlatform } else { 'Auto' }
	    & $wpsShimScript -Action Install -AssemblyPath $wpsShimAssemblyPath -Platform $shimPlatform | Out-Null
	    Write-Host "WPS 原生 COM 加载桥已更新：$wpsShimAssemblyPath" -ForegroundColor Green
}
Write-Host "加载项已注册：$manifestUri" -ForegroundColor Green
Write-Host "目标宿主：$hostDisplayName（$hostArchitecture）" -ForegroundColor Green

if ($LaunchWord -and $effectiveHostTarget -ne 'Word') {
    throw '-LaunchWord 只能与 Word 目标宿主一起使用。'
}
if ($LaunchWps -and $effectiveHostTarget -ne 'Wps') {
    throw '-LaunchWps 只能与 Wps 目标宿主一起使用。'
}
if ($LaunchWord -or $LaunchWps) {
    Start-Process -FilePath $hostCandidates[0]
}
