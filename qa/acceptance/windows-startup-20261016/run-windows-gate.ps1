param()
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
# 正式测试前只读核对既有隔离库，避免跑完构建和前端后才发现服务未启动。
# 不初始化、不启动数据库，也不连接宿主业务库。
$qaDatabase=Join-Path $root '.qa/website-postgres-20261016/data'
$qaPgControl='C:\Program Files\PostgreSQL\17\bin\pg_ctl.exe'
if(-not (Test-Path -LiteralPath $qaPgControl) -or -not (Test-Path -LiteralPath $qaDatabase)){
  throw 'QA_POSTGRES_EXISTING_ENVIRONMENT_REQUIRED'
}
$qaDatabaseStatus=& $qaPgControl status -D $qaDatabase 2>&1
if($LASTEXITCODE -ne 0 -or -not (Get-NetTCPConnection -LocalAddress 127.0.0.1 -LocalPort 55432 -State Listen -ErrorAction SilentlyContinue)){
  throw 'QA_POSTGRES_NOT_RUNNING: 请恢复已有的127.0.0.1:55432隔离测试库；不重新初始化。'
}
# 本机 Codex 的 Git 在用户目录 .cache，显式使用健康 C 盘工具，不调用 E 盘旧 Git。
$gitDirectory=Join-Path $env:USERPROFILE '.cache/codex-runtimes/codex-primary-runtime/dependencies/native/git/mingw64/bin'
if(-not (Test-Path -LiteralPath (Join-Path $gitDirectory 'git.exe'))){throw 'HEALTHY_GIT_REQUIRED'}
$env:PATH=$gitDirectory+';'+$env:PATH
$env:PARTYOPS_TEST_TEMP=Join-Path $root '.test-temp'
$env:PARTYOPS_DOCUMENT_FORMATTER_SOURCE=Join-Path $root '.qa/windows-formatter-upstream'
$stamp=Get-Date -Format 'yyyyMMdd-HHmmss'
$log=Join-Path $PSScriptRoot ('full-function-gate-'+$stamp+'.log')
Push-Location $root
try {
  & (Join-Path $root 'scripts/test.ps1') *> $log
  if($LASTEXITCODE -ne 0){throw 'FULL_FUNCTION_TEST_FAILED'}
  & (Join-Path $root 'backend/.venv/Scripts/python.exe') (Join-Path $root 'scripts/verify-full-function-gate.py') verify --root $root
  if($LASTEXITCODE -ne 0){throw 'FINAL_GATE_VERIFICATION_FAILED'}
  Write-Output ('PASS '+$log)
} finally {
  Pop-Location
  Write-Output ('日志：'+$log)
}
