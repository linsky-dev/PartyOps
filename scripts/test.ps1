param(
  [string]$DocumentFormatterSource = $env:PARTYOPS_DOCUMENT_FORMATTER_SOURCE
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$python = Join-Path $root "backend\.venv\Scripts\python.exe"
$corepackCommand = Get-Command "corepack" -ErrorAction SilentlyContinue
if (-not (Test-Path -LiteralPath $python)) {
  throw "缺少后端 Python 环境：$python。请先按开发说明安装锁定依赖。"
}
if (-not $corepackCommand) {
  throw "未找到 Corepack。请安装 Node.js 22 并启用 package.json 指定的 pnpm 版本。"
}
$corepack = $corepackCommand.Source

if ([string]::IsNullOrWhiteSpace($DocumentFormatterSource)) {
  $DocumentFormatterSource = "E:\paiban\PartyOps.DocumentFormatter.Source"
}
$formatterBuild = Join-Path $DocumentFormatterSource "tools\Build-Windows.ps1"
if (-not (Test-Path -LiteralPath $formatterBuild)) {
  throw "缺少新排版工具唯一功能规格：$DocumentFormatterSource。打包门禁要求先完成其 x64/x86 源码构建与功能回归。"
}

function Invoke-Checked {
  param(
    [Parameter(Mandatory = $true)][scriptblock]$Command,
    [Parameter(Mandatory = $true)][string]$Name
  )
  & $Command
  if ($LASTEXITCODE -ne 0) {
    throw "$Name 失败，退出码：$LASTEXITCODE"
  }
}

function Invoke-CheckedWithRetry {
  param(
    [Parameter(Mandatory = $true)][scriptblock]$Command,
    [Parameter(Mandatory = $true)][string]$Name
  )
  for ($attempt = 1; $attempt -le 2; $attempt += 1) {
    & $Command
    if ($LASTEXITCODE -eq 0) {
      return
    }
    if ($attempt -eq 1) {
      Write-Warning "$Name 首次执行失败（退出码：$LASTEXITCODE），2 秒后进行唯一一次重试。"
      Start-Sleep -Seconds 2
    }
  }
  throw "$Name 失败，退出码：$LASTEXITCODE"
}

function Wait-CommitHeadroom {
  param(
    [Parameter(Mandatory = $true)][string]$Stage,
    [int64]$MinimumBytes = 11GB,
    [int]$TimeoutSeconds = 60
  )
  $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
  do {
    $system = Get-CimInstance Win32_OperatingSystem
    $freeBytes = [int64]$system.FreeVirtualMemory * 1KB
    if ($freeBytes -ge $MinimumBytes) {
      Write-Host ("{0} 提交内存余量：{1:N2} GB。" -f $Stage, ($freeBytes / 1GB))
      return
    }
    Start-Sleep -Seconds 2
  } while ((Get-Date) -lt $deadline)
  $message = (
    "[BUILD_RESOURCE_HEADROOM] {0} 前仅剩 {1:N2} GB 提交内存，门禁至少需要 {2:N2} GB；" +
    "请等待本地模型释放后重试。"
  ) -f $Stage, ($freeBytes / 1GB), ($MinimumBytes / 1GB)
  throw $message
}

function Initialize-TestTempRoot {
  param([string]$RequestedPath = $env:PARTYOPS_TEST_TEMP)

  if ([string]::IsNullOrWhiteSpace($RequestedPath)) {
    $RequestedPath = if (Test-Path -LiteralPath "D:\") {
      "D:\PartyOps-TestTemp"
    }
    else {
      Join-Path $root ".test-temp"
    }
  }
  New-Item -ItemType Directory -Path $RequestedPath -Force | Out-Null
  $resolved = (Resolve-Path -LiteralPath $RequestedPath).Path
  $freeBytes = (Get-PSDrive -Name ([System.IO.Path]::GetPathRoot($resolved).TrimEnd("\").TrimEnd(":"))).Free
  if ($freeBytes -lt 2GB) {
    throw ("[TEST_DISK_HEADROOM] 测试临时目录 {0} 所在磁盘仅剩 {1:N2} GB，至少需要 2 GB。" -f $resolved, ($freeBytes / 1GB))
  }
  Write-Host ("测试临时目录：{0}（可用 {1:N2} GB）。" -f $resolved, ($freeBytes / 1GB))
  return $resolved
}

# 先重新构建并运行用户提供的新工具原始 x64/x86 功能契约，避免仅验证迁移后的
# 自有测试而漏掉规格源中的能力变化。此步骤不会发布或启动外部产品窗口。
# 禁止 MSBuild 节点在两次构建后常驻。发布机可能同时运行本地模型，遗留
# 编译进程会挤满 Windows 提交内存并让后续审计无法启动。
$env:MSBUILDDISABLENODEREUSE = "1"
$formatterCompilerPidsBefore = @(
  Get-Process -Name "VBCSCompiler" -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty Id
)
try {
  # 保留单个 Roslyn 共享编译服务器。关闭共享编译会在 /m 构建中并发启动
  # 多个 csc.exe，反而更容易在本地模型驻留时耗尽提交内存。
  Wait-CommitHeadroom -Stage "新排版工具 Release|x64 构建"
  Invoke-Checked { & $formatterBuild -Configuration Release -Platform x64 } "新排版工具 Release|x64 源码构建与功能回归"
  Wait-CommitHeadroom -Stage "新排版工具 Release|x86 构建"
  Invoke-Checked { & $formatterBuild -Configuration Release -Platform x86 } "新排版工具 Release|x86 源码构建与功能回归"
}
finally {
  # 只回收本轮新建的 Roslyn 服务，既释放发布门禁占用，也不影响调用前
  # 已存在的开发会话编译服务器。
  Get-Process -Name "VBCSCompiler" -ErrorAction SilentlyContinue |
    Where-Object { $_.Id -notin $formatterCompilerPidsBefore } |
    Stop-Process -Force -ErrorAction SilentlyContinue
}

Invoke-Checked { & $corepack pnpm --dir (Join-Path $root "frontend") audit --prod --audit-level high } "前端生产依赖审计"
Invoke-Checked { & $corepack pnpm --dir (Join-Path $root "website") audit --prod --audit-level high } "官网生产依赖审计"
Invoke-Checked { & $python -m pip check } "Python 依赖一致性检查"
Invoke-CheckedWithRetry {
  & $python -m pip_audit -r (Join-Path $root "backend\requirements-release.txt")
} "Python 依赖审计"
Invoke-Checked { & (Join-Path $root "scripts\scan-secrets.ps1") } "Git 历史与工作区凭据扫描"
Invoke-Checked {
  & $python -m bandit -r (Join-Path $root "backend\app") (Join-Path $root "packaging\windows") -x (Join-Path $root "backend\.test-data") -ll
} "Python 中高危静态安全扫描"
Invoke-Checked { & $python -m compileall -q (Join-Path $root "backend\app") (Join-Path $root "backend\tests") } "Python 编译检查"
Invoke-Checked { & $python -m ruff check (Join-Path $root "backend\app") (Join-Path $root "backend\tests") } "Python Ruff 检查"
Invoke-Checked { & $corepack pnpm --dir (Join-Path $root "frontend") run typecheck } "前端类型检查"
Invoke-Checked { & $corepack pnpm --dir (Join-Path $root "frontend") run test:coverage } "前端覆盖率测试"
Invoke-Checked { & $corepack pnpm --dir (Join-Path $root "frontend") run test:sites } "静态入口测试"
Invoke-Checked { & $corepack pnpm --dir (Join-Path $root "frontend") run build } "前端生产构建"
# 官网源码按发布策略不进入主仓库，发布门禁显式限制单 Worker，避免构建机
# 同时驻留本地模型时 V8 并发预留耗尽 Windows 提交内存。
Invoke-Checked {
  & $corepack pnpm --dir (Join-Path $root "website") exec vitest run --coverage --minWorkers=1 --maxWorkers=1
} "官网覆盖率测试"
Invoke-Checked { & $corepack pnpm --dir (Join-Path $root "website") run test:sites } "官网静态入口测试"
Invoke-Checked { & $corepack pnpm --dir (Join-Path $root "website") run build } "官网生产构建"
$testTempRoot = Initialize-TestTempRoot
$previousTemp = $env:TEMP
$previousTmp = $env:TMP
$env:TEMP = $testTempRoot
$env:TMP = $testTempRoot
Push-Location (Join-Path $root "backend")
try {
    # Argon2 登录校验和一致快照用例都会真实申请资源；在提交内存不足时
    # 继续运行只会制造随机失败，不能作为可发布证据。
    Wait-CommitHeadroom -Stage "后端全量测试" -MinimumBytes 8GB
    Invoke-Checked {
      & $python -m coverage erase
      & $python -m coverage run --branch -m pytest tests
    } "后端全量测试"
    Invoke-Checked {
      & $python -m coverage html
      & $python -m coverage json --fail-under=0 -o coverage-release.json
    } "后端覆盖率报告"
    Invoke-Checked {
      & $python (Join-Path $root "scripts\verify-coverage.py") coverage-release.json --line 95 --branch 92
    } "后端覆盖率门禁"
}
finally {
    Pop-Location
    $env:TEMP = $previousTemp
    $env:TMP = $previousTmp
}

# 只有前述全量功能、覆盖率、安全与生产构建全部通过，才写入平台打包门禁。
Invoke-Checked {
  & $python (Join-Path $root "scripts\verify-full-function-gate.py") record --root $root
} "记录全功能测试门禁"
