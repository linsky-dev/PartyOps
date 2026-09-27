using System.Runtime.CompilerServices;
using DocumentRepository.Models;

namespace DocumentRepository.Services.Replace;

public static class ReplaceRuleSummaryService
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string Build(ReplaceRule rule, int index)
	{
		ReplaceRuleNormalizer.Normalize(rule);
		string text = (rule.Enabled ? "启用" : "停用");
		string text2 = (rule.FormatOnly ? "格式替换" : (rule.UseWildcard ? "WPS通配符替换" : (rule.UseRegex ? "正则替换" : "文字替换")));
		string text3 = BuildAction(rule);
		return $"{index}. [{text}] {rule.Name}｜{text2}｜{text3}";
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string BuildAction(ReplaceRule rule)
	{
		if (!rule.FormatOnly)
		{
			string arg = Compact(rule.FindText, 24);
			string arg2 = (string.IsNullOrEmpty(rule.ReplaceText) ? "删除" : Compact(rule.ReplaceText, 20));
			return $"{arg} → {arg2}";
		}
		return "按格式查找并修改格式";
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string Compact(string value, int maximum)
	{
		string text = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
		if (text.Length > maximum)
		{
			return text.Substring(0, maximum) + "…";
		}
		return text;
	}
}
