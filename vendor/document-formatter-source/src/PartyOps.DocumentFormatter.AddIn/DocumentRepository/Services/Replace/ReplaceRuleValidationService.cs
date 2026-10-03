using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;

namespace DocumentRepository.Services.Replace;

public static class ReplaceRuleValidationService
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static IList<string> Validate(ReplaceRule rule)
	{
		List<string> list = new List<string>();
		if (rule == null)
		{
			list.Add("替换规则不能为空。");
			return list;
		}
		ReplaceRuleNormalizer.Normalize(rule);
		if (string.IsNullOrWhiteSpace(rule.Name))
		{
			list.Add("请填写规则名称。");
		}
		if (rule.UseWildcard && ((rule.FindFormat != null && !rule.FindFormat.IsEmpty) || (rule.ReplaceFormat != null && !rule.ReplaceFormat.IsEmpty)))
		{
			list.Add("WPS 通配符替换只处理文字。需要修改格式时，请另建一条格式规则并放在后面执行。");
		}
		if (rule.FormatOnly)
		{
			if (rule.FindFormat == null || rule.FindFormat.IsEmpty)
			{
				list.Add("仅修改格式时，必须设置查找格式。");
			}
			if (rule.ReplaceFormat == null || rule.ReplaceFormat.IsEmpty)
			{
				list.Add("仅修改格式时，必须设置替换后的格式。");
			}
			return list;
		}
		if (string.IsNullOrWhiteSpace(rule.FindText))
		{
			list.Add("请填写查找内容。");
			return list;
		}
		if (!rule.UseWildcard)
		{
			try
			{
				ReplaceRegexService.CreateRegex(rule);
			}
			catch (Exception ex)
			{
				list.Add(ex.Message);
			}
		}
		return list;
	}
}
