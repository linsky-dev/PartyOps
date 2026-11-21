param(
  [Parameter(Mandatory = $true)]
  [ValidateSet('windows_amd64','windows7_amd64','windows7_x86')]
  [string]$Package,
  [string]$BuildKit = $env:PARTYOPS_BUILD_KIT,
  [string]$BuildDirectory = $env:PARTYOPS_COMPAT_BUILD_DIR,
  [string]$InnoCompiler
)

# 兼容构建工具与缓存使用检出目录或显式配置的路径。
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
if (!$BuildKit) { $BuildKit = Join-Path $repoRoot '.build-kit' }
if (!$BuildDirectory) { $BuildDirectory = Join-Path $repoRoot '.partyops-vm-lab\builds\windows-local' }
$temporaryRoot = Join-Path $BuildDirectory 'tmp'
New-Item -ItemType Directory -Path $temporaryRoot -Force | Out-Null
$env:TEMP = $temporaryRoot
$env:TMP = $temporaryRoot
$env:PIP_CACHE_DIR = Join-Path $BuildDirectory 'pip-cache'
$env:PYTHONUTF8 = '1'
if (!$InnoCompiler) { $InnoCompiler = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe' }
if (-not (Test-Path -LiteralPath $innoCompiler)) { throw '本机已登记的 Inno Setup 6 不存在。' }
Push-Location $repoRoot
try {
  if ($Package -eq 'windows_amd64') {
    $buildPython = Join-Path $buildKit 'build-host\Scripts\python.exe'
    & (Join-Path $repoRoot 'packaging\windows\build-windows.ps1') -Python $buildPython -InnoCompiler $innoCompiler
  } else {
    $architecture = if ($Package -eq 'windows7_amd64') { 'amd64' } else { 'x86' }
    $buildPython = Join-Path $buildKit "win7-build-env-rc9-$architecture-v2\Scripts\python.exe"
    $wheelhouse = Join-Path $buildKit "calamine-win7-20260908\wheelhouse\$architecture"
    & (Join-Path $repoRoot 'packaging\windows\build-windows7.ps1') -Architecture $architecture `
      -Python $buildPython -Wheelhouse $wheelhouse -EvidenceRoot (Join-Path $repoRoot 'backend\legacy\evidence') `
      -InnoCompiler $innoCompiler
  }
  if ($LASTEXITCODE -ne 0) { throw "本地 $Package 构建失败：$LASTEXITCODE" }
} finally {
  Pop-Location
}
