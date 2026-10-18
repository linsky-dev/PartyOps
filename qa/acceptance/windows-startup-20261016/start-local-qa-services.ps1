# 由用户手工运行：工具自动审批拒绝启动这些服务，助手不执行此脚本。
# 仅启动隔离 PostgreSQL 测试库和当前 Win10 QA 虚拟机的本机查看页。
# 不连接现有 PostgreSQL 业务库，不注册 Windows 服务，不安装 PartyOps。
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$qaRoot = Join-Path $root '.qa/website-postgres-20261016'
$data = Join-Path $qaRoot 'data'
$pgBin = 'C:\Program Files\PostgreSQL\17\bin'
$python = Join-Path $root 'backend/.venv/Scripts/python.exe'
$novnc = Join-Path $root '.qa/novnc'
foreach ($file in @((Join-Path $pgBin 'initdb.exe'), (Join-Path $pgBin 'pg_ctl.exe'), $python, (Join-Path $novnc 'vnc.html'))) {
  if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "缺少既有测试工具：$file" }
}
foreach ($port in @(55432,4337)) {
  if (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue) { throw "端口 $port 已占用，先核对状态，不重复启动。" }
}
if (-not (Get-NetTCPConnection -LocalPort 28000 -State Listen -ErrorAction SilentlyContinue)) { throw '原 Win10 QA 虚拟机查看端口未就绪。' }
if (Test-Path -LiteralPath $qaRoot) { throw 'QA 目录已存在，先核对上次执行结果，不重新初始化。' }
New-Item -ItemType Directory -Path $qaRoot | Out-Null
$passwordFile = Join-Path $qaRoot 'qa-password.local'
# 沿用仓库集成测试的合成账号口令，仅用于本机新建的可丢弃测试库。
[IO.File]::WriteAllText($passwordFile, 'partyops-qa-only', [Text.UTF8Encoding]::new($false))
try {
  & (Join-Path $pgBin 'initdb.exe') -D $data -U postgres -E UTF8 --locale=C --auth=scram-sha-256 --pwfile=$passwordFile *> (Join-Path $PSScriptRoot 'website-postgres-init.log')
  if ($LASTEXITCODE -ne 0) { throw '隔离测试库初始化失败，请保留目录和日志。' }
} finally {
  Remove-Item -LiteralPath $passwordFile -Force
}
# 真实限频测试并发建立 20 个连接，预留其他用例连接，避免测试库容量造成伪失败。
& (Join-Path $pgBin 'pg_ctl.exe') -D $data -l (Join-Path $qaRoot 'server.log') -o '-h 127.0.0.1 -p 55432 -c max_connections=64 -c shared_buffers=16MB' -w start
if ($LASTEXITCODE -ne 0) { throw '隔离测试库启动失败，请保留日志。' }
Write-Host '隔离测试库已启动；继续启动虚拟机查看服务。请保持本窗口打开。'
Write-Host '查看页：http://127.0.0.1:4337/vnc.html'
& $python -m websockify --web $novnc 127.0.0.1:4337 127.0.0.1:28000
