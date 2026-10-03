param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePath,
    [ValidateSet('x86', 'x64')]
    [string]$Platform = 'x86',
    [string]$OutputDirectory = '',
    [string]$EvidencePath = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\diagnostics\real-document-output'
}
if ([string]::IsNullOrWhiteSpace($EvidencePath)) {
    $EvidencePath = Join-Path $root 'artifacts\diagnostics\standalone-wps-e2e-20260829.json'
}

$requires64Bit = $Platform -eq 'x64'
if ([Environment]::Is64BitProcess -ne $requires64Bit) {
    $windowsPowerShell = if ($requires64Bit) {
        Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    }
    else {
        Join-Path $env:SystemRoot 'SysWOW64\WindowsPowerShell\v1.0\powershell.exe'
    }
    & $windowsPowerShell -NoProfile -STA -ExecutionPolicy Bypass -File $PSCommandPath `
        -SourcePath $SourcePath -Platform $Platform -OutputDirectory $OutputDirectory -EvidencePath $EvidencePath
    exit $LASTEXITCODE
}

$source = [System.IO.Path]::GetFullPath($SourcePath)
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
$evidence = [System.IO.Path]::GetFullPath($EvidencePath)
$allowedArtifacts = [System.IO.Path]::GetFullPath((Join-Path $root 'artifacts'))
if (!(Test-Path -LiteralPath $source -PathType Leaf)) {
    throw "真实文档不存在：$source"
}
if (!$output.StartsWith($allowedArtifacts + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "端到端输出必须位于项目 artifacts 内：$output"
}
if (!$evidence.StartsWith($allowedArtifacts + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "端到端证据必须位于项目 artifacts 内：$evidence"
}

$assemblyDirectory = Join-Path $root "src\PartyOps.DocumentFormatter.Desktop\bin\$Platform\Release"
$mainAssemblyPath = Join-Path $assemblyDirectory 'PartyOps.DocumentFormatter.AddIn.dll'
if (!(Test-Path -LiteralPath $mainAssemblyPath)) {
    throw "独立版业务程序集不存在，请先构建：$mainAssemblyPath"
}

New-Item -ItemType Directory -Force -Path $output | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $evidence) | Out-Null
$sourceHashBefore = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash

Push-Location $assemblyDirectory
try {
    Get-ChildItem -LiteralPath $assemblyDirectory -Filter '*.dll' -File |
        Where-Object { $_.FullName -ne $mainAssemblyPath } |
        ForEach-Object {
            try { [void][System.Reflection.Assembly]::LoadFrom($_.FullName) }
            catch { }
        }
    [void][System.Reflection.Assembly]::LoadFrom($mainAssemblyPath)

    [DocumentRepository.Services.Hosting.HostThreadRuntime]::Initialize('StandaloneRealDocumentE2E')
    $messageFilter = [DocumentRepository.Services.Hosting.Standalone.ComBusyRetryMessageFilter]::Register()
    try {
        $request = New-Object DocumentRepository.Models.Standalone.StandaloneBatchRequest
        $request.SourcePaths = [string[]]@($source)
        $request.OutputDirectory = $output
        $request.FeatureId = 'format'
        $request.FeatureDisplayName = '一键排版'
        $request.OutputSuffix = '_已排版'
        $request.HostPreference = [DocumentRepository.Models.Standalone.OfficeHostPreference]::Wps
        $request.ExportDocx = $true
        $request.ExportPdf = $false
        $request.ExportTxt = $false
        $request.CancellationToken = [System.Threading.CancellationToken]::None

        $processor = New-Object DocumentRepository.Services.Hosting.Standalone.StandaloneBatchProcessor
        $result = $processor.Execute($request, $null)
    }
    finally {
        $messageFilter.Dispose()
    }
}
finally {
    Pop-Location
}

$sourceHashAfter = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
$jobs = @($result.Jobs | ForEach-Object {
    [ordered]@{
        source_path = $_.SourcePath
        success = [bool]$_.Success
        cancelled = [bool]$_.Cancelled
        message = $_.Message
        host = $_.HostDisplayName
        outputs = @($_.OutputPaths)
        outputs_exist = @($_.OutputPaths | Where-Object { !(Test-Path -LiteralPath $_ -PathType Leaf) }).Count -eq 0
    }
})
$passed = $result.SuccessCount -eq 1 -and $result.FailureCount -eq 0 -and
    $sourceHashBefore -eq $sourceHashAfter -and @($jobs | Where-Object { !$_.outputs_exist }).Count -eq 0
$desktopExe = Join-Path $assemblyDirectory 'PartyOps.DocumentFormatter.Desktop.exe'
$versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($desktopExe)
$record = [ordered]@{
    generated_at = [DateTimeOffset]::Now.ToString('o')
    passed = $passed
    platform = $Platform
    product_version = $versionInfo.ProductVersion
    file_version = $versionInfo.FileVersion
    source_path = $source
    source_sha256_before = $sourceHashBefore
    source_sha256_after = $sourceHashAfter
    source_unchanged = $sourceHashBefore -eq $sourceHashAfter
    success_count = $result.SuccessCount
    failure_count = $result.FailureCount
    cancelled_count = $result.CancelledCount
    jobs = $jobs
}
[System.IO.File]::WriteAllText(
    $evidence,
    (($record | ConvertTo-Json -Depth 8) + [Environment]::NewLine),
    [System.Text.UTF8Encoding]::new($false)
)

if (!$passed) {
    throw "真实文档端到端排版失败，证据：$evidence"
}
Write-Host "真实文档端到端排版通过：$evidence" -ForegroundColor Green
