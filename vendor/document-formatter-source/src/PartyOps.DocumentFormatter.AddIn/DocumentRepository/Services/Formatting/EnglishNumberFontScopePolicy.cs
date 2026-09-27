using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;

namespace DocumentRepository.Services.Formatting;

public static class EnglishNumberFontScopePolicy
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool UsesConfiguredFont(FormatConfig config, ElementType type)
	{
		if (config != null)
		{
			string text = ResolveElementFont(config, type);
			if (text != null)
			{
				return MatchesLevel3Font(config, text);
			}
			return false;
		}
		throw new ArgumentNullException("config");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string ResolveStyleFont(FormatConfig config, ElementType type, string elementFontName)
	{
		if (config == null)
		{
			throw new ArgumentNullException("config");
		}
		string text = RequireText(elementFontName, "元素字体");
		if (!config.EnableEnglishFont || !MatchesLevel3Font(config, text))
		{
			return text;
		}
		return RequireText(config.EnglishNumberFontName, "英文数字字体");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool MatchesLevel3Font(FormatConfig config, string elementFontName)
	{
		if (config == null)
		{
			throw new ArgumentNullException("config");
		}
		TextStyle textStyle = config.Level3 ?? throw new FormatException("三级标题参数缺失。");
		return SameFontName(elementFontName, RequireText(textStyle.FontName, "三级标题字体"));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string ResolveElementFont(FormatConfig config, ElementType type)
	{
		if (config == null)
		{
			throw new ArgumentNullException("config");
		}
		switch (type)
		{
		case ElementType.Body:
		case ElementType.Salutation:
		case ElementType.Signature:
		case ElementType.SignatureDate:
		case ElementType.AttachmentListFirst:
		case ElementType.AttachmentListSingle:
		case ElementType.AttachmentListContinuation:
			return RequireStyleFont(config.Body, "正文");
		case ElementType.SubTitle:
		case ElementType.AttachmentSubTitle:
			return RequireStyleFont(config.Level2, "二级标题");
		case ElementType.Table:
			return RequireText((config.TableOptions ?? throw new FormatException("表格参数缺失。")).BodyFontName, "表格正文字体");
		case ElementType.AttachmentMarker:
			return RequireStyleFont(config.Level1, "一级标题");
		case ElementType.Level2Title:
			return RequireStyleFont(config.Level2, "二级标题");
		case ElementType.Level3Title:
			return RequireStyleFont(config.Level3, "三级标题");
		case ElementType.TableHeader:
			return RequireText((config.TableOptions ?? throw new FormatException("表格参数缺失。")).HeaderFontName, "表头字体");
		case ElementType.MainTitle:
			return RequireStyleFont(config.MainTitle, "主标题");
		case ElementType.AttachmentTitle:
			return RequireText((config.AttachmentOptions ?? throw new FormatException("附件参数缺失。")).AttachmentMarkerFontName, "附件标题字体");
		case ElementType.Level1Title:
			return RequireStyleFont(config.Level1, "一级标题");
		default:
			return null;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string RequireStyleFont(TextStyle style, string name)
	{
		if (style == null)
		{
			throw new FormatException(name + "参数缺失。");
		}
		return RequireText(style.FontName, name + "字体");
	}

	private static bool SameFontName(string left, string right)
	{
		return string.Equals(NormalizeFontName(left), NormalizeFontName(right), StringComparison.OrdinalIgnoreCase);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string NormalizeFontName(string value)
	{
		string text = RequireText(value, "字体");
		char[] array = new char[text.Length];
		int length = 0;
		string text2 = text;
		foreach (char c in text2)
		{
			if (!char.IsWhiteSpace(c) && c != '_' && c != '-' && c != '－')
			{
				array[length++] = c;
			}
		}
		return new string(array, 0, length);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string RequireText(string value, string fieldName)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw new FormatException(fieldName + "为空。");
		}
		return value.Trim();
	}
}
