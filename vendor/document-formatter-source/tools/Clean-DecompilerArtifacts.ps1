param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$sourceRoot = Join-Path $Root 'src'
if (!(Test-Path -LiteralPath $sourceRoot)) {
    throw "源码目录不存在：$sourceRoot"
}

# 这些行是反编译器在无法解析 WinForms 调用返回类型时写入的诊断注释，
# 不属于业务逻辑。只删除完整匹配的独立注释行，避免触碰正常代码。
$diagnosticLine = '^\s*//(?:IL_|ILSpy generated this explicit interface implementation from \.override directive)'
$encoding = [System.Text.UTF8Encoding]::new($false)
$changedFiles = 0
$removedLines = 0

Get-ChildItem -LiteralPath $sourceRoot -Recurse -Filter '*.cs' -File | ForEach-Object {
    $path = $_.FullName
    $lines = [System.IO.File]::ReadAllLines($path)
    $kept = [System.Collections.Generic.List[string]]::new($lines.Length)
    $removedFromFile = 0

    foreach ($line in $lines) {
        if ($line -match $diagnosticLine) {
            $removedFromFile++
            continue
        }
        $kept.Add($line)
    }

    if ($removedFromFile -gt 0) {
        [System.IO.File]::WriteAllLines($path, $kept, $encoding)
        $changedFiles++
        $removedLines += $removedFromFile
        Write-Host "已清理 $removedFromFile 行：$path"
    }
}

Write-Host "反编译诊断清理完成：$changedFiles 个文件，$removedLines 行。" -ForegroundColor Green
