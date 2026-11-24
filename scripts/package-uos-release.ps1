$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root "artifacts"
$version = if ($env:PARTYOPS_VERSION) { $env:PARTYOPS_VERSION } else { "1.4.6-rc.1" }
if ($version -ne "1.4.6-rc.1") { throw "[UOS_RELEASE_VERSION_INVALID] 仅允许 1.4.6-rc.1 当前候选。" }
$stagingRoot = Join-Path $root ".release-kit"
$releaseName = "PartyOps-$version-UOS-amd64-arm64-offline"
$staging = Join-Path $stagingRoot $releaseName
$archive = Join-Path $artifacts "$releaseName.zip"
# 只制作当前 UOS 双架构安装套件；跨平台更新包由统一发行工序负责。
$required = @(
  "PartyOps_${version}_linux_amd64.deb",
  "PartyOps_${version}_linux_arm64.deb",
  "PartyOps_${version}_linux_amd64.deb.sha256",
  "PartyOps_${version}_linux_arm64.deb.sha256",
  "PartyOps-linux-amd64.tar.zst",
  "PartyOps-linux-arm64.tar.zst",
  "SHA256SUMS.amd64",
  "SHA256SUMS.arm64"
)

foreach ($name in $required) {
  if (-not (Test-Path -LiteralPath (Join-Path $artifacts $name))) {
    throw "缺少双架构原生制品：artifacts\$name。请分别在 UOS V20 amd64 和 ARM64 目标机完成构建后汇总。"
  }
}

# 校验原生 DEB 和 portable TAR 完整字节，拒绝仅凭 SHA 文件存在封包。
foreach ($architecture in @("amd64", "arm64")) {
  foreach ($entry in @(
    @{ File = "PartyOps_${version}_linux_${architecture}.deb"; Manifest = "PartyOps_${version}_linux_${architecture}.deb.sha256" },
    @{ File = "PartyOps-linux-${architecture}.tar.zst"; Manifest = "SHA256SUMS.${architecture}" }
  )) {
    $manifestPath = Join-Path $artifacts $entry.Manifest
    $checksumLine = [IO.File]::ReadAllText($manifestPath).Trim()
    $pattern = '^([0-9a-fA-F]{64})\s+\*?' + [regex]::Escape($entry.File) + '$'
    if ($checksumLine -notmatch $pattern) {
      throw "[UOS_RELEASE_CHECKSUM_INVALID] $($entry.Manifest) 未精确绑定 $($entry.File)"
    }
    $expected = $Matches[1].ToLowerInvariant()
    $actual = (Get-FileHash -LiteralPath (Join-Path $artifacts $entry.File) -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($expected -ne $actual) {
      throw "[UOS_RELEASE_HASH_MISMATCH] $($entry.File) 与 $($entry.Manifest) 不一致"
    }
  }
}

if (Test-Path -LiteralPath $stagingRoot) {
  $resolved = (Resolve-Path -LiteralPath $stagingRoot).Path
  $project = (Resolve-Path -LiteralPath $root).Path
  if (-not $resolved.StartsWith($project, [StringComparison]::OrdinalIgnoreCase)) {
    throw "发布暂存目录不在工作区内，拒绝清理。"
  }
  Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path `
  (Join-Path $staging "artifacts"), `
  (Join-Path $staging "packaging\uos"), `
  (Join-Path $staging "docs") | Out-Null

Copy-Item -LiteralPath (Join-Path $root "install.sh") -Destination $staging
Copy-Item -LiteralPath (Join-Path $root "一键安装党建智办.sh") -Destination $staging
# 历史根目录“安装说明.txt”已不在当前源码中；从当前版本生成指南，
# 避免离线包汇总因遗留文件名直接失败。
$installationGuide = @(
  "党建智办 PartyOps $version UOS V20 离线安装说明",
  "一、备份原有账号、数据、附件和配置后，在解压目录运行 bash install.sh。",
  "二、原位升级无需卸载旧版本；安装器按机器架构自动选择 amd64/arm64 DEB。",
  "三、安装前自动校验 BUILD-KIT-SHA256SUMS 和安装包 SHA-256。",
  "四、失败时查看 artifacts/one-click-install.log；勿删除业务数据。",
  "五、当前为内部候选版，正式发布仍需 UOS 目标机安装验收。"
) -join [Environment]::NewLine
[IO.File]::WriteAllText(
  (Join-Path $staging "安装说明.txt"),
  $installationGuide + [Environment]::NewLine,
  (New-Object System.Text.UTF8Encoding($false))
)
Copy-Item -LiteralPath (Join-Path $root "packaging\uos\one-click-install.sh") `
  -Destination (Join-Path $staging "packaging\uos")
foreach ($document in @(
  "one-click-install.md",
  "release-notes-v1.4.6-rc.1.md",
  "upgrade-1.4.3.md",
  "党建智办-1.4.3-更新说明.txt",
  "upgrade-1.4.2.md",
  "党建智办-1.4.2-更新说明.txt",
  "upgrade-1.4.1.md",
  "党建智办-1.4.1-更新说明.txt",
  "upgrade-1.4.0.md",
  "党建智办-1.4.0-更新说明.txt",
  "upgrade-1.3.4.md",
  "党建智办-1.3.4-更新说明.txt",
  "upgrade-1.3.3.md",
  "党建智办-1.3.3-更新说明.txt",
  "upgrade-1.3.2.md",
  "党建智办-1.3.2-更新说明.txt",
  "upgrade-1.3.1.md",
  "党建智办-1.3.1-更新说明.txt",
  "upgrade-1.3.0.md",
  "党建智办-1.3.0-更新说明.txt",
  "architecture-1.3.0.md",
  "upgrade-1.2.0.md",
  "architecture-1.2.0.md",
  "upgrade-1.1.3.md",
  "党建智办-1.1.3-更新说明.txt",
  "upgrade-1.1.2.md",
  "upgrade-1.1.md",
  "installation-checklist.md",
  "operations-runbook.md",
  "backup-restore.md"
)) {
  Copy-Item -LiteralPath (Join-Path $root "docs\$document") `
    -Destination (Join-Path $staging "docs")
}
foreach ($name in $required) {
  Copy-Item -LiteralPath (Join-Path $artifacts $name) `
    -Destination (Join-Path $staging "artifacts")
}

$utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)
Get-ChildItem -LiteralPath $staging -File -Recurse |
  Where-Object { $_.Extension -eq ".sh" -or $_.Name -like "SHA256SUMS.*" } |
  ForEach-Object {
    $content = [System.IO.File]::ReadAllText($_.FullName)
    [System.IO.File]::WriteAllText(
      $_.FullName,
      $content.Replace("`r`n", "`n").Replace("`r", "`n"),
      $utf8WithoutBom
    )
  }

# Build the per-architecture manifest required by one-click-install.sh.
# The portable build's SHA256SUMS only contains the TAR. Append the exact,
# independently checked DEB hash in the STAGING COPY, not the original input.
foreach ($architecture in @("amd64", "arm64")) {
  $debFile = "PartyOps_${version}_linux_${architecture}.deb"
  $tarFile = "PartyOps-linux-${architecture}.tar.zst"
  $debHash = (Get-FileHash -LiteralPath (Join-Path $artifacts $debFile) -Algorithm SHA256).Hash.ToLowerInvariant()
  $tarHash = (Get-FileHash -LiteralPath (Join-Path $artifacts $tarFile) -Algorithm SHA256).Hash.ToLowerInvariant()
  $stagedSha = Join-Path $staging "artifacts/SHA256SUMS.${architecture}"
  [IO.File]::WriteAllText(
    $stagedSha, "$tarHash  $tarFile`n$debHash  $debFile`n", $utf8WithoutBom
  )
}

# The installed one-click script refuses an archive without this exact
# top-level manifest. Hash the FINAL staged bytes after newline normalization.
$buildKitManifest = Join-Path $staging "BUILD-KIT-SHA256SUMS"
$manifestLines = @(
  Get-ChildItem -LiteralPath $staging -File -Recurse -Force |
    Sort-Object FullName |
    ForEach-Object {
      $relative = $_.FullName.Substring($staging.Length).TrimStart('\', '/').Replace('\', '/')
      $entryHash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
      "$entryHash  $relative"
    }
)
[IO.File]::WriteAllText(
  $buildKitManifest, (($manifestLines -join "`n") + "`n"), $utf8WithoutBom
)

if (Test-Path -LiteralPath $archive) {
  Remove-Item -LiteralPath $archive -Force
}
Compress-Archive -LiteralPath $staging -DestinationPath $archive -CompressionLevel Optimal

# 双架构安装目录仅允许公开安装制品和说明，禁止混入发布签名私钥。
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($archive)
try {
  $forbiddenEntries = @(
    $zip.Entries | Where-Object {
      $_.FullName -match '(^|/)(release-keys|private-keys?)(/|$)' -or
      $_.FullName -match '(?i)(private[-_]?key|secret[-_]?key|update-private-key).*\.pem$'
    }
  )
  if ($forbiddenEntries.Count -gt 0) {
    throw "一键安装目录检测到禁止分发的私钥文件：$($forbiddenEntries[0].FullName)"
  }
}
finally {
  $zip.Dispose()
}

$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
[System.IO.File]::WriteAllText(
  "$archive.sha256",
  "$hash  $([IO.Path]::GetFileName($archive))`n",
  $utf8WithoutBom
)
Write-Host "双架构一键安装目录已打包：$archive"

$resolvedStaging = (Resolve-Path -LiteralPath $stagingRoot).Path
$resolvedRoot = (Resolve-Path -LiteralPath $root).Path
if (-not $resolvedStaging.StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase)) {
  throw "发布暂存目录不在工作区内，拒绝清理。"
}
Remove-Item -LiteralPath $stagingRoot -Recurse -Force
