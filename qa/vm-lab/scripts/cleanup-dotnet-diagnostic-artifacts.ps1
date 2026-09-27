# 仅清理本轮已登记的五个可重建宿主载荷；日志、配方、介质与候选包保留。
$ErrorActionPreference = 'Stop'
$evidenceRoot = 'D:\PartyOps-VM-Lab\reports\win7-x64\dotnet-extraction-20260908'
$fullRoot = 'E:\codex\PartyOps\.partyops-vm-lab\reports\win7-dotnet-extraction-20260908'
$indexPath = Join-Path $evidenceRoot 'rebuildable-artifacts.json'
$stopPath = Join-Path $evidenceRoot 'normal-stop-after-diagnostic.json'
$resultPath = Join-Path $evidenceRoot 'host-diagnostic-cleanup-20260908.json'
if (Test-Path -LiteralPath $resultPath) { throw 'DIAGNOSTIC_CLEANUP_ALREADY_RECORDED' }
$index = Get-Content -LiteralPath $indexPath -Raw | ConvertFrom-Json
$stopped = Get-Content -LiteralPath $stopPath -Raw | ConvertFrom-Json
if ($index.guest_uuid -ne '406ef8ee-e342-405f-933f-7ddce0a55f1b' -or
    $stopped.uuid -ne $index.guest_uuid -or $stopped.status -ne 'stopped' -or
    $stopped.qemu_process_active -ne $false -or $stopped.shutdown_event.data.reason -ne 'guest-shutdown') {
    throw 'DIAGNOSTIC_CLEANUP_GUEST_STOP_NOT_PROVEN'
}
$allowed = @(
    (Join-Path $evidenceRoot 'sentinel-ultra64.exe'),
    (Join-Path $evidenceRoot 'sentinel-prefix.exe'),
    (Join-Path $evidenceRoot 'offline-ultra64.exe'),
    (Join-Path $evidenceRoot 'compressed-prefix.bin'),
    (Join-Path $fullRoot 'full-layout.exe')
)
$registered = @($index.host_artifacts)
if ($registered.Count -ne 5 -or @($registered.path | Select-Object -Unique).Count -ne 5) {
    throw 'DIAGNOSTIC_CLEANUP_INDEX_CHANGED'
}
$processes = @(Get-CimInstance Win32_Process)
if ($processes.Count -eq 0) { throw 'DIAGNOSTIC_CLEANUP_PROCESS_SNAPSHOT_UNAVAILABLE' }
if (@($processes | Where-Object { $_.Name -like 'qemu*' -and $_.CommandLine -like ('*' + $index.guest_uuid + '*') }).Count -gt 0) {
    throw 'DIAGNOSTIC_CLEANUP_GUEST_STILL_RUNNING'
}
$verified = @()
foreach ($entry in $registered) {
    if ($entry.rebuildable -ne $true -or $entry.role -ne 'controlled-extraction-diagnostic' -or $entry.path -notin $allowed) {
        throw 'DIAGNOSTIC_CLEANUP_NOT_IN_EXACT_ALLOWLIST'
    }
    $resolved = (Resolve-Path -LiteralPath $entry.path).ProviderPath
    if ($resolved -notin $allowed) { throw 'DIAGNOSTIC_CLEANUP_RESOLVED_PATH_CHANGED' }
    $ancestor = $resolved
    while ($ancestor) {
        $item = Get-Item -LiteralPath $ancestor -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'DIAGNOSTIC_CLEANUP_REPARSE_POINT' }
        $parentInfo = [IO.Directory]::GetParent($ancestor)
        $parent = if ($null -ne $parentInfo) { $parentInfo.FullName } else { $null }
        if ($parent -eq $ancestor) { break }
        $ancestor = $parent
    }
    $file = Get-Item -LiteralPath $resolved -Force
    if ($file.PSIsContainer -or $file.Length -ne [long]$entry.bytes) { throw 'DIAGNOSTIC_CLEANUP_FILE_LENGTH_CHANGED' }
    $references = @($processes | Where-Object {
        $_.ExecutablePath -eq $resolved -or ($_.CommandLine -and $_.CommandLine.IndexOf($resolved, [StringComparison]::OrdinalIgnoreCase) -ge 0)
    })
    if ($references.Count -gt 0) { throw 'DIAGNOSTIC_CLEANUP_RUNNING_REFERENCE' }
    $stream = [IO.File]::Open($resolved, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
    try {
        $hash = [Security.Cryptography.SHA256]::Create()
        try { $actual = [BitConverter]::ToString($hash.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
        finally { $hash.Dispose() }
    } finally { $stream.Dispose() }
    if ($actual -ne $entry.sha256) { throw 'DIAGNOSTIC_CLEANUP_HASH_CHANGED' }
    $verified += [pscustomobject]@{path=$resolved;bytes=[long]$file.Length;sha256=$actual;no_reparse=$true;no_running_reference=$true;exclusive_read_verified=$true}
}
$record = [ordered]@{
    generated_at=[DateTime]::UtcNow.ToString('o');scope='five-registered-rebuildable-host-diagnostics-only';
    index_sha256=(Get-FileHash -LiteralPath $indexPath -Algorithm SHA256).Hash.ToLowerInvariant();
    guest_stop_evidence=$stopPath;verified=$verified;results=@();reclaimed_bytes=[long]0;status='verified';runtime_environment_passed=$false
}
function Save-CleanupRecord {
    [IO.File]::WriteAllText($resultPath, ($record | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))
}
Save-CleanupRecord
foreach ($item in $verified) {
    try {
        # 已先逐一确认绝对路径及父级，按文件删除，不递归、不拼接另一个 shell。
        Remove-Item -LiteralPath $item.path -ErrorAction Stop
        if (Test-Path -LiteralPath $item.path) { throw 'DIAGNOSTIC_CLEANUP_FILE_STILL_EXISTS' }
        $record.results += [pscustomobject]@{path=$item.path;deleted=$true;bytes=$item.bytes;at=[DateTime]::UtcNow.ToString('o')}
        $record.reclaimed_bytes += $item.bytes
        Save-CleanupRecord
    } catch {
        $record.status='failed'
        $record.results += [pscustomobject]@{path=$item.path;deleted=$false;reason=$_.Exception.Message}
        Save-CleanupRecord
        throw
    }
}
$record.status='completed'
Save-CleanupRecord
[pscustomobject]@{report=$resultPath;deleted_files=$record.results.Count;reclaimed_bytes=$record.reclaimed_bytes} | ConvertTo-Json
