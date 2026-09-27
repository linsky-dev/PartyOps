param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [ValidateSet('AnyCPU', 'x86', 'x64')]
    [string]$Platform = 'x64'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$coverageRoot = Join-Path $root 'artifacts\coverage'
$sandbox = Join-Path $coverageRoot 'sandbox'
$resolvedCoverageRoot = [System.IO.Path]::GetFullPath($coverageRoot)
$resolvedSandbox = [System.IO.Path]::GetFullPath($sandbox)
if (!$resolvedSandbox.StartsWith($resolvedCoverageRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "覆盖率沙箱必须位于 artifacts\coverage 内：$resolvedSandbox"
}

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$installationPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath | Select-Object -First 1
$coverageTool = Join-Path $installationPath 'Common7\IDE\Extensions\Microsoft\CodeCoverage.Console\Microsoft.CodeCoverage.Console.exe'
if (!(Test-Path -LiteralPath $coverageTool)) {
    throw "Microsoft Code Coverage Console 不存在：$coverageTool"
}
$settingsPath = Join-Path $PSScriptRoot 'Coverage.Dynamic.config'
if (!(Test-Path -LiteralPath $settingsPath)) {
    throw "动态覆盖率配置不存在：$settingsPath"
}

if (Test-Path -LiteralPath $sandbox) {
    Remove-Item -LiteralPath $sandbox -Recurse -Force
}
New-Item -ItemType Directory -Path $sandbox -Force | Out-Null

$outputSegment = if ($Platform -eq 'AnyCPU') { $Configuration } else { "$Platform\$Configuration" }
$tests = @(
    'RecognitionRegressionTests',
    'SignatureLayoutHostSmoke',
    'FeatureParityRegressionTests'
)
$reports = [System.Collections.Generic.List[string]]::new()

try {
    foreach ($testName in $tests) {
        $sourceDirectory = Join-Path $root "tests\$testName\bin\$outputSegment"
        $sourceExecutable = Join-Path $sourceDirectory "$testName.exe"
        if (!(Test-Path -LiteralPath $sourceExecutable)) {
            throw "覆盖率测试程序不存在，请先构建：$sourceExecutable"
        }
        $testSandbox = Join-Path $sandbox $testName
        New-Item -ItemType Directory -Path $testSandbox -Force | Out-Null
        Copy-Item -Path (Join-Path $sourceDirectory '*') -Destination $testSandbox -Recurse -Force
        $testExecutable = Join-Path $testSandbox "$testName.exe"
        $reportPath = Join-Path $coverageRoot ($testName + '.cobertura.xml')
        $logPath = Join-Path $coverageRoot ($testName + '.coverage.log')
        foreach ($generatedPath in @($reportPath, $logPath)) {
            if (Test-Path -LiteralPath $generatedPath) {
                Remove-Item -LiteralPath $generatedPath -Force
            }
        }
        & $coverageTool collect $testExecutable `
            --settings $settingsPath `
            --output $reportPath `
            --output-format cobertura `
            --log-file $logPath `
            --log-level Info `
            --nologo
        if ($LASTEXITCODE -ne 0) {
            throw "覆盖率收集失败：$testName；退出码=$LASTEXITCODE"
        }
        $logText = [System.IO.File]::ReadAllText($logPath)
        if ($logText -match '(?im)^.*\[(ERR|WRN)\].*(instrument|restore).*$') {
            throw "覆盖率插桩日志包含错误：$testName；详见 $logPath"
        }
        [xml]$testCoverage = Get-Content -LiteralPath $reportPath -Raw
        if ([int64]$testCoverage.coverage.'lines-covered' -le 0) {
            throw "覆盖率报告没有命中任何源码行：$testName"
        }
        $reports.Add($reportPath)
    }

    $combinedPath = Join-Path $coverageRoot 'combined.cobertura.xml'
    & $coverageTool merge @($reports) --output $combinedPath --output-format cobertura --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "覆盖率报告合并失败；退出码=$LASTEXITCODE"
    }
    [xml]$coverage = Get-Content -LiteralPath $combinedPath -Raw
    Write-Host ("覆盖率收集完成：行 {0:P2}（{1}/{2}）；分支 {3:P2}（{4}/{5}）" -f `
        [double]::Parse($coverage.coverage.'line-rate', [Globalization.CultureInfo]::InvariantCulture),
        [int64]$coverage.coverage.'lines-covered',
        [int64]$coverage.coverage.'lines-valid',
        [double]::Parse($coverage.coverage.'branch-rate', [Globalization.CultureInfo]::InvariantCulture),
        [int64]$coverage.coverage.'branches-covered',
        [int64]$coverage.coverage.'branches-valid') -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $sandbox) {
        Remove-Item -LiteralPath $sandbox -Recurse -Force
    }
}
