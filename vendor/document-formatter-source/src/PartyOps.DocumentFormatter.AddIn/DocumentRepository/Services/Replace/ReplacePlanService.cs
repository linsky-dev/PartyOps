using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Models.Rules;
using DocumentRepository.Services.Configuration;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.Rules;

namespace DocumentRepository.Services.Replace;

public static class ReplacePlanService
{
	private const int CurrentSchemaVersion = 4;

	public const string DefaultPlanId = "default_replace_plan";

	public const string ParenthesizedOrdinalPlanId = "preset_paragraph_ordinal_parenthesized";

	public const string YiShiOrdinalPlanId = "preset_paragraph_ordinal_yishi";

	public const string YiShiToParenthesizedPlanId = "preset_paragraph_yishi_parenthesized";

	public const string EnglishNumberFontPlanId = "preset_english_number_times_new_roman";

	public const string DeleteSpacesPlanId = "preset_delete_spaces";

	private static readonly object LockObj = new object();

	private static readonly string ConfigPath = ApplicationDataPaths.ReplacePlans;

	public static RuleLoadResult<ReplacePlanSet> LastLoadResult { get; private set; }

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static RuleStoreDescriptor GetStoreDescriptor()
	{
		return new RuleStoreDescriptor
		{
			Id = "replace.plans",
			StorePath = ConfigPath,
			OwnerService = "ReplacePlanService",
			ModelTypeName = "ReplacePlanSet"
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static RuleLoadResult<ReplacePlanSet> LoadResult()
	{
		lock (LockObj)
		{
			RuleLoadResult<ReplacePlanSet> ruleLoadResult = XmlRuleStore.LoadWithSelfHealing(GetStoreDescriptor(), CreateDefaultSet, null);
			if (!ruleLoadResult.Usable)
			{
				LastLoadResult = ruleLoadResult;
				return ruleLoadResult;
			}
			try
			{
				ReplacePlanSet value = ruleLoadResult.Value;
				if (value.SchemaVersion < 4)
				{
					ArchiveBeforeSchemaMigration(value.SchemaVersion);
					value = NormalizeSet(value);
					XmlRuleStore.Save(GetStoreDescriptor(), value);
				}
				else
				{
					value = NormalizeSet(value);
				}
				ruleLoadResult.Value = value;
				LastLoadResult = ruleLoadResult;
				return ruleLoadResult;
			}
			catch (Exception ex)
			{
				LogService.Warn("ReplacePlanService normalize failed. StoreId=replace.plans", ex);
				LastLoadResult = RuleLoadResult<ReplacePlanSet>.Fallback(CreateDefaultSet(), "replace.plans", ConfigPath, "rule-store-in-memory-default", ex);
				return LastLoadResult;
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ReplacePlanSet Load()
	{
		RuleLoadResult<ReplacePlanSet> ruleLoadResult = LoadResult();
		if (!ruleLoadResult.Usable)
		{
			throw RuleStoreUnavailableException.From(ruleLoadResult, "替换规则库");
		}
		return ruleLoadResult.Value;
	}

	public static RuleLoadResult<ReplacePlanSet> ResetToDefault()
	{
		lock (LockObj)
		{
			return XmlRuleStore.ResetToDefault(GetStoreDescriptor(), CreateDefaultSet, null);
		}
	}

	public static void Save(ReplacePlanSet set)
	{
		lock (LockObj)
		{
			if (set != null && set.SchemaVersion < 4)
			{
				ArchiveBeforeSchemaMigration(set.SchemaVersion);
			}
			XmlRuleStore.Save(GetStoreDescriptor(), NormalizeSet(set));
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void NormalizeAndPersistStore()
	{
		lock (LockObj)
		{
			RuleLoadResult<ReplacePlanSet> ruleLoadResult = LoadResult();
			if (ruleLoadResult.Usable)
			{
				ReplacePlanSet value = ruleLoadResult.Value;
				if (value.SchemaVersion < 4)
				{
					ArchiveBeforeSchemaMigration(value.SchemaVersion);
				}
				XmlRuleStore.Save(GetStoreDescriptor(), NormalizeSet(value));
				return;
			}
			throw RuleStoreUnavailableException.From(ruleLoadResult, "替换规则库");
		}
	}

	internal static ReplacePlanSet NormalizeImportCandidate(ReplacePlanSet set)
	{
		return NormalizeSet(set);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ReplacePlan GetActivePlan()
	{
		ReplacePlanSet replacePlanSet = Load();
		foreach (ReplacePlan plan in replacePlanSet.Plans)
		{
			if (string.Equals(plan.Id, replacePlanSet.ActivePlanId, StringComparison.OrdinalIgnoreCase))
			{
				return plan.Clone();
			}
		}
		throw new InvalidOperationException("Active replace plan does not exist: " + replacePlanSet.ActivePlanId);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void SetActivePlan(string planId)
	{
		if (string.IsNullOrWhiteSpace(planId))
		{
			throw new ArgumentException("Replace plan id cannot be empty.", "planId");
		}
		ReplacePlanSet replacePlanSet = Load();
		foreach (ReplacePlan plan in replacePlanSet.Plans)
		{
			if (string.Equals(plan.Id, planId, StringComparison.OrdinalIgnoreCase))
			{
				replacePlanSet.ActivePlanId = planId;
				Save(replacePlanSet);
				return;
			}
		}
		throw new InvalidOperationException("Replace plan does not exist: " + planId);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ReplacePlanSet CreateDefaultSet()
	{
		ReplacePlan replacePlan = CreateEnglishPunctuationPlan();
		return new ReplacePlanSet
		{
			SchemaVersion = 4,
			ActivePlanId = replacePlan.Id,
			Plans = new List<ReplacePlan>
			{
				replacePlan,
				CreatePresetPlan("preset_paragraph_ordinal_parenthesized", ReplaceRegexPresetService.CreateParenthesizedPreset()),
				CreatePresetPlan("preset_paragraph_ordinal_yishi", ReplaceRegexPresetService.CreateYiShiPreset()),
				CreatePresetPlan("preset_paragraph_yishi_parenthesized", ReplaceRegexPresetService.CreateYiShiToParenthesizedPreset()),
				CreateEnglishNumberFontPlan(),
				CreateDeleteSpacesPlan()
			}
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ReplacePlanSet NormalizeSet(ReplacePlanSet set)
	{
		if (set == null)
		{
			throw new InvalidOperationException("Replace plan set cannot be empty.");
		}
		if (set.Plans != null)
		{
			if (set.Plans.Count != 0)
			{
				if (set.SchemaVersion < 4)
				{
					AddPresetIfMissing(set, "preset_paragraph_ordinal_parenthesized", ReplaceRegexPresetService.CreateParenthesizedPreset());
					AddPresetIfMissing(set, "preset_paragraph_ordinal_yishi", ReplaceRegexPresetService.CreateYiShiPreset());
					AddPresetIfMissing(set, "preset_paragraph_yishi_parenthesized", ReplaceRegexPresetService.CreateYiShiToParenthesizedPreset());
					AddPlanIfMissing(set, CreateEnglishNumberFontPlan());
					AddPlanIfMissing(set, CreateDeleteSpacesPlan());
					AddPlanIfMissing(set, CreateEnglishPunctuationPlan());
					set.SchemaVersion = 4;
				}
				foreach (ReplacePlan plan in set.Plans)
				{
					NormalizePlan(plan);
				}
				if (string.IsNullOrWhiteSpace(set.ActivePlanId) || !set.Plans.Exists((ReplacePlan p) => string.Equals(p.Id, set.ActivePlanId, StringComparison.OrdinalIgnoreCase)))
				{
					throw new InvalidOperationException("Active replace plan does not exist: " + set.ActivePlanId);
				}
				return set;
			}
			throw new InvalidOperationException("At least one replace plan is required.");
		}
		throw new InvalidOperationException("Replace plan list cannot be empty.");
	}

	private static void AddPresetIfMissing(ReplacePlanSet set, string id, ReplaceRegexPreset preset)
	{
		if (!set.Plans.Exists((ReplacePlan plan) => string.Equals(plan.Id, id, StringComparison.OrdinalIgnoreCase)))
		{
			set.Plans.Add(CreatePresetPlan(id, preset));
		}
	}

	private static ReplacePlan CreatePresetPlan(string id, ReplaceRegexPreset preset)
	{
		return new ReplacePlan
		{
			Id = id,
			Name = preset.Name,
			Rules = new List<ReplaceRule> { ReplaceRegexPresetService.CreateRule(preset) }
		};
	}

	private static void AddPlanIfMissing(ReplacePlanSet set, ReplacePlan candidate)
	{
		ReplacePlanMigrationPolicy.AddIfMissing(set, candidate);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ArchiveBeforeSchemaMigration(int sourceSchemaVersion)
	{
		if (File.Exists(ConfigPath))
		{
			string reason = "schema-" + Math.Max(0, sourceSchemaVersion) + "-to-" + 4;
			XmlRuleStore.ArchiveUnreadableStore(ConfigPath, reason);
			LogService.Info("Replace plan schema migration backup created.");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ReplacePlan CreateEnglishPunctuationPlan()
	{
		return new ReplacePlan
		{
			Id = "default_replace_plan",
			Name = "英文标点替换为中文标点",
			Rules = new List<ReplaceRule>
			{
				CreatePunctuationRule("英文逗号转中文逗号", "(?<=[\\u3400-\\u9fff”’）】》]),|,(?=[\\u3400-\\u9fff“‘（【《])", "，"),
				CreatePunctuationRule("英文句号转中文句号", "(?<=[\\u3400-\\u9fff”’）】》])\\.|\\.(?=[\\u3400-\\u9fff“‘（【《])", "。"),
				CreatePunctuationRule("英文冒号转中文冒号", "(?<=[\\u3400-\\u9fff”’）】》]):|:(?=[\\u3400-\\u9fff“‘（【《])", "："),
				CreatePunctuationRule("英文分号转中文分号", "(?<=[\\u3400-\\u9fff”’）】》]);|;(?=[\\u3400-\\u9fff“‘（【《])", "；"),
				CreatePunctuationRule("英文问号转中文问号", "(?<=[\\u3400-\\u9fff”’）】》])\\?|\\?(?=[\\u3400-\\u9fff“‘（【《])", "？"),
				CreatePunctuationRule("英文叹号转中文叹号", "(?<=[\\u3400-\\u9fff”’）】》])!|!(?=[\\u3400-\\u9fff“‘（【《])", "！")
			}
		};
	}

	private static ReplaceRule CreatePunctuationRule(string name, string pattern, string replacement)
	{
		return new ReplaceRule
		{
			Name = name,
			Enabled = true,
			FindText = pattern,
			UseRegex = true,
			ReplaceText = replacement,
			FindFormat = new ReplaceFormatCondition(),
			ReplaceFormat = new ReplaceFormatTarget()
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ReplacePlan CreateEnglishNumberFontPlan()
	{
		return new ReplacePlan
		{
			Id = "preset_english_number_times_new_roman",
			Name = "英文和数字统一为新罗马字体",
			Rules = new List<ReplaceRule>
			{
				new ReplaceRule
				{
					Name = "英文和数字统一为 Times New Roman",
					Enabled = true,
					FindText = "[A-Za-z0-9]+",
					UseRegex = true,
					ReplaceText = "$0",
					FindFormat = new ReplaceFormatCondition(),
					ReplaceFormat = new ReplaceFormatTarget
					{
						FontName = "Times New Roman"
					}
				}
			}
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ReplacePlan CreateDeleteSpacesPlan()
	{
		return new ReplacePlan
		{
			Id = "preset_delete_spaces",
			Name = "删除空格",
			Rules = new List<ReplaceRule>
			{
				new ReplaceRule
				{
					Name = "删除半角和全角空格",
					Enabled = true,
					FindText = "[ \\u3000]+",
					UseRegex = true,
					ReplaceText = string.Empty,
					FindFormat = new ReplaceFormatCondition(),
					ReplaceFormat = new ReplaceFormatTarget()
				}
			}
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void NormalizePlan(ReplacePlan plan)
	{
		if (plan == null)
		{
			throw new InvalidOperationException("Replace plan cannot be empty.");
		}
		if (!string.IsNullOrWhiteSpace(plan.Id))
		{
			if (string.IsNullOrWhiteSpace(plan.Name))
			{
				throw new InvalidOperationException("Replace plan name cannot be empty.");
			}
			if (plan.Rules == null)
			{
				plan.Rules = new List<ReplaceRule>();
			}
			{
				foreach (ReplaceRule rule in plan.Rules)
				{
					ReplaceRuleNormalizer.Normalize(rule ?? throw new InvalidOperationException("Replace rule cannot be empty."));
				}
				return;
			}
		}
		throw new InvalidOperationException("Replace plan id cannot be empty.");
	}
}
