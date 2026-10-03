$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$gitleaksCommand = Get-Command "gitleaks" -ErrorAction SilentlyContinue
if (-not $gitleaksCommand) {
  throw "未找到 gitleaks。请从 gitleaks/gitleaks 官方 Release 安装固定版本后重试。"
}
$gitleaks = $gitleaksCommand.Source
$config = Join-Path $root ".gitleaks.toml"
$exceptionManifest = Join-Path $root "qa/vm-lab/release-preparation/gitleaks-workspace-exceptions-20260923.json"
$exceptionHelper = Join-Path $root "scripts/build-workspace-gitleaks-ignore.py"
$ignoreFile = Join-Path $root ("qa/vm-lab/release-preparation/workspace-gitleaksignore-" + [Guid]::NewGuid().ToString("N") + ".txt")

# 避免 Windows Git 的全局 Office textconv 把外部工具缺失误报成扫描失败；
# gitleaks 仍直接扫描 Git 对象和当前目录中的普通/二进制内容。
$previousGitConfigSystem = $env:GIT_CONFIG_SYSTEM
$env:GIT_CONFIG_SYSTEM = if ($env:OS -eq "Windows_NT") { "NUL" } else { "/dev/null" }
try {
  & $gitleaks git --redact --no-banner --config $config --exit-code 1 $root
  if ($LASTEXITCODE -ne 0) { throw "Git 历史凭据扫描失败。" }
  if (Test-Path -LiteralPath $exceptionManifest) {
    $python = @(
      (Join-Path $root "backend/.venv/Scripts/python.exe"),
      (Join-Path $root "backend/.venv/bin/python")
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $python) {
      $pythonCommand = Get-Command "python3" -ErrorAction SilentlyContinue
      if (-not $pythonCommand) { $pythonCommand = Get-Command "python" -ErrorAction SilentlyContinue }
      if (-not $pythonCommand) { throw "工作区凭据例外需要 Python 3 标准库。" }
      $python = $pythonCommand.Source
    }
    # 仅目录扫描使用本轮逐文件 SHA-256 绑定的例外；Git 历史不读取它。
    & $python $exceptionHelper --root $root --manifest $exceptionManifest --output $ignoreFile
    if ($LASTEXITCODE -ne 0) { throw "工作区凭据例外内容校验失败。" }
    & $gitleaks dir --redact --no-banner --config $config --exit-code 1 `
      --gitleaks-ignore-path $ignoreFile $root
    if ($LASTEXITCODE -ne 0) { throw "工作区凭据扫描失败。" }
    # 防止扫描期间被忽略的文件发生变化后仍以旧指纹放行。
    & $python $exceptionHelper --root $root --manifest $exceptionManifest
    if ($LASTEXITCODE -ne 0) { throw "工作区凭据例外扫描后校验失败。" }
  }
  else {
    # 普通干净检出没有这轮 QA 清单时，直接按原规则扫描且不加载任何例外。
    & $gitleaks dir --redact --no-banner --config $config --exit-code 1 $root
    if ($LASTEXITCODE -ne 0) { throw "工作区凭据扫描失败。" }
  }
}
finally {
  if (Test-Path -LiteralPath $ignoreFile) {
    Remove-Item -LiteralPath $ignoreFile -Force
  }
  if ($null -eq $previousGitConfigSystem) {
    Remove-Item Env:GIT_CONFIG_SYSTEM -ErrorAction SilentlyContinue
  }
  else {
    $env:GIT_CONFIG_SYSTEM = $previousGitConfigSystem
  }
}

Write-Host "Git 历史与工作区凭据扫描通过。"
