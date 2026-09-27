using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Services.CompilationFormatting;

public static class CompilationTocOwnershipAnalyzer
{
	private const string ArticlePrefix = "SXCF_ART_";

	private const string ArticleBeginSuffix = "_BEGIN";

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool TryParseArticleBeginBookmark(string fieldCode, out string bookmarkName)
	{
		bookmarkName = null;
		if (string.IsNullOrWhiteSpace(fieldCode))
		{
			return false;
		}
		string[] array = fieldCode.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i + 1 < array.Length; i++)
		{
			if (string.Equals(array[i], "PAGEREF", StringComparison.OrdinalIgnoreCase))
			{
				string text = array[i + 1].Trim(new char[] { '"', '\'', '{', '}' });
				if (IsArticleBeginBookmark(text))
				{
					bookmarkName = text.ToUpperInvariant();
					return true;
				}
				return false;
			}
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool TryParseArticleHyperlinkBookmark(string fieldCode, out string bookmarkName)
	{
		bookmarkName = null;
		if (string.IsNullOrWhiteSpace(fieldCode))
		{
			return false;
		}
		string[] array = fieldCode.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
		if (array.Length >= 2 && string.Equals(array[0], "HYPERLINK", StringComparison.OrdinalIgnoreCase))
		{
			for (int i = 1; i < array.Length; i++)
			{
				string text = null;
				if (string.Equals(array[i], "\\l", StringComparison.OrdinalIgnoreCase) && i + 1 < array.Length)
				{
					text = array[i + 1];
				}
				else if (array[i].IndexOf('#') >= 0)
				{
					text = array[i];
				}
				if (text != null)
				{
					text = text.Trim(new char[] { '"', '\'', '{', '}', '#' });
					if (IsArticleBeginBookmark(text))
					{
						bookmarkName = text.ToUpperInvariant();
						return true;
					}
				}
			}
			return false;
		}
		return false;
	}

	public static IList<CompilationTocOwnedRegion> BuildOwnedRegions(IList<CompilationTocFieldFact> facts)
	{
		List<CompilationTocFieldFact> list = new List<CompilationTocFieldFact>();
		if (facts != null)
		{
			foreach (CompilationTocFieldFact fact in facts)
			{
				if (fact != null && fact.IsOwnedEntryParagraph && fact.ParagraphEnd > fact.ParagraphStart)
				{
					list.Add(fact);
				}
			}
		}
		list.Sort(delegate(CompilationTocFieldFact left, CompilationTocFieldFact right)
		{
			int num2 = left.ParagraphStart.CompareTo(right.ParagraphStart);
			return (num2 == 0) ? left.FieldStart.CompareTo(right.FieldStart) : num2;
		});
		List<CompilationTocOwnedRegion> list2 = new List<CompilationTocOwnedRegion>();
		CompilationTocOwnedRegion compilationTocOwnedRegion = null;
		int num = -1;
		foreach (CompilationTocFieldFact item in list)
		{
			if (compilationTocOwnedRegion == null || item.ParagraphStart > num)
			{
				compilationTocOwnedRegion = new CompilationTocOwnedRegion
				{
					Start = item.ParagraphStart,
					End = item.ParagraphEnd
				};
				list2.Add(compilationTocOwnedRegion);
			}
			else if (item.ParagraphEnd > compilationTocOwnedRegion.End)
			{
				compilationTocOwnedRegion.End = item.ParagraphEnd;
			}
			compilationTocOwnedRegion.BookmarkNames.Add(item.BookmarkName ?? string.Empty);
			if (item.ParagraphEnd > num)
			{
				num = item.ParagraphEnd;
			}
		}
		return list2;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool IsArticleBeginBookmark(string value)
	{
		if (!string.IsNullOrEmpty(value) && value.StartsWith("SXCF_ART_", StringComparison.OrdinalIgnoreCase) && value.EndsWith("_BEGIN", StringComparison.OrdinalIgnoreCase))
		{
			int length = "SXCF_ART_".Length;
			int num = value.Length - "SXCF_ART_".Length - "_BEGIN".Length;
			if (num == 3)
			{
				for (int i = length; i < length + num; i++)
				{
					if (value[i] < '0' || value[i] > '9')
					{
						return false;
					}
				}
				return true;
			}
			return false;
		}
		return false;
	}
}
