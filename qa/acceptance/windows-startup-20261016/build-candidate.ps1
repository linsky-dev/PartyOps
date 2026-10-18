param([switch]$BuildPayloadOnly)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$gitDirectory = Join-Path $env:USERPROFILE '.cache/codex-runtimes/codex-primary-runtime/dependencies/native/git/mingw64/bin'
$python = Join-Path $root 'backend/.venv/Scripts/python.exe'
$buildShell = (Get-Process -Id $PID).Path
$inno = Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 6/ISCC.exe'
foreach ($file in @((Join-Path $gitDirectory 'git.exe'), $python, $inno)) {
  if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "缺少构建工具：$file" }
}
# 只改变当前进程环境；沿用正式门禁，不重复下载或组装旧平台包。
$env:PATH = $gitDirectory + ';' + $env:PATH
$env:PARTYOPS_DOCUMENT_FORMATTER_SOURCE = Join-Path $root '.qa/windows-formatter-upstream'
$log = Join-Path $PSScriptRoot ('candidate-build-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.log')
Push-Location $root
try {
  if ($BuildPayloadOnly) {
    # 内层仅执行正式打包；外层 record-build.py 负责前后门禁及载荷封存。
    & (Join-Path $root 'packaging/windows/build-windows.ps1') -Python $python -InnoCompiler $inno *> $log
    if ($LASTEXITCODE -ne 0) { throw '候选构建失败，请查看本次日志。' }
  } else {
    # Python 中转后切换 PS5 会继承 PS7 的模块路径，使 Get-FileHash 等内置命令丢失。
    # 固定使用启动本入口的同一 PowerShell，不修改全局模块路径或正式构建脚本。
    & $python (Join-Path $PSScriptRoot 'record-candidate-build.py') -- $buildShell -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath -BuildPayloadOnly
    if ($LASTEXITCODE -ne 0) { throw '正式构建/载荷封存未通过，禁止覆盖安装。' }
  }
} finally {
  Pop-Location
  Write-Output ('构建日志：' + $log)
}
