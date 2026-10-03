using System;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Services.Conversion.PdfToWord;

public static class FontNameResolver
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string ToWordFontName(string pdfFontName)
	{
		string text = StripSubsetPrefix(pdfFontName);
		if (!string.IsNullOrWhiteSpace(text))
		{
			string text2 = text.ToLowerInvariant();
			if (!ContainsAny(text2, "simsun", "songti", "sung"))
			{
				if (!ContainsAny(text2, "simhei", "hei"))
				{
					if (ContainsAny(text2, "fangsong", "fang"))
					{
						return "仿宋";
					}
					if (ContainsAny(text2, "kaiti", "kait", "kai"))
					{
						return "楷体";
					}
					if (!ContainsAny(text2, "yahei", "yaheiui"))
					{
						if (!ContainsAny(text2, "timesnewroman", "times"))
						{
							if (ContainsAny(text2, "arial"))
							{
								return "Arial";
							}
							if (ContainsAny(text2, "calibri"))
							{
								return "Calibri";
							}
							if (ContainsAny(text2, "cambria"))
							{
								return "Cambria";
							}
							if (!ContainsAny(text2, "consolas"))
							{
								if (ContainsAny(text2, "courier"))
								{
									return "Courier New";
								}
								return CleanBaseName(text);
							}
							return "Consolas";
						}
						return "Times New Roman";
					}
					return "微软雅黑";
				}
				return "黑体";
			}
			return "宋体";
		}
		return "宋体";
	}

	private static string StripSubsetPrefix(string fontName)
	{
		if (string.IsNullOrEmpty(fontName))
		{
			return string.Empty;
		}
		int num = fontName.IndexOf('+');
		if (num < 0)
		{
			return fontName;
		}
		return fontName.Substring(num + 1);
	}

	private static bool ContainsAny(string text, params string[] tokens)
	{
		foreach (string value in tokens)
		{
			if (text.IndexOf(value, StringComparison.Ordinal) >= 0)
			{
				return true;
			}
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string CleanBaseName(string baseName)
	{
		string text = baseName.Trim();
		string[] array = new string[5] { "-Bold", "-Italic", "-Oblique", "-MT", "MT" };
		foreach (string text2 in array)
		{
			if (text.EndsWith(text2, StringComparison.OrdinalIgnoreCase))
			{
				text = text.Substring(0, text.Length - text2.Length).Trim();
				break;
			}
		}
		if (!string.IsNullOrWhiteSpace(text))
		{
			return text;
		}
		return "宋体";
	}
}
