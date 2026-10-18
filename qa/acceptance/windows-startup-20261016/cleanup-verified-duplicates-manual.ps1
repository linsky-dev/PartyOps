#requires -Version 5.1
<#
仅供用户审阅后手工运行；助手不得执行此脚本。此前自动清理被审批策略拒绝。
默认只核验；加 -Apply 才修补入口并删除三个固定目标。不会启动或创建虚拟机。
不访问 E 盘，不删除 pytest-3、记录、备份、ISO、DEB、业务数据或源码。
旧 system.qcow2 删除后无法从本脚本恢复；仅保留两个小入口的原始字节备份。
#>
[CmdletBinding()]
param([switch]$Apply)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8 = New-Object Text.UTF8Encoding($false)
$project = 'C:\PartyOps-VM-Lab\windows-startup-repair-20261016'
$qa = Join-Path $project 'qa\acceptance\windows-startup-20261016'
$vm = 'C:\PartyOps-VM-Lab\vms\kylin-v10-sp1-arm64'
$oldDisk = Join-Path $vm 'system.qcow2'
$retainedDisk = Join-Path $vm 'render-probe.qcow2'
$qemuImg = 'D:\PartyOps-VM-Lab\tools\qemu-11.1.0\qemu-img.exe'
$vendor = Join-Path $project 'vendor\windows\ocr'
$targets = @('.qa\ocr-public-recovery-8a79eb0e6a2b4a288463ab60a3ee8129', '.qa\ocr-recovered-inspect')
$run = $null
$receipt = [ordered]@{ schema=1; started_at=[DateTime]::UtcNow.ToString('o'); apply=[bool]$Apply;
    scope='一个旧安装原盘及两个已验证OCR重复目录；不访问E盘，不启动或重建VM';
    irreversible='旧system.qcow2删除后失去干净OS基线；不复制qcow2，入口备份不能恢复原盘'; items=@() }

function Assert-ExactPath([string]$Path, [string]$Expected) {
    $full = [IO.Path]::GetFullPath($Path)
    if ($full -cne $Expected -or $full -notmatch '^[CD]:\\') { throw "PATH_REFUSED: $Path" }
    $current = $full
    while ($current) {
        $item = Get-Item -LiteralPath $current -Force
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "REPARSE_REFUSED: $current" }
        $parent = [IO.Path]::GetDirectoryName($current)
        if (-not $parent -or $parent -eq $current) { break }
        $current = $parent
    }
}
function Get-SafeFiles([string]$Root) {
    Assert-ExactPath $Root $Root
    $pending = New-Object 'Collections.Generic.Stack[string]'
    $pending.Push($Root)
    while ($pending.Count) {
        $directory = $pending.Pop()
        Assert-ExactPath $directory $directory
        foreach ($item in @(Get-ChildItem -LiteralPath $directory -Force)) {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "REPARSE_REFUSED: $($item.FullName)" }
            if ($item.PSIsContainer) { $pending.Push($item.FullName) } else { $item }
        }
    }
}
function Assert-NoQemu {
    # 不依据历史PID；命令行不可读时也停止，避免无法判断占用。
    $processes = @(Get-CimInstance Win32_Process -Filter "Name LIKE 'qemu%'" )
    foreach ($process in $processes) {
        if (-not $process.CommandLine) { throw 'QEMU_COMMAND_LINE_UNAVAILABLE' }
        foreach ($path in @($vm,$oldDisk,$retainedDisk)) {
            if ($process.CommandLine.IndexOf($path,[StringComparison]::OrdinalIgnoreCase) -ge 0) {
                throw "VM_IN_USE: PID $($process.ProcessId)"
            }
        }
    }
}
function Get-DiskInfo([string]$Path) {
    Assert-ExactPath $Path $Path
    $output = @(& $qemuImg info --output=json $Path)
    if ($LASTEXITCODE -ne 0) { throw "QEMU_IMG_INFO_FAILED: $Path" }
    $info = ($output -join "`n") | ConvertFrom-Json
    if ($info.format -ne 'qcow2' -or [long]$info.'virtual-size' -ne 64GB -or
        $info.PSObject.Properties['backing-filename'] -or $info.PSObject.Properties['full-backing-filename']) {
        throw "INDEPENDENT_DISK_REQUIRED: $Path"
    }
    return $info
}
function Get-StreamHash($Stream) {
    $Stream.Position = 0
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($Stream))).Replace('-','').ToLowerInvariant() }
    finally { $sha.Dispose(); $Stream.Position = 0 }
}
function Assert-Parsed([string]$Text) {
    $tokens=$null; $parseErrors=$null
    $ast = [Management.Automation.Language.Parser]::ParseInput($Text,[ref]$tokens,[ref]$parseErrors)
    if ($parseErrors.Count) { throw ('PATCH_PARSE_FAILED: ' + ($parseErrors.Message -join '; ')) }
    return $ast
}
function Replace-Once([string]$Text,[string]$Before,[string]$After) {
    if (($Text.Split(@($Before),[StringSplitOptions]::None)).Count -ne 2) { throw 'PATCH_CONTEXT_NOT_UNIQUE' }
    return $Text.Replace($Before,$After)
}
function Get-PatchedEntrypoints {
    $bootPath = Join-Path $vm 'boot-installed-kernel.ps1'
    $launchPath = Join-Path $vm 'launch.ps1'
    $expected = @('33ae601f2c94c4394dfb76b947d613c592d7a2dc048c9ab98ae3f711c30f2314',
        '2c5a819443010ca7857eed10cb7e24c0d492a288b0bc4abd39e07959947e06db')
    $paths = @($bootPath,$launchPath)
    $texts = @()
    for ($i=0; $i -lt 2; $i++) {
        Assert-ExactPath $paths[$i] $paths[$i]
        if ((Get-FileHash -LiteralPath $paths[$i] -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected[$i]) {
            throw "ENTRYPOINT_CHANGED: $($paths[$i])"
        }
        $texts += [IO.File]::ReadAllText($paths[$i])
    }
    $before = @'
Assert-Disk $disk
# 显式实验开关只接受本任务固定的独立克隆，原盘和身份记录保持不变。
$bootDisk = $disk
if ($RenderProbeClone) {
    $bootDisk = Join-Path $vmRoot 'render-probe.qcow2'
    Assert-Disk $bootDisk
}
'@
    $after = @'
# 原安装盘已退休；默认与兼容开关均使用已完成业务验收的现有独立盘。
$bootDisk = Join-Path $vmRoot 'render-probe.qcow2'
Assert-Disk $bootDisk
'@
    # 保持原有换行；只替换已核对的三个局部片段。
    $newline = "`n"
    if ($texts[0].Contains("`r`n")) { $newline = "`r`n" }
    $patchedBoot = Replace-Once $texts[0] ($before.Replace("`r`n","`n").Replace("`n",$newline)) ($after.Replace("`r`n","`n").Replace("`n",$newline))
    $patchedBoot = Replace-Once $patchedBoot '    if ($RenderProbeClone) {' '    if ($bootDisk -ne $disk) {'
    $guard = @'
# 旧原盘已退休；历史入口只保留共享函数，所有动作均转向现用启动入口。
throw 'ORIGINAL_DISK_RETIRED：禁止重建system.qcow2；请使用boot-installed-kernel.ps1启动现有render-probe.qcow2。'

'@
    $newline = "`n"
    if ($texts[1].Contains("`r`n")) { $newline = "`r`n" }
    $patchedLaunch = Replace-Once $texts[1] "if (`$SshPort -eq `$QmpPort) {" (($guard.Replace("`r`n","`n").Replace("`n",$newline)) + "if (`$SshPort -eq `$QmpPort) {")
    $bootAst = Assert-Parsed $patchedBoot
    $launchAst = Assert-Parsed $patchedLaunch
    # 启动入口依赖AST提取，逐个保证共享函数正文完全不变。
    $originalAst = Assert-Parsed $texts[1]
    foreach ($name in @('Assert-RegularFile','Assert-PortAvailable','Assert-HostCapacity','Assert-NoActiveVm','Set-PrivateDirectory','Assert-Disk','Get-QmpIdentity')) {
        $predicate = { param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }
        $old = $originalAst.Find($predicate,$true); $new = $launchAst.Find($predicate,$true)
        if (-not $old -or -not $new -or $old.Extent.Text -cne $new.Extent.Text) { throw "SHARED_FUNCTION_CHANGED: $name" }
    }
    return @([pscustomobject]@{path=$bootPath;expected=$expected[0];text=$patchedBoot},
        [pscustomobject]@{path=$launchPath;expected=$expected[1];text=$patchedLaunch})
}
function Save-Receipt {
    [IO.File]::WriteAllText((Join-Path $run 'receipt.json'),($receipt | ConvertTo-Json -Depth 12),$utf8)
}
function Invoke-OldDiskCleanup {
    $result = [ordered]@{path=$oldDisk;status='checking';retained_disk=$retainedDisk}
    $receipt.items += $result
    $handles = New-Object 'Collections.Generic.List[IDisposable]'
    try {
        Assert-NoQemu
        Assert-ExactPath $qemuImg $qemuImg
        $result['retained_info'] = Get-DiskInfo $retainedDisk
        $result['old_info'] = Get-DiskInfo $oldDisk
        if ((Get-Item -LiteralPath $oldDisk).Length -ne 15454830592 -or
            (Get-Item -LiteralPath $retainedDisk).Length -ne 21178875904) { throw 'DISK_SIZE_CHANGED' }
        $plan = @(Get-PatchedEntrypoints)
        # 与正常启动共用锁；文件独占检验两盘当前无打开句柄。
        Assert-ExactPath (Join-Path $vm '.launch.lock') (Join-Path $vm '.launch.lock')
        $launchLock = [IO.File]::Open((Join-Path $vm '.launch.lock'),'Open','ReadWrite','None')
        $handles.Add($launchLock)
        $retained = [IO.File]::Open($retainedDisk,'Open','Read','None'); $handles.Add($retained)
        $old = [IO.File]::Open($oldDisk,'Open','Read','None'); $handles.Add($old)
        if ($old.Length -ne 15454830592 -or $retained.Length -ne 21178875904) { throw 'LOCKED_DISK_SIZE_CHANGED' }
        $result['old_bytes'] = $old.Length
        $result['old_sha256'] = Get-StreamHash $old
        $result['entrypoints'] = @()
        foreach ($entry in $plan) {
            $handle = [IO.File]::Open($entry.path,'Open','ReadWrite','None'); $handles.Add($handle)
            if ((Get-StreamHash $handle) -ne $entry.expected) { throw "ENTRYPOINT_CHANGED: $($entry.path)" }
            $entry | Add-Member -NotePropertyName handle -NotePropertyValue $handle
        }
        Assert-NoQemu
        if (-not $Apply) { $result.status='verified_no_mutation'; return }
        # 全部候选已在内存解析成功；备份只包含两个原入口，不复制磁盘。
        foreach ($entry in $plan) {
            $bytes = New-Object byte[] ([int]$entry.handle.Length)
            $entry.handle.Position=0
            $read=0
            while ($read -lt $bytes.Length) {
                $n=$entry.handle.Read($bytes,$read,$bytes.Length-$read)
                if (-not $n) { throw 'ENTRYPOINT_BACKUP_READ_FAILED' }; $read += $n
            }
            $backup = Join-Path $run ((Split-Path -Leaf $entry.path) + '.before')
            [IO.File]::WriteAllBytes($backup,$bytes)
            if ((Get-FileHash -LiteralPath $backup -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.expected) { throw 'BACKUP_HASH_FAILED' }
            $result.entrypoints += [ordered]@{path=$entry.path;before_sha256=$entry.expected;backup=$backup;installed=$false}
        }
        $result.status='installing_entrypoints'; Save-Receipt
        for ($i=0; $i -lt $plan.Count; $i++) {
            $entry=$plan[$i]; $bytes=$utf8.GetBytes($entry.text)
            $entry.handle.Position=0; $entry.handle.Write($bytes,0,$bytes.Length); $entry.handle.SetLength($bytes.Length); $entry.handle.Flush()
            $result.entrypoints[$i].installed=$true
            $result.entrypoints[$i]['after_sha256']=Get-StreamHash $entry.handle
            $entry.handle.Position=0
            $reader=New-Object IO.StreamReader($entry.handle,$utf8,$true,4096,$true)
            try { $installed=$reader.ReadToEnd() } finally { $reader.Dispose() }
            if ($installed -cne $entry.text) { throw 'INSTALLED_ENTRYPOINT_MISMATCH' }
            $null=Assert-Parsed $installed
        }
        $result.status='entrypoints_installed_old_disk_pending'; Save-Receipt
        Assert-NoQemu
        Assert-ExactPath $oldDisk $oldDisk
        Assert-ExactPath $retainedDisk $retainedDisk
        # Windows的FileShare.None不允许自身删除；释放旧盘句柄后立即精确删除。
        # 保留现用盘与启动互斥锁直到完成，阻止正常入口同时启动。
        $old.Dispose()
        Remove-Item -LiteralPath $oldDisk -Force -ErrorAction Stop
        if (Test-Path -LiteralPath $oldDisk) { throw 'OLD_DISK_STILL_PRESENT' }
        $result.status='removed'
    } catch { $result.status='failed'; $result['error']=$_.Exception.Message; Write-Warning $_.Exception.Message }
    finally { foreach ($handle in $handles) { $handle.Dispose() }; Save-Receipt }
}
function Invoke-OcrCleanup([string]$Relative) {
    $target=Join-Path $project $Relative
    $result=[ordered]@{path=$target;status='checking'}; $receipt.items += $result
    $handles=New-Object 'Collections.Generic.List[IDisposable]'
    $sourceHandles=New-Object 'Collections.Generic.List[IDisposable]'
    $lockedCopies=@{}
    try {
        Assert-ExactPath $target $target
        Assert-ExactPath $vendor $vendor
        $retainedByHash=@{}
        foreach ($file in @(Get-SafeFiles $vendor)) {
            $hash=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            if (-not $retainedByHash.ContainsKey($hash)) { $retainedByHash[$hash]=$file.FullName }
        }
        $files=@(Get-SafeFiles $target | Sort-Object FullName)
        $size=0L; $manifest=@()
        foreach ($file in $files) {
            $handle=[IO.File]::Open($file.FullName,'Open','Read','None'); $handles.Add($handle); $sourceHandles.Add($handle)
            $hash=Get-StreamHash $handle; $size += $handle.Length
            if (-not $retainedByHash.ContainsKey($hash)) { throw "RETAINED_COPY_MISSING: $($file.FullName)" }
            # 锁住对应保留副本直到删除结束，避免验证后被更换。
            if (-not $lockedCopies.ContainsKey($hash)) {
                $copy=[IO.File]::Open($retainedByHash[$hash],'Open','Read','None'); $handles.Add($copy)
                if ((Get-StreamHash $copy) -ne $hash -or $copy.Length -ne $handle.Length) { throw 'RETAINED_COPY_CHANGED' }
                $lockedCopies[$hash]=$copy
            }
            if ($lockedCopies[$hash].Length -ne $handle.Length) { throw 'RETAINED_COPY_SIZE_CHANGED' }
            $manifest += [ordered]@{path=$file.FullName;bytes=$handle.Length;sha256=$hash;retained=$retainedByHash[$hash]}
        }
        if ($files.Count -ne 62 -or $size -ne 114801597) { throw 'OCR_INVENTORY_CHANGED' }
        $result['files']=$files.Count; $result['bytes']=$size
        $manifestPath=Join-Path $run ((Split-Path -Leaf $target)+'.files.json')
        [IO.File]::WriteAllText($manifestPath,($manifest | ConvertTo-Json -Depth 5),$utf8)
        $result['manifest']=$manifestPath
        if (-not $Apply) { $result.status='verified_no_mutation'; return }
        # 持有全部待删文件句柄期间再次校验目录树，删除前释放待删句柄。
        $again=@(Get-SafeFiles $target | Sort-Object FullName)
        if (($again.FullName -join "`n") -cne ($files.FullName -join "`n")) { throw 'OCR_TREE_CHANGED' }
        $result.status='verified_pending_delete'; Save-Receipt
        foreach ($handle in $sourceHandles) { $handle.Dispose() }
        Assert-ExactPath $target $target
        $null=@(Get-SafeFiles $target)
        Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction Stop
        if (Test-Path -LiteralPath $target) { throw 'OCR_DIRECTORY_STILL_PRESENT' }
        $result.status='removed'
    } catch { $result.status='failed'; $result['error']=$_.Exception.Message; Write-Warning $_.Exception.Message }
    finally { foreach ($handle in $handles) { $handle.Dispose() }; Save-Receipt }
}

# 所有留痕均留在固定QA目录；不在其他盘或目录生成制品。
Assert-ExactPath $qa $qa
if ([IO.Path]::GetFullPath($PSScriptRoot) -cne $qa) { throw 'SCRIPT_LOCATION_REFUSED' }
$run=Join-Path $qa ('duplicate-cleanup-manual-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run -ErrorAction Stop | Out-Null
Save-Receipt
Invoke-OldDiskCleanup
foreach ($relative in $targets) { Invoke-OcrCleanup $relative }
$receipt['finished_at']=[DateTime]::UtcNow.ToString('o')
$failed=@($receipt.items | Where-Object { $_.status -eq 'failed' })
$receipt['status']='verified_no_mutation'
if ($Apply) { $receipt.status='completed' }
if ($failed.Count) { $receipt.status='failed_or_partial' }
Save-Receipt
Write-Host "回执：$(Join-Path $run 'receipt.json')"
if ($failed.Count) { throw "CLEANUP_FAILED_OR_PARTIAL: $($failed.Count)项；保留回执，不自动重试、不自动回滚入口。" }
