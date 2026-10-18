#requires -Version 5.1
# 纯静态检查：只读取脚本/入口并在内存生成候选，不执行清理主体。
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$path=Join-Path $PSScriptRoot 'cleanup-verified-duplicates-manual.ps1'
$tokens=$null; $parseErrors=$null
$text=[IO.File]::ReadAllText($path,[Text.Encoding]::UTF8)
$ast=[Management.Automation.Language.Parser]::ParseInput($text,[ref]$tokens,[ref]$parseErrors)
if ($parseErrors.Count) { throw ($parseErrors.Message -join '; ') }
foreach ($name in @('Assert-ExactPath','Assert-Parsed','Replace-Once','Get-PatchedEntrypoints')) {
    $node=$ast.Find({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true)
    if (-not $node) { throw "MISSING_FUNCTION: $name" }
    . ([scriptblock]::Create($node.Extent.Text))
}
$vm='C:\PartyOps-VM-Lab\vms\kylin-v10-sp1-arm64'
$plan=@(Get-PatchedEntrypoints)
if ($plan.Count -ne 2) { throw 'PLAN_COUNT' }
if ($plan[0].text.Contains('Assert-Disk $disk')) { throw 'OLD_DISK_STILL_REQUIRED' }
if (-not $plan[0].text.Contains('if ($bootDisk -ne $disk) {')) { throw 'DEFAULT_DRIVE_NOT_REBOUND' }
if (-not $plan[0].text.Contains('$bootDisk = Join-Path $vmRoot ''render-probe.qcow2''')) { throw 'RETAINED_DISK_MISSING' }
if (-not $plan[1].text.Contains("throw 'ORIGINAL_DISK_RETIRED")) { throw 'RETIREMENT_GUARD_MISSING' }
$old=[IO.File]::ReadAllText($plan[1].path)
$candidate=Assert-Parsed $plan[1].text
$guard=@($candidate.EndBlock.Statements | Where-Object { $_ -is [Management.Automation.Language.ThrowStatementAst] })
if ($guard.Count -ne 1 -or -not $guard[0].Extent.Text.Contains('ORIGINAL_DISK_RETIRED')) { throw 'TOP_LEVEL_RETIREMENT_GUARD_MISSING' }
$deletes=@($ast.FindAll({param($n) $n -is [Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Remove-Item'},$true))
if ($deletes.Count -ne 2) { throw 'DELETE_COMMAND_COUNT' }
foreach ($command in $deletes) { if (-not $command.Extent.Text.Contains('-LiteralPath')) { throw 'NON_LITERAL_DELETE' } }
[pscustomobject]@{status='passed_static_only';powershell=$PSVersionTable.PSVersion.ToString();
    cleanup_sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant();
    candidates=@($plan | ForEach-Object { [ordered]@{path=$_.path;original_sha256=$_.expected;candidate_characters=$_.text.Length} });
    checks=@('UTF8 ParseInput','fixed path ancestry','original entrypoint hashes','in-memory patches parse',
        'shared function bodies unchanged','default and switch use retained disk','old launcher top-level retirement guard','two literal delete commands');
    destructive_script_executed=$false;vm_started=$false} | ConvertTo-Json -Depth 6
