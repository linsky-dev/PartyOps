using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Services.Configuration;

public static class ApplicationDataPaths
{
	public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DocumentRepository");

	public static readonly string LegacyFormatConfig = Path.Combine(Root, "FormatConfig.xml");

	public static readonly string FormatTemplates = Path.Combine(Root, "Templates.xml");

	public static readonly string RenameRules = Path.Combine(Root, "RenameRules.xml");

	public static readonly string RedHeaderTemplates = Path.Combine(Root, "RedHeaderTemplates.xml");

	public static readonly string ReplacePlans = Path.Combine(Root, "ReplacePlans.xml");

	public static readonly string MigrationRoot = Path.Combine(Root, "MigrationBackups");

	public static readonly string ImportBackupRoot = Path.Combine(Root, "ImportBackups");

	public static readonly string Migration440Marker = Path.Combine(Root, "Migration_4.4.0.completed");

	private static readonly string DefaultRecoveryRoot = Path.Combine(Root, "Recovery");

	private static string recoveryRootOverride;

	private static readonly string DefaultReuseLogRoot = Path.Combine(DefaultRecoveryRoot, "reuse-log");

	private static string reuseLogRootOverride;

	private static readonly string DefaultRecoveryFallbackRoot = Path.Combine(Root, "RecoveryFallback");

	private static string recoveryFallbackRootOverride;

	public static string RecoveryRoot => recoveryRootOverride ?? DefaultRecoveryRoot;

	public static string ReuseLogRoot => reuseLogRootOverride ?? DefaultReuseLogRoot;

	public static string RecoveryFallbackRoot => recoveryFallbackRootOverride ?? DefaultRecoveryFallbackRoot;

	internal static void SetRecoveryRootOverrideForTesting(string path)
	{
		recoveryRootOverride = path;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string RecoveryDirectory(string recoveryId)
	{
		if (!string.IsNullOrWhiteSpace(recoveryId))
		{
			string text = recoveryId.Trim();
			string text2 = text;
			foreach (char c in text2)
			{
				if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
				{
					throw new ArgumentException("恢复标识含非法字符，拒绝拼接受控目录。", "recoveryId");
				}
			}
			string fullPath = Path.GetFullPath(Path.Combine(RecoveryRoot, text));
			string fullPath2 = Path.GetFullPath(RecoveryRoot);
			if (!IsControlledChildPath(fullPath2, fullPath))
			{
				throw new ArgumentException("恢复标识导致目录越界，拒绝拼接受控目录。", "recoveryId");
			}
			return fullPath;
		}
		throw new ArgumentException("恢复标识不能为空。", "recoveryId");
	}

	internal static void SetReuseLogRootOverrideForTesting(string path)
	{
		reuseLogRootOverride = path;
	}

	internal static void SetRecoveryFallbackRootOverrideForTesting(string path)
	{
		recoveryFallbackRootOverride = path;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string RecoveryFallbackDirectory(string recoveryId)
	{
		if (!string.IsNullOrWhiteSpace(recoveryId))
		{
			string text = recoveryId.Trim();
			string text2 = text;
			foreach (char c in text2)
			{
				if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
				{
					throw new ArgumentException("恢复标识含非法字符，拒绝拼接受控目录。", "recoveryId");
				}
			}
			string fullPath = Path.GetFullPath(Path.Combine(RecoveryFallbackRoot, text));
			string fullPath2 = Path.GetFullPath(RecoveryFallbackRoot);
			if (!IsControlledChildPath(fullPath2, fullPath))
			{
				throw new ArgumentException("恢复标识导致目录越界，拒绝拼接受控目录。", "recoveryId");
			}
			return fullPath;
		}
		throw new ArgumentException("恢复标识不能为空。", "recoveryId");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string ReuseLogDirectory(string recoveryId)
	{
		if (string.IsNullOrWhiteSpace(recoveryId))
		{
			throw new ArgumentException("恢复标识不能为空。", "recoveryId");
		}
		string text = recoveryId.Trim();
		string text2 = text;
		foreach (char c in text2)
		{
			if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
			{
				throw new ArgumentException("恢复标识含非法字符，拒绝拼接受控目录。", "recoveryId");
			}
		}
		string fullPath = Path.GetFullPath(Path.Combine(ReuseLogRoot, text));
		string fullPath2 = Path.GetFullPath(ReuseLogRoot);
		if (!IsControlledChildPath(fullPath2, fullPath))
		{
			throw new ArgumentException("恢复标识导致目录越界，拒绝拼接受控目录。", "recoveryId");
		}
		return fullPath;
	}

	private static bool IsControlledChildPath(string rootPath, string candidatePath)
	{
		char directorySeparatorChar = Path.DirectorySeparatorChar;
		char altDirectorySeparatorChar = Path.AltDirectorySeparatorChar;
		string text = rootPath;
		if (!text.EndsWith(directorySeparatorChar.ToString(), StringComparison.Ordinal) && !text.EndsWith(altDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
		{
			text += directorySeparatorChar;
		}
		StringComparison comparisonType = ((directorySeparatorChar == '\\') ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
		return candidatePath.StartsWith(text, comparisonType);
	}
}
