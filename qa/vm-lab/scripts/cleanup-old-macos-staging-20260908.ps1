# 仅删除已核对的 1.4.3-rc.9 PKG 暂存副本；来源和旧验收摘要原样保留。
$ErrorActionPreference = 'Stop'
$cleanupRoot = [IO.Path]::GetFullPath('E:/codex/PartyOps')
$cleanupReport = 'D:/PartyOps-VM-Lab/reports/cleanup-old-macos-staging-20260908.json'
if (Test-Path -LiteralPath $cleanupReport) { throw 'CLEANUP_RECEIPT_ALREADY_EXISTS' }
$cleanupRelative = @(
    '.tmp-macos-run-32339083048/partyops-macos-x86_64-rc9/PartyOps_1.4.3-rc.9_macos_x86_64.pkg',
    '.tmp-macos-run-32339083048/partyops-macos-arm64-rc9/PartyOps_1.4.3-rc.9_macos_arm64.pkg',
    '.tmp-macos-run-32341111859/partyops-macos-x86_64-rc9/PartyOps_1.4.3-rc.9_macos_x86_64.pkg',
    '.tmp-macos-run-32341111859/partyops-macos-arm64-rc9/PartyOps_1.4.3-rc.9_macos_arm64.pkg'
)
$cleanupProcesses = @(Get-CimInstance Win32_Process | Where-Object ProcessId -ne $PID)
$cleanupRows = @(foreach ($cleanupItem in $cleanupRelative) {
    $cleanupPath = [IO.Path]::GetFullPath((Join-Path $cleanupRoot $cleanupItem))
    if (-not $cleanupPath.StartsWith($cleanupRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'OUTSIDE_PROJECT' }
    $cleanupFile = Get-Item -LiteralPath $cleanupPath -Force
    if ($cleanupFile.PSIsContainer) { throw 'REGULAR_PKG_REQUIRED' }
    for ($cleanupParent = $cleanupFile; $null -ne $cleanupParent; $cleanupParent = $cleanupParent.Parent) {
        if ($cleanupParent.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'REPARSE_REJECTED' }
    }
    $cleanupAttestation = Get-Content -LiteralPath ($cleanupPath + '.attestation.json') -Raw | ConvertFrom-Json
    if ($cleanupAttestation.version -ne '1.4.3-rc.9' -or $cleanupAttestation.real_device_validation -ne $false) { throw 'OLD_VERSION_NOT_PROVEN' }
    $cleanupDirectory = Split-Path -Parent $cleanupPath
    if (@($cleanupProcesses | Where-Object {
        ($_.CommandLine -and $_.CommandLine.IndexOf($cleanupDirectory, [StringComparison]::OrdinalIgnoreCase) -ge 0) -or
        ($_.ExecutablePath -and $_.ExecutablePath.StartsWith($cleanupDirectory + '\', [StringComparison]::OrdinalIgnoreCase))
    }).Count) { throw 'ACTIVE_PROCESS_REFERENCE' }
    [ordered]@{path=$cleanupPath;bytes=$cleanupFile.Length;sha256=(Get-FileHash -LiteralPath $cleanupPath -Algorithm SHA256).Hash.ToLowerInvariant();deleted=$false}
})
[long]$cleanupTotal = 0
foreach ($cleanupRow in $cleanupRows) { $cleanupTotal += [long]$cleanupRow.bytes }
$cleanupReceipt = [ordered]@{generated_at=[DateTimeOffset]::Now.ToString('o');status='prepared';reason='用户要求删除过时安装包和中间产物；仅保留最新版本';files=$cleanupRows;logical_bytes=$cleanupTotal;physical_reclaimed_bytes=$null;preserves=@('attestation.json','旧来源记录','当前候选','原版 ISO','基础 VM','业务数据');recovery='按保留的旧来源记录重新获取，当前本机不保留副本'}
function Save-CleanupReceipt { $cleanupReceipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $cleanupReport -Encoding utf8NoBOM }
Save-CleanupReceipt
foreach ($cleanupRow in $cleanupRows) {
    if ((Get-Item -LiteralPath $cleanupRow.path).Length -ne $cleanupRow.bytes) { throw 'FILE_CHANGED' }
    Remove-Item -LiteralPath $cleanupRow.path -Force
    if (Test-Path -LiteralPath $cleanupRow.path) { throw 'DELETE_NOT_CONFIRMED' }
    $cleanupRow.deleted = $true
    Save-CleanupReceipt
}
$cleanupReceipt.status = 'completed'
$cleanupReceipt.completed_at = [DateTimeOffset]::Now.ToString('o')
Save-CleanupReceipt
[ordered]@{status=$cleanupReceipt.status;files=$cleanupRows.Count;logical_bytes=$cleanupReceipt.logical_bytes;report=$cleanupReport} | ConvertTo-Json
