param(
  [ValidateSet("x86", "x64", "AnyCPU")][string]$Platform = "x64",
  [string]$DocumentFormatterSource = $env:PARTYOPS_DOCUMENT_FORMATTER_SOURCE,
  [string]$StageDirectory = "",
  [string]$PythonPath = $env:PARTYOPS_PYTHON_BIN
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($DocumentFormatterSource)) {
  $DocumentFormatterSource = Join-Path $repoRoot "vendor\document-formatter-source"
}
$DocumentFormatterSource = (Resolve-Path -LiteralPath $DocumentFormatterSource).Path
$sourceVerifier = Join-Path $repoRoot "scripts\verify-document-formatter-source-snapshot.py"
$python = $PythonPath
if ([string]::IsNullOrWhiteSpace($python)) {
  $python = Join-Path $repoRoot "backend\.venv\Scripts\python.exe"
}
if (-not (Test-Path -LiteralPath $python -PathType Leaf)) {
  $pythonCommand = Get-Command python -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($pythonCommand) { $python = $pythonCommand.Source }
}
if (-not (Test-Path -LiteralPath $python -PathType Leaf)) {
  throw "[FORMATTER_PYTHON_MISSING] 缺少锁定的后端 Python：$python"
}
$sourceSnapshotJson = & $python $sourceVerifier --source $DocumentFormatterSource
if ($LASTEXITCODE -ne 0) {
  throw "[FORMATTER_SOURCE_DIVERGED] 内嵌排版源码与锁定上游不一致，拒绝构建。"
}
$sourceSnapshot = $sourceSnapshotJson | ConvertFrom-Json
Write-Output $sourceSnapshotJson
$sourceProject = Join-Path $DocumentFormatterSource "src\PartyOps.DocumentFormatter.AddIn\PartyOps.DocumentFormatter.AddIn.csproj"
if (-not (Test-Path -LiteralPath $sourceProject -PathType Leaf)) {
  throw "[FORMATTER_SOURCE_MISSING] 原排版工具业务项目不存在：$sourceProject"
}

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
  throw "[FORMATTER_MSBUILD_MISSING] 未找到 Visual Studio vswhere。"
}
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
$visualStudioRoot = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath | Select-Object -First 1
if (-not $msbuild -or -not $visualStudioRoot) {
  throw "[FORMATTER_MSBUILD_MISSING] 未找到 Visual Studio 2022 MSBuild。"
}
$publicAssemblies = Join-Path $visualStudioRoot "Common7\IDE\PublicAssemblies"
$frameworkFolder = if ($Platform -eq "x64") { "Framework64" } else { "Framework" }
$frameworkPath = Join-Path $env:WINDIR "Microsoft.NET\$frameworkFolder\v4.0.30319"
if (-not (Test-Path -LiteralPath (Join-Path $frameworkPath "mscorlib.dll") -PathType Leaf)) {
  throw "[FORMATTER_FRAMEWORK_MISSING] 当前系统缺少 .NET Framework 4.x $Platform 引用程序集。"
}

$project = Join-Path $repoRoot "packaging\windows\formatter-host\PartyOps.DocumentFormatter.Host.csproj"
& $msbuild $project /m:1 /restore /t:Rebuild /p:Configuration=Release "/p:Platform=$Platform" `
  "/p:DocumentFormatterSource=$DocumentFormatterSource" `
  "/p:VsPublicAssembliesPath=$publicAssemblies" `
  "/p:FrameworkPathOverride=$frameworkPath" `
  /p:DebugSymbols=false /p:DebugType=None /p:EmbedUntrackedSources=false `
  /nodeReuse:false /verbosity:minimal
if ($LASTEXITCODE -ne 0) {
  throw "[FORMATTER_HOST_BUILD_FAILED] 原排版源码宿主 $Platform 构建失败，退出码 $LASTEXITCODE。"
}

$output = Join-Path $repoRoot "packaging\windows\formatter-host\bin\$Platform\Release"
$required = @(
  "PartyOps.DocumentFormatter.Host.exe",
  "PartyOps.DocumentFormatter.AddIn.dll",
  "Microsoft.Bcl.HashCode.dll",
  "System.Buffers.dll",
  "System.Memory.dll",
  "System.Numerics.Vectors.dll",
  "System.Runtime.CompilerServices.Unsafe.dll",
  "UglyToad.PdfPig.dll",
  "UglyToad.PdfPig.Core.dll",
  "UglyToad.PdfPig.DocumentLayoutAnalysis.dll",
  "UglyToad.PdfPig.Fonts.dll",
  "UglyToad.PdfPig.Package.dll",
  "UglyToad.PdfPig.Tokenization.dll",
  "UglyToad.PdfPig.Tokens.dll"
)
$sourceOutput = Join-Path $DocumentFormatterSource "src\PartyOps.DocumentFormatter.AddIn\bin\Release"
foreach ($name in $required) {
  $path = Join-Path $output $name
  # PdfPig 的部分模块由反射路径加载，旧 csproj 不会把它们全部复制到
  # 宿主目录；仍从同一次锁定源码构建产物取件，禁止从全局缓存拼装。
  $sourceDependency = Join-Path $sourceOutput $name
  if (-not (Test-Path -LiteralPath $path -PathType Leaf) `
      -and (Test-Path -LiteralPath $sourceDependency -PathType Leaf)) {
    Copy-Item -LiteralPath $sourceDependency -Destination $path -Force
  }
  if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -le 0) {
    throw "[FORMATTER_HOST_INCOMPLETE] $Platform 宿主缺少运行文件：$name"
  }
}

$selfTestDirectory = Join-Path $output ".self-test"
New-Item -ItemType Directory -Path $selfTestDirectory -Force | Out-Null
$selfTestResult = Join-Path $selfTestDirectory "result.json"
try {
  $selfTestProcess = Start-Process `
    -FilePath (Join-Path $output "PartyOps.DocumentFormatter.Host.exe") `
    -ArgumentList @("--self-test", $selfTestResult) `
    -WindowStyle Hidden `
    -Wait `
    -PassThru
  if ($selfTestProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $selfTestResult -PathType Leaf)) {
    throw "[FORMATTER_HOST_SELFTEST_FAILED] $Platform 宿主无法加载原排版规则程序集。"
  }
  $selfTest = Get-Content -Raw -LiteralPath $selfTestResult | ConvertFrom-Json
  if ($selfTest.passed -ne $true `
      -or $selfTest.engine -ne "source-standalone-batch-processor" `
      -or @($selfTest.features).Count -ne 6) {
    throw "[FORMATTER_HOST_SELFTEST_INVALID] $Platform 宿主能力自检结果无效。"
  }
} finally {
  if (Test-Path -LiteralPath $selfTestDirectory) {
    Remove-Item -LiteralPath $selfTestDirectory -Recurse -Force
  }
}

if (-not [string]::IsNullOrWhiteSpace($StageDirectory)) {
  $stage = [IO.Path]::GetFullPath($StageDirectory)
  $allowedStageRoot = $repoRoot.TrimEnd("\", "/") + [IO.Path]::DirectorySeparatorChar
  if (-not $stage.StartsWith($allowedStageRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "[FORMATTER_STAGE_OUTSIDE_WORKSPACE] 宿主暂存目录必须位于 PartyOps 工作区内：$stage"
  }
  New-Item -ItemType Directory -Path $stage -Force | Out-Null
  Get-ChildItem -LiteralPath $stage -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
  foreach ($name in $required) {
    Copy-Item -LiteralPath (Join-Path $output $name) -Destination (Join-Path $stage $name) -Force
  }
  $images = Join-Path $output "images"
  if (Test-Path -LiteralPath $images -PathType Container) {
    Copy-Item -LiteralPath $images -Destination (Join-Path $stage "images") -Recurse -Force
  }
  $record = [ordered]@{
    schema = 2
    architecture = $Platform
    platform = "windows"
    adapter = "dotnet-framework-com-source"
    source_project = "PartyOps.DocumentFormatter.AddIn"
    source_snapshot_sha256 = [string]$sourceSnapshot.aggregate_sha256
    source_snapshot_files = [int]$sourceSnapshot.file_count
    features = @("format", "replace", "redheader", "rename", "convert", "pdf-to-word")
    capabilities = 25
    built_at = [DateTimeOffset]::Now.ToOffset([TimeSpan]::FromHours(8)).ToString("o")
    timezone = "Asia/Shanghai"
    host_sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $stage "PartyOps.DocumentFormatter.Host.exe")).Hash.ToLowerInvariant()
    rules_sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $stage "PartyOps.DocumentFormatter.AddIn.dll")).Hash.ToLowerInvariant()
  }
  [IO.File]::WriteAllText(
    (Join-Path $stage "source-host.json"),
    (($record | ConvertTo-Json -Depth 4) + "`n"),
    [Text.UTF8Encoding]::new($false)
  )

  # 发布载荷不携带 PDB，也不得在 PE/程序集内留下开发机源码路径。运行时本来
  # 不需要这些路径；清除并校验它们可直接证明安装后的宿主不会回读构建目录。
  $forbiddenBuildPaths = @($repoRoot, $DocumentFormatterSource) |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Select-Object -Unique
  foreach ($binary in Get-ChildItem -LiteralPath $stage -File) {
    if ($binary.Extension -notin @(".exe", ".dll")) { continue }
    $bytes = [IO.File]::ReadAllBytes($binary.FullName)
    $ascii = [Text.Encoding]::ASCII.GetString($bytes)
    $unicode = [Text.Encoding]::Unicode.GetString($bytes)
    foreach ($forbiddenPath in $forbiddenBuildPaths) {
      if ($ascii.IndexOf($forbiddenPath, [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
          $unicode.IndexOf($forbiddenPath, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw "[FORMATTER_HOST_BUILD_PATH_LEAK] 随包宿主泄漏开发机构建路径：$($binary.Name)"
      }
    }
  }
  if (Get-ChildItem -LiteralPath $stage -Recurse -File -Filter "*.pdb" | Select-Object -First 1) {
    throw "[FORMATTER_HOST_PDB_LEAK] 随包宿主不得携带调试符号。"
  }

  # 从已经复制完成、且不包含源码树的暂存目录再次实际加载规则程序集。
  # 这一步专门拦截“只在开发机构建输出目录可运行、安装后缺依赖”的问题。
  $relocatedSelfTest = Join-Path $stage ".relocated-self-test.json"
  try {
    $relocatedProcess = Start-Process `
      -FilePath (Join-Path $stage "PartyOps.DocumentFormatter.Host.exe") `
      -ArgumentList @("--self-test", $relocatedSelfTest) `
      -WorkingDirectory $stage `
      -WindowStyle Hidden `
      -Wait `
      -PassThru
    if ($relocatedProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $relocatedSelfTest -PathType Leaf)) {
      throw "[FORMATTER_HOST_RELOCATION_FAILED] $Platform 随包宿主脱离源码目录后无法启动。"
    }
    $relocatedPayload = Get-Content -Raw -LiteralPath $relocatedSelfTest | ConvertFrom-Json
    if ($relocatedPayload.passed -ne $true `
        -or $relocatedPayload.engine -ne "source-standalone-batch-processor" `
        -or @($relocatedPayload.features).Count -ne 6) {
      throw "[FORMATTER_HOST_RELOCATION_INVALID] $Platform 随包宿主脱离源码目录后的能力自检无效。"
    }
  } finally {
    if (Test-Path -LiteralPath $relocatedSelfTest) {
      Remove-Item -LiteralPath $relocatedSelfTest -Force
    }
  }
}

Write-Host "[FORMATTER_HOST_BUILD_OK] $Platform $output" -ForegroundColor Green
