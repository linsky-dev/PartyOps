param(
    [Parameter(Mandatory = $true)]
    [string]$ApplicationDirectory,
    [string]$MsBuildTasksPath = 'E:\codex\PartyOps\.build-kit\vs2022\MSBuild\Current\Bin\Microsoft.Build.Tasks.Core.dll'
)

$ErrorActionPreference = 'Stop'
$isCorePowerShell = $PSVersionTable.PSEdition -eq 'Core'
if ($isCorePowerShell) {
    $windowsPowerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    & $windowsPowerShell -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath -ApplicationDirectory $ApplicationDirectory -MsBuildTasksPath $MsBuildTasksPath
    if ($LASTEXITCODE -ne 0) {
        throw "VSTO 发布包的 .NET Framework 验证失败，退出码：$LASTEXITCODE"
    }
    return
}

$applicationDirectory = [System.IO.Path]::GetFullPath($ApplicationDirectory)
$applicationManifest = Join-Path $applicationDirectory 'PartyOps.DocumentFormatter.AddIn.dll.manifest'
$deploymentManifest = Join-Path $applicationDirectory 'PartyOps.DocumentFormatter.AddIn.vsto'
foreach ($path in @($applicationManifest, $deploymentManifest)) {
    if (!(Test-Path -LiteralPath $path)) {
        throw "VSTO 清单不存在：$path"
    }
}

Add-Type -AssemblyName System.Security
if (!(Test-Path -LiteralPath $MsBuildTasksPath)) {
    throw "MSBuild 清单任务程序集不存在：$MsBuildTasksPath"
}
$tasksAssembly = [System.Reflection.Assembly]::LoadFrom($MsBuildTasksPath)
$sha256SignatureDescription = $tasksAssembly.GetType('System.Deployment.Internal.CodeSigning.RSAPKCS1SHA256SignatureDescription', $true)
[System.Security.Cryptography.CryptoConfig]::AddAlgorithm(
    $sha256SignatureDescription,
    'http://www.w3.org/2000/09/xmldsig#rsa-sha256'
)
[System.Security.Cryptography.CryptoConfig]::AddAlgorithm(
    [System.Security.Cryptography.SHA256CryptoServiceProvider],
    'http://www.w3.org/2000/09/xmldsig#sha256'
)
$checks = [System.Collections.Generic.List[object]]::new()
function Add-Check([string]$Name, [bool]$Passed, [string]$Detail) {
    $checks.Add([pscustomobject]@{ Check = $Name; Passed = $Passed; Detail = $Detail })
}

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

function Test-RootSignature([System.Xml.XmlDocument]$Document) {
    $signature = $Document.DocumentElement.SelectSingleNode('*[local-name()="Signature" and @Id="StrongNameSignature"]')
    if ($null -eq $signature) {
        return $false
    }
    $signedXml = [System.Security.Cryptography.Xml.SignedXml]::new($Document)
    $signedXml.LoadXml([System.Xml.XmlElement]$signature)
    return $signedXml.CheckSignature()
}

$appXml = [System.Xml.XmlDocument]::new()
$appXml.PreserveWhitespace = $true
$appXml.Load($applicationManifest)
$deployXml = [System.Xml.XmlDocument]::new()
$deployXml.PreserveWhitespace = $true
$deployXml.Load($deploymentManifest)

Add-Check '应用清单 XML 签名' (Test-RootSignature $appXml) 'StrongNameSignature 必须通过密码学验证。'
Add-Check '部署清单 XML 签名' (Test-RootSignature $deployXml) 'StrongNameSignature 必须通过密码学验证。'

$referencedFiles = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$referenceNodes = @($appXml.DocumentElement.SelectNodes('*[local-name()="dependency"]/*[local-name()="dependentAssembly" and string-length(@codebase) > 0]')) +
    @($appXml.DocumentElement.SelectNodes('*[local-name()="file"]'))
$hashFailures = [System.Collections.Generic.List[string]]::new()
foreach ($reference in $referenceNodes) {
    $relative = if ($reference.LocalName -eq 'file') { $reference.GetAttribute('name') } else { $reference.GetAttribute('codebase') }
    [void]$referencedFiles.Add($relative)
    $path = Join-Path $applicationDirectory $relative
    if (!(Test-Path -LiteralPath $path)) {
        $hashFailures.Add("缺失：$relative")
        continue
    }
    $expectedSize = [long]$reference.GetAttribute('size')
    $expectedDigest = $reference.SelectSingleNode('*[local-name()="hash"]/*[local-name()="DigestValue"]').InnerText
    $actual = Get-Item -LiteralPath $path
    if ($actual.Length -ne $expectedSize) {
        $hashFailures.Add("长度不符：$relative")
    }
    elseif ((Get-FileDigest $path) -ne $expectedDigest) {
        $hashFailures.Add("SHA-256 不符：$relative")
    }
}
$hashDetail = if ($hashFailures.Count -eq 0) { "$($referenceNodes.Count) 个文件全部匹配。" } else { $hashFailures -join '；' }
Add-Check '应用文件 SHA-256/长度' ($hashFailures.Count -eq 0) $hashDetail

$actualFiles = Get-ChildItem -LiteralPath $applicationDirectory -Recurse -File |
    Where-Object { $_.Name -notin @('PartyOps.DocumentFormatter.AddIn.dll.manifest', 'PartyOps.DocumentFormatter.AddIn.vsto') -and $_.Extension -ne '.pdb' } |
    ForEach-Object { $_.FullName.Substring($applicationDirectory.Length + 1).Replace('/', '\') }
$unreferenced = @($actualFiles | Where-Object { !$referencedFiles.Contains($_) })
$missingInventory = @($referencedFiles | Where-Object { $_ -notin $actualFiles })
Add-Check '发布文件清单闭合' ($unreferenced.Count -eq 0 -and $missingInventory.Count -eq 0) "未引用=$($unreferenced -join ',')；缺失=$($missingInventory -join ',')"

$manifestDependency = $deployXml.DocumentElement.SelectSingleNode('*[local-name()="dependency"]/*[local-name()="dependentAssembly"]')
$manifestDigest = $manifestDependency.SelectSingleNode('*[local-name()="hash"]/*[local-name()="DigestValue"]').InnerText
$manifestSize = [long]$manifestDependency.GetAttribute('size')
$actualManifest = Get-Item -LiteralPath $applicationManifest
Add-Check '部署到应用清单哈希' ($actualManifest.Length -eq $manifestSize -and (Get-FileDigest $applicationManifest) -eq $manifestDigest) '部署清单必须绑定已签名应用清单。'

$appIdentity = $appXml.DocumentElement.SelectSingleNode('*[local-name()="assemblyIdentity"]')
$dependencyIdentity = $manifestDependency.SelectSingleNode('*[local-name()="assemblyIdentity"]')
$sameIdentity = $appIdentity.GetAttribute('name') -eq $dependencyIdentity.GetAttribute('name') -and
    $appIdentity.GetAttribute('version') -eq $dependencyIdentity.GetAttribute('version') -and
    $appIdentity.GetAttribute('publicKeyToken') -eq $dependencyIdentity.GetAttribute('publicKeyToken')
Add-Check '清单标识一致' $sameIdentity "应用=$($appIdentity.GetAttribute('name'))/$($appIdentity.GetAttribute('version'))"

$entryPoint = $appXml.SelectSingleNode('//*[local-name()="entryPoint" and @class="DocumentRepository.ThisAddIn"]')
$wordAddIn = $appXml.SelectSingleNode('//*[local-name()="appAddIn" and @application="Word" and @keyName="partyops.documentformatter"]')
$ribbon = $appXml.SelectSingleNode('//*[local-name()="ribbonType" and contains(@name,"DocumentRepository.Ribbon1")]')
Add-Check 'VSTO Word 入口' ($null -ne $entryPoint -and $null -ne $wordAddIn -and $null -ne $ribbon) 'ThisAddIn、Word appAddIn 与 Ribbon1 均须存在。'
Add-Check '辅助程序齐全' ((Test-Path -LiteralPath (Join-Path $applicationDirectory 'PartyOps.DocumentFormatter.PdfToWord.exe')) -and (Test-Path -LiteralPath (Join-Path $applicationDirectory 'PartyOps.DocumentFormatter.AddInRepair.exe'))) 'PDF 转 Word与修复工具必须随包发布。'

$checks | Format-Table -AutoSize
if ($checks.Passed -contains $false) {
    throw 'VSTO 发布包验证失败。'
}
Write-Host "VSTO 发布包验证通过：$applicationDirectory" -ForegroundColor Green
