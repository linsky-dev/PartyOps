param([Parameter(ValueFromRemainingArguments=$true)][string[]]$LabArguments)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$labPython = Join-Path $repoRoot 'backend\.venv\Scripts\python.exe'
if (-not (Test-Path -LiteralPath $labPython)) {
  throw '缺少仓库锁定 Python 环境，请先安装后端开发依赖。'
}
& $labPython (Join-Path $PSScriptRoot 'lab.py') @LabArguments
exit $LASTEXITCODE
