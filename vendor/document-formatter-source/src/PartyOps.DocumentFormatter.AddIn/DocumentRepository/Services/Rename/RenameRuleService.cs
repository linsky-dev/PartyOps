using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace DocumentRepository.Services.Rename;

public static class RenameRuleService
{
	public static RenameRule GetActiveRule()
	{
		return RenameRuleManager.GetActiveRule();
	}

	public static bool ValidateRule(RenameRule rule, out string error)
	{
		return RenameRuleManager.ValidateRule(rule, out error);
	}

	public static bool RuleUsesPart(RenameRule rule, string part)
	{
		if (rule != null && rule.Parts != null)
		{
			return rule.Parts.Any((RenameRulePart p) => string.Equals(p.Type, part, StringComparison.OrdinalIgnoreCase));
		}
		return false;
	}

	public static void EnsureRuleParts(RenameRule rule)
	{
		RenameRuleManager.EnsureRuleParts(rule);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string FormatDate(DateTime date)
	{
		return date.ToString("yyyy.MM.dd");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string SanitizeFileName(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return "";
		}
		name = Regex.Replace(name, "[\\\\\\/:\\*\\?\"<>\\|]", "");
		name = Regex.Replace(name, "[\\r\\n\\t]", "");
		name = Regex.Replace(name, "\\s+", "");
		name = name.Trim(new char[1] { '.' });
		return name;
	}
}
