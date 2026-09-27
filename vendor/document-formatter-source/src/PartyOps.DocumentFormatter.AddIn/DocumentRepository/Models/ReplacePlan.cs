using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DocumentRepository.Services.Replace;

namespace DocumentRepository.Models;

public class ReplacePlan
{
	public string Id { get; set; }

	public string Name { get; set; }

	public List<ReplaceRule> Rules { get; set; }

	[MethodImpl(MethodImplOptions.NoInlining)]
	public ReplacePlan()
	{
		Id = Guid.NewGuid().ToString("N");
		Name = "新替换方案";
		Rules = new List<ReplaceRule>();
	}

	public ReplacePlan Clone()
	{
		ReplacePlan replacePlan = new ReplacePlan
		{
			Id = Id,
			Name = Name,
			Rules = new List<ReplaceRule>()
		};
		if (Rules != null)
		{
			foreach (ReplaceRule rule in Rules)
			{
				replacePlan.Rules.Add(ReplaceRuleNormalizer.Clone(rule));
			}
		}
		return replacePlan;
	}
}
