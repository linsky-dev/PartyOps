function Add-VerifiedPartyOpsDotNet48Prerequisite {
  param(
    [Parameter(Mandatory = $true)][string]$RepoRoot,
    [Parameter(Mandatory = $true)][string]$Destination
  )

  $runtimeRoot = Join-Path $RepoRoot "vendor\windows\dotnet-framework-4.8"
  $installer = Join-Path $runtimeRoot "ndp48-x86-x64-allos-enu.exe"
  $sourcePath = Join-Path $runtimeRoot "SOURCE.json"
  if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
    throw "[DOTNET48_SOURCE_MISSING] 缺少微软官方 .NET Framework 4.8 来源清单。"
  }

  try {
    $source = Get-Content -Raw -LiteralPath $sourcePath | ConvertFrom-Json
  } catch {
    throw "[DOTNET48_SOURCE_INVALID] .NET Framework 4.8 来源清单无法解析。"
  }
  $expectedHash = "0A3A390C47E639D0F7FC65B21195FEE6B7F65B066F80F70C60FAB191D14B7E40"
  $expectedLength = 121346568
  if ($source.version -ne "4.8" -or
      $source.filename -ne "ndp48-x86-x64-allos-enu.exe" -or
      $source.sha256 -ne $expectedHash -or
      [long]$source.size -ne $expectedLength -or
      $source.timezone -ne "Asia/Shanghai" -or
      $source.authenticode.status -ne "Valid") {
    throw "[DOTNET48_SOURCE_INVALID] .NET Framework 4.8 来源、版本、大小或哈希契约不一致。"
  }

  if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) {
    New-Item -ItemType Directory -Path $runtimeRoot -Force | Out-Null
    $temporary = "$installer.download"
    Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
    $previousProtocol = [Net.ServicePointManager]::SecurityProtocol
    try {
      [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
      for ($attempt = 1; $attempt -le 2; $attempt += 1) {
        try {
          Invoke-WebRequest `
            -Uri ([string]$source.download_url) `
            -OutFile $temporary `
            -UseBasicParsing `
            -MaximumRedirection 10
          break
        } catch {
          $statusCode = 0
          if ($_.Exception.Response -and $_.Exception.Response.StatusCode) {
            $statusCode = [int]$_.Exception.Response.StatusCode
          }
          if ($attempt -ge 2) {
            throw "[DOTNET48_DOWNLOAD_FAILED] 微软 .NET Framework 4.8 离线运行时下载失败：$($_.Exception.Message)"
          }
          if ($statusCode -eq 429) {
            Start-Sleep -Seconds 20
          } elseif ($statusCode -ge 500 -or $statusCode -eq 0) {
            Start-Sleep -Seconds 2
          } else {
            throw "[DOTNET48_DOWNLOAD_FAILED] 微软下载端返回 HTTP $statusCode。"
          }
        }
      }
    } finally {
      [Net.ServicePointManager]::SecurityProtocol = $previousProtocol
    }
    if (-not (Test-Path -LiteralPath $temporary -PathType Leaf)) {
      throw "[DOTNET48_DOWNLOAD_MISSING] 微软下载完成后未生成离线运行时文件。"
    }
    Move-Item -LiteralPath $temporary -Destination $installer -Force
  }

  $item = Get-Item -LiteralPath $installer
  $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $installer).Hash
  if ($item.Length -ne $expectedLength -or $actualHash -ne $expectedHash) {
    Remove-Item -LiteralPath $installer -Force -ErrorAction SilentlyContinue
    throw "[DOTNET48_HASH_MISMATCH] .NET Framework 4.8 离线运行时与锁定的微软文件不一致。"
  }
  $signature = Get-AuthenticodeSignature -LiteralPath $installer
  if ($signature.Status -ne "Valid" -or
      $signature.SignerCertificate.Subject -ne $source.authenticode.subject -or
      $signature.SignerCertificate.Thumbprint -ne $source.authenticode.certificate_thumbprint) {
    Remove-Item -LiteralPath $installer -Force -ErrorAction SilentlyContinue
    throw "[DOTNET48_SIGNATURE_INVALID] .NET Framework 4.8 离线运行时的微软签名无效或证书不一致。"
  }

  $destinationRoot = [IO.Path]::GetFullPath($Destination)
  $repoFullPath = [IO.Path]::GetFullPath($RepoRoot).TrimEnd("\", "/") + [IO.Path]::DirectorySeparatorChar
  if (-not $destinationRoot.StartsWith($repoFullPath, [StringComparison]::OrdinalIgnoreCase)) {
    throw "[DOTNET48_DESTINATION_INVALID] .NET 前置包只能写入当前 PartyOps 构建目录。"
  }
  New-Item -ItemType Directory -Path $destinationRoot -Force | Out-Null
  Copy-Item -LiteralPath $installer -Destination (Join-Path $destinationRoot $source.filename) -Force
  Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $destinationRoot "SOURCE.json") -Force
  $copiedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $destinationRoot $source.filename)).Hash
  if ($copiedHash -ne $expectedHash) {
    throw "[DOTNET48_COPY_MISMATCH] .NET Framework 4.8 前置包写入后哈希不一致。"
  }
}
