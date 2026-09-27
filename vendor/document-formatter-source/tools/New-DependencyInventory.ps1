param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot),
    [string]$OutputPath = ''
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $Root 'artifacts\dependency-inventory.json'
}

$packageIdentity = @{
    'Microsoft.Bcl.HashCode.dll' = @{ id = 'Microsoft.Bcl.HashCode'; version = '1.1.1' }
    'System.Memory.dll' = @{ id = 'System.Memory'; version = '4.5.5' }
    'System.Buffers.dll' = @{ id = 'System.Buffers'; version = '4.5.1' }
    'System.Numerics.Vectors.dll' = @{ id = 'System.Numerics.Vectors'; version = '4.5.0' }
    'System.Runtime.CompilerServices.Unsafe.dll' = @{ id = 'System.Runtime.CompilerServices.Unsafe'; version = '4.5.3' }
}

$files = @()
foreach ($file in (Get-ChildItem -LiteralPath (Join-Path $Root 'lib') -Filter '*.dll' -File | Sort-Object Name)) {
    $assemblyName = $null
    try {
        $assemblyName = [Reflection.AssemblyName]::GetAssemblyName($file.FullName)
    }
    catch {
        # 原生依赖没有托管程序集元数据时，保留文件版本和哈希即可。
    }
    $package = $packageIdentity[$file.Name]
    if ($file.Name.StartsWith('UglyToad.PdfPig', [StringComparison]::Ordinal)) {
        $package = @{ id = 'PdfPig'; version = '0.1.9' }
    }
    if ($file.Name -eq 'Microsoft.Office.Tools.Common.v4.0.Utilities.dll') {
        $package = @{ id = 'Visual Studio Tools for Office Runtime'; version = '10.0.30319.1' }
    }
    $files += [ordered]@{
        file = $file.Name
        sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        size = $file.Length
        managed = $null -ne $assemblyName
        assembly_name = if ($null -ne $assemblyName) { $assemblyName.Name } else { $null }
        assembly_version = if ($null -ne $assemblyName) { $assemblyName.Version.ToString() } else { $null }
        file_version = $file.VersionInfo.FileVersion
        product_version = $file.VersionInfo.ProductVersion
        reconstructed_package_id = if ($null -ne $package) { $package.id } else { $null }
        reconstructed_package_version = if ($null -ne $package) { $package.version } else { $null }
    }
}

$report = [ordered]@{
    schema_version = 1
    generated_at = [DateTimeOffset]::Now.ToString('o')
    source_directory = (Join-Path $Root 'lib')
    notice = (Join-Path $Root 'assets\THIRD-PARTY-NOTICES.txt')
    files = $files
    vulnerability_audit = [ordered]@{
        status = 'partial-binary-reconstruction'
        access_date = '2026-08-28'
        official_sources = @(
            'https://www.nuget.org/packages/PdfPig/0.1.9',
            'https://www.nuget.org/packages/System.Memory/4.5.5',
            'https://www.nuget.org/packages/System.Buffers/4.5.1',
            'https://www.nuget.org/packages/System.Runtime.CompilerServices.Unsafe/4.5.3',
            'https://www.nuget.org/packages/Microsoft.Bcl.HashCode/1.1.1'
        )
        conclusion = '未发现能直接映射到所识别精确版本的 NuGet 高危警告；因原始包锁和还原元数据缺失，不能作为完整的无高危 CVE 保证。'
    }
}

New-Item -ItemType Directory -Path (Split-Path -Parent $OutputPath) -Force | Out-Null
[System.IO.File]::WriteAllText($OutputPath, ($report | ConvertTo-Json -Depth 8) + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
Write-Host "依赖清单已生成：$OutputPath" -ForegroundColor Green
