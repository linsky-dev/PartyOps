using System;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Services.CompilationFormatting;

public static class CompilationMarkerParser
{
	public const string MarkerText = "@@汇编@@";

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool IsExactMarker(string paragraphText)
	{
		if (string.IsNullOrWhiteSpace(paragraphText))
		{
			return false;
		}
		return StripBoundarySpaces(paragraphText) == "@@汇编@@";
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool IsMalformedMarkerParagraph(string paragraphText)
	{
		if (!string.IsNullOrEmpty(paragraphText))
		{
			if (paragraphText.IndexOf("@@汇编@@", StringComparison.Ordinal) >= 0)
			{
				return !IsExactMarker(paragraphText);
			}
			return false;
		}
		return false;
	}

	public static string StripBoundarySpaces(string text)
	{
		if (!string.IsNullOrEmpty(text))
		{
			int i;
			for (i = 0; i < text.Length && IsSpace(text[i]); i++)
			{
			}
			if (i == text.Length)
			{
				return "";
			}
			int num = text.Length - 1;
			while (num > i && IsSpace(text[num]))
			{
				num--;
			}
			return text.Substring(i, num - i + 1);
		}
		return text;
	}

	private static bool IsSpace(char c)
	{
		if (c != ' ' && c != '\u3000')
		{
			return c == '\t';
		}
		return true;
	}
}
