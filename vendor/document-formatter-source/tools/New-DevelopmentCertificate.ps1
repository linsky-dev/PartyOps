param(
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'certificates\development'),
    [string]$Password = 'partyops.documentformatter-development-only'
)

$ErrorActionPreference = 'Stop'
$pfxPath = Join-Path $OutputDirectory 'PartyOps.DocumentFormatter.Development.pfx'
$cerPath = Join-Path $OutputDirectory 'PartyOps.DocumentFormatter.Development.cer'

if ((Test-Path -LiteralPath $pfxPath) -and (Test-Path -LiteralPath $cerPath)) {
    $existing = $null
    try {
        $existing = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
            $pfxPath,
            $Password,
            [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet
        )
        if ($existing.HasPrivateKey -and $existing.NotAfter.ToUniversalTime() -gt [DateTime]::UtcNow.AddDays(30)) {
            Write-Host "复用开发签名证书：$($existing.Thumbprint)" -ForegroundColor Green
            return [pscustomobject]@{
                PfxPath = $pfxPath
                CerPath = $cerPath
                Password = $Password
                Thumbprint = $existing.Thumbprint
                Subject = $existing.Subject
                NotAfter = $existing.NotAfter
            }
        }
    }
    catch [System.Security.Cryptography.CryptographicException] {
        # 恢复工程可能遗留使用旧产品口令生成的开发证书；开发证书可安全再生，
        # 因此密码不匹配时不让发布流程中断，直接在原位置生成 PartyOps 证书。
        Write-Warning "现有开发证书无法使用当前口令读取，将重新生成：$pfxPath"
    }
    finally {
        if ($null -ne $existing) {
            $existing.Dispose()
        }
    }
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$rsa = [System.Security.Cryptography.RSA]::Create(3072)
try {
    $request = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
        'CN=PartyOps.DocumentFormatter Development Publisher, O=PartyOps.DocumentFormatter Recovery',
        $rsa,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1
    )
    $request.CertificateExtensions.Add(
        [System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false, $false, 0, $true)
    )
    $request.CertificateExtensions.Add(
        [System.Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new(
            [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature,
            $true
        )
    )
    $enhancedKeyUsages = [System.Security.Cryptography.OidCollection]::new()
    [void]$enhancedKeyUsages.Add([System.Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.3', 'Code Signing'))
    $request.CertificateExtensions.Add(
        [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($enhancedKeyUsages, $true)
    )
    $request.CertificateExtensions.Add(
        [System.Security.Cryptography.X509Certificates.X509SubjectKeyIdentifierExtension]::new($request.PublicKey, $false)
    )

    $certificate = $request.CreateSelfSigned([DateTimeOffset]::Now.AddDays(-1), [DateTimeOffset]::Now.AddYears(3))
    [System.IO.File]::WriteAllBytes(
        $pfxPath,
        $certificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $Password)
    )
    [System.IO.File]::WriteAllBytes(
        $cerPath,
        $certificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert)
    )

    Write-Warning '已生成自签名开发证书。它仅用于本机恢复验收，不代表原发布者身份，也不应作为对外生产证书。'
    [pscustomobject]@{
        PfxPath = $pfxPath
        CerPath = $cerPath
        Password = $Password
        Thumbprint = $certificate.Thumbprint
        Subject = $certificate.Subject
        NotAfter = $certificate.NotAfter
    }
}
finally {
    $rsa.Dispose()
}
