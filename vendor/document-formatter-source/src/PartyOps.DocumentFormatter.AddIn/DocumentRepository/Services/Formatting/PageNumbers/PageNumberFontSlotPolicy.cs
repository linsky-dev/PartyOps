using System;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Services.Formatting.PageNumbers;

internal static class PageNumberFontSlotPolicy
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static PageNumberFontSlots Resolve(string bodyEastAsianFontName, string pageNumberFontName)
	{
		return new PageNumberFontSlots(Require(bodyEastAsianFontName, "正文中文字体"), Require(pageNumberFontName, "页码字体"));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string Require(string value, string fieldName)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw new FormatException(fieldName + "为空。");
		}
		return value.Trim();
	}
}
