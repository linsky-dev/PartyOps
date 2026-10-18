param(
  [string]$RepoRoot,
  [string]$InstallerPath,
  [string]$ExtractorDirectory
)

# 此路径恢复公开成品中的原始文件，不重建也不冒充丢失的自制 ZIP。
function Assert-PartyOpsOcrRecoveryTree {
  param([Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$RepoRoot)
  $ErrorActionPreference = 'Stop'
  $manifestPath = Join-Path $RepoRoot "packaging\windows\ocr-public-recovery-manifest.json"
  if ((Get-FileHash -Algorithm SHA256 -LiteralPath $manifestPath).Hash -ne
      "FA4EF50093A52C72932550A48C01CDEA7FE44056D44BCC6460FA40CDFFDBDFF3") {
    throw "OCR 恢复清单 SHA-256 不匹配。"
  }
  $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
  if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
    throw "缺少经公开成品恢复的 OCR 目录：$Root"
  }
  # 检查目录本身及父目录，避免已存在的链接把读取/写入转向其他磁盘。
  $repoPath = [System.IO.Path]::GetFullPath($RepoRoot).TrimEnd('\')
  $rootPath = [System.IO.Path]::GetFullPath($Root).TrimEnd('\')
  if (-not $rootPath.StartsWith($repoPath + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "OCR 恢复目录必须位于当前工作区。"
  }
  $ancestor = $rootPath
  while ($ancestor.Length -ge $repoPath.Length) {
    if ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
      throw "OCR 恢复路径含重解析点：$ancestor"
    }
    if ($ancestor -eq $repoPath) { break }
    $ancestor = [System.IO.Path]::GetDirectoryName($ancestor)
  }
  $entries = @(Get-ChildItem -LiteralPath $rootPath -Recurse -Force)
  if ($entries | Where-Object { $_.Attributes -band [System.IO.FileAttributes]::ReparsePoint }) {
    throw "OCR 恢复载荷含重解析点。"
  }
  $files = @($entries | Where-Object { -not $_.PSIsContainer })
  $expected = @($manifest.files.PSObject.Properties)
  if ($files.Count -ne $expected.Count) { throw "OCR 恢复载荷文件集合不匹配。" }
  foreach ($file in $files) {
    $relative = $file.FullName.Substring($rootPath.Length + 1).Replace('\', '/')
    $entry = $manifest.files.PSObject.Properties[$relative]
    if ($null -eq $entry -or
        (Get-FileHash -Algorithm SHA256 -LiteralPath $file.FullName).Hash -ne $entry.Value) {
      throw "OCR 恢复载荷 SHA-256 不匹配：$relative"
    }
  }
}

function Restore-PartyOpsOcrFromPublicInstaller {
  param([Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$InstallerPath,
        [Parameter(Mandatory = $true)][string]$ExtractorDirectory)
  $ErrorActionPreference = 'Stop'
  if ((Get-FileHash -Algorithm SHA256 -LiteralPath $InstallerPath).Hash -ne
      "A7C044B0B830CE416FDF02C84DAA2E7AA4929C0833D9B530BC47486D861A81BE") {
    throw "公开 PartyOps 安装包 SHA-256 不匹配，拒绝恢复 OCR。"
  }
  # 固定维护者发布 tag 670 的两个文件，禁止执行未知解包程序或旁加载 DLL。
  $toolHashes = @{
    'innoextract.exe' = '0DAB51583E403970065DB49F87A6A2BF683AB6EBFDF27BEDE2BF633DD232A1A2'
    'libbz2-1.dll' = 'B79CD3A7102E359EF6324E98966A67939E58EC2B651AE2847CAE84A4DFA453F7'
  }
  $toolEntries = @(Get-ChildItem -LiteralPath $ExtractorDirectory -Force)
  if ($toolEntries.Count -ne 2 -or ($toolEntries | Where-Object {
      $_.PSIsContainer -or ($_.Attributes -band [System.IO.FileAttributes]::ReparsePoint)
    })) { throw "OCR 解包工具目录只能包含固定发布的两个普通文件。" }
  foreach ($name in $toolHashes.Keys) {
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $ExtractorDirectory $name)).Hash -ne $toolHashes[$name]) {
      throw "OCR 解包工具 SHA-256 不匹配：$name"
    }
  }
  $output = Join-Path $RepoRoot 'vendor\windows\ocr\tesseract-5.5.3-windows-amd64-recovered'
  if (Test-Path -LiteralPath $output) {
    Assert-PartyOpsOcrRecoveryTree -Root $output -RepoRoot $RepoRoot
    Write-Output "OCR 恢复目录已通过逐文件校验，无需重复提取：$output"
    return
  }
  # 所有暂存文件留在当前工作区；从不执行 PartyOps 安装器。
  $stage = Join-Path $RepoRoot ('.qa\ocr-public-recovery-' + [Guid]::NewGuid().ToString('N'))
  foreach ($base in @($RepoRoot, (Join-Path $RepoRoot '.qa'), (Join-Path $RepoRoot 'vendor'),
      (Join-Path $RepoRoot 'vendor\windows'), (Join-Path $RepoRoot 'vendor\windows\ocr'))) {
    if ((Test-Path -LiteralPath $base) -and
        ((Get-Item -LiteralPath $base -Force).Attributes -band [System.IO.FileAttributes]::ReparsePoint)) {
      throw "OCR 恢复输出路径含重解析点：$base"
    }
  }
  New-Item -ItemType Directory -Path $stage | Out-Null
  & (Join-Path $ExtractorDirectory 'innoextract.exe') --extract --include 'app\ocr' --output-dir $stage $InstallerPath
  if ($LASTEXITCODE -ne 0) { throw "公开安装包 OCR 只读提取失败。" }
  $extracted = Join-Path $stage 'app\ocr'
  Assert-PartyOpsOcrRecoveryTree -Root $extracted -RepoRoot $RepoRoot
  Copy-Item -LiteralPath $extracted -Destination $output -Recurse
  Assert-PartyOpsOcrRecoveryTree -Root $output -RepoRoot $RepoRoot
  Write-Output "已恢复并校验 62 个 OCR 文件：$output"
}

if ($MyInvocation.InvocationName -ne '.') {
  Restore-PartyOpsOcrFromPublicInstaller -RepoRoot $RepoRoot -InstallerPath $InstallerPath -ExtractorDirectory $ExtractorDirectory
}
