param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Debug',
    [ValidateSet('AnyCPU','x86','x64')][string]$Platform = 'AnyCPU'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'Verify-RecoveredSource.ps1') -Root $root

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (!(Test-Path $vswhere)) {
    throw 'Visual Studio 2022 / vswhere not found. Install VS 2022 with Office/SharePoint development and .NET Framework 4.8 targeting pack.'
}
# Build Tools 是独立产品 SKU，必须显式包含，否则会误报 MSBuild 未安装。
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1
if (!$msbuild) { throw 'MSBuild not found.' }
$visualStudioInstallPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath | Select-Object -First 1
if (!$visualStudioInstallPath) { throw 'Visual Studio installation path not found.' }
$vsPublicAssembliesPath = Join-Path $visualStudioInstallPath 'Common7\IDE\PublicAssemblies'
if (!(Test-Path -LiteralPath (Join-Path $vsPublicAssembliesPath 'extensibility.dll'))) {
    throw "Visual Studio Extensibility interop assembly not found: $vsPublicAssembliesPath"
}

$sln = Join-Path $root 'PartyOps.DocumentFormatter.sln'
$solutionPlatform = if ($Platform -eq 'AnyCPU') { 'Any CPU' } else { $Platform }
$msbuildArguments = @(
    $sln,
    '/m',
    '/restore',
    '/t:Rebuild',
    "/p:Configuration=$Configuration",
    "/p:Platform=$solutionPlatform",
    "/p:VsPublicAssembliesPath=$vsPublicAssembliesPath"
)

# 当前机器可能只有 .NET Framework 4.8.1 运行时而没有 4.8 Targeting Pack。
# 优先使用正式引用程序集；缺失时才使用同机运行时程序集完成可重复的离线构建。
$targetingPack = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
if (!(Test-Path (Join-Path $targetingPack 'mscorlib.dll'))) {
    $frameworkFolder = if ($Platform -eq 'x86') { 'Framework' } else { 'Framework64' }
    $frameworkPath = Join-Path $env:WINDIR "Microsoft.NET\$frameworkFolder\v4.0.30319"
    if (!(Test-Path (Join-Path $frameworkPath 'mscorlib.dll'))) {
        throw '.NET Framework 4.8 reference assemblies and runtime fallback are both unavailable.'
    }
    Write-Warning ".NET Framework 4.8 Targeting Pack 未安装；本次使用现有运行时引用：$frameworkPath"
    $msbuildArguments += "/p:FrameworkPathOverride=$frameworkPath"
}

Write-Host "MSBuild: $msbuild"
& $msbuild @msbuildArguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& (Join-Path $PSScriptRoot 'Run-RegressionTests.ps1') -Configuration $Configuration -Platform $Platform

Write-Host 'Build and regression tests completed. Use tools\Publish-Vsto.ps1 to generate signed VSTO packages.' -ForegroundColor Green
