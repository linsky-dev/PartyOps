using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Rename;
using DocumentRepository.Models.Rules;
using DocumentRepository.Services.Configuration;
using DocumentRepository.Services.Rules;

namespace DocumentRepository;

public static class RenameRuleManager
{
	public class RotateWordSelection
	{
		public string Word;

		public int NextIndex;
	}

	private static readonly object LockObj = new object();

	private static readonly string DefaultConfigPath = ApplicationDataPaths.RenameRules;

	private static string configPathOverrideForTesting;

	private static readonly HashSet<string> RotateResetApplied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private static string ConfigPath => configPathOverrideForTesting ?? DefaultConfigPath;

	public static RuleLoadResult<RenameRuleSet> LastLoadResult { get; private set; }

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static void SetConfigPathOverrideForTesting(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			configPathOverrideForTesting = null;
			return;
		}
		string fullPath = Path.GetFullPath(path);
		string fullPath2 = Path.GetFullPath(Path.GetTempPath());
		string value = (fullPath2.EndsWith("\\", StringComparison.Ordinal) ? fullPath2 : (fullPath2 + "\\"));
		if (!fullPath.StartsWith(value, StringComparison.OrdinalIgnoreCase) || !string.Equals(Path.GetExtension(fullPath), ".xml", StringComparison.OrdinalIgnoreCase))
		{
			throw new ArgumentException("测试命名规则路径必须是系统临时目录内的 XML 文件。", "path");
		}
		configPathOverrideForTesting = fullPath;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static RuleStoreDescriptor GetStoreDescriptor()
	{
		return new RuleStoreDescriptor
		{
			Id = "rename.rules",
			StorePath = ConfigPath,
			OwnerService = "RenameRuleManager",
			ModelTypeName = "RenameRuleSet"
		};
	}

	public static RuleLoadResult<RenameRuleSet> LoadResult()
	{
		lock (LockObj)
		{
			LastLoadResult = XmlRuleStore.LoadWithSelfHealing(GetStoreDescriptor(), CreateDefaultSet, NormalizeSet);
			return LastLoadResult;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static RenameRuleSet Load()
	{
		RuleLoadResult<RenameRuleSet> ruleLoadResult = LoadResult();
		if (!ruleLoadResult.Usable)
		{
			throw RuleStoreUnavailableException.From(ruleLoadResult, "命名规则库");
		}
		return ruleLoadResult.Value;
	}

	public static RuleLoadResult<RenameRuleSet> ResetToDefault()
	{
		lock (LockObj)
		{
			return XmlRuleStore.ResetToDefault(GetStoreDescriptor(), CreateDefaultSet, NormalizeSet);
		}
	}

	public static void Save(RenameRuleSet set)
	{
		lock (LockObj)
		{
			XmlRuleStore.Save(GetStoreDescriptor(), set, NormalizeSet);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void NormalizeAndPersistStore()
	{
		lock (LockObj)
		{
			RuleLoadResult<RenameRuleSet> ruleLoadResult = LoadResult();
			if (!ruleLoadResult.Usable)
			{
				throw RuleStoreUnavailableException.From(ruleLoadResult, "命名规则库");
			}
			XmlRuleStore.Save(GetStoreDescriptor(), ruleLoadResult.Value, NormalizeSet);
		}
	}

	internal static RenameRuleSet NormalizeImportCandidate(RenameRuleSet set)
	{
		return NormalizeSet(set);
	}

	public static RenameRule GetActiveRule()
	{
		RenameRuleSet renameRuleSet = Load();
		foreach (RenameRule rule in renameRuleSet.Rules)
		{
			if (string.Equals(rule.Id, renameRuleSet.ActiveRuleId, StringComparison.OrdinalIgnoreCase))
			{
				return rule.Clone();
			}
		}
		throw RenameOperationException.Create(RenameFailureReasonCode.ActiveRuleMissing, RenameFailureStage.RuleLoad);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void SetActiveRule(string ruleId)
	{
		if (!string.IsNullOrWhiteSpace(ruleId))
		{
			RenameRuleSet renameRuleSet = Load();
			bool flag = false;
			foreach (RenameRule rule in renameRuleSet.Rules)
			{
				if (string.Equals(rule.Id, ruleId, StringComparison.OrdinalIgnoreCase))
				{
					flag = true;
					break;
				}
			}
			if (!flag)
			{
				throw new InvalidOperationException("Rename rule does not exist: " + ruleId);
			}
			renameRuleSet.ActiveRuleId = ruleId;
			Save(renameRuleSet);
			return;
		}
		throw new ArgumentException("Rename rule id cannot be empty.", "ruleId");
	}

	public static RotateWordSelection ResolveRotateWord(RenameRule rule)
	{
		if (rule != null && !string.IsNullOrWhiteSpace(rule.RotateWords))
		{
			List<string> list = ParseRotateWords(rule.RotateWords);
			if (list.Count != 0)
			{
				int num = rule.RotateIndex;
				if (rule.ResetRotateOnStartup && !RotateResetApplied.Contains(rule.Id))
				{
					num = 0;
					RotateResetApplied.Add(rule.Id);
				}
				if (num < 0)
				{
					num = 0;
				}
				num %= list.Count;
				return new RotateWordSelection
				{
					Word = list[num],
					NextIndex = (num + 1) % list.Count
				};
			}
			return null;
		}
		return null;
	}

	public static void CommitRotateWord(RenameRule rule, RotateWordSelection selection)
	{
		if (rule != null && selection != null)
		{
			UpdateRuleRotateIndex(rule.Id, selection.NextIndex);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void UpdateRuleRotateIndex(string ruleId, int nextIndex)
	{
		if (string.IsNullOrWhiteSpace(ruleId))
		{
			throw new ArgumentException("Rename rule id cannot be empty.", "ruleId");
		}
		RenameRuleSet renameRuleSet = Load();
		foreach (RenameRule rule in renameRuleSet.Rules)
		{
			if (rule != null && string.Equals(rule.Id, ruleId, StringComparison.OrdinalIgnoreCase))
			{
				rule.RotateIndex = Math.Max(0, nextIndex);
				Save(renameRuleSet);
				return;
			}
		}
		throw new InvalidOperationException("Rename rule does not exist: " + ruleId);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static RenameRuleSet CreateDefaultSet()
	{
		RenameRule renameRule = new RenameRule
		{
			Id = "default_main_title",
			Name = "主标题",
			RenameMode = "online",
			Parts = new List<RenameRulePart>
			{
				new RenameRulePart("mainTitle")
			}
		};
		RenameRule item = new RenameRule
		{
			Id = "default_docnum_main_title",
			Name = "发文字号+主标题",
			RenameMode = "online",
			Parts = new List<RenameRulePart>
			{
				new RenameRulePart("docNumber"),
				new RenameRulePart("mainTitle")
			}
		};
		RenameRule item2 = new RenameRule
		{
			Id = "default_main_title_date",
			Name = "主标题+时间",
			RenameMode = "online",
			DateFormat = "yyyy.MM.dd",
			Parts = new List<RenameRulePart>
			{
				new RenameRulePart("mainTitle"),
				new RenameRulePart("date")
			}
		};
		return new RenameRuleSet
		{
			ActiveRuleId = renameRule.Id,
			Rules = new List<RenameRule> { renameRule, item, item2 }
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool ValidateRule(RenameRule rule, out string message)
	{
		message = "";
		if (rule == null)
		{
			message = "规则不能为空。";
			return false;
		}
		if (string.IsNullOrWhiteSpace(rule.Name))
		{
			message = "规则名称不能为空。";
			return false;
		}
		EnsureRuleParts(rule);
		if (rule.Parts == null || rule.Parts.Count == 0)
		{
			message = "规则至少需要包含一个命名部件。";
			return false;
		}
		foreach (RenameRulePart part in rule.Parts)
		{
			if (part == null || string.IsNullOrWhiteSpace(part.Type))
			{
				message = "规则中存在无效部件，请删除后重新添加。";
				return false;
			}
			if (!string.Equals(part.Type, "custom", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(part.Text))
			{
				if (!IsValidFilenameText(part.Text))
				{
					message = "自定义文字包含不能用于文件名的字符，请删除 \\ / : * ? \" < > | 等符号。";
					return false;
				}
				continue;
			}
			message = "自定义部件不能为空。";
			return false;
		}
		if (RuleHasPart(rule, "date") && string.IsNullOrWhiteSpace(rule.DateFormat))
		{
			message = "启用时间后，请填写时间格式。";
			return false;
		}
		if (RuleHasPart(rule, "date"))
		{
			try
			{
				DateTime.Now.ToString(rule.DateFormat);
			}
			catch
			{
				message = "时间格式不正确，请使用类似 yyyy.MM.dd 的格式。";
				return false;
			}
		}
		if (RuleHasPart(rule, "rotate"))
		{
			List<string> list = ParseRotateWords(rule.RotateWords);
			if (list.Count == 0)
			{
				message = "命名规则包含轮替词时，请先填写轮替词。";
				return false;
			}
			foreach (string item in list)
			{
				if (!IsValidFilenameText(item))
				{
					message = "轮替词包含不能用于文件名的字符，请删除 \\ / : * ? \" < > | 等符号。";
					return false;
				}
			}
		}
		if (string.Equals(rule.RenameMode, "copy", StringComparison.OrdinalIgnoreCase) && string.Equals(rule.SavePathMode, "custom", StringComparison.OrdinalIgnoreCase))
		{
			if (string.IsNullOrWhiteSpace(rule.CustomSaveDirectory))
			{
				message = "另存重命名选择自定义地址时，请先选择另存路径。";
				return false;
			}
			if (!Directory.Exists(rule.CustomSaveDirectory))
			{
				message = "另存路径不存在，请重新选择。";
				return false;
			}
		}
		return true;
	}

	public static List<string> ParseRotateWords(string text)
	{
		List<string> list = new List<string>();
		if (!string.IsNullOrWhiteSpace(text))
		{
			string[] array = text.Split(new char[1] { '、' }, StringSplitOptions.RemoveEmptyEntries);
			for (int i = 0; i < array.Length; i++)
			{
				string text2 = (array[i] ?? "").Trim();
				if (text2.Length > 0)
				{
					list.Add(text2);
				}
			}
			return list;
		}
		return list;
	}

	private static bool IsValidFilenameText(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return true;
		}
		char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
		foreach (char c in text)
		{
			char[] array = invalidFileNameChars;
			foreach (char c2 in array)
			{
				if (c == c2)
				{
					return false;
				}
			}
			if (char.IsControl(c))
			{
				return false;
			}
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static RenameRuleSet NormalizeSet(RenameRuleSet set)
	{
		if (set == null)
		{
			throw new InvalidOperationException("Rename rule set cannot be empty.");
		}
		if (set.Rules == null)
		{
			throw new InvalidOperationException("Rename rule list cannot be empty.");
		}
		if (set.Rules.Count == 0)
		{
			throw new InvalidOperationException("At least one rename rule is required.");
		}
		foreach (RenameRule rule in set.Rules)
		{
			if (rule != null)
			{
				if (string.IsNullOrWhiteSpace(rule.Id))
				{
					rule.Id = Guid.NewGuid().ToString("N");
				}
				if (string.IsNullOrWhiteSpace(rule.Name))
				{
					rule.Name = "未命名规则";
				}
				if (string.IsNullOrWhiteSpace(rule.DateFormat))
				{
					rule.DateFormat = "yyyy.MM.dd";
				}
				if (!string.Equals(rule.RenameMode, "copy", StringComparison.OrdinalIgnoreCase) && !string.Equals(rule.RenameMode, "online", StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidOperationException("Invalid rename mode: " + rule.RenameMode);
				}
				if (string.Equals(rule.SavePathMode, "custom", StringComparison.OrdinalIgnoreCase) || string.Equals(rule.SavePathMode, "source", StringComparison.OrdinalIgnoreCase))
				{
					if (rule.CustomSaveDirectory == null)
					{
						rule.CustomSaveDirectory = "";
					}
					if (rule.RotateWords == null)
					{
						rule.RotateWords = "";
					}
					if (rule.RotateIndex < 0)
					{
						rule.RotateIndex = 0;
					}
					EnsureRuleParts(rule);
					continue;
				}
				throw new InvalidOperationException("Invalid rename save path mode: " + rule.SavePathMode);
			}
			throw new InvalidOperationException("Rename rule cannot be empty.");
		}
		bool flag = false;
		foreach (RenameRule rule2 in set.Rules)
		{
			if (string.Equals(rule2.Id, set.ActiveRuleId, StringComparison.OrdinalIgnoreCase))
			{
				flag = true;
				break;
			}
		}
		if (!flag)
		{
			throw new InvalidOperationException("Active rename rule does not exist: " + set.ActiveRuleId);
		}
		return set;
	}

	private static bool RuleHasPart(RenameRule rule, string partType)
	{
		if (rule == null || rule.Parts == null)
		{
			return false;
		}
		foreach (RenameRulePart part in rule.Parts)
		{
			if (part != null && string.Equals(part.Type, partType, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void EnsureRuleParts(RenameRule rule)
	{
		if (rule == null)
		{
			throw new ArgumentNullException("rule");
		}
		if (rule.Parts == null)
		{
			rule.Parts = new List<RenameRulePart>();
		}
	}
}
