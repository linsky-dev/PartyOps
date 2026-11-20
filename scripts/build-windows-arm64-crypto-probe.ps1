param(
    [Parameter(Mandatory = $true)]
    [string] $OutputDirectory
)
# 只为原生 Windows ARM64 国密能力探测构建隔离依赖，不制作发行包。
# 源码采用仓库 uv.lock 中的 cryptography 50.0.0 sdist 以及
# macOS Intel 同源 SHA-256 验证过的 OpenSSL 3.5.7。
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$PSNativeCommandUseErrorActionPreference = $true

if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString() -ne 'Arm64') {
    throw '[ARM_CRYPTO_WRONG_HOST] 必须使用原生 Windows ARM64 Runner。'
}
$pythonArch = (& python -c 'import platform; print(platform.machine().lower())').Trim()
if ($LASTEXITCODE -ne 0 -or $pythonArch -notin @('arm64', 'aarch64')) {
    throw '[ARM_CRYPTO_WRONG_PYTHON] 必须使用原生 ARM64 Python。'
}
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$stage = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $stage -Force | Out-Null
$opensslName = 'openssl-3.5.7'
$opensslHash = 'a8c0d28a529ca480f9f36cf5792e2cd21984552a3c8e4aa11a24aa31aeac98e8'
$cryptoHash = 'eeac2acb5a20ed25e0ad6d1df9891a520b78b404266b6d11778f25d5d691a6c9'
$opensslTar = Join-Path $stage "$opensslName.tar.gz"
$cryptoTar = Join-Path $stage 'cryptography-50.0.0.tar.gz'
$opensslUrl = "https://github.com/openssl/openssl/releases/download/$opensslName/$opensslName.tar.gz"
$cryptoUrl = 'https://files.pythonhosted.org/packages/de/41/6cbdcf9142d00fe82836fbb51e503e58088575cf7a0fe1dbff6695bf0840/cryptography-50.0.0.tar.gz'
foreach ($source in @(
    @{ Url = $opensslUrl; Path = $opensslTar; Hash = $opensslHash },
    @{ Url = $cryptoUrl; Path = $cryptoTar; Hash = $cryptoHash }
)) {
    Invoke-WebRequest -Uri $source.Url -OutFile $source.Path -MaximumRedirection 5
    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $source.Path).Hash.ToLowerInvariant()
    if ($actual -ne $source.Hash) {
        throw "[ARM_CRYPTO_SOURCE_HASH] 不可信源码输入：$($source.Path)"
    }
}
# 复用仓库已有的 TAR 路径/类型/权限/展开体积校验，校验后才解包。
$validator = @'
import gzip, subprocess, sys
with gzip.open(sys.argv[1], "rb") as stream:
    result = subprocess.run(
        [sys.executable, sys.argv[2], "--expected-root", "openssl-3.5.7",
         "--max-members", "100000", "--max-bytes", "1073741824", "--allow-implicit-root"],
        stdin=stream, check=False
    )
sys.exit(result.returncode)
'@
$validationScript = Join-Path $root 'scripts/validate-portable-tar.py'
& python -c $validator $opensslTar $validationScript
if ($LASTEXITCODE -ne 0) { throw '[ARM_CRYPTO_SOURCE_TAR] OpenSSL 归档校验失败。' }
$extract = Join-Path $stage 'source'
New-Item -ItemType Directory -Path $extract -Force | Out-Null
& tar.exe -xzf $opensslTar -C $extract
if ($LASTEXITCODE -ne 0) { throw '[ARM_CRYPTO_EXTRACT] OpenSSL 源码解包失败。' }

$vswhere = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Microsoft Visual Studio/Installer/vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) {
    throw '[ARM_CRYPTO_MSVC_MISSING] 未找到 Visual Studio 安装探测器。'
}
$vsInstall = (& $vswhere -latest -products '*' -property installationPath | Select-Object -First 1)
if ($LASTEXITCODE -ne 0 -or -not $vsInstall) {
    throw '[ARM_CRYPTO_MSVC_MISSING] 未检测到原生 MSVC 工具链。'
}
$vcvars = Join-Path $vsInstall 'VC/Auxiliary/Build/vcvarsall.bat'
if (-not (Test-Path -LiteralPath $vcvars)) {
    throw '[ARM_CRYPTO_MSVC_MISSING] 未检测到 vcvarsall.bat。'
}
$prefix = (Join-Path $stage 'openssl-static').Replace('\', '/')
$sourceDir = Join-Path $extract $opensslName
$probe = Join-Path $root 'scripts/probe-sm-crypto-runtime.py'
$probeResult = Join-Path $root 'crypto-runtime.json'
$batch = Join-Path $stage 'build-crypto.cmd'
$log = Join-Path $root 'arm64-source-build.log'
# 所有编译和 Python 扩展构建必须在同一个 vcvarsall 上下文中。
$commands = @(
    '@echo off',
    'setlocal enableextensions',
    "call ""$vcvars"" arm64",
    'if errorlevel 1 exit /b 11',
    'where cl || exit /b 12',
    'where nmake || exit /b 13',
    'where perl || exit /b 14',
    'where cargo || exit /b 15',
    "cd /d ""$sourceDir""",
    "perl Configure VC-WIN64-ARM no-shared no-tests no-makedepend no-asm --prefix=""$prefix"" --openssldir=""$prefix/ssl""",
    'if errorlevel 1 exit /b 21',
    'nmake',
    'if errorlevel 1 exit /b 22',
    'nmake install_sw',
    'if errorlevel 1 exit /b 23',
    "set OPENSSL_DIR=$prefix",
    'set OPENSSL_STATIC=1',
    'set CARGO_BUILD_JOBS=2',
    'python -m pip install --disable-pip-version-check --only-binary=:all: cffi==2.1.0 pycparser==3.0',
    'if errorlevel 1 exit /b 31',
    "python -m pip install --disable-pip-version-check --no-cache-dir --no-deps ""$cryptoTar""",
    'if errorlevel 1 exit /b 32',
    "python ""$probe"" --output ""$probeResult""",
    'if errorlevel 1 exit /b 41',
    'python -c "from cryptography.hazmat.backends.openssl.backend import backend; v=backend.openssl_version_text(); print(v); assert v.startswith(''OpenSSL 3.5.7 '')"',
    'if errorlevel 1 exit /b 42'
)
[IO.File]::WriteAllLines($batch, $commands, [Text.Encoding]::ASCII)
# 原生编译的完整日志不写在 CI 控制台，保留到证据制品。
$commandLine = [char]34 + $batch + [char]34
$PSNativeCommandUseErrorActionPreference = $false
& cmd.exe /d /c $commandLine *> $log
$buildExitCode = $LASTEXITCODE
Get-Content -LiteralPath $log -Tail 80
if ($buildExitCode -ne 0) {
    throw "[ARM_CRYPTO_SOURCE_BUILD_FAILED] 源码构建或向量检查失败（cmd exit $buildExitCode）。"
}
Write-Output '[ARM_CRYPTO_SOURCE_PROBE_PASS] 原生 ARM64、OpenSSL 3.5.7 与 cryptography 50.0.0 已通过向量与篡改拒绝检查。'
