param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)
$ErrorActionPreference = 'Stop'
$sourceRoot = Join-Path $Root 'src'
$main = Join-Path $Root 'src\PartyOps.DocumentFormatter.AddIn'
if (!(Test-Path -LiteralPath $sourceRoot)) { throw "Source root not found: $sourceRoot" }
if (!(Test-Path -LiteralPath $main)) { throw "Main project not found: $main" }

$checks = @()
function Add-Check($Name, $Passed, $Detail) {
    $script:checks += [pscustomobject]@{ Check=$Name; Passed=[bool]$Passed; Detail=$Detail }
}

# 只统计可维护源码，排除 MSBuild 在 bin/obj 中生成的框架属性文件。
$files = @(Get-ChildItem -LiteralPath $sourceRoot -Recurse -Filter '*.cs' -File |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' })
$mainFiles = @($files | Where-Object {
    $_.FullName.StartsWith($main + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
})
$text = ($files | Get-Content -Raw) -join "`n"
Add-Check 'Confuser module references removed' ($text -notmatch '_003CModule_003E') 'Expected 0 references to the recovered constant runtime.'
Add-Check 'Confuser attributes removed' ($text -notmatch 'ConfusedBy|SuppressIldasm') 'Expected no Confuser assembly markers.'
Add-Check 'ILSpy diagnostic comments removed' ($text -notmatch '(?m)^\s*//IL_') 'Expected no IL_xxxx diagnostic comments.'
Add-Check 'ILSpy override comments removed' ($text -notmatch '(?m)^\s*//ILSpy generated this explicit interface implementation') 'Expected no generated .override comments.'
# C# 关键字区分大小写；不能把 Word COM API 的 GoTo 方法误判为反编译跳转。
Add-Check 'Decompiler goto labels removed' ($text -cnotmatch '(?m)^\s*goto\b') 'Expected no decompiler goto statements in main source.'
Add-Check 'No hard decompile errors' ($text -notmatch 'Cannot decompile|Incompatible stack heights|Unknown result type|OpCode not supported') 'Expected no hard decompiler failures.'
Add-Check 'Decompiler identifiers removed' ($text -notmatch '_002Ector|\[ref\]') 'Expected no invalid decompiler identifiers or pseudo-ref syntax.'
Add-Check 'Main entry point recovered' (Test-Path (Join-Path $main 'DocumentRepository\ThisAddIn.cs')) 'DocumentRepository.ThisAddIn'
Add-Check 'Ribbon recovered' (Test-Path (Join-Path $main 'DocumentRepository\Ribbon1.cs')) 'DocumentRepository.Ribbon1'
Add-Check 'Format pipeline recovered' (Test-Path (Join-Path $main 'DocumentRepository\Pipelines\Format\FormatPipeline.cs')) 'FormatPipeline'
Add-Check 'Style engine recovered' (Test-Path (Join-Path $main 'DocumentRepository\Services\Formatting\StyleBasedFormatEngine.cs')) 'StyleBasedFormatEngine'

$checks | Format-Table -AutoSize
if ($checks.Passed -contains $false) {
    throw 'Recovered-source structural checks failed.'
}
Write-Host "Recovered-source structural checks passed ($($files.Count) total C# files; $($mainFiles.Count) main C# files)." -ForegroundColor Green
