using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Rules;

namespace DocumentRepository.Services.Rules;

public static class RuleValidationService
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static RuleValidationResult ValidateCatalog(IEnumerable<RuleCatalogItem> items)
	{
		RuleValidationResult ruleValidationResult = new RuleValidationResult();
		if (items != null)
		{
			List<RuleCatalogItem> list = items.ToList();
			if (list.Count == 0)
			{
				ruleValidationResult.Add(RuleValidationIssue.Error("", "Rule catalog must contain at least one item."));
				return ruleValidationResult;
			}
			HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			{
				foreach (RuleCatalogItem item in list)
				{
					if (item == null)
					{
						ruleValidationResult.Add(RuleValidationIssue.Error("", "Rule catalog contains an empty item."));
						continue;
					}
					try
					{
						item.EnsureValid();
					}
					catch (Exception ex)
					{
						ruleValidationResult.Add(RuleValidationIssue.Error(item.Id ?? "", ex.Message));
						continue;
					}
					if (!hashSet.Add(item.Id))
					{
						ruleValidationResult.Add(RuleValidationIssue.Error(item.Id, "Duplicate rule catalog id: " + item.Id));
					}
					if (item.Status == RuleCatalogStatus.NeedsMigration)
					{
						ruleValidationResult.Add(RuleValidationIssue.Warning(item.Id, "Rule source still needs migration: " + item.DisplayName));
					}
				}
				return ruleValidationResult;
			}
		}
		ruleValidationResult.Add(RuleValidationIssue.Error("", "Rule catalog cannot be empty."));
		return ruleValidationResult;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void AssertSuccess(RuleValidationResult result)
	{
		if (result != null)
		{
			if (!result.Success)
			{
				IEnumerable<string> values = from i in result.Issues
					where i.Severity == RuleValidationSeverity.Error
					select (!string.IsNullOrWhiteSpace(i.RuleId)) ? (i.RuleId + ": " + i.Message) : i.Message;
				throw new InvalidOperationException(string.Join(Environment.NewLine, values));
			}
			return;
		}
		throw new InvalidOperationException("Rule validation result cannot be empty.");
	}
}
