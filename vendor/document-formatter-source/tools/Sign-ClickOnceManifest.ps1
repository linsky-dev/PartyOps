param(
    [Parameter(Mandatory = $true)]
    [string]$ManifestPath,
    [Parameter(Mandatory = $true)]
    [string]$CertificatePath,
    [string]$CertificatePassword = '',
    [string]$TimestampUrl = '',
    [string]$MsBuildPath = 'E:\codex\PartyOps\.build-kit\vs2022\MSBuild\Current\Bin\MSBuild.exe'
)

$ErrorActionPreference = 'Stop'
foreach ($requiredPath in @($ManifestPath, $CertificatePath, $MsBuildPath)) {
    if (!(Test-Path -LiteralPath $requiredPath)) {
        throw "签名输入不存在：$requiredPath"
    }
}

$signingProject = Join-Path $PSScriptRoot 'Sign-ClickOnceManifest.proj'
$signingTaskProject = Join-Path $PSScriptRoot 'ManifestSigningTask\ManifestSigningTask.csproj'
if (!(Test-Path -LiteralPath $signingProject)) {
    throw "签名项目不存在：$signingProject"
}
if (!(Test-Path -LiteralPath $signingTaskProject)) {
    throw "签名任务项目不存在：$signingTaskProject"
}

# 让 MSBuild 使用自身的程序集绑定配置完成签名；PFX 始终从 E 盘读取，
# 不导入 Windows 证书存储区。密码通过子进程环境传递，不写入命令行日志。
$previousPassword = $env:PARTYOPS_MANIFEST_CERT_PASSWORD
try {
    $env:PARTYOPS_MANIFEST_CERT_PASSWORD = $CertificatePassword
    & $MsBuildPath $signingTaskProject '/nologo' '/verbosity:minimal' '/t:Build' '/p:Configuration=Release'
    if ($LASTEXITCODE -ne 0) {
        throw "MSBuild 签名任务构建失败，退出码：$LASTEXITCODE"
    }
    $arguments = @(
        $signingProject,
        '/nologo',
        '/verbosity:minimal',
        '/t:Sign',
        "/p:ManifestPath=$ManifestPath",
        "/p:CertificatePath=$CertificatePath"
    )
    if (![string]::IsNullOrWhiteSpace($TimestampUrl)) {
        $arguments += "/p:TimestampUrl=$TimestampUrl"
    }
    & $MsBuildPath @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "MSBuild 清单签名失败，退出码：$LASTEXITCODE"
    }
}
finally {
    $env:PARTYOPS_MANIFEST_CERT_PASSWORD = $previousPassword
}
