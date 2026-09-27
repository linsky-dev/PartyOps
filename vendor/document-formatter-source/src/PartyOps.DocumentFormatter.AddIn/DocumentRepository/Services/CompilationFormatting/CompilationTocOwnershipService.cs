using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.CompilationFormatting;
using DocumentRepository.Services.Analysis;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.CompilationFormatting;

public static class CompilationTocOwnershipService
{
	private sealed class TitleLinkFact
	{
		public string BookmarkName { get; set; }

		public int FieldStart { get; set; }

		public int ResultEnd { get; set; }
	}

	public static CompilationTocOwnershipSnapshot Capture(Document document, IList<CompilationTocEntry> entries, CompilationTocOptions options)
	{
		return Capture(document, entries, options, includeTrailingSelectionTolerance: true);
	}

	private static CompilationTocOwnershipSnapshot Capture(Document document, IList<CompilationTocEntry> entries, CompilationTocOptions options, bool includeTrailingSelectionTolerance)
	{
		CompilationTocOwnershipSnapshot compilationTocOwnershipSnapshot = new CompilationTocOwnershipSnapshot();
		if (document == null)
		{
			return compilationTocOwnershipSnapshot;
		}
		Dictionary<string, string> expectedTitles = BuildExpectedTitles(entries);
		List<CompilationTocFieldFact> list = CollectPluginFields(document, expectedTitles);
		IList<CompilationTocOwnedRegion> list2 = CompilationTocOwnershipAnalyzer.BuildOwnedRegions(list);
		foreach (CompilationTocOwnedRegion item in list2)
		{
			ExtendCandidateRegion(document, item, entries, options, includeTrailingSelectionTolerance);
		}
		compilationTocOwnershipSnapshot.PluginFields = list;
		compilationTocOwnershipSnapshot.OwnedRegions = list2;
		return compilationTocOwnershipSnapshot;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int RemoveOrphanRegions(Document document, IList<CompilationTocEntry> entries, CompilationTocOptions options, out int pluginFieldCount)
	{
		pluginFieldCount = 0;
		CompilationTocOwnershipSnapshot compilationTocOwnershipSnapshot = Capture(document, entries, options, includeTrailingSelectionTolerance: false);
		pluginFieldCount = compilationTocOwnershipSnapshot.PluginFields.Count;
		int num = Math.Max(1, pluginFieldCount + 1);
		int num2 = 0;
		int num3 = pluginFieldCount + 1;
		for (int i = 0; i < num; i++)
		{
			CompilationTocOwnershipSnapshot compilationTocOwnershipSnapshot2 = ((i == 0) ? compilationTocOwnershipSnapshot : Capture(document, entries, options, includeTrailingSelectionTolerance: false));
			if (compilationTocOwnershipSnapshot2.PluginFields.Count >= num3 && i > 0)
			{
				throw new InvalidOperationException("汇编目录清理未取得进展，已停止以保护文档内容。");
			}
			num3 = compilationTocOwnershipSnapshot2.PluginFields.Count;
			if (compilationTocOwnershipSnapshot2.OwnedRegions.Count == 0)
			{
				break;
			}
			int start;
			int end;
			bool flag = TryGetMarkedRegion(document, out start, out end);
			List<CompilationTocOwnedRegion> list = new List<CompilationTocOwnedRegion>();
			foreach (CompilationTocOwnedRegion ownedRegion in compilationTocOwnershipSnapshot2.OwnedRegions)
			{
				if (!flag || ownedRegion.Start >= end || ownedRegion.End <= start)
				{
					list.Add(ownedRegion);
				}
			}
			if (list.Count == 0)
			{
				break;
			}
			list.Sort((CompilationTocOwnedRegion left, CompilationTocOwnedRegion right) => right.Start.CompareTo(left.Start));
			foreach (CompilationTocOwnedRegion item in list)
			{
				DeleteOwnedRegionParagraphs(document, item, compilationTocOwnershipSnapshot2.PluginFields);
				num2++;
			}
		}
		if (num2 > 0)
		{
			LogService.Info("汇编目录：已清理失去书签边界的插件目录区域 " + num2 + " 个。");
		}
		return num2;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void DeleteOwnedRegionParagraphs(Document document, CompilationTocOwnedRegion region, IList<CompilationTocFieldFact> fields)
	{
		List<CompilationTocFieldFact> list = new List<CompilationTocFieldFact>();
		foreach (CompilationTocFieldFact field in fields)
		{
			if (field != null && field.IsOwnedEntryParagraph && field.ParagraphStart >= region.Start && field.ParagraphEnd <= region.End)
			{
				list.Add(field);
			}
		}
		list.Sort((CompilationTocFieldFact left, CompilationTocFieldFact right) => right.FieldStart.CompareTo(left.FieldStart));
		LogService.Info("汇编目录清理：候选条目=" + list.Count + "，范围=" + region.Start + ".." + region.End + "。");
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			object Start = region.Start;
			object End = region.End;
			value = document.Range(ref Start, ref End);
			foreach (CompilationTocFieldFact item in list)
			{
				DeleteMatchingPluginField(document, item);
			}
			Microsoft.Office.Interop.Word.Range range = value;
			End = Type.Missing;
			Start = Type.Missing;
			range.Delete(ref End, ref Start);
		}
		finally
		{
			ComObjectRelease.Release(ref value, "CompilationTocOwnership.Remove.LiveRegion");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void DeleteMatchingPluginField(Document document, CompilationTocFieldFact expected)
	{
		Fields value = null;
		Field value2 = null;
		Microsoft.Office.Interop.Word.Range value3 = null;
		try
		{
			value = document.Fields;
			for (int num = value.Count; num >= 1; num--)
			{
				Field value4 = null;
				Microsoft.Office.Interop.Word.Range value5 = null;
				try
				{
					value4 = value[num];
					value5 = value4.Code;
					if (value5.Start == expected.FieldStart && CompilationTocOwnershipAnalyzer.TryParseArticleBeginBookmark(value5.Text, out var bookmarkName) && string.Equals(bookmarkName, expected.BookmarkName, StringComparison.OrdinalIgnoreCase))
					{
						value2 = value4;
						value3 = value5;
						value4 = null;
						value5 = null;
						break;
					}
				}
				finally
				{
					ComObjectRelease.Release(ref value5, "CompilationTocOwnership.Remove.ScanCode");
					ComObjectRelease.Release(ref value4, "CompilationTocOwnership.Remove.ScanField");
				}
			}
			if (value2 == null)
			{
				throw new InvalidOperationException("汇编目录清理时无法重新定位插件页码域。");
			}
			value2.Unlink();
		}
		finally
		{
			ComObjectRelease.Release(ref value3, "CompilationTocOwnership.Remove.Code");
			ComObjectRelease.Release(ref value2, "CompilationTocOwnership.Remove.Field");
			ComObjectRelease.Release(ref value, "CompilationTocOwnership.Remove.Fields");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void VerifyCanonicalState(Document document, IList<CompilationTocEntry> entries, CompilationTocOptions options)
	{
		if (document != null)
		{
			Dictionary<string, string> dictionary = BuildExpectedTitles(entries);
			CompilationTocOwnershipSnapshot compilationTocOwnershipSnapshot = Capture(document, entries, options);
			if (compilationTocOwnershipSnapshot.PluginFields.Count == dictionary.Count)
			{
				Dictionary<string, int> dictionary2 = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
				foreach (CompilationTocFieldFact pluginField in compilationTocOwnershipSnapshot.PluginFields)
				{
					dictionary2.TryGetValue(pluginField.BookmarkName ?? string.Empty, out var value);
					dictionary2[pluginField.BookmarkName ?? string.Empty] = value + 1;
				}
				foreach (string key in dictionary.Keys)
				{
					if (!dictionary2.TryGetValue(key, out var value2) || value2 != 1)
					{
						throw new InvalidOperationException("汇编目录唯一性验证失败：文章页码引用不是唯一值。");
					}
				}
				if (!TryGetMarkedRegion(document, out var start, out var end))
				{
					throw new InvalidOperationException("汇编目录唯一性验证失败：目录边界书签无效。");
				}
				foreach (CompilationTocFieldFact pluginField2 in compilationTocOwnershipSnapshot.PluginFields)
				{
					if (pluginField2.FieldStart < start || pluginField2.FieldStart > end)
					{
						throw new InvalidOperationException("汇编目录唯一性验证失败：存在书签区域外的插件页码域。");
					}
				}
				if (compilationTocOwnershipSnapshot.OwnedRegions.Count != 1)
				{
					throw new InvalidOperationException("汇编目录唯一性验证失败：目录候选区数量不是一套。");
				}
				return;
			}
			throw new InvalidOperationException("汇编目录唯一性验证失败：插件页码域=" + compilationTocOwnershipSnapshot.PluginFields.Count + "，文章=" + dictionary.Count + "。");
		}
		throw new ArgumentNullException("document");
	}

	public static CompilationTocRangeRelation ClassifySelection(Document document, int selectionStart, int selectionEnd, IList<CompilationTocEntry> entries, CompilationTocOptions options)
	{
		CompilationTocOwnershipSnapshot compilationTocOwnershipSnapshot = Capture(document, entries, options);
		bool flag = false;
		foreach (CompilationTocOwnedRegion ownedRegion in compilationTocOwnershipSnapshot.OwnedRegions)
		{
			CompilationTocRangeRelation compilationTocRangeRelation = CompilationTocRangeClassifier.Classify(ownedRegion.Start, ownedRegion.End, selectionStart, selectionEnd);
			switch (compilationTocRangeRelation)
			{
			case CompilationTocRangeRelation.Overlapping:
				flag = true;
				break;
			case CompilationTocRangeRelation.Inside:
				return compilationTocRangeRelation;
			}
		}
		if (!flag)
		{
			return CompilationTocRangeRelation.Outside;
		}
		return CompilationTocRangeRelation.Overlapping;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static List<CompilationTocFieldFact> CollectPluginFields(Document document, Dictionary<string, string> expectedTitles)
	{
		List<CompilationTocFieldFact> list = new List<CompilationTocFieldFact>();
		List<TitleLinkFact> titleLinks = CollectTitleLinkFacts(document, expectedTitles);
		Fields value = null;
		try
		{
			value = document.Fields;
			for (int i = 1; i <= value.Count; i++)
			{
				Field value2 = null;
				Microsoft.Office.Interop.Word.Range value3 = null;
				Microsoft.Office.Interop.Word.Range value4 = null;
				Paragraphs value5 = null;
				Paragraph value6 = null;
				Microsoft.Office.Interop.Word.Range value7 = null;
				try
				{
					value2 = value[i];
					value3 = value2.Code;
					if (CompilationTocOwnershipAnalyzer.TryParseArticleBeginBookmark(value3?.Text, out var bookmarkName))
					{
						value4 = value2.Result;
						value5 = value4.Paragraphs;
						if (value5 != null && value5.Count != 0)
						{
							value6 = value5[1];
							value7 = value6.Range;
							string value8;
							bool num = expectedTitles.TryGetValue(bookmarkName, out value8);
							bool flag = num && ParagraphStartsWithTitle(value7.Text, value8);
							bool flag2 = num && ParagraphHasTocEntryShape(value7.Text);
							TitleLinkFact titleLinkFact = FindAdjacentTitleLink(titleLinks, bookmarkName, value3.Start);
							int paragraphStart = ((titleLinkFact == null) ? value7.Start : Math.Min(value7.Start, titleLinkFact.FieldStart));
							list.Add(new CompilationTocFieldFact
							{
								BookmarkName = bookmarkName,
								FieldStart = value3.Start,
								ParagraphStart = paragraphStart,
								ParagraphEnd = value7.End,
								IsOwnedEntryParagraph = (titleLinkFact != null || flag || flag2)
							});
						}
					}
				}
				finally
				{
					ComObjectRelease.Release(ref value7, "CompilationTocOwnership.Field.ParagraphRange");
					ComObjectRelease.Release(ref value6, "CompilationTocOwnership.Field.Paragraph");
					ComObjectRelease.Release(ref value5, "CompilationTocOwnership.Field.Paragraphs");
					ComObjectRelease.Release(ref value4, "CompilationTocOwnership.Field.Result");
					ComObjectRelease.Release(ref value3, "CompilationTocOwnership.Field.Code");
					ComObjectRelease.Release(ref value2, "CompilationTocOwnership.Field");
				}
			}
			return list;
		}
		finally
		{
			ComObjectRelease.Release(ref value, "CompilationTocOwnership.Fields");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static List<TitleLinkFact> CollectTitleLinkFacts(Document document, Dictionary<string, string> expectedTitles)
	{
		List<TitleLinkFact> list = new List<TitleLinkFact>();
		Fields value = null;
		try
		{
			value = document.Fields;
			for (int i = 1; i <= value.Count; i++)
			{
				Field value2 = null;
				Microsoft.Office.Interop.Word.Range value3 = null;
				Microsoft.Office.Interop.Word.Range value4 = null;
				try
				{
					value2 = value[i];
					value3 = value2.Code;
					if (CompilationTocOwnershipAnalyzer.TryParseArticleHyperlinkBookmark(value3?.Text, out var bookmarkName) && expectedTitles.ContainsKey(bookmarkName))
					{
						value4 = value2.Result;
						list.Add(new TitleLinkFact
						{
							BookmarkName = bookmarkName,
							FieldStart = Math.Max(0, value3.Start - 1),
							ResultEnd = value4.End
						});
					}
				}
				finally
				{
					ComObjectRelease.Release(ref value4, "CompilationTocOwnership.TitleFact.Result");
					ComObjectRelease.Release(ref value3, "CompilationTocOwnership.TitleFact.Code");
					ComObjectRelease.Release(ref value2, "CompilationTocOwnership.TitleFact.Field");
				}
			}
			return list;
		}
		finally
		{
			ComObjectRelease.Release(ref value, "CompilationTocOwnership.TitleFact.Fields");
		}
	}

	private static TitleLinkFact FindAdjacentTitleLink(IList<TitleLinkFact> titleLinks, string bookmarkName, int pageRefCodeStart)
	{
		TitleLinkFact titleLinkFact = null;
		foreach (TitleLinkFact titleLink in titleLinks)
		{
			if (titleLink != null && string.Equals(titleLink.BookmarkName, bookmarkName, StringComparison.OrdinalIgnoreCase) && titleLink.ResultEnd <= pageRefCodeStart && pageRefCodeStart - titleLink.ResultEnd <= 4 && (titleLinkFact == null || titleLink.ResultEnd > titleLinkFact.ResultEnd))
			{
				titleLinkFact = titleLink;
			}
		}
		return titleLinkFact;
	}

	private static Dictionary<string, string> BuildExpectedTitles(IList<CompilationTocEntry> entries)
	{
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (entries == null)
		{
			return dictionary;
		}
		foreach (CompilationTocEntry entry in entries)
		{
			if (entry != null && !string.IsNullOrWhiteSpace(entry.BookmarkName))
			{
				string text = NormalizeVisibleText(entry.Title);
				if (text.Length != 0)
				{
					dictionary[entry.BookmarkName] = text;
				}
			}
		}
		return dictionary;
	}

	private static bool ParagraphStartsWithTitle(string paragraphText, string expectedTitle)
	{
		return NormalizeVisibleText(paragraphText).StartsWith(expectedTitle, StringComparison.Ordinal);
	}

	private static bool ParagraphHasTocEntryShape(string paragraphText)
	{
		if (!string.IsNullOrEmpty(paragraphText))
		{
			int num = paragraphText.LastIndexOf('\t');
			if (num > 0 && num < paragraphText.Length - 1)
			{
				string text = NormalizeVisibleText(paragraphText.Substring(0, num));
				string text2 = NormalizeVisibleText(paragraphText.Substring(num + 1));
				if (text.Length > 0)
				{
					return text2.Length > 0;
				}
				return false;
			}
			return false;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string NormalizeVisibleText(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return string.Empty;
		}
		return text.Replace("\r", string.Empty).Replace("\n", string.Empty).Replace("\a", string.Empty)
			.Replace("\v", string.Empty)
			.Replace("\f", string.Empty)
			.Trim();
	}

	private static void ExtendCandidateRegion(Document document, CompilationTocOwnedRegion region, IList<CompilationTocEntry> entries, CompilationTocOptions options, bool includeTrailingSelectionTolerance)
	{
		if (region != null)
		{
			ExtendStartWithTitle(document, region, options);
			if (includeTrailingSelectionTolerance)
			{
				ExtendEndWithStructuralParagraphs(document, region, entries);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ExtendStartWithTitle(Document document, CompilationTocOwnedRegion region, CompilationTocOptions options)
	{
		if (region.Start <= 0)
		{
			return;
		}
		Microsoft.Office.Interop.Word.Range value = null;
		Paragraphs value2 = null;
		Paragraph value3 = null;
		Microsoft.Office.Interop.Word.Range value4 = null;
		try
		{
			object Start = region.Start - 1;
			object End = region.Start - 1;
			value = document.Range(ref Start, ref End);
			value2 = value.Paragraphs;
			if (value2 != null && value2.Count != 0)
			{
				value3 = value2[1];
				value4 = value3.Range;
				string a = ParagraphIdentityTextNormalizer.Normalize(value4.Text);
				string text = ((options == null) ? null : NormalizeVisibleText(options.TitleText));
				if (string.Equals(a, "目录", StringComparison.Ordinal) || (!string.IsNullOrEmpty(text) && string.Equals(a, text, StringComparison.Ordinal)))
				{
					region.Start = value4.Start;
				}
			}
		}
		finally
		{
			ComObjectRelease.Release(ref value4, "CompilationTocOwnership.Title.Range");
			ComObjectRelease.Release(ref value3, "CompilationTocOwnership.Title.Paragraph");
			ComObjectRelease.Release(ref value2, "CompilationTocOwnership.Title.Paragraphs");
			ComObjectRelease.Release(ref value, "CompilationTocOwnership.Title.Probe");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ExtendEndWithStructuralParagraphs(Document document, CompilationTocOwnedRegion region, IList<CompilationTocEntry> entries)
	{
		int num = ResolveFirstArticleStart(document, entries);
		for (int i = 0; i < 2; i++)
		{
			if (region.End >= num)
			{
				break;
			}
			Microsoft.Office.Interop.Word.Range value = null;
			Paragraphs value2 = null;
			Paragraph value3 = null;
			Microsoft.Office.Interop.Word.Range value4 = null;
			try
			{
				object Start = region.End;
				object End = region.End;
				value = document.Range(ref Start, ref End);
				value2 = value.Paragraphs;
				if (value2 == null || value2.Count == 0)
				{
					break;
				}
				value3 = value2[1];
				value4 = value3.Range;
				if (value4.Start < region.End || value4.End > num || !IsStructuralOnly(value4.Text) || value4.End <= region.End)
				{
					break;
				}
				region.End = value4.End;
			}
			finally
			{
				ComObjectRelease.Release(ref value4, "CompilationTocOwnership.Trailing.Range");
				ComObjectRelease.Release(ref value3, "CompilationTocOwnership.Trailing.Paragraph");
				ComObjectRelease.Release(ref value2, "CompilationTocOwnership.Trailing.Paragraphs");
				ComObjectRelease.Release(ref value, "CompilationTocOwnership.Trailing.Probe");
			}
		}
	}

	private static bool IsStructuralOnly(string text)
	{
		if (!string.IsNullOrEmpty(text))
		{
			foreach (char c in text)
			{
				if (c != '\r' && c != '\n' && c != '\a' && c != '\v' && c != '\f' && !char.IsWhiteSpace(c))
				{
					return false;
				}
			}
			return true;
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ResolveFirstArticleStart(Document document, IList<CompilationTocEntry> entries)
	{
		int num = int.MaxValue;
		if (entries == null)
		{
			return num;
		}
		Bookmarks value = null;
		try
		{
			value = document.Bookmarks;
			foreach (CompilationTocEntry entry in entries)
			{
				if (entry == null || string.IsNullOrWhiteSpace(entry.BookmarkName) || !value.Exists(entry.BookmarkName))
				{
					continue;
				}
				Bookmark value2 = null;
				Microsoft.Office.Interop.Word.Range value3 = null;
				try
				{
					Bookmarks bookmarks = value;
					object Index = entry.BookmarkName;
					value2 = bookmarks.get_Item(ref Index);
					value3 = value2.Range;
					if (value3.Start < num)
					{
						num = value3.Start;
					}
				}
				finally
				{
					ComObjectRelease.Release(ref value3, "CompilationTocOwnership.Article.Range");
					ComObjectRelease.Release(ref value2, "CompilationTocOwnership.Article.Bookmark");
				}
			}
			return num;
		}
		finally
		{
			ComObjectRelease.Release(ref value, "CompilationTocOwnership.Article.Bookmarks");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool TryGetMarkedRegion(Document document, out int start, out int end)
	{
		start = 0;
		end = 0;
		Bookmarks value = null;
		Bookmark value2 = null;
		Bookmark value3 = null;
		Microsoft.Office.Interop.Word.Range value4 = null;
		Microsoft.Office.Interop.Word.Range value5 = null;
		try
		{
			value = document.Bookmarks;
			if (value.Exists("SXCF_TOC_BEGIN") && value.Exists("SXCF_TOC_END"))
			{
				Bookmarks bookmarks = value;
				object Index = "SXCF_TOC_BEGIN";
				value2 = bookmarks.get_Item(ref Index);
				Bookmarks bookmarks2 = value;
				Index = "SXCF_TOC_END";
				value3 = bookmarks2.get_Item(ref Index);
				value4 = value2.Range;
				value5 = value3.Range;
				start = value4.Start;
				end = value5.Start;
				return end > start;
			}
			return false;
		}
		catch
		{
			return false;
		}
		finally
		{
			ComObjectRelease.Release(ref value5, "CompilationTocOwnership.Marked.EndRange");
			ComObjectRelease.Release(ref value4, "CompilationTocOwnership.Marked.BeginRange");
			ComObjectRelease.Release(ref value3, "CompilationTocOwnership.Marked.EndBookmark");
			ComObjectRelease.Release(ref value2, "CompilationTocOwnership.Marked.BeginBookmark");
			ComObjectRelease.Release(ref value, "CompilationTocOwnership.Marked.Bookmarks");
		}
	}
}
