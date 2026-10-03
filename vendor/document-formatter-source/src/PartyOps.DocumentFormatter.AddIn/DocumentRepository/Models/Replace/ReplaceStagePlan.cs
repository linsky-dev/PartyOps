using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Models.Replace;

public sealed class ReplaceStagePlan
{
	public int RuleIndex { get; private set; }

	public string RuleName { get; private set; }

	public IReadOnlyList<ReplaceStageTarget> Targets { get; private set; }

	[MethodImpl(MethodImplOptions.NoInlining)]
	public ReplaceStagePlan(int ruleIndex, string ruleName, IReadOnlyList<ReplaceStageTarget> targets)
	{
		if (ruleIndex < 0)
		{
			throw new ArgumentOutOfRangeException("ruleIndex");
		}
		if (targets == null)
		{
			throw new ArgumentNullException("targets");
		}
		List<ReplaceStageTarget> list = new List<ReplaceStageTarget>(targets.Count);
		ReplaceStageTarget replaceStageTarget = null;
		foreach (ReplaceStageTarget target in targets)
		{
			if (target == null)
			{
				throw new ArgumentException("替换计划不能包含空目标。", "targets");
			}
			if (replaceStageTarget != null && target.Start < replaceStageTarget.End)
			{
				throw new ArgumentException("替换计划目标必须按位置升序且不能重叠。", "targets");
			}
			list.Add(target);
			replaceStageTarget = target;
		}
		RuleIndex = ruleIndex;
		RuleName = ruleName ?? string.Empty;
		Targets = list.AsReadOnly();
	}
}
