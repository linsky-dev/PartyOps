[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [ValidateSet('AnyCPU','x86','x64')][string]$Platform = 'x86',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$buildScript = Join-Path $projectRoot 'build.ps1'

if (-not $SkipBuild) {
    & $buildScript -Configuration $Configuration -Platform $Platform
    if ($LASTEXITCODE -ne 0) {
        throw "独立版构建失败，退出码：$LASTEXITCODE"
    }
}

$platformSegment = if ($Platform -eq 'AnyCPU') { '' } else { $Platform }
$sourceDirectory = if ([string]::IsNullOrWhiteSpace($platformSegment)) {
    Join-Path $projectRoot "src\PartyOps.DocumentFormatter.Desktop\bin\$Configuration"
} else {
    Join-Path $projectRoot "src\PartyOps.DocumentFormatter.Desktop\bin\$platformSegment\$Configuration"
}
$desktopExe = Join-Path $sourceDirectory 'PartyOps.DocumentFormatter.Desktop.exe'
if (-not (Test-Path -LiteralPath $desktopExe -PathType Leaf)) {
    throw "独立版可执行文件不存在：$desktopExe"
}

$fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($desktopExe).FileVersion
if ([string]::IsNullOrWhiteSpace($fileVersion)) {
    $version = 'unknown'
}
else {
    $version = ([Version]$fileVersion).ToString(3)
}
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$packageName = "PartyOps.DocumentFormatter-Standalone-$version-$Platform-$timestamp"
$publishRoot = Join-Path $projectRoot 'artifacts\publish\standalone'
$packageRoot = Join-Path $publishRoot $packageName
$applicationRoot = Join-Path $packageRoot 'application'
New-Item -ItemType Directory -Path $applicationRoot -Force | Out-Null

$excludedNames = @('输出', 'test-artifacts')
Get-ChildItem -LiteralPath $sourceDirectory | Where-Object {
    $_.Name -notin $excludedNames -and $_.Extension -ne '.pdb'
} | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $applicationRoot -Recurse -Force
}

$readme = @"
partyops公文排版助手独立版 $version（$Platform）

1. 直接运行 application\PartyOps.DocumentFormatter.Desktop.exe，无需注册 WPS/Word 加载项。
2. 本机 WPS 是 32 位时请选择 x86 包；Microsoft Word/WPS 位数必须与本包一致。
3. 所有业务处理都在输出副本上执行，源文件保持不变；首次使用可点击“引擎自检”。
4. 软件无需用户中心、账号登录或 WebView2；运行需要 .NET Framework 4.8 和已安装的 WPS 文字或 Microsoft Word。
5. 本包是便携目录，不向 C 盘安装程序文件；模板与参数保存在当前用户配置目录。

构建时间：$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')
构建配置：$Configuration|$Platform
"@
$readmePath = Join-Path $packageRoot '使用说明.txt'
Set-Content -LiteralPath $readmePath -Value $readme -Encoding utf8NoBOM

$checksums = Get-ChildItem -LiteralPath $applicationRoot -File -Recurse | Sort-Object FullName | ForEach-Object {
    [pscustomobject]@{
        path = [System.IO.Path]::GetRelativePath($packageRoot, $_.FullName)
        length = $_.Length
        sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    }
}
$manifest = [ordered]@{
    schema_version = 1
    product = 'partyops公文排版助手独立版'
    version = $version
    configuration = $Configuration
    platform = $Platform
    generated_at = (Get-Date).ToString('o')
    executable = 'application\PartyOps.DocumentFormatter.Desktop.exe'
    source_install_location = $sourceDirectory
    file_count = @($checksums).Count
    files = @($checksums)
}
$manifestPath = Join-Path $packageRoot 'package-manifest.json'
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM

$zipPath = "$packageRoot.zip"
Compress-Archive -LiteralPath $applicationRoot, $readmePath, $manifestPath -DestinationPath $zipPath -CompressionLevel Optimal

$verification = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
foreach ($file in $verification.files) {
    $candidate = Join-Path $packageRoot $file.path
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw "发布包文件缺失：$($file.path)"
    }
    $actualHash = (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash
    if ($actualHash -ne $file.sha256) {
        throw "发布包哈希验证失败：$($file.path)"
    }
}

[pscustomobject]@{
    PackageRoot = $packageRoot
    ZipPath = $zipPath
    Platform = $Platform
    FileCount = @($checksums).Count
    Verified = $true
} | Format-List
