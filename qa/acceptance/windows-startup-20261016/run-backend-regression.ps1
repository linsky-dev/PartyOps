# 仅运行后端全量回归；不写正式全功能门禁，不构建或安装产品。
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$python = Join-Path $root 'backend/.venv/Scripts/python.exe'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$log = Join-Path $PSScriptRoot ('backend-final-' + $stamp + '.log')
$xml = Join-Path $PSScriptRoot ('backend-final-' + $stamp + '.xml')
$os = Get-CimInstance Win32_OperatingSystem
if ($os.FreeVirtualMemory * 1KB -lt 8GB) { throw '后端回归需要至少 8 GiB 空闲提交额度。' }
$previousTemp = $env:TEMP
$previousTmp = $env:TMP
$previousSource = $env:PARTYOPS_DOCUMENT_FORMATTER_SOURCE
$env:TEMP = Join-Path $root '.test-temp'
$env:TMP = $env:TEMP
$env:PARTYOPS_DOCUMENT_FORMATTER_SOURCE = Join-Path $root '.qa/windows-formatter-upstream'
Push-Location (Join-Path $root 'backend')
try {
    & $python -m coverage erase
    if ($LASTEXITCODE -ne 0) { throw '清空本轮覆盖率计数失败。' }
    & $python -m coverage run --branch -m pytest tests --junitxml $xml *> $log
    $testsExit = $LASTEXITCODE
    & $python -m coverage json --fail-under=0 -o coverage-release.json *>> $log
    if ($LASTEXITCODE -ne 0) { throw '覆盖率报告生成失败。' }
    & $python (Join-Path $root 'scripts/verify-coverage.py') coverage-release.json --line 95 --branch 92 *>> $log
    $coverageExit = $LASTEXITCODE
    if ($testsExit -ne 0 -or $coverageExit -ne 0) { throw "后端回归未通过：tests=$testsExit coverage=$coverageExit" }
    Write-Output '后端源码回归及覆盖率通过；不代表正式全功能门禁或 Win10 安装版验收通过。'
} finally {
    Pop-Location
    $env:TEMP = $previousTemp
    $env:TMP = $previousTmp
    $env:PARTYOPS_DOCUMENT_FORMATTER_SOURCE = $previousSource
    Write-Output ('日志：' + $log)
    Write-Output ('JUnit：' + $xml)
}
