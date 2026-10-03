using System;
using System.Runtime.CompilerServices;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Formatting;

public static class DocumentFontSlotService
{
	public static void Apply(Font font, string eastAsianFontName, string asciiFontName)
	{
		ApplyIfNeeded(font, eastAsianFontName, asciiFontName);
	}

	public static void ApplyAscii(Font font, string asciiFontName)
	{
		ApplyAsciiIfNeeded(font, asciiFontName);
	}

	public static void ApplyEastAsian(Font font, string eastAsianFontName)
	{
		ApplyEastAsianIfNeeded(font, eastAsianFontName);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool ApplyIfNeeded(Font font, string eastAsianFontName, string asciiFontName)
	{
		if (font == null)
		{
			throw new ArgumentNullException("font");
		}
		string text = RequireFontName(eastAsianFontName, "中文字体");
		string text2 = RequireFontName(asciiFontName, "英文数字字体");
		bool result = false;
		if (!SameFontName(font.NameFarEast, text))
		{
			font.NameFarEast = text;
			result = true;
		}
		if (!SameFontName(font.NameAscii, text2))
		{
			font.NameAscii = text2;
			result = true;
		}
		return result;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool ApplyAsciiIfNeeded(Font font, string asciiFontName)
	{
		if (font == null)
		{
			throw new ArgumentNullException("font");
		}
		string text = RequireFontName(asciiFontName, "英文数字字体");
		if (SameFontName(font.NameAscii, text))
		{
			return false;
		}
		font.NameAscii = text;
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool ApplyEastAsianIfNeeded(Font font, string eastAsianFontName)
	{
		if (font != null)
		{
			string text = RequireFontName(eastAsianFontName, "中文字体");
			if (SameFontName(font.NameFarEast, text))
			{
				return false;
			}
			font.NameFarEast = text;
			return true;
		}
		throw new ArgumentNullException("font");
	}

	internal static bool SameFontName(string actual, string expected)
	{
		return string.Equals((actual ?? string.Empty).Trim(), (expected ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string RequireFontName(string value, string fieldName)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw new FormatException(fieldName + "为空。");
		}
		return value.Trim();
	}
}
