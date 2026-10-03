using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Serialization;
using DocumentRepository.Services.FileSafety;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.RedHeader;
using DocumentRepository.Services.Replace;
using DocumentRepository.Services.Rules;

namespace DocumentRepository.Services.Configuration;

public static class ConfigurationMigrationService
{
	private const string TargetVersion = "4.4.0";

	private static readonly object SyncRoot = new object();

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ConfigurationMigrationResult Ensure440Migration()
	{
		lock (SyncRoot)
		{
			if (!TryReadValidMarker(out var backupDirectory))
			{
				if (File.Exists(ApplicationDataPaths.Migration440Marker))
				{
					QuarantineInvalidMarker();
				}
				Directory.CreateDirectory(ApplicationDataPaths.Root);
				string text = CreateBackupDirectory();
				ConfigurationMigrationTransaction configurationMigrationTransaction = ConfigurationMigrationTransaction.Capture(text, new string[5]
				{
					ApplicationDataPaths.LegacyFormatConfig,
					ApplicationDataPaths.FormatTemplates,
					ApplicationDataPaths.RenameRules,
					ApplicationDataPaths.RedHeaderTemplates,
					ApplicationDataPaths.ReplacePlans
				});
				try
				{
					int legacyRedHeaderTemplateCount = BackupLegacyRedHeaderTemplates(text);
					SeedFormatTemplatesFromLegacySingleConfigIfNeeded();
					ConfigManager.NormalizeAndPersistStore();
					RenameRuleManager.NormalizeAndPersistStore();
					RedHeaderTemplateService.NormalizeAndPersistStore();
					ReplacePlanService.NormalizeAndPersistStore();
					WriteMarker(text, legacyRedHeaderTemplateCount);
					LogService.Info("ConfigurationMigrationService.Ensure440Migration: 4.4.0 configuration migration completed. Backup=" + text + "; legacyRedHeaderTemplates=" + legacyRedHeaderTemplateCount);
					return new ConfigurationMigrationResult
					{
						AlreadyCompleted = false,
						BackupDirectory = text,
						LegacyRedHeaderTemplateCount = legacyRedHeaderTemplateCount
					};
				}
				catch (Exception ex)
				{
					try
					{
						configurationMigrationTransaction.Rollback();
					}
					catch (Exception ex2)
					{
						LogService.Error("ConfigurationMigrationService.Rollback", ex2);
						throw new AggregateException("4.4.0 configuration migration failed and rollback was incomplete.", ex, ex2);
					}
					LogService.Error("ConfigurationMigrationService.Ensure440Migration", ex);
					throw new InvalidOperationException("4.4.0 configuration migration failed. Original configuration has been restored from backup.", ex);
				}
			}
			return new ConfigurationMigrationResult
			{
				AlreadyCompleted = true,
				BackupDirectory = backupDirectory
			};
		}
	}

	private static void SeedFormatTemplatesFromLegacySingleConfigIfNeeded()
	{
		if (!File.Exists(ApplicationDataPaths.FormatTemplates) && File.Exists(ApplicationDataPaths.LegacyFormatConfig))
		{
			FormatConfig formatConfig = TryReadLegacyFormatConfig();
			if (formatConfig != null)
			{
				TemplateCollection value = new TemplateCollection
				{
					CurrentIndex = 1,
					Templates = new List<FormatConfig>
					{
						CloneFormatConfig(formatConfig),
						CloneFormatConfig(formatConfig),
						CloneFormatConfig(formatConfig)
					}
				};
				XmlRuleStore.Save(ApplicationDataPaths.FormatTemplates, value);
			}
		}
	}

	private static FormatConfig CloneFormatConfig(FormatConfig source)
	{
		XmlSerializer val = new XmlSerializer(typeof(FormatConfig));
		using MemoryStream memoryStream = new MemoryStream();
		val.Serialize((Stream)memoryStream, (object)source);
		memoryStream.Position = 0L;
		return (FormatConfig)val.Deserialize((Stream)memoryStream);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string CreateBackupDirectory()
	{
		Directory.CreateDirectory(ApplicationDataPaths.MigrationRoot);
		string text = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
		string text2 = Path.Combine(ApplicationDataPaths.MigrationRoot, "4.4.0_" + text);
		Directory.CreateDirectory(text2);
		return text2;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int BackupLegacyRedHeaderTemplates(string backupDirectory)
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		AddLegacyTemplateDirectory(hashSet, AppDomain.CurrentDomain.BaseDirectory);
		AddLegacyTemplateDirectory(hashSet, Path.GetDirectoryName(typeof(ConfigurationMigrationService).Assembly.Location));
		string text = Path.Combine(backupDirectory, "LegacyRedHeaderTemplates");
		int num = 0;
		foreach (string item in hashSet)
		{
			if (!Directory.Exists(item))
			{
				continue;
			}
			string[] array = new string[3] { "*.dotx", "*.dot", "*.wpt" };
			foreach (string searchPattern in array)
			{
				string[] files = Directory.GetFiles(item, searchPattern, SearchOption.TopDirectoryOnly);
				foreach (string text2 in files)
				{
					Directory.CreateDirectory(text);
					string text3 = Path.Combine(text, Path.GetFileName(text2));
					if (File.Exists(text3))
					{
						string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(text2);
						string extension = Path.GetExtension(text2);
						text3 = Path.Combine(text, fileNameWithoutExtension + "_" + num + extension);
					}
					File.Copy(text2, text3, overwrite: false);
					num++;
				}
			}
		}
		return num;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void AddLegacyTemplateDirectory(ISet<string> directories, string baseDirectory)
	{
		if (!string.IsNullOrWhiteSpace(baseDirectory))
		{
			directories.Add(Path.Combine(baseDirectory, "RedHeaderTemplates"));
		}
	}

	private static void WriteMarker(string backupDirectory, int legacyRedHeaderTemplateCount)
	{
		AtomicFileService.WriteFileAtomically(ApplicationDataPaths.Migration440Marker, [MethodImpl(MethodImplOptions.NoInlining)] (string tempPath) =>
		{
			File.WriteAllLines(tempPath, new string[4]
			{
				"Version=4.4.0",
				"CompletedAt=" + DateTime.Now.ToString("O", CultureInfo.InvariantCulture),
				"BackupDirectory=" + backupDirectory,
				"LegacyRedHeaderTemplateCount=" + legacyRedHeaderTemplateCount
			}, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		});
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static FormatConfig TryReadLegacyFormatConfig()
	{
		try
		{
			using FileStream fileStream = new FileStream(ApplicationDataPaths.LegacyFormatConfig, FileMode.Open, FileAccess.Read, FileShare.Read);
			return ((FormatConfig)new XmlSerializer(typeof(FormatConfig)).Deserialize((Stream)fileStream)) ?? throw new InvalidDataException("Legacy format configuration is empty.");
		}
		catch (InvalidOperationException error)
		{
			return RecoverInvalidLegacyFormatConfig(error);
		}
		catch (InvalidDataException error2)
		{
			return RecoverInvalidLegacyFormatConfig(error2);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static FormatConfig RecoverInvalidLegacyFormatConfig(Exception error)
	{
		string text = XmlRuleStore.ArchiveUnreadableStore(ApplicationDataPaths.LegacyFormatConfig, "legacy-corrupt");
		LogService.Warn("ConfigurationMigrationService ignored unreadable legacy format configuration. Archive=" + text, error);
		return null;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool TryReadValidMarker(out string backupDirectory)
	{
		backupDirectory = string.Empty;
		if (!File.Exists(ApplicationDataPaths.Migration440Marker))
		{
			return false;
		}
		try
		{
			string a = null;
			string s = null;
			string[] array = File.ReadAllLines(ApplicationDataPaths.Migration440Marker, Encoding.UTF8);
			foreach (string text in array)
			{
				int num = text.IndexOf('=');
				if (num <= 0)
				{
					continue;
				}
				string a2 = text.Substring(0, num).Trim();
				string text2 = text.Substring(num + 1).Trim();
				if (string.Equals(a2, "Version", StringComparison.Ordinal))
				{
					a = text2;
				}
				else if (!string.Equals(a2, "CompletedAt", StringComparison.Ordinal))
				{
					if (string.Equals(a2, "BackupDirectory", StringComparison.Ordinal))
					{
						backupDirectory = text2;
					}
				}
				else
				{
					s = text2;
				}
			}
			DateTime result;
			return string.Equals(a, "4.4.0", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(backupDirectory) && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out result);
		}
		catch (Exception ex)
		{
			LogService.Warn("ConfigurationMigrationService.ReadMarker", ex);
			backupDirectory = string.Empty;
			return false;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void QuarantineInvalidMarker()
	{
		string text = XmlRuleStore.ArchiveUnreadableStore(ApplicationDataPaths.Migration440Marker, "invalid-marker");
		File.Delete(ApplicationDataPaths.Migration440Marker);
		LogService.Warn("ConfigurationMigrationService invalid completion marker quarantined. Archive=" + text);
	}
}
