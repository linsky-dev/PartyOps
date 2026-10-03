using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using DocumentRepository.Models;
using DocumentRepository.Models.Replace;

namespace DocumentRepository.Services.Replace;

public static class ReplaceStagePlanner
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ReplaceStagePlan Plan(int ruleIndex, string ruleName, ReplaceRule rule, string scopeText)
	{
		if (rule == null)
		{
			throw new ArgumentNullException("rule");
		}
		MatchCollection matchCollection = ReplaceRegexService.CreateRegex(rule).Matches(scopeText ?? string.Empty);
		List<ReplaceStageTarget> list = new List<ReplaceStageTarget>();
		foreach (Match item in matchCollection)
		{
			if (item.Success)
			{
				if (item.Length <= 0)
				{
					throw ReplaceOperationException.Create(ReplaceFailureReasonCode.ZeroLengthMatch, ReplaceFailureStage.ApplyRules);
				}
				list.Add(new ReplaceStageTarget(item.Index, item.Index + item.Length, item.Value, ReplaceRegexService.ResolveReplacement(item, rule)));
			}
		}
		for (int i = 1; i < list.Count; i++)
		{
			if (list[i].Start < list[i - 1].End)
			{
				throw ReplaceOperationException.Create(ReplaceFailureReasonCode.OverlappingTargets, ReplaceFailureStage.ApplyRules);
			}
		}
		return new ReplaceStagePlan(ruleIndex, ruleName, list);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string ApplyToTextModel(ReplaceStagePlan stage, string scopeText)
	{
		if (stage != null)
		{
			StringBuilder stringBuilder = new StringBuilder(scopeText ?? string.Empty);
			for (int num = stage.Targets.Count - 1; num >= 0; num--)
			{
				ReplaceStageTarget replaceStageTarget = stage.Targets[num];
				if (!string.Equals(stringBuilder.ToString(replaceStageTarget.Start, replaceStageTarget.Length), replaceStageTarget.OriginalText, StringComparison.Ordinal))
				{
					throw ReplaceOperationException.Create(ReplaceFailureReasonCode.TargetChanged, ReplaceFailureStage.ApplyRules);
				}
				stringBuilder.Remove(replaceStageTarget.Start, replaceStageTarget.Length);
				stringBuilder.Insert(replaceStageTarget.Start, replaceStageTarget.ReplacementText);
			}
			return stringBuilder.ToString();
		}
		throw new ArgumentNullException("stage");
	}
}
