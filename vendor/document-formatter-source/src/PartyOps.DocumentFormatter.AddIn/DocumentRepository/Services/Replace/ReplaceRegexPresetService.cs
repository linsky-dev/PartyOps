using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;

namespace DocumentRepository.Services.Replace;

public static class ReplaceRegexPresetService
{
	public const string ParagraphOrdinalPattern = "(?:(?<=\\r)|\\A)([ \\t\u3000]*)第([零〇一二三四五六七八九十百千万两]+)(?:[、，,：:。．.]|[ \\t\u3000]+)";

	public const string YiShiOrdinalPattern = "(?:(?<=\\r)|\\A)([ \\t\u3000]*)([零〇一二三四五六七八九十百千万两]+)是(?:[、，,：:。．.][ \\t\u3000]*)?";

	public static IList<ReplaceRegexPreset> GetPresets()
	{
		return new List<ReplaceRegexPreset>
		{
			CreateParenthesizedPreset(),
			CreateYiShiPreset(),
			CreateYiShiToParenthesizedPreset(),
			CreateChineseLevelOneToParenthesizedPreset(),
			CreateParenthesizedToChineseLevelOnePreset(),
			CreateArabicDotToParenthesizedPreset(),
			CreateParenthesizedArabicToDotPreset(),
			CreateHalfWidthParenthesesPreset()
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ReplaceRegexPreset CreateParenthesizedPreset()
	{
		return new ReplaceRegexPreset
		{
			Id = "paragraph_ordinal_parenthesized",
			Name = "段首“第一”转“（一）”",
			Description = "只处理自然段开头的“第一、第二……”序号，不处理正文中的“第一时间”等词语。",
			Pattern = "(?:(?<=\\r)|\\A)([ \\t\u3000]*)第([零〇一二三四五六七八九十百千万两]+)(?:[、，,：:。．.]|[ \\t\u3000]+)",
			Replacement = "$1（$2）",
			SampleText = "第一，强化理论学习。\r\n第二，提升服务效能。"
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ReplaceRegexPreset CreateYiShiPreset()
	{
		return new ReplaceRegexPreset
		{
			Id = "paragraph_ordinal_yishi",
			Name = "段首“第一”转“一是”",
			Description = "只处理自然段开头的“第一、第二……”序号，并转换为“一是、二是……”表达。",
			Pattern = "(?:(?<=\\r)|\\A)([ \\t\u3000]*)第([零〇一二三四五六七八九十百千万两]+)(?:[、，,：:。．.]|[ \\t\u3000]+)",
			Replacement = "$1$2是",
			SampleText = "第一，强化理论学习。\r\n第二，提升服务效能。"
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ReplaceRegexPreset CreateYiShiToParenthesizedPreset()
	{
		return new ReplaceRegexPreset
		{
			Id = "paragraph_yishi_parenthesized",
			Name = "段首“一是”转“（一）”",
			Description = "只处理自然段开头的“一是、二是、三是……”序号，并转换为“（一）（二）（三）……”表达。",
			Pattern = "(?:(?<=\\r)|\\A)([ \\t\u3000]*)([零〇一二三四五六七八九十百千万两]+)是(?:[、，,：:。．.][ \\t\u3000]*)?",
			Replacement = "$1（$2）",
			SampleText = "一是强化理论学习。\r\n二是提升服务效能。\r\n三是压实工作责任。"
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ReplaceRegexPreset CreateChineseLevelOneToParenthesizedPreset()
	{
		return new ReplaceRegexPreset
		{
			Id = "heading_chinese_to_parenthesized",
			Name = "一级标题“一、”转“（一）”",
			Description = "只处理自然段开头的中文序号标题，将“一、二、……”转换为“（一）（二）……”。",
			Pattern = "(?:(?<=\\r)|\\A)([ \\t\u3000]*)([零〇一二三四五六七八九十百千万两]+)[、，,：:。．.][ \\t\u3000]*",
			Replacement = "$1（$2）",
			SampleText = "一、强化理论学习\r\n二、提升服务效能"
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ReplaceRegexPreset CreateParenthesizedToChineseLevelOnePreset()
	{
		return new ReplaceRegexPreset
		{
			Id = "heading_parenthesized_to_chinese",
			Name = "二级标题“（一）”转“一、”",
			Description = "只处理自然段开头的括号中文序号，将“（一）（二）……”转换为“一、二、……”。",
			Pattern = "(?:(?<=\\r)|\\A)([ \\t\u3000]*)[（(]([零〇一二三四五六七八九十百千万两]+)[）)][、，,：:。．. \\t\u3000]*",
			Replacement = "$1$2、",
			SampleText = "（一）强化理论学习\r\n（二）提升服务效能"
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ReplaceRegexPreset CreateArabicDotToParenthesizedPreset()
	{
		return new ReplaceRegexPreset
		{
			Id = "heading_arabic_dot_to_parenthesized",
			Name = "数字标题“1.”转“（1）”",
			Description = "只处理自然段开头的阿拉伯数字标题，将“1.、2.”转换为“（1）（2）”。",
			Pattern = "(?:(?<=\\r)|\\A)([ \\t\u3000]*)([0-9]+)[.．、][ \\t\u3000]*",
			Replacement = "$1（$2）",
			SampleText = "1. 强化理论学习\r\n2. 提升服务效能"
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ReplaceRegexPreset CreateParenthesizedArabicToDotPreset()
	{
		return new ReplaceRegexPreset
		{
			Id = "heading_parenthesized_arabic_to_dot",
			Name = "数字标题“（1）”转“1.”",
			Description = "只处理自然段开头的括号阿拉伯数字标题，将“（1）（2）”转换为“1.、2.”。",
			Pattern = "(?:(?<=\\r)|\\A)([ \\t\u3000]*)[（(]([0-9]+)[）)][.．、 \\t\u3000]*",
			Replacement = "$1$2.",
			SampleText = "（1）强化理论学习\r\n（2）提升服务效能"
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ReplaceRegexPreset CreateHalfWidthParenthesesPreset()
	{
		return new ReplaceRegexPreset
		{
			Id = "heading_halfwidth_parentheses",
			Name = "半角标题“(一)”转“（一）”",
			Description = "只处理自然段开头的半角括号中文序号，将半角括号统一为全角中文括号。",
			Pattern = "(?:(?<=\\r)|\\A)([ \\t\u3000]*)\\(([零〇一二三四五六七八九十百千万两]+)\\)",
			Replacement = "$1（$2）",
			SampleText = "(一)强化理论学习\r\n(二)提升服务效能"
		};
	}

	public static ReplaceRule CreateRule(ReplaceRegexPreset preset)
	{
		return new ReplaceRule
		{
			Name = preset.Name,
			Enabled = true,
			FindText = preset.Pattern,
			UseRegex = true,
			ReplaceText = preset.Replacement,
			FindFormat = new ReplaceFormatCondition(),
			ReplaceFormat = new ReplaceFormatTarget()
		};
	}
}
