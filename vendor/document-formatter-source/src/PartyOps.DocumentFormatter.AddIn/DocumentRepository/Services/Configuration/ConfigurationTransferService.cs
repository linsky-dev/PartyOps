using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.Serialization;
using DocumentRepository.Models;
using DocumentRepository.Models.RedHeader;
using DocumentRepository.Services.FileSafety;
using DocumentRepository.Services.Formatting;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.RedHeader;
using DocumentRepository.Services.Replace;
using Microsoft.Win32;

namespace DocumentRepository.Services.Configuration;

public static class ConfigurationTransferService
{
	private sealed class StoreDefinition
	{
		public string Id { get; private set; }

		public string PackagePath { get; private set; }

		public string LocalPath { get; private set; }

		public StoreDefinition(string id, string packagePath, string localPath)
		{
			Id = id;
			PackagePath = packagePath;
			LocalPath = localPath;
		}
	}

	private sealed class StoreSnapshot
	{
		public string LocalPath { get; private set; }

		public string BackupPath { get; private set; }

		public bool Existed { get; private set; }

		public StoreSnapshot(string localPath, string backupPath, bool existed)
		{
			LocalPath = localPath;
			BackupPath = backupPath;
			Existed = existed;
		}
	}

	private sealed class ImportCandidate
	{
		public ConfigurationPackagePayload Payload { get; set; }

		public Dictionary<string, byte[]> NormalizedContents { get; set; }

		public List<string> Warnings { get; set; }
	}

	private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
	{
		public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

		public new bool Equals(object x, object y)
		{
			return x == y;
		}

		bool IEqualityComparer<object>.Equals(object x, object y)
		{
			return this.Equals(x, y);
		}

		public int GetHashCode(object value)
		{
			return RuntimeHelpers.GetHashCode(value);
		}

		int IEqualityComparer<object>.GetHashCode(object value)
		{
			return this.GetHashCode(value);
		}
	}

	private const string ProductId = "partyops.documentformatter.configuration";

	private const string ProductName = "partyops公文排版助手";

	private const int PackageSchemaVersion = 1;

	private const long MaxXmlCharacters = 5242880L;

	private static readonly object SyncRoot = new object();

	private static readonly StoreDefinition[] Stores = new StoreDefinition[4]
	{
		new StoreDefinition("format.templates", "data/Templates.xml", ApplicationDataPaths.FormatTemplates),
		new StoreDefinition("redheader.templates", "data/RedHeaderTemplates.xml", ApplicationDataPaths.RedHeaderTemplates),
		new StoreDefinition("rename.rules", "data/RenameRules.xml", ApplicationDataPaths.RenameRules),
		new StoreDefinition("replace.plans", "data/ReplacePlans.xml", ApplicationDataPaths.ReplacePlans)
	};

	public static event EventHandler ConfigurationImported;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string CreateDefaultFileName()
	{
		return "partyops公文排版助手_参数备份_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".sxpconfig";
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ConfigurationExportResult Export(string packagePath)
	{
		if (string.IsNullOrWhiteSpace(packagePath))
		{
			throw new ArgumentException("请选择参数包保存位置。", "packagePath");
		}
		lock (SyncRoot)
		{
			EnsureStoresReady();
			string currentProductVersion = GetCurrentProductVersion();
			List<KeyValuePair<string, KeyValuePair<string, byte[]>>> list = new List<KeyValuePair<string, KeyValuePair<string, byte[]>>>();
			StoreDefinition[] stores = Stores;
			foreach (StoreDefinition storeDefinition in stores)
			{
				byte[] array = File.ReadAllBytes(storeDefinition.LocalPath);
				if (array.Length == 0)
				{
					throw new InvalidDataException("本机参数文件为空：" + Path.GetFileName(storeDefinition.LocalPath));
				}
				list.Add(new KeyValuePair<string, KeyValuePair<string, byte[]>>(storeDefinition.Id, new KeyValuePair<string, byte[]>(storeDefinition.PackagePath, array)));
			}
			ConfigurationPackagePayload payload = ConfigurationPackageCodec.Create("partyops.documentformatter.configuration", "partyops公文排版助手", currentProductVersion, 1, DateTime.Now, list);
			AtomicFileWriteResult atomicFileWriteResult = AtomicFileService.WriteFileAtomically(packagePath, delegate(string tempPath)
			{
				ConfigurationPackageCodec.Write(tempPath, payload);
			}, delegate(string tempPath)
			{
				ValidatePackageAndBuildCandidate(tempPath);
			});
			LogService.Info("Configuration parameters exported. Path=" + packagePath + "; Length=" + atomicFileWriteResult.Length + "; Version=" + currentProductVersion);
			return new ConfigurationExportResult
			{
				PackagePath = atomicFileWriteResult.TargetPath,
				Length = atomicFileWriteResult.Length,
				ProductVersion = currentProductVersion
			};
		}
	}

	public static ConfigurationImportInspection Inspect(string packagePath)
	{
		lock (SyncRoot)
		{
			ImportCandidate importCandidate = ValidatePackageAndBuildCandidate(packagePath);
			return new ConfigurationImportInspection
			{
				ProductVersion = importCandidate.Payload.Manifest.ProductVersion,
				ExportedAt = ParseExportedAt(importCandidate.Payload.Manifest.ExportedAt),
				Warnings = new List<string>(importCandidate.Warnings)
			};
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ConfigurationImportResult Import(string packagePath)
	{
		lock (SyncRoot)
		{
			ImportCandidate importCandidate = ValidatePackageAndBuildCandidate(packagePath);
			EnsureStoresReady();
			string text = CommitCandidate(importCandidate);
			LogService.Info("Configuration parameters imported. Package=" + packagePath + "; Backup=" + text + "; SourceVersion=" + importCandidate.Payload.Manifest.ProductVersion);
			RaiseConfigurationImported();
			return new ConfigurationImportResult
			{
				ProductVersion = importCandidate.Payload.Manifest.ProductVersion,
				BackupDirectory = text,
				Warnings = new List<string>(importCandidate.Warnings)
			};
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ImportCandidate ValidatePackageAndBuildCandidate(string packagePath)
	{
		ConfigurationPackagePayload configurationPackagePayload;
		try
		{
			configurationPackagePayload = ConfigurationPackageCodec.Read(packagePath);
		}
		catch (InvalidDataException)
		{
			throw;
		}
		catch (Exception innerException)
		{
			throw new InvalidDataException("无法读取参数包。", innerException);
		}
		ValidateManifest(configurationPackagePayload.Manifest);
		ValidateExpectedStores(configurationPackagePayload);
		TemplateCollection templateCollection = DeserializeStrict<TemplateCollection>(configurationPackagePayload.Contents["data/Templates.xml"], "一键排版参数", ConfigManager.NormalizeImportCandidate);
		RedHeaderTemplateSet redHeaderTemplateSet = DeserializeStrict<RedHeaderTemplateSet>(configurationPackagePayload.Contents["data/RedHeaderTemplates.xml"], "一键套红参数", RedHeaderTemplateService.Normalize);
		RenameRuleSet renameRuleSet = DeserializeStrict<RenameRuleSet>(configurationPackagePayload.Contents["data/RenameRules.xml"], "一键命名参数", RenameRuleManager.NormalizeImportCandidate);
		ReplacePlanSet replacePlanSet = DeserializeStrict<ReplacePlanSet>(configurationPackagePayload.Contents["data/ReplacePlans.xml"], "一键替换参数", ReplacePlanService.NormalizeImportCandidate);
		Dictionary<string, byte[]> normalizedContents = new Dictionary<string, byte[]>(StringComparer.Ordinal)
		{
			{
				"data/Templates.xml",
				SerializeXml(templateCollection)
			},
			{
				"data/RedHeaderTemplates.xml",
				SerializeXml(redHeaderTemplateSet)
			},
			{
				"data/RenameRules.xml",
				SerializeXml(renameRuleSet)
			},
			{
				"data/ReplacePlans.xml",
				SerializeXml(replacePlanSet)
			}
		};
		return new ImportCandidate
		{
			Payload = configurationPackagePayload,
			NormalizedContents = normalizedContents,
			Warnings = BuildCompatibilityWarnings(templateCollection, redHeaderTemplateSet, renameRuleSet, replacePlanSet)
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ValidateManifest(ConfigurationPackageManifest manifest)
	{
		if (manifest != null)
		{
			if (!string.Equals(manifest.ProductId, "partyops.documentformatter.configuration", StringComparison.Ordinal))
			{
				throw new InvalidDataException("该文件不是partyops公文排版助手参数包。");
			}
			if (manifest.PackageSchemaVersion != 1)
			{
				throw new InvalidDataException("参数包结构版本不受当前插件支持。");
			}
			if (!string.IsNullOrWhiteSpace(manifest.ProductVersion))
			{
				if (TryParseVersion(manifest.ProductVersion, out var version) && TryParseVersion(GetCurrentProductVersion(), out var version2))
				{
					if (!(version > version2))
					{
						return;
					}
					throw new InvalidDataException("参数包来自更高版本 " + manifest.ProductVersion + "，请先升级partyops公文排版助手后再导入。");
				}
				throw new InvalidDataException("参数包版本号无效。");
			}
			throw new InvalidDataException("参数包未记录来源版本。");
		}
		throw new InvalidDataException("参数包清单为空。");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ValidateExpectedStores(ConfigurationPackagePayload payload)
	{
		if (payload.Manifest.Entries.Count == Stores.Length && payload.Contents.Count == Stores.Length)
		{
			StoreDefinition[] stores = Stores;
			foreach (StoreDefinition store in stores)
			{
				ConfigurationPackageEntry configurationPackageEntry = payload.Manifest.Entries.FirstOrDefault((ConfigurationPackageEntry item) => string.Equals(item.Id, store.Id, StringComparison.OrdinalIgnoreCase));
				if (configurationPackageEntry == null || !string.Equals(configurationPackageEntry.Path, store.PackagePath, StringComparison.Ordinal) || !payload.Contents.ContainsKey(store.PackagePath))
				{
					throw new InvalidDataException("参数包缺少配置：" + store.Id);
				}
			}
			return;
		}
		throw new InvalidDataException("参数包必须完整包含四类插件参数。");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string CommitCandidate(ImportCandidate candidate)
	{
		Directory.CreateDirectory(ApplicationDataPaths.ImportBackupRoot);
		string text = Path.Combine(ApplicationDataPaths.ImportBackupRoot, DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
		Directory.CreateDirectory(text);
		List<StoreSnapshot> list = new List<StoreSnapshot>();
		StoreDefinition[] stores = Stores;
		foreach (StoreDefinition storeDefinition in stores)
		{
			bool flag = File.Exists(storeDefinition.LocalPath);
			string text2 = Path.Combine(text, Path.GetFileName(storeDefinition.LocalPath));
			if (flag)
			{
				File.Copy(storeDefinition.LocalPath, text2, overwrite: false);
			}
			list.Add(new StoreSnapshot(storeDefinition.LocalPath, text2, flag));
		}
		try
		{
			stores = Stores;
			foreach (StoreDefinition store in stores)
			{
				byte[] bytes = candidate.NormalizedContents[store.PackagePath];
				AtomicFileService.WriteFileAtomically(store.LocalPath, delegate(string tempPath)
				{
					File.WriteAllBytes(tempPath, bytes);
				}, delegate(string tempPath)
				{
					ValidateCommittedStore(store.PackagePath, File.ReadAllBytes(tempPath));
				});
			}
			ReloadCommittedConfiguration();
			return text;
		}
		catch (Exception ex)
		{
			try
			{
				Rollback(list);
				ReloadCommittedConfiguration();
			}
			catch (Exception ex2)
			{
				LogService.Error("Configuration import rollback failed. Backup=" + text, ex2);
				throw new AggregateException("参数导入失败，并且自动恢复未完整完成。原参数备份位于：" + text, ex, ex2);
			}
			LogService.Error("Configuration import failed and was rolled back. Backup=" + text, ex);
			throw new InvalidOperationException("参数导入失败，本机原参数已自动恢复。", ex);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ValidateCommittedStore(string packagePath, byte[] bytes)
	{
		if (string.Equals(packagePath, "data/Templates.xml", StringComparison.Ordinal))
		{
			DeserializeStrict<TemplateCollection>(bytes, "一键排版参数", ConfigManager.NormalizeImportCandidate);
		}
		else if (!string.Equals(packagePath, "data/RedHeaderTemplates.xml", StringComparison.Ordinal))
		{
			if (!string.Equals(packagePath, "data/RenameRules.xml", StringComparison.Ordinal))
			{
				if (!string.Equals(packagePath, "data/ReplacePlans.xml", StringComparison.Ordinal))
				{
					throw new InvalidDataException("未知参数文件：" + packagePath);
				}
				DeserializeStrict<ReplacePlanSet>(bytes, "一键替换参数", ReplacePlanService.NormalizeImportCandidate);
			}
			else
			{
				DeserializeStrict<RenameRuleSet>(bytes, "一键命名参数", RenameRuleManager.NormalizeImportCandidate);
			}
		}
		else
		{
			DeserializeStrict<RedHeaderTemplateSet>(bytes, "一键套红参数", RedHeaderTemplateService.Normalize);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void Rollback(IEnumerable<StoreSnapshot> snapshots)
	{
		List<Exception> list = new List<Exception>();
		foreach (StoreSnapshot snapshot in snapshots)
		{
			try
			{
				if (snapshot.Existed)
				{
					AtomicFileService.WriteFileAtomically(snapshot.LocalPath, delegate(string tempPath)
					{
						File.Copy(snapshot.BackupPath, tempPath, overwrite: false);
					});
				}
				else if (File.Exists(snapshot.LocalPath))
				{
					File.Delete(snapshot.LocalPath);
				}
			}
			catch (Exception innerException)
			{
				list.Add(new IOException("无法恢复参数文件：" + snapshot.LocalPath, innerException));
			}
		}
		if (list.Count > 0)
		{
			throw new AggregateException("恢复本机原参数失败。", list);
		}
	}

	private static void EnsureStoresReady()
	{
		Directory.CreateDirectory(ApplicationDataPaths.Root);
		ConfigManager.NormalizeAndPersistStore();
		RedHeaderTemplateService.NormalizeAndPersistStore();
		RenameRuleManager.NormalizeAndPersistStore();
		ReplacePlanService.NormalizeAndPersistStore();
	}

	private static void ReloadCommittedConfiguration()
	{
		ConfigManager.Reload();
		RedHeaderTemplateService.Load();
		RenameRuleManager.Load();
		ReplacePlanService.Load();
		DocumentStyleManager.InvalidateCache();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static T DeserializeStrict<T>(byte[] bytes, string displayName, Func<T, T> normalize)
	{
		if (bytes == null || bytes.Length == 0)
		{
			throw new InvalidDataException(displayName + "为空。");
		}
		try
		{
			XmlReaderSettings val = new XmlReaderSettings
			{
				DtdProcessing = (DtdProcessing)0,
				XmlResolver = null,
				MaxCharactersInDocument = 5242880L,
				MaxCharactersFromEntities = 0L,
				CloseInput = false
			};
			using MemoryStream memoryStream = new MemoryStream(bytes, writable: false);
			XmlReader val2 = XmlReader.Create((Stream)memoryStream, val);
			try
			{
				T val3 = (T)new XmlSerializer(typeof(T)).Deserialize(val2);
				if (val3 != null)
				{
					return (normalize == null) ? val3 : normalize(val3);
				}
				throw new InvalidDataException(displayName + "为空。");
			}
			finally
			{
				((IDisposable)val2)?.Dispose();
			}
		}
		catch (InvalidDataException)
		{
			throw;
		}
		catch (Exception innerException)
		{
			throw new InvalidDataException(displayName + "无法读取或参数不合法。", innerException);
		}
	}

	private static byte[] SerializeXml<T>(T value)
	{
		using MemoryStream memoryStream = new MemoryStream();
		new XmlSerializer(typeof(T)).Serialize((Stream)memoryStream, (object)value);
		return memoryStream.ToArray();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static List<string> BuildCompatibilityWarnings(TemplateCollection templates, RedHeaderTemplateSet redHeaders, RenameRuleSet renameRules, ReplacePlanSet replacePlans)
	{
		List<string> list = new List<string>();
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		HashSet<object> visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
		CollectFontNames(templates, hashSet, visited);
		CollectFontNames(redHeaders, hashSet, visited);
		CollectFontNames(replacePlans, hashSet, visited);
		try
		{
			HashSet<string> installedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			InstalledFontCollection val = new InstalledFontCollection();
			try
			{
				FontFamily[] families = ((FontCollection)val).Families;
				foreach (FontFamily val2 in families)
				{
					installedNames.Add(val2.Name);
				}
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
			AddRegistryFontNames(Registry.LocalMachine, installedNames);
			AddRegistryFontNames(Registry.CurrentUser, installedNames);
			string[] array = hashSet.Where((string name) => !IsFontSentinel(name) && !installedNames.Contains(name)).OrderBy<string, string>((string name) => name, StringComparer.CurrentCultureIgnoreCase).ToArray();
			if (array.Length != 0)
			{
				list.Add("本机未安装以下参数字体：" + string.Join("、", array));
			}
		}
		catch (Exception ex)
		{
			LogService.Warn("ConfigurationTransferService.CheckInstalledFonts", ex);
		}
		string[] array2 = (from rule in renameRules.Rules
			where rule != null && string.Equals(rule.SavePathMode, "custom", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(rule.CustomSaveDirectory) && !Directory.Exists(rule.CustomSaveDirectory)
			select rule.CustomSaveDirectory.Trim()).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToArray();
		if (array2.Length != 0)
		{
			list.Add("以下一键命名保存路径在本机不存在：" + string.Join("；", array2));
		}
		return list;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void AddRegistryFontNames(RegistryKey root, ISet<string> result)
	{
		if (root == null || result == null)
		{
			return;
		}
		try
		{
			using RegistryKey registryKey = root.OpenSubKey("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Fonts", writable: false);
			if (registryKey == null)
			{
				return;
			}
			string[] valueNames = registryKey.GetValueNames();
			for (int i = 0; i < valueNames.Length; i++)
			{
				string text = valueNames[i] ?? string.Empty;
				int num = text.LastIndexOf(" (", StringComparison.Ordinal);
				if (num > 0)
				{
					text = text.Substring(0, num);
				}
				text = text.Trim();
				if (text.Length == 0)
				{
					continue;
				}
				result.Add(text);
				string[] array = text.Split(new char[1] { '&' }, StringSplitOptions.RemoveEmptyEntries);
				for (int j = 0; j < array.Length; j++)
				{
					string text2 = array[j].Trim();
					if (text2.Length > 0)
					{
						result.Add(text2);
					}
				}
			}
		}
		catch (Exception ex)
		{
			LogService.Warn("ConfigurationTransferService.ReadInstalledFontsRegistry", ex);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool IsFontSentinel(string name)
	{
		if (!string.Equals(name, "不限", StringComparison.OrdinalIgnoreCase) && !string.Equals(name, "不修改", StringComparison.OrdinalIgnoreCase) && !string.Equals(name, "默认", StringComparison.OrdinalIgnoreCase))
		{
			return string.Equals(name, "跟随原格式", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void CollectFontNames(object value, ISet<string> result, ISet<object> visited)
	{
		if (value == null)
		{
			return;
		}
		Type type = value.GetType();
		if (type == typeof(string) || type.IsPrimitive || type.IsEnum || type.IsValueType || !visited.Add(value))
		{
			return;
		}
		if (value is IEnumerable enumerable)
		{
			{
				foreach (object item in enumerable)
				{
					CollectFontNames(item, result, visited);
				}
				return;
			}
		}
		PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
		foreach (PropertyInfo propertyInfo in properties)
		{
			if (!propertyInfo.CanRead || propertyInfo.GetIndexParameters().Length != 0)
			{
				continue;
			}
			object value2;
			try
			{
				value2 = propertyInfo.GetValue(value, null);
			}
			catch
			{
				continue;
			}
			if (propertyInfo.PropertyType == typeof(string) && (propertyInfo.Name.EndsWith("FontName", StringComparison.OrdinalIgnoreCase) || propertyInfo.Name.EndsWith("Font", StringComparison.OrdinalIgnoreCase)))
			{
				string text = ((value2 as string) ?? string.Empty).Trim();
				if (text.Length > 0)
				{
					result.Add(text);
				}
			}
			else
			{
				CollectFontNames(value2, result, visited);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void RaiseConfigurationImported()
	{
		EventHandler configurationImported = ConfigurationTransferService.ConfigurationImported;
		if (configurationImported == null)
		{
			return;
		}
		Delegate[] invocationList = configurationImported.GetInvocationList();
		for (int i = 0; i < invocationList.Length; i++)
		{
			EventHandler eventHandler = (EventHandler)invocationList[i];
			try
			{
				eventHandler(null, EventArgs.Empty);
			}
			catch (Exception ex)
			{
				LogService.Warn("ConfigurationTransferService.ConfigurationImported", ex);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string GetCurrentProductVersion()
	{
		Assembly assembly = typeof(ConfigurationTransferService).Assembly;
		AssemblyInformationalVersionAttribute assemblyInformationalVersionAttribute = assembly.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), inherit: false).OfType<AssemblyInformationalVersionAttribute>().FirstOrDefault();
		if (assemblyInformationalVersionAttribute == null || string.IsNullOrWhiteSpace(assemblyInformationalVersionAttribute.InformationalVersion))
		{
			Version version = assembly.GetName().Version;
			if (!(version == null))
			{
				return version.ToString();
			}
			return "0.0.0";
		}
		return assemblyInformationalVersionAttribute.InformationalVersion.Trim();
	}

	private static bool TryParseVersion(string text, out Version version)
	{
		version = null;
		string text2 = (text ?? string.Empty).Trim();
		int num = text2.IndexOfAny(new char[2] { '-', '+' });
		if (num >= 0)
		{
			text2 = text2.Substring(0, num);
		}
		return Version.TryParse(text2, out version);
	}

	private static DateTime? ParseExportedAt(string text)
	{
		if (!DateTime.TryParse(text, null, DateTimeStyles.RoundtripKind, out var result))
		{
			return null;
		}
		return result.ToLocalTime();
	}
}
