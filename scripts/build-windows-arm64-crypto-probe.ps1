param(
    [Parameter(Mandatory = $true)]
    [string] $OutputDirectory
)
# 只为原生 Windows ARM64 国密能力探测构建隔离依赖，不制作发行包。
# 源码采用仓库 uv.lock 中的 cryptography 50.0.0 sdist 以及
# macOS Intel 同源 SHA-256 验证过的 OpenSSL 3.5.7。
$ErrorActionPreference = 'Stop'
# Windows ARM64 Runner 的默认 cp1252 无法打印已有 TAR 校验器的中文成功信息。
# 只为本次独立探测进程固定 UTF-8，不改变全平台生产运行时。
$env:PYTHONUTF8 = '1'
$env:PYTHONIOENCODING = 'utf-8'
Set-StrictMode -Version Latest
$PSNativeCommandUseErrorActionPreference = $false

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
from pathlib import Path
source = Path(sys.argv[1])
expanded = source.with_suffix("")
# subprocess(stdin=GzipFile) passes the underlying compressed fd on Windows,
# not decompressed TAR bytes. Stream to a real TAR file first.
with gzip.open(source, "rb") as compressed, expanded.open("xb") as plain:
    while data := compressed.read(1024 * 1024):
        plain.write(data)
with expanded.open("rb") as stream:
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
# 此目录只用于临时 CI 证据，不作为已签名的可发布依赖仓库。
$evidence = Join-Path $root 'artifacts/arm64-crypto-probe'
$wheelhouse = Join-Path $evidence 'wheelhouse'
New-Item -ItemType Directory -Path $wheelhouse -Force | Out-Null
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
    "python -m pip wheel --disable-pip-version-check --no-cache-dir --no-deps --wheel-dir ""$wheelhouse"" ""$cryptoTar""",
    'if errorlevel 1 exit /b 32',
    "python -m pip install --disable-pip-version-check --no-index --no-deps --find-links ""$wheelhouse"" cryptography==50.0.0",
    'if errorlevel 1 exit /b 33',
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
# 无网络离线安装通过后，才留存本机 Runner 构建的 wheel 和完整来源清单。
# 这不是已签名、已冻结或可发布的正式 PartyOps 运行时制品。
$wheels = @(Get-ChildItem -LiteralPath $wheelhouse -File -Filter 'cryptography-50.0.0-*.whl')
if ($wheels.Count -ne 1 -or $wheels[0].Name -notmatch '^cryptography-50\.0\.0-cp3\d{2}-abi3-win_arm64\.whl$') {
    throw '[ARM_CRYPTO_WHEEL_INVALID] 原生 ARM64 构建结果缺失、多于一个或平台标签异常。'
}
$proof = Get-Content -LiteralPath $probeResult -Raw -Encoding UTF8 | ConvertFrom-Json
if ($proof.status -ne 'PASS' -or
    $proof.runtime.cryptography -ne '50.0.0' -or
    -not $proof.runtime.cryptography_openssl.StartsWith('OpenSSL 3.5.7 ') -or
    $proof.vectors.sm3_abc -ne 'PASS' -or
    $proof.vectors.sm4_gcm_rfc8998_appendix_a_1 -ne 'PASS' -or
    $proof.vectors.sm4_gcm_modified_tag_rejected -ne 'PASS' -or
    $proof.vectors.sm4_gcm_modified_aad_rejected -ne 'PASS') {
    throw '[ARM_CRYPTO_WHEEL_UNVERIFIED] 未满足全部运行时标准向量。'
}
$manifest = [ordered]@{
    schema_version = 1
    status = 'BUILD_ONLY'
    usable_for_release = $false
    signed = $false
    git_sha = $env:GITHUB_SHA
    runner = $env:RUNNER_NAME
    architecture = 'windows-arm64'
    python_architecture = $pythonArch
    openssl_version = $proof.runtime.cryptography_openssl
    openssl_source_sha256 = $opensslHash
    cryptography_version = '50.0.0'
    cryptography_source_sha256 = $cryptoHash
    wheel_filename = $wheels[0].Name
    wheel_sha256 = (Get-FileHash -LiteralPath $wheels[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    vector_validation = 'PASS'
    offline_wheel_install = 'PASS'
    release_note = 'Unsigned CI-only native dependency build; not a release or installation acceptance.'
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'build-manifest.json') -Encoding UTF8
Write-Output '[ARM_CRYPTO_SOURCE_PROBE_PASS] 原生 ARM64、OpenSSL 3.5.7、cryptography 50.0.0、离线 wheel 安装及标准向量验证通过。'
