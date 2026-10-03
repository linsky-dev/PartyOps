using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Models.Replace;

namespace DocumentRepository.Services.Replace;

public static class ReplaceRuleNormalizer
{
	public static ReplaceRule Clone(ReplaceRule rule)
	{
		if (rule == null)
		{
			return new ReplaceRule();
		}
		return new ReplaceRule
		{
			Name = (rule.Name ?? ""),
			Enabled = rule.Enabled,
			FindText = (rule.FindText ?? ""),
			FormatOnly = rule.FormatOnly,
			UseRegex = rule.UseRegex,
			UseWildcard = rule.UseWildcard,
			ReplaceText = (rule.ReplaceText ?? ""),
			FindFormat = CloneFind(rule.FindFormat),
			ReplaceFormat = CloneTarget(rule.ReplaceFormat)
		};
	}

	public static void Normalize(ReplaceRule rule)
	{
		if (rule != null)
		{
			if (string.IsNullOrWhiteSpace(rule.Name))
			{
				rule.Name = BuildDefaultName(rule);
			}
			if (rule.FindText == null)
			{
				rule.FindText = "";
			}
			if (rule.ReplaceText == null)
			{
				rule.ReplaceText = "";
			}
			if (rule.FindFormat == null)
			{
				rule.FindFormat = new ReplaceFormatCondition();
			}
			if (rule.ReplaceFormat == null)
			{
				rule.ReplaceFormat = new ReplaceFormatTarget();
			}
			if (!rule.FormatOnly && string.IsNullOrWhiteSpace(rule.FindText) && !rule.FindFormat.IsEmpty && !rule.ReplaceFormat.IsEmpty)
			{
				rule.FormatOnly = true;
			}
			if (rule.FormatOnly)
			{
				rule.UseRegex = false;
				rule.UseWildcard = false;
			}
			if (rule.UseRegex && rule.UseWildcard)
			{
				throw ReplaceOperationException.Create(ReplaceFailureReasonCode.RuleConfigurationInvalid, ReplaceFailureStage.ApplyRules);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string BuildDefaultName(ReplaceRule rule)
	{
		if (rule != null)
		{
			if (rule.FormatOnly)
			{
				return "格式替换";
			}
			if (!rule.UseWildcard)
			{
				if (!rule.UseRegex)
				{
					string.IsNullOrWhiteSpace(rule.FindText);
					return "文字替换";
				}
				return "正则替换";
			}
			return "WPS通配符替换";
		}
		return "新替换规则";
	}

	private static ReplaceFormatCondition CloneFind(ReplaceFormatCondition source)
	{
		if (source == null)
		{
			return new ReplaceFormatCondition();
		}
		return new ReplaceFormatCondition
		{
			FontName = source.FontName,
			SizeText = source.SizeText,
			Bold = source.Bold,
			Italic = source.Italic,
			Underline = source.Underline,
			Alignment = source.Alignment,
			OutlineLevel = source.OutlineLevel,
			FirstIndentChars = source.FirstIndentChars,
			SpaceBeforeLines = source.SpaceBeforeLines,
			SpaceAfterLines = source.SpaceAfterLines,
			LineSpacing = source.LineSpacing
		};
	}

	private static ReplaceFormatTarget CloneTarget(ReplaceFormatTarget source)
	{
		if (source == null)
		{
			return new ReplaceFormatTarget();
		}
		return new ReplaceFormatTarget
		{
			FontName = source.FontName,
			SizeText = source.SizeText,
			Bold = source.Bold,
			Italic = source.Italic,
			Underline = source.Underline,
			Alignment = source.Alignment,
			OutlineLevel = source.OutlineLevel,
			FirstIndentChars = source.FirstIndentChars,
			SpaceBeforeLines = source.SpaceBeforeLines,
			SpaceAfterLines = source.SpaceAfterLines,
			LineSpacing = source.LineSpacing
		};
	}
}
