param(
    [ValidateSet('x86', 'x64')]
    [string]$Platform = 'x64',
    [string]$OutputRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\publish'),
    [string]$CertificatePath = '',
    [string]$CertificatePassword = '',
    [string]$TimestampUrl = '',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root 'assets'
$publishRoot = Join-Path $OutputRoot $Platform
$applicationDirectory = Join-Path $publishRoot 'application'
$allowedPublishRoot = [System.IO.Path]::GetFullPath((Join-Path $root 'artifacts\publish'))
$resolvedPublishRoot = [System.IO.Path]::GetFullPath($publishRoot)

if (!$resolvedPublishRoot.StartsWith($allowedPublishRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "发布目录必须位于项目 artifacts\publish 内：$resolvedPublishRoot"
}

if (!$SkipBuild) {
    & (Join-Path $PSScriptRoot 'Build-Windows.ps1') -Configuration Release -Platform $Platform
}

$addinOutput = Join-Path $root "src\PartyOps.DocumentFormatter.AddIn\bin\$Platform\Release"
$wpsShimOutput = Join-Path $root "src\PartyOps.DocumentFormatter.WpsShim\bin\$Platform\Release"
$pdfOutput = Join-Path $root "src\PartyOps.DocumentFormatter.PdfToWord\bin\$Platform\Release\PartyOps.DocumentFormatter.PdfToWord.exe"
$repairOutput = Join-Path $root "src\PartyOps.DocumentFormatter.AddInRepair\bin\$Platform\Release\PartyOps.DocumentFormatter.AddInRepair.exe"
foreach ($required in @($addinOutput, $wpsShimOutput, $pdfOutput, $repairOutput)) {
    if (!(Test-Path -LiteralPath $required)) {
        throw "发布输入不存在，请先构建：$required"
    }
}

# 发布目录只包含可再生制品；先验证边界，再替换旧制品，防止陈旧文件混入清单。
if (Test-Path -LiteralPath $publishRoot) {
    Remove-Item -LiteralPath $publishRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $applicationDirectory | Out-Null

Get-ChildItem -LiteralPath $addinOutput -Recurse -File |
    Where-Object { $_.Extension -ne '.pdb' } |
    ForEach-Object {
        $relative = $_.FullName.Substring($addinOutput.Length).TrimStart('\')
        $destination = Join-Path $applicationDirectory $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $destination -Force
    }
Copy-Item -LiteralPath $pdfOutput -Destination (Join-Path $applicationDirectory 'PartyOps.DocumentFormatter.PdfToWord.exe') -Force
Copy-Item -LiteralPath $repairOutput -Destination (Join-Path $applicationDirectory 'PartyOps.DocumentFormatter.AddInRepair.exe') -Force
Copy-Item -LiteralPath (Join-Path $wpsShimOutput 'PartyOps.DocumentFormatter.WpsShim.dll') -Destination (Join-Path $applicationDirectory 'PartyOps.DocumentFormatter.WpsShim.dll') -Force
# WPS COM 入口直接实现官方 IDTExtensibility2；PIA 必须与加载桥同目录发布，
# 避免仅安装 Build Tools 而未写入 GAC 的机器在 COM 激活时解析失败。
Copy-Item -LiteralPath (Join-Path $wpsShimOutput 'extensibility.dll') -Destination (Join-Path $applicationDirectory 'extensibility.dll') -Force
Copy-Item -LiteralPath (Join-Path $assets 'THIRD-PARTY-NOTICES.txt') -Destination $applicationDirectory -Force

if ([string]::IsNullOrWhiteSpace($CertificatePath)) {
    $developmentCertificate = & (Join-Path $PSScriptRoot 'New-DevelopmentCertificate.ps1')
    $CertificatePath = $developmentCertificate.PfxPath
    $CertificatePassword = $developmentCertificate.Password
    $publicCertificatePath = $developmentCertificate.CerPath
    $certificateMode = 'development-self-signed'
}
else {
    if (!(Test-Path -LiteralPath $CertificatePath)) {
        throw "代码签名证书不存在：$CertificatePath"
    }
    $certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
        $CertificatePath,
        $CertificatePassword,
        [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet
    )
    if (!$certificate.HasPrivateKey) {
        throw '指定的 PFX 不包含私钥。'
    }
    $publicCertificatePath = Join-Path $publishRoot 'publisher.cer'
    [System.IO.File]::WriteAllBytes(
        $publicCertificatePath,
        $certificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert)
    )
    $certificateMode = 'external-code-signing'
}

$applicationManifest = Join-Path $applicationDirectory 'PartyOps.DocumentFormatter.AddIn.dll.manifest'
$deploymentManifest = Join-Path $applicationDirectory 'PartyOps.DocumentFormatter.AddIn.vsto'
$mainAssemblyPath = Join-Path $applicationDirectory 'PartyOps.DocumentFormatter.AddIn.dll'
$mainAssemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($mainAssemblyPath)
$version = $mainAssemblyName.Version.ToString()

# 清单从当前程序集元数据全新生成，不复用任何历史品牌模板、签名或发布标识。
$applicationManifestTemplate = @"
<?xml version="1.0" encoding="utf-8"?>
<asmv1:assembly xsi:schemaLocation="urn:schemas-microsoft-com:asm.v1 assembly.adaptive.xsd"
  manifestVersion="1.0"
  xmlns:asmv1="urn:schemas-microsoft-com:asm.v1"
  xmlns:asmv2="urn:schemas-microsoft-com:asm.v2"
  xmlns:asmv3="urn:schemas-microsoft-com:asm.v3"
  xmlns:co.v1="urn:schemas-microsoft-com:clickonce.v1"
  xmlns:co.v2="urn:schemas-microsoft-com:clickonce.v2"
  xmlns:dsig="http://www.w3.org/2000/09/xmldsig#"
  xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
  xmlns="urn:schemas-microsoft-com:asm.v2">
  <asmv1:assemblyIdentity name="PartyOps.DocumentFormatter.AddIn.dll" version="$version" publicKeyToken="0000000000000000" language="neutral" processorArchitecture="msil" type="win32" />
  <asmv1:description>partyops公文排版助手</asmv1:description>
  <application />
  <entryPoint>
    <co.v1:customHostSpecified />
  </entryPoint>
  <trustInfo>
    <security>
      <applicationRequestMinimum>
        <PermissionSet Unrestricted="true" ID="Custom" SameSite="site" />
        <defaultAssemblyRequest permissionSetReference="Custom" />
      </applicationRequestMinimum>
      <requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
        <requestedExecutionLevel level="asInvoker" uiAccess="false" />
      </requestedPrivileges>
    </security>
  </trustInfo>
  <dependency>
    <dependentOS>
      <osVersionInfo>
        <os majorVersion="6" minorVersion="1" buildNumber="7601" servicePackMajor="1" />
      </osVersionInfo>
    </dependentOS>
  </dependency>
  <dependency>
    <dependentAssembly dependencyType="preRequisite" allowDelayedBinding="true">
      <assemblyIdentity name="Microsoft.Windows.CommonLanguageRuntime" version="4.0.30319.0" />
    </dependentAssembly>
  </dependency>
  <dependency>
    <dependentAssembly dependencyType="preRequisite" allowDelayedBinding="true">
      <assemblyIdentity name="Microsoft.Office.Tools" version="10.0.0.0" publicKeyToken="b03f5f7f11d50a3a" language="neutral" processorArchitecture="msil" />
    </dependentAssembly>
  </dependency>
  <dependency>
    <dependentAssembly dependencyType="preRequisite" allowDelayedBinding="true">
      <assemblyIdentity name="Microsoft.Office.Tools.Common" version="10.0.0.0" publicKeyToken="b03f5f7f11d50a3a" language="neutral" processorArchitecture="msil" />
    </dependentAssembly>
  </dependency>
  <dependency>
    <dependentAssembly dependencyType="preRequisite" allowDelayedBinding="true">
      <assemblyIdentity name="Microsoft.Office.Tools.Word" version="10.0.0.0" publicKeyToken="b03f5f7f11d50a3a" language="neutral" processorArchitecture="msil" />
    </dependentAssembly>
  </dependency>
  <dependency>
    <dependentAssembly dependencyType="preRequisite" allowDelayedBinding="true">
      <assemblyIdentity name="Microsoft.Office.Tools.v4.0.Framework" version="10.0.0.0" publicKeyToken="b03f5f7f11d50a3a" language="neutral" processorArchitecture="msil" />
    </dependentAssembly>
  </dependency>
  <dependency>
    <dependentAssembly dependencyType="preRequisite" allowDelayedBinding="true">
      <assemblyIdentity name="Microsoft.VisualStudio.Tools.Applications.Runtime" version="10.0.0.0" publicKeyToken="b03f5f7f11d50a3a" language="neutral" processorArchitecture="msil" />
    </dependentAssembly>
  </dependency>
  <vstav3:addIn xmlns:vstav3="urn:schemas-microsoft-com:vsta.v3">
    <vstav3:entryPointsCollection>
      <vstav3:entryPoints>
        <vstav3:entryPoint class="DocumentRepository.ThisAddIn">
          <assemblyIdentity name="PartyOps.DocumentFormatter.AddIn" version="$version" language="neutral" processorArchitecture="msil" />
        </vstav3:entryPoint>
      </vstav3:entryPoints>
    </vstav3:entryPointsCollection>
    <vstav3:update enabled="false" />
    <vstav3:application>
      <vstov4:customizations xmlns:vstov4="urn:schemas-microsoft-com:vsto.v4">
        <vstov4:customization>
          <vstov4:appAddIn application="Word" loadBehavior="3" keyName="partyops.documentformatter">
            <vstov4:friendlyName>partyops公文排版助手</vstov4:friendlyName>
            <vstov4:description>partyops公文排版助手</vstov4:description>
            <vstov4.1:ribbonTypes xmlns:vstov4.1="urn:schemas-microsoft-com:vsto.v4.1">
              <vstov4.1:ribbonType name="DocumentRepository.Ribbon1, PartyOps.DocumentFormatter.AddIn, Version=$version, Culture=neutral, PublicKeyToken=null" />
            </vstov4.1:ribbonTypes>
          </vstov4:appAddIn>
        </vstov4:customization>
      </vstov4:customizations>
    </vstav3:application>
  </vstav3:addIn>
</asmv1:assembly>
"@

$deploymentManifestTemplate = @"
<?xml version="1.0" encoding="utf-8"?>
<asmv1:assembly xsi:schemaLocation="urn:schemas-microsoft-com:asm.v1 assembly.adaptive.xsd"
  manifestVersion="1.0"
  xmlns:asmv1="urn:schemas-microsoft-com:asm.v1"
  xmlns:asmv2="urn:schemas-microsoft-com:asm.v2"
  xmlns:co.v2="urn:schemas-microsoft-com:clickonce.v2"
  xmlns:dsig="http://www.w3.org/2000/09/xmldsig#"
  xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
  xmlns="urn:schemas-microsoft-com:asm.v2">
  <asmv1:assemblyIdentity name="PartyOps.DocumentFormatter.AddIn.vsto" version="$version" publicKeyToken="0000000000000000" language="neutral" processorArchitecture="msil" />
  <asmv1:description asmv2:publisher="PartyOps" asmv2:product="partyops公文排版助手" />
  <deployment install="false" mapFileExtensions="true" />
  <compatibleFrameworks xmlns="urn:schemas-microsoft-com:clickonce.v2">
    <framework targetVersion="4.8" profile="Full" supportedRuntime="4.0.30319" />
  </compatibleFrameworks>
  <dependency>
    <dependentAssembly dependencyType="install" codebase="PartyOps.DocumentFormatter.AddIn.dll.manifest" size="0">
      <assemblyIdentity name="PartyOps.DocumentFormatter.AddIn.dll" version="$version" publicKeyToken="0000000000000000" language="neutral" processorArchitecture="msil" type="win32" />
    </dependentAssembly>
  </dependency>
</asmv1:assembly>
"@

[System.IO.File]::WriteAllText($applicationManifest, $applicationManifestTemplate, [System.Text.UTF8Encoding]::new($false))
[System.IO.File]::WriteAllText($deploymentManifest, $deploymentManifestTemplate, [System.Text.UTF8Encoding]::new($false))

$asmv1 = 'urn:schemas-microsoft-com:asm.v1'
$asmv2 = 'urn:schemas-microsoft-com:asm.v2'
$dsig = 'http://www.w3.org/2000/09/xmldsig#'

function Get-FileDigest([string]$Path) {
    $stream = [System.IO.File]::OpenRead($Path)
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        return [Convert]::ToBase64String($sha256.ComputeHash($stream))
    }
    finally {
        $sha256.Dispose()
        $stream.Dispose()
    }
}

function Add-HashNode([System.Xml.XmlDocument]$Document, [System.Xml.XmlElement]$Parent, [string]$Path) {
    $hash = $Document.CreateElement('hash', $asmv2)
    $transforms = $Document.CreateElement('dsig', 'Transforms', $dsig)
    $transform = $Document.CreateElement('dsig', 'Transform', $dsig)
    $transform.SetAttribute('Algorithm', 'urn:schemas-microsoft-com:HashTransforms.Identity')
    [void]$transforms.AppendChild($transform)
    $method = $Document.CreateElement('dsig', 'DigestMethod', $dsig)
    $method.SetAttribute('Algorithm', 'http://www.w3.org/2000/09/xmldsig#sha256')
    $value = $Document.CreateElement('dsig', 'DigestValue', $dsig)
    $value.InnerText = Get-FileDigest $Path
    [void]$hash.AppendChild($transforms)
    [void]$hash.AppendChild($method)
    [void]$hash.AppendChild($value)
    [void]$Parent.AppendChild($hash)
}

function Save-XmlWithoutBom([System.Xml.XmlDocument]$Document, [string]$Path) {
    $settings = [System.Xml.XmlWriterSettings]::new()
    $settings.Encoding = [System.Text.UTF8Encoding]::new($false)
    $settings.Indent = $true
    $settings.NewLineChars = "`r`n"
    $settings.NewLineHandling = [System.Xml.NewLineHandling]::Replace
    $writer = [System.Xml.XmlWriter]::Create($Path, $settings)
    try {
        $Document.Save($writer)
    }
    finally {
        $writer.Dispose()
    }
}

function Remove-OldSignature([System.Xml.XmlDocument]$Document) {
    @($Document.DocumentElement.ChildNodes) |
        Where-Object { $_.LocalName -in @('Signature', 'publisherIdentity') } |
        ForEach-Object { [void]$Document.DocumentElement.RemoveChild($_) }
}

$appXml = [System.Xml.XmlDocument]::new()
$appXml.PreserveWhitespace = $false
$appXml.Load($applicationManifest)
Remove-OldSignature $appXml
$appRoot = $appXml.DocumentElement
$appIdentity = $appRoot.SelectSingleNode('*[local-name()="assemblyIdentity"]')
$appIdentity.SetAttribute('version', $version)

# 清除模板中所有旧的本地文件依赖；保留 .NET/VSTO/GAC 前置依赖。
@($appRoot.ChildNodes) | Where-Object {
    $_.LocalName -eq 'dependency' -and
    $null -ne $_.SelectSingleNode('*[local-name()="dependentAssembly" and string-length(@codebase) > 0]')
} | ForEach-Object { [void]$appRoot.RemoveChild($_) }
@($appRoot.ChildNodes) | Where-Object { $_.LocalName -eq 'file' } |
    ForEach-Object { [void]$appRoot.RemoveChild($_) }

$addinNode = $appRoot.SelectSingleNode('*[local-name()="addIn"]')
$packageFiles = Get-ChildItem -LiteralPath $applicationDirectory -Recurse -File |
    Where-Object { $_.Name -notin @('PartyOps.DocumentFormatter.AddIn.dll.manifest', 'PartyOps.DocumentFormatter.AddIn.vsto') -and $_.Extension -ne '.pdb' } |
    Sort-Object FullName

foreach ($file in $packageFiles) {
    $relative = $file.FullName.Substring($applicationDirectory.Length + 1).Replace('/', '\')
    $assemblyName = $null
    if ($file.Extension -ieq '.dll') {
        try {
            $assemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($file.FullName)
        }
        catch [System.BadImageFormatException] {
            $assemblyName = $null
        }
    }

    if ($null -ne $assemblyName) {
        $dependency = $appXml.CreateElement('dependency', $asmv2)
        $dependentAssembly = $appXml.CreateElement('dependentAssembly', $asmv2)
        $dependentAssembly.SetAttribute('dependencyType', 'install')
        $dependentAssembly.SetAttribute('allowDelayedBinding', 'true')
        $dependentAssembly.SetAttribute('codebase', $relative)
        $dependentAssembly.SetAttribute('size', $file.Length.ToString())
        $identity = $appXml.CreateElement('assemblyIdentity', $asmv2)
        $identity.SetAttribute('name', $assemblyName.Name)
        $identity.SetAttribute('version', $assemblyName.Version.ToString())
        $identity.SetAttribute('language', [string]::IsNullOrWhiteSpace($assemblyName.CultureName) ? 'neutral' : $assemblyName.CultureName)
        $architecture = switch ($assemblyName.ProcessorArchitecture.ToString()) {
            'X86' { 'x86' }
            'Amd64' { 'amd64' }
            default { 'msil' }
        }
        $identity.SetAttribute('processorArchitecture', $architecture)
        $tokenBytes = $assemblyName.GetPublicKeyToken()
        if ($null -ne $tokenBytes -and $tokenBytes.Length -gt 0) {
            $token = -join ($tokenBytes | ForEach-Object { $_.ToString('x2') })
            $identity.SetAttribute('publicKeyToken', $token)
        }
        [void]$dependentAssembly.AppendChild($identity)
        Add-HashNode $appXml $dependentAssembly $file.FullName
        [void]$dependency.AppendChild($dependentAssembly)
        [void]$appRoot.InsertBefore($dependency, $addinNode)
    }
    else {
        $fileNode = $appXml.CreateElement('file', $asmv2)
        $fileNode.SetAttribute('name', $relative)
        $fileNode.SetAttribute('size', $file.Length.ToString())
        Add-HashNode $appXml $fileNode $file.FullName
        [void]$appRoot.InsertBefore($fileNode, $addinNode)
    }
}

$appXml.SelectNodes('//*[local-name()="entryPoint"]/*[local-name()="assemblyIdentity" and @name="PartyOps.DocumentFormatter.AddIn"]') |
    ForEach-Object { $_.SetAttribute('version', $version) }
$appXml.SelectNodes('//*[local-name()="ribbonType"]') | ForEach-Object {
    $_.SetAttribute('name', ($_.GetAttribute('name') -replace 'Version=[^,]+', "Version=$version"))
}
Save-XmlWithoutBom $appXml $applicationManifest

& (Join-Path $PSScriptRoot 'Sign-ClickOnceManifest.ps1') `
    -ManifestPath $applicationManifest `
    -CertificatePath $CertificatePath `
    -CertificatePassword $CertificatePassword `
    -TimestampUrl $TimestampUrl

$signedAppXml = [System.Xml.XmlDocument]::new()
$signedAppXml.Load($applicationManifest)
$signedAppIdentity = $signedAppXml.DocumentElement.SelectSingleNode('*[local-name()="assemblyIdentity"]')

$deployXml = [System.Xml.XmlDocument]::new()
$deployXml.PreserveWhitespace = $false
$deployXml.Load($deploymentManifest)
Remove-OldSignature $deployXml
$deployRoot = $deployXml.DocumentElement
$deployIdentity = $deployRoot.SelectSingleNode('*[local-name()="assemblyIdentity"]')
$deployIdentity.SetAttribute('version', $version)
$dependentManifest = $deployRoot.SelectSingleNode('*[local-name()="dependency"]/*[local-name()="dependentAssembly"]')
$dependentManifest.SetAttribute('codebase', 'PartyOps.DocumentFormatter.AddIn.dll.manifest')
$dependentManifest.SetAttribute('size', (Get-Item -LiteralPath $applicationManifest).Length.ToString())
$dependentIdentity = $dependentManifest.SelectSingleNode('*[local-name()="assemblyIdentity"]')
$dependentIdentity.RemoveAll()
foreach ($attribute in $signedAppIdentity.Attributes) {
    $dependentIdentity.SetAttribute($attribute.Name, $attribute.Value)
}
@($dependentManifest.ChildNodes) | Where-Object { $_.LocalName -eq 'hash' } |
    ForEach-Object { [void]$dependentManifest.RemoveChild($_) }
Add-HashNode $deployXml $dependentManifest $applicationManifest
Save-XmlWithoutBom $deployXml $deploymentManifest

& (Join-Path $PSScriptRoot 'Sign-ClickOnceManifest.ps1') `
    -ManifestPath $deploymentManifest `
    -CertificatePath $CertificatePath `
    -CertificatePassword $CertificatePassword `
    -TimestampUrl $TimestampUrl

if ($certificateMode -eq 'development-self-signed') {
    Copy-Item -LiteralPath $publicCertificatePath -Destination (Join-Path $publishRoot 'publisher.cer') -Force
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PublishTemplates\Install-Local.ps1') -Destination $publishRoot -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Register-WpsComAddIn.ps1') -Destination $publishRoot -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Verify-VstoPackage.ps1') -Destination $publishRoot -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Get-OfficeHostInventory.ps1') -Destination $publishRoot -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PublishTemplates\README.txt') -Destination $publishRoot -Force

& (Join-Path $PSScriptRoot 'Verify-VstoPackage.ps1') -ApplicationDirectory $applicationDirectory

$summary = [ordered]@{
    generatedAt = [DateTimeOffset]::Now.ToString('o')
    platform = $Platform
    version = $version
    certificateMode = $certificateMode
    certificateThumbprint = ([System.Security.Cryptography.X509Certificates.X509Certificate2]::new((Join-Path $publishRoot 'publisher.cer'))).Thumbprint
    applicationDirectory = $applicationDirectory
    fileCount = $packageFiles.Count
}
$summary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $publishRoot 'publish-summary.json') -Encoding utf8NoBOM
Write-Host "VSTO 发布包已生成并验证：$publishRoot" -ForegroundColor Green
