using System;
using System.Security;
using Microsoft.Build.Framework;
using Microsoft.Build.Tasks.Deployment.ManifestUtilities;
using Microsoft.Build.Utilities;

namespace PartyOps.DocumentFormatter.Build;

/// <summary>
/// 从磁盘 PFX 直接签署 ClickOnce/VSTO 清单，避免把私钥导入 Windows 证书存储区。
/// </summary>
public sealed class SignClickOnceManifestTask : Task
{
    [Required]
    public string ManifestPath { get; set; } = string.Empty;

    [Required]
    public string CertificatePath { get; set; } = string.Empty;

    public string TimestampUrl { get; set; } = string.Empty;

    public override bool Execute()
    {
        try
        {
            string plainPassword = Environment.GetEnvironmentVariable("PARTYOPS_MANIFEST_CERT_PASSWORD") ?? string.Empty;
            using SecureString securePassword = new SecureString();
            foreach (char character in plainPassword)
            {
                securePassword.AppendChar(character);
            }
            securePassword.MakeReadOnly();

            Uri? timestamp = string.IsNullOrWhiteSpace(TimestampUrl) ? null : new Uri(TimestampUrl);
            SecurityUtilities.SignFile(CertificatePath, securePassword, timestamp, ManifestPath);
            Log.LogMessage(MessageImportance.High, "ClickOnce/VSTO 清单已签名：{0}", ManifestPath);
            return true;
        }
        catch (Exception exception)
        {
            Log.LogErrorFromException(exception, true);
            return false;
        }
    }
}
