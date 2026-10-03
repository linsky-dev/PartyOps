using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using DocumentRepository.Services.Analysis;
using DocumentRepository.Services.Detection;

namespace DocumentRepository.Services.CompilationFormatting;

public static class CompilationArticleTitleResolver
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static CompilationArticleTitleResolution Resolve(IList<string> paragraphTexts, int contentStartInclusive, int contentEndExclusive, FormatConfig config)
	{
		if (paragraphTexts != null)
		{
			if (contentStartInclusive < 0 || contentStartInclusive > paragraphTexts.Count)
			{
				throw new ArgumentOutOfRangeException("contentStartInclusive");
			}
			if (contentEndExclusive >= contentStartInclusive && contentEndExclusive <= paragraphTexts.Count)
			{
				CompilationArticleTitleResolution compilationArticleTitleResolution = new CompilationArticleTitleResolution();
				int num = FindNextNonEmpty(paragraphTexts, contentStartInclusive, contentEndExclusive);
				if (num < 0)
				{
					compilationArticleTitleResolution.ErrorMessage = "标记后没有非空主标题。";
					return compilationArticleTitleResolution;
				}
				FormatConfig cfg = config ?? new FormatConfig();
				List<string> list = new List<string>();
				string item = NormalizeTitlePart(paragraphTexts[num]);
				list.Add(item);
				compilationArticleTitleResolution.TitleStartParagraphIndex = num;
				compilationArticleTitleResolution.TitleEndParagraphIndex = num;
				compilationArticleTitleResolution.TitleParagraphCount = 1;
				for (int i = num + 1; i < contentEndExclusive; i++)
				{
					string text = NormalizeTitlePart(paragraphTexts[i]);
					if (!string.IsNullOrWhiteSpace(text))
					{
						if (MainTitleCandidateDetector.IsSubTitleCandidate(text, cfg) || !MainTitleCandidateDetector.IsMainTitleCandidate(text, cfg))
						{
							break;
						}
						list.Add(text);
						compilationArticleTitleResolution.TitleEndParagraphIndex = i;
						compilationArticleTitleResolution.TitleParagraphCount++;
					}
				}
				compilationArticleTitleResolution.Title = CombineTitleParts(list);
				return compilationArticleTitleResolution;
			}
			throw new ArgumentOutOfRangeException("contentEndExclusive");
		}
		throw new ArgumentNullException("paragraphTexts");
	}

	public static bool IsBlankParagraph(string text)
	{
		return string.IsNullOrWhiteSpace(NormalizeTitlePart(text));
	}

	public static string NormalizeTitlePart(string text)
	{
		return ParagraphIdentityTextNormalizer.Normalize(text);
	}

	public static List<string> SplitParagraphTexts(string fullText)
	{
		List<string> list = new List<string>();
		string text = fullText ?? string.Empty;
		int num = 0;
		while (num < text.Length)
		{
			int num2 = text.IndexOf('\r', num);
			int num3 = text.IndexOf('\f', num);
			int num4;
			if (num3 < 0 || (num2 >= 0 && num3 >= num2) || (num3 + 1 < text.Length && text[num3 + 1] == '\r'))
			{
				num4 = ((num2 < 0) ? text.Length : (num2 + 1));
				if (num4 < text.Length && text[num4] == '\a')
				{
					num4++;
				}
			}
			else
			{
				num4 = num3 + 1;
			}
			list.Add(text.Substring(num, num4 - num));
			num = num4;
		}
		return list;
	}

	private static int FindNextNonEmpty(IList<string> texts, int start, int end)
	{
		for (int i = start; i < end; i++)
		{
			if (!IsBlankParagraph(texts[i]))
			{
				return i;
			}
		}
		return -1;
	}

	private static string CombineTitleParts(IList<string> parts)
	{
		StringBuilder stringBuilder = new StringBuilder();
		foreach (string part in parts)
		{
			string text = NormalizeTitlePart(part);
			if (text.Length != 0)
			{
				if (stringBuilder.Length > 0 && NeedsAsciiWordSeparator(stringBuilder[stringBuilder.Length - 1], text[0]))
				{
					stringBuilder.Append(' ');
				}
				stringBuilder.Append(text);
			}
		}
		return stringBuilder.ToString();
	}

	private static bool NeedsAsciiWordSeparator(char left, char right)
	{
		if (IsAsciiLetterOrDigit(left))
		{
			return IsAsciiLetterOrDigit(right);
		}
		return false;
	}

	private static bool IsAsciiLetterOrDigit(char value)
	{
		if ((value < 'A' || value > 'Z') && (value < 'a' || value > 'z'))
		{
			if (value >= '0')
			{
				return value <= '9';
			}
			return false;
		}
		return true;
	}
}
