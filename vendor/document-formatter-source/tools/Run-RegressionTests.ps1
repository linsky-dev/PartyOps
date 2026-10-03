param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [ValidateSet('AnyCPU', 'x86', 'x64')]
    [string]$Platform = 'AnyCPU'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$outputSegment = if ($Platform -eq 'AnyCPU') { $Configuration } else { "$Platform\$Configuration" }
$tests = @(
    (Join-Path $root "tests\RecognitionRegressionTests\bin\$outputSegment\RecognitionRegressionTests.exe")
    (Join-Path $root "tests\SignatureLayoutHostSmoke\bin\$outputSegment\SignatureLayoutHostSmoke.exe")
    (Join-Path $root "tests\FeatureParityRegressionTests\bin\$outputSegment\FeatureParityRegressionTests.exe")
)

foreach ($test in $tests) {
    if (!(Test-Path -LiteralPath $test)) {
        throw "回归测试程序不存在：$test"
    }
    Write-Host "运行回归测试：$test"
    & $test
    if ($LASTEXITCODE -ne 0) {
        throw "回归测试失败：$test；退出码=$LASTEXITCODE"
    }
}
Write-Host "回归测试全部通过：$Configuration|$Platform" -ForegroundColor Green
