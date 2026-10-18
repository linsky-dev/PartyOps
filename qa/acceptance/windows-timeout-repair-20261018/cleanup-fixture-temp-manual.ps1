# 仅供用户手工执行：自动审批拒绝过以下四个精确测试目录的删除。
# 保留源码、验收记录、业务数据、虚拟机和安装包；不修改扫描规则。
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$relative = @(
  'qa/hf2', 'qa/hf3', 'qa/hf4',
  'qa/acceptance/windows-startup-20261016/pytest-host-fixture-20261018-1722'
)
$rows = @()
foreach ($name in $relative) {
  $target = [IO.Path]::GetFullPath((Join-Path $root $name))
  if (-not $target.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'OUTSIDE_WORKSPACE' }
  if (-not (Test-Path -LiteralPath $target)) { continue }
  $ancestor = Get-Item -LiteralPath $target -Force
  while ($null -ne $ancestor) {
    if ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'REPARSE_ANCESTOR_BLOCKED' }
    $ancestor = $ancestor.Parent
  }
  # pytest为每个用例创建同目录的*current符号链接。只识别这类链接，不遍历其目标。
  $items = @()
  $links = @()
  $queue = [Collections.Generic.Queue[string]]::new()
  $queue.Enqueue($target)
  while ($queue.Count) {
    foreach ($item in Get-ChildItem -LiteralPath $queue.Dequeue() -Force -ErrorAction Stop) {
      if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        if ($item.DirectoryName -and -not $item.PSIsContainer) { throw 'UNEXPECTED_FILE_LINK' }
        if ($item.Parent.FullName -ne $target -or $item.Name -notlike '*current' -or $item.LinkType -ne 'SymbolicLink') { throw 'UNEXPECTED_TEST_LINK' }
        $destinations = @($item.Target)
        if ($destinations.Count -ne 1 -or -not [IO.Path]::IsPathRooted($destinations[0])) { throw 'AMBIGUOUS_TEST_LINK' }
        $destination = [IO.Path]::GetFullPath($destinations[0])
        if (-not $destination.StartsWith($target + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'OUTSIDE_TEST_LINK' }
        $destinationItem = Get-Item -LiteralPath $destination -Force -ErrorAction Stop
        if (-not $destinationItem.PSIsContainer -or ($destinationItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'INVALID_TEST_LINK_TARGET' }
        $links += $item.FullName
        continue
      }
      $items += $item
      if ($item.PSIsContainer) { $queue.Enqueue($item.FullName) }
    }
  }
  if ($items | Where-Object { $_.Name -eq '.lock' }) { throw 'ACTIVE_TEST_LOCK' }
  $files = @($items | Where-Object { -not $_.PSIsContainer })
  $rows += [pscustomobject]@{ path=$target; files=$files.Count; bytes=($files | Measure-Object Length -Sum).Sum; links=$links; status='pending' }
}
$record = Join-Path $PSScriptRoot 'fixture-temp-cleanup.json'
$receipt = [ordered]@{
  time=(Get-Date -Format o)
  scope='仅四个已结束的合成测试目录，不包含业务数据或正式pytest-3目录'
  tests='修正后短目录两次40通过；额外深目录失败未确证，不充当产品验收'
  items=$rows
}
function Save-Receipt {
  [IO.File]::WriteAllText($record, ($receipt | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
}
Save-Receipt
foreach ($row in $rows) {
  try {
    # Directory.Delete非递归地删除目录符号链接本身，不进入其目标。
    foreach ($link in $row.links) { [IO.Directory]::Delete($link, $false) }
    Remove-Item -LiteralPath $row.path -Recurse -Force -ErrorAction Stop
    $row.status = 'removed'
    Save-Receipt
  } catch {
    $row.status = 'failed'
    Save-Receipt
    throw
  }
}
Write-Output ('已完成合成测试目录清理，处理目录数：' + $rows.Count)
