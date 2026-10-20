# 本轮本地精确清理；默认仅生成dryrun，只有显式Execute才删除。
# 所有删除均由当前PowerShell原生LiteralPath执行，不调用其他shell。
[CmdletBinding()]
param([switch]$Execute, [string]$GithubReadback)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Workspace = 'C:\PartyOps-VM-Lab\windows-startup-repair-20261016'
$Publication = Join-Path $Workspace 'qa\acceptance\windows-startup-20261016\publication-windows-20261018'
$InventoryPath = Join-Path $Publication 'cleanup-candidates-readonly-20261018.json'
$FinalHash = 'e3e2ea45ccd38acff059b5713826a78f998a56ccef75341633f67bd4e17e57da'
$OldHash = '3c81fdba89781722f94e82d31c7ff99b4357a1374fc6454ad47d3cfa464d1e27'
$OldPublication = Join-Path $Workspace 'qa\acceptance\windows-startup-20261016\publication-windows-20261017'
$FinalFiles = @((Join-Path $Workspace 'artifacts\PartyOps_1.4.5-rc.6_windows_amd64.exe'), (Join-Path $Publication 'batch\PartyOps_1.4.5-rc.6_windows_amd64_startup-20261018.exe'))
$CachePaths = @(
    # 正式验收复核仍读取冻结程序目录，必须保留；只清理可再生成的构建运行库。
    'artifacts\windows-runtime',
    '.build-windows\alembic-data', '.build-windows\sitecustomize', '.build-windows\spec', '.build-windows\work',
    '.build-kit\downloads', '.build-kit\vs2022', '.build-kit\vs_BuildTools.exe',
    '.qa\deferred-cache\VMware-workstation-full-17.6.4-24832109.exe', '.qa\deferred-cache\wps-office-runtime.tar.gz'
) | ForEach-Object { Join-Path $Workspace $_ }
$OldFiles = @(
    (Join-Path $OldPublication 'prepared\PartyOps_1.4.5-rc.6_windows_amd64.exe'),
    (Join-Path $OldPublication 'draft\PartyOps_1.4.5-rc.6_windows_amd64_startup-20261017.exe'),
    (Join-Path $OldPublication 'batch\PartyOps_1.4.5-rc.6_windows_amd64_startup-20261017.exe'),
    (Join-Path $Workspace ('artifacts\candidate-history\' + $OldHash + '\PartyOps_1.4.5-rc.6_windows_amd64.exe'))
)
$OldSidecars = @($OldFiles | ForEach-Object { $_ + '.sha256' })

function Save-Exclusive([string]$Path, $Value) {
    $bytes = (New-Object Text.UTF8Encoding($false)).GetBytes(($Value | ConvertTo-Json -Depth 18) + "`n")
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush() } finally { $stream.Dispose() }
}
function Read-Json([string]$Path) { return (Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json) }
function File-Hash([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Space {
    $drive = New-Object IO.DriveInfo('C:\')
    return @{drive='C:';available_bytes=$drive.AvailableFreeSpace;total_free_bytes=$drive.TotalFreeSpace;total_bytes=$drive.TotalSize}
}
function Assert-Path([string]$Path) {
    $absolute = [IO.Path]::GetFullPath($Path)
    if (-not [IO.Path]::IsPathRooted($Path) -or -not $absolute.StartsWith($Workspace + '\', [StringComparison]::OrdinalIgnoreCase) -or $absolute -cne $Path) { throw 'EXACT_WORKSPACE_ABSOLUTE_PATH_REQUIRED' }
    if ($absolute -match '(?i)(?:^|[\\/])(?:pytest[^\\/]*|\.test-temp)(?:[\\/]|$)') { throw 'REJECTED_TEST_TEMP_SCOPE_FORBIDDEN' }
    # 逐级检查父目录，不通过junction或符号链接访问目标。
    $cursor = $absolute
    while ($cursor.Length -ge $Workspace.Length) {
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'REPARSE_PATH_FORBIDDEN' }
        }
        if ($cursor -ieq $Workspace) { break }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
}
function Inspect-Tree([string]$Path) {
    Assert-Path $Path
    if (-not (Test-Path -LiteralPath $Path)) { return @{path=$Path;present=$false;logical_bytes=0;files=0;directories=0} }
    $pending = New-Object 'Collections.Generic.Stack[System.IO.FileSystemInfo]'
    $pending.Push((Get-Item -LiteralPath $Path -Force)); $bytes = [long]0; $files = 0; $directories = 0
    while ($pending.Count) {
        $item = $pending.Pop(); $item.Refresh()
        $current = [IO.Path]::GetFullPath($item.FullName)
        if (-not $current.StartsWith($Path.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -and $current -cne $Path) { throw 'DESCENDANT_SCOPE_ESCAPE' }
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'DESCENDANT_REPARSE_FORBIDDEN' }
        if (($item.Attributes -band [IO.FileAttributes]::Directory) -ne 0) {
            $directories++
            # 已验证祖先无reparse；逐层.NET枚举避免每个文件重复访问全部父链。
            foreach ($child in $item.GetFileSystemInfos()) {
                if (($child.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'DESCENDANT_REPARSE_FORBIDDEN' }
                $pending.Push($child)
            }
        } else { $files++; $bytes += $item.Length }
    }
    return @{path=$Path;present=$true;logical_bytes=$bytes;files=$files;directories=$directories}
}
function Assert-NoBuild {
    foreach ($row in @(Get-CimInstance Win32_Process)) {
        if ($row.ProcessId -eq $PID) { continue }
        $buildName = $row.Name -match '^(?i:cl|link|nmake|msbuild|makensis|iscc|pyinstaller|nuitka|cmake)\.exe$'
        $buildCommand = [string]$row.CommandLine -match '(?i)(?:-m\s+PyInstaller|[\\/]scripts[\\/](?:build[^\\/\s]*|test\.ps1)|(?:build_windows|build-windows)\.(?:py|ps1)|--(?:onefile|onedir))'
        if ($buildName -or $buildCommand) {
            $handle = Get-Process -Id $row.ProcessId -ErrorAction SilentlyContinue
            if ($null -ne $handle -and -not $handle.HasExited) { throw 'ACTIVE_BUILD_PROCESS_BLOCKS_CLEANUP' }
        }
    }
}
function Assert-Final {
    foreach ($path in $FinalFiles) {
        Assert-Path $path
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -ne 449988807 -or (File-Hash $path) -cne $FinalHash) { throw 'FINAL_E3E_PACKAGE_MUST_BE_RETAINED' }
        $sidecar = $path + '.sha256'; Assert-Path $sidecar
        if ((Get-Content -LiteralPath $sidecar -Raw -Encoding UTF8).Trim() -cne ($FinalHash + '  ' + [IO.Path]::GetFileName($path))) { throw 'FINAL_SIDECAR_MUST_BE_RETAINED' }
    }
}
function Assert-Inventory {
    $inventory = Read-Json $InventoryPath
    foreach ($path in $CachePaths) {
        $matches = @($inventory.items | Where-Object {$_.path -ceq $path -and $_.decision -eq ([string][char]0x53ef + [char]0x6e05 + [char]0x7406 + [char]0x5019 + [char]0x9009)})
        if ($matches.Count -ne 1) { throw 'CACHE_NOT_IN_READONLY_CANDIDATE_INVENTORY' }
    }
    $group = @($inventory.hardlink_groups | Where-Object {$_.sha256 -ceq $OldHash})
    if ($group.Count -ne 1 -or $group[0].logical_bytes_per_link -ne 449978846 -or $group[0].reported_link_count -ne 4) { throw 'EXACT_OLD_HARDLINK_GROUP_REQUIRED' }
    if (@(Compare-Object ($OldFiles | Sort-Object) ($group[0].hardlink_aliases_from_fsutil | Sort-Object) -CaseSensitive).Count) { throw 'OLD_HARDLINK_PATHS_CHANGED' }
    foreach ($path in $OldFiles) {
        Assert-Path $path
        if (Test-Path -LiteralPath $path) {
            if ((Get-Item -LiteralPath $path).PSIsContainer -or (Get-Item -LiteralPath $path).Length -ne 449978846 -or (File-Hash $path) -cne $OldHash) { throw 'OLD_3C81_PACKAGE_IDENTITY_CHANGED' }
        }
    }
    foreach ($path in $OldSidecars) {
        Assert-Path $path
        if (Test-Path -LiteralPath $path) {
            if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
                (Get-Content -LiteralPath $path -Raw -Encoding UTF8).Trim() -cne ($OldHash + '  ' + [IO.Path]::GetFileName($path.Substring(0, $path.Length - 7)))) { throw 'OLD_SIDECAR_EXACT_HASH_AND_NAME_REQUIRED' }
        }
    }
}
function Assert-ProductionProof {
    if (-not $GithubReadback) { throw 'EXPLICIT_CURRENT_GITHUB_READBACK_REQUIRED' }
    Assert-Path $GithubReadback
    $python = Join-Path $Workspace 'backend\.venv\Scripts\python.exe'
    # 这里只复用既有只读凭证核验；所有文件删除始终由当前PowerShell执行。
    $probe = Join-Path $Publication 'verify-local-cleanup-proofs.py'
    $output = & $python -X utf8 -B $probe (Join-Path $Publication 'production-website-readback.json') $GithubReadback 2>&1
    if ($LASTEXITCODE -ne 0 -or ($output -join "`n").Trim() -cne 'CURRENT_PRODUCTION_AND_GITHUB_PROOFS_VALID') { throw 'CURRENT_PRODUCTION_AND_GITHUB_PROOFS_REQUIRED' }
}

$before = Space
Assert-Final
Assert-NoBuild
Assert-Inventory
$plan = @($CachePaths + $OldFiles + $OldSidecars | ForEach-Object { Inspect-Tree $_ })
$proofStatus = 'not-requested-dryrun-only'
if ($GithubReadback) { Assert-ProductionProof; $proofStatus = 'validated' }
$record = @{status='dryrun-no-deletion';time_utc=[DateTime]::UtcNow.ToString('o');workspace=$Workspace;inventory_sha256=(File-Hash $InventoryPath);final_sha256=$FinalHash;proof_status=$proofStatus;before_space=$before;plan=$plan;preserved='final package and sidecars; evidence; WPS outputs; source; backups; VM; rejected test temp directories';results=@()}
$stamp = [Guid]::NewGuid().ToString('N').Substring(0,12)
if (-not $Execute) {
    $record.after_space = Space
    $target = Join-Path $Publication ('local-cleanup-dryrun-' + $stamp + '.json')
    Save-Exclusive $target $record
    Write-Output ('DRYRUN_NO_DELETION ' + $target)
    return
}
# 单次intent保留；未知中断不得删intent后重发。
Assert-ProductionProof
Assert-NoBuild
Assert-Final
$intent = Join-Path $Publication 'local-cleanup.execute.attempt.json'
if ((Test-Path -LiteralPath $intent) -or (Test-Path -LiteralPath (Join-Path $Publication 'local-cleanup.execute.result.json'))) { throw 'CLEANUP_ALREADY_ATTEMPTED_INSPECT_ONLY' }
$record.status = 'execute-started'
Save-Exclusive $intent $record
$results = @(); $failed = $false
foreach ($entry in $plan) {
    try {
        Assert-NoBuild
        $current = Inspect-Tree $entry.path
        if ($current.present) {
            if ($entry.path -cin $OldFiles -and (File-Hash $entry.path) -cne $OldHash) { throw 'OLD_PACKAGE_CHANGED_BEFORE_DELETE' }
            if ($entry.path -cin $OldSidecars -and (Get-Content -LiteralPath $entry.path -Raw -Encoding UTF8).Trim() -cne ($OldHash + '  ' + [IO.Path]::GetFileName($entry.path.Substring(0, $entry.path.Length - 7)))) { throw 'OLD_SIDECAR_CHANGED_BEFORE_DELETE' }
            # 绝对路径已核位于精确允许目录，且刚逐项核过所有后代无reparse。
            Remove-Item -LiteralPath $entry.path -Force -Recurse
        }
        $result = @{path=$entry.path;status=($(if ($current.present) {'deleted'} else {'already-absent'}));logical_bytes_before=$current.logical_bytes;time_utc=[DateTime]::UtcNow.ToString('o')}
        $results += $result
        Save-Exclusive (Join-Path $Publication ('local-cleanup-item-' + $stamp + '-' + $results.Count + '.json')) $result
    } catch {
        $results += @{path=$entry.path;status='failed-stop';error_type=$_.Exception.GetType().FullName;time_utc=[DateTime]::UtcNow.ToString('o')}
        $failed = $true; break
    }
}
$finalVerified = $false
try { Assert-Final; $finalVerified = $true } catch { $failed = $true }
$receipt = @{status=($(if ($failed) {'stopped-review-required'} else {'completed'}));attempt_sha256=(File-Hash $intent);results=$results;before_space=$before;after_space=(Space);final_retained_verified=$finalVerified;completed_at=[DateTime]::UtcNow.ToString('o')}
Save-Exclusive (Join-Path $Publication 'local-cleanup.execute.result.json') $receipt
if ($failed) { throw 'CLEANUP_STOPPED_INSPECT_ONLY' }
Write-Output 'LOCAL_CLEANUP_COMPLETED'
