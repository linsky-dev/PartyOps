using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.CompilationFormatting;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.CompilationFormatting;

public static class CompilationSeparatorService
{
	public const string KindPage = "page";

	public const string KindSection = "section";

	public const string KindNone = "none";

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string AnchorName(int orderIndex)
	{
		return "SXCF_SEP_" + orderIndex.ToString("000");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string ResolveDesiredKind(CompilationFormatOptions options)
	{
		if (options != null)
		{
			if (options.PageNumberMode != CompilationPageNumberMode.RestartPerArticle)
			{
				if (options.StartEachArticleOnNewPage)
				{
					return "page";
				}
				return "none";
			}
			return "section";
		}
		return "none";
	}

	public static string FindConflict(Document document, CompilationArticleList articleList, CompilationManifest manifest, CompilationFormatOptions options)
	{
		return FindConflict(document, articleList, manifest, options, null);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string FindConflict(Document document, CompilationArticleList articleList, CompilationManifest manifest, CompilationFormatOptions options, CompilationDocumentScanResult scan)
	{
		bool flag = scan != null && scan.BodyMarkerParagraphIndexes.Count > 0;
		string text = ResolveDesiredKind(options);
		for (int i = 0; i < articleList.Articles.Count; i++)
		{
			if (i < 1 && (!(text == "section") || (articleList.FrontMatterEndParagraphIndex < 0 && !options.GenerateToc)))
			{
				continue;
			}
			CompilationArticleInfo compilationArticleInfo = articleList.Articles[i];
			if (!flag)
			{
				CompilationSeparatorRecord record = GetRecord(manifest, compilationArticleInfo.OrderIndex);
				if (record != null && VerifyOwnership(document, compilationArticleInfo, record))
				{
					continue;
				}
			}
			bool flag2;
			bool flag3;
			if (flag)
			{
				int index = scan.BodyMarkerParagraphIndexes[i] + 1;
				Microsoft.Office.Interop.Word.Range value = null;
				try
				{
					object Start = scan.ParagraphStarts[index];
					object End = scan.ParagraphEnds[index];
					value = document.Range(ref Start, ref End);
					flag2 = HasPageBreakBeforeRange(value);
					flag3 = IsRangeAtSectionStart(value);
				}
				finally
				{
					ComObjectRelease.Release(ref value, "CompilationSeparator.PrecheckTitleRange");
				}
			}
			else
			{
				flag2 = HasPageBreakBeforeTitle(document, compilationArticleInfo);
				flag3 = IsAtSectionStart(document, compilationArticleInfo);
			}
			if (!(text == "section" && flag2) || flag3)
			{
				continue;
			}
			return "第 " + compilationArticleInfo.OrderIndex + " 篇标题前存在用户插入的分页符，与当前模板的“每篇重新开始页码”（需要分节符）冲突。已在修改文档前停止，请手工删除该分页符或调整模板后重试。";
		}
		return null;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool Reconcile(Document document, CompilationArticleList articleList, CompilationManifest manifest, CompilationFormatOptions options)
	{
		if (document != null)
		{
			if (articleList != null && manifest != null)
			{
				string text = ResolveDesiredKind(options);
				bool result = false;
				bool flag = text == "section" && (articleList.FrontMatterEndParagraphIndex >= 0 || options.GenerateToc);
				for (int i = 0; i < articleList.Articles.Count; i++)
				{
					CompilationArticleInfo compilationArticleInfo = articleList.Articles[i];
					bool flag2 = i >= 1 || flag;
					string desired = text;
					if (i != 0)
					{
						if ((flag2 || GetRecord(manifest, compilationArticleInfo.OrderIndex) != null) && ReconcileArticle(document, compilationArticleInfo, manifest, desired, flag2))
						{
							result = true;
						}
					}
					else if (flag && !IsAtSectionStart(document, compilationArticleInfo))
					{
						InsertBreakOwned(document, compilationArticleInfo, sectionBreak: true);
						string text2 = AnchorName(compilationArticleInfo.OrderIndex);
						if (document.Bookmarks.Exists(text2))
						{
							Bookmarks bookmarks = document.Bookmarks;
							object Index = text2;
							bookmarks.get_Item(ref Index).Delete();
						}
						SetRecord(manifest, compilationArticleInfo.OrderIndex, null, null);
						result = true;
					}
				}
				return result;
			}
			return false;
		}
		throw new ArgumentNullException("document");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool ReconcileArticle(Document document, CompilationArticleInfo article, CompilationManifest manifest, string desired, bool inScope)
	{
		CompilationSeparatorRecord record = GetRecord(manifest, article.OrderIndex);
		bool flag = record != null && VerifyOwnership(document, article, record);
		if (record != null && !flag)
		{
			SetRecord(manifest, article.OrderIndex, null, null);
			LogService.Info("汇编排版：第 " + article.OrderIndex + " 篇分隔符所有权凭证失效，按用户所有保留。");
		}
		if (!flag)
		{
			if (!inScope || desired == "none")
			{
				return false;
			}
			bool flag2 = HasPageBreakBeforeTitle(document, article);
			bool flag3 = IsAtSectionStart(document, article);
			if (!(desired == "page"))
			{
				if (desired == "section")
				{
					if (!flag3)
					{
						if (!flag2)
						{
							InsertBreakOwned(document, article, sectionBreak: true);
							SetRecord(manifest, article.OrderIndex, "section", AnchorName(article.OrderIndex));
							return true;
						}
						throw new InvalidOperationException("第 " + article.OrderIndex + " 篇标题前存在用户分页符，无法安全转换，已停止。");
					}
					return false;
				}
				return false;
			}
			if (flag2 || flag3)
			{
				return false;
			}
			InsertBreakOwned(document, article, sectionBreak: false);
			SetRecord(manifest, article.OrderIndex, "page", AnchorName(article.OrderIndex));
			return true;
		}
		if (!inScope || desired == "none")
		{
			RemoveOwnedBreak(document, article, record);
			SetRecord(manifest, article.OrderIndex, null, null);
			return true;
		}
		if (!(record.Kind == desired))
		{
			RemoveOwnedBreak(document, article, record);
			InsertBreakOwned(document, article, desired == "section");
			SetRecord(manifest, article.OrderIndex, desired, AnchorName(article.OrderIndex));
			return true;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool VerifyOwnership(Document document, CompilationArticleInfo article, CompilationSeparatorRecord record)
	{
		if (string.IsNullOrWhiteSpace(record.AnchorBookmark))
		{
			return false;
		}
		if (!string.Equals(record.AnchorBookmark, AnchorName(record.OrderIndex), StringComparison.Ordinal))
		{
			return false;
		}
		if (record.OrderIndex != article.OrderIndex)
		{
			return false;
		}
		if (document.Bookmarks.Exists(record.AnchorBookmark))
		{
			Microsoft.Office.Interop.Word.Range range = null;
			Paragraph paragraph = null;
			Microsoft.Office.Interop.Word.Range range2 = null;
			Paragraph paragraph2 = null;
			Microsoft.Office.Interop.Word.Range range3 = null;
			try
			{
				Bookmarks bookmarks = document.Bookmarks;
				object Index = record.AnchorBookmark;
				range = bookmarks.get_Item(ref Index).Range;
				paragraph = range.Paragraphs[1];
				range2 = paragraph.Range;
				string text = range2.Text ?? "";
				if (text.IndexOf('\f') < 0 || text.Replace("\f", "").Replace("\r", "").Trim()
					.Length != 0)
				{
					return false;
				}
				Paragraph paragraph3 = paragraph;
				Index = Type.Missing;
				paragraph2 = paragraph3.Next(ref Index);
				int num = 0;
				int num2 = -1;
				while (true)
				{
					if (paragraph2 != null && num++ < 6)
					{
						range3 = paragraph2.Range;
						string text2 = (range3.Text ?? "").TrimEnd('\r', '\a');
						if (text2.IndexOf('\f') >= 0 && text2.Replace("\f", "").Trim().Length == 0)
						{
							ComObjectRelease.Release(range3, "CompilationSeparator.VerifyWalkRange");
							range3 = null;
							Paragraph paragraph4 = paragraph2;
							Index = Type.Missing;
							Paragraph paragraph5 = paragraph4.Next(ref Index);
							ComObjectRelease.Release(paragraph2, "CompilationSeparator.VerifyWalkPara");
							paragraph2 = paragraph5;
							continue;
						}
						if (string.IsNullOrWhiteSpace(text2))
						{
							break;
						}
						num2 = range3.Sections[1].Index;
					}
					if (paragraph2 != null && num2 >= 0)
					{
						int index = range2.Sections[1].Index;
						if (!(record.Kind == "page") || index == num2)
						{
							if (record.Kind == "section" && index == num2)
							{
								return false;
							}
							return true;
						}
						return false;
					}
					return false;
				}
				return false;
			}
			catch
			{
				return false;
			}
			finally
			{
				ComObjectRelease.Release(range3, "CompilationSeparator.VerifyNextRange");
				ComObjectRelease.Release(paragraph2, "CompilationSeparator.VerifyNext");
				ComObjectRelease.Release(range2, "CompilationSeparator.VerifyAnchorRange");
				ComObjectRelease.Release(paragraph, "CompilationSeparator.VerifyAnchorPara");
				ComObjectRelease.Release(range, "CompilationSeparator.VerifyAnchor");
			}
		}
		return false;
	}

	private static CompilationSeparatorRecord GetRecord(CompilationManifest manifest, int orderIndex)
	{
		if (manifest.Separators == null)
		{
			return null;
		}
		foreach (CompilationSeparatorRecord separator in manifest.Separators)
		{
			if (separator != null && separator.OrderIndex == orderIndex)
			{
				return separator;
			}
		}
		return null;
	}

	private static void SetRecord(CompilationManifest manifest, int orderIndex, string kind, string anchor)
	{
		if (manifest.Separators == null)
		{
			manifest.Separators = new List<CompilationSeparatorRecord>();
		}
		for (int num = manifest.Separators.Count - 1; num >= 0; num--)
		{
			if (manifest.Separators[num] != null && manifest.Separators[num].OrderIndex == orderIndex)
			{
				manifest.Separators.RemoveAt(num);
			}
		}
		if (kind != null)
		{
			manifest.Separators.Add(new CompilationSeparatorRecord
			{
				OrderIndex = orderIndex,
				Kind = kind,
				AnchorBookmark = anchor
			});
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool IsInsideCompilationTocRegion(Document document, int position)
	{
		Microsoft.Office.Interop.Word.Range range = null;
		Microsoft.Office.Interop.Word.Range range2 = null;
		try
		{
			if (!document.Bookmarks.Exists("SXCF_TOC_BEGIN") || !document.Bookmarks.Exists("SXCF_TOC_END"))
			{
				return false;
			}
			Bookmarks bookmarks = document.Bookmarks;
			object Index = "SXCF_TOC_BEGIN";
			range = bookmarks.get_Item(ref Index).Range;
			Bookmarks bookmarks2 = document.Bookmarks;
			Index = "SXCF_TOC_END";
			range2 = bookmarks2.get_Item(ref Index).Range;
			return position >= range.Start && position < range2.Start;
		}
		catch
		{
			return false;
		}
		finally
		{
			ComObjectRelease.Release(range2, "CompilationSeparator.TocEnd");
			ComObjectRelease.Release(range, "CompilationSeparator.TocBegin");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool HasPageBreakBeforeTitle(Document document, CompilationArticleInfo article)
	{
		Paragraph titleParagraph = GetTitleParagraph(document, article);
		if (titleParagraph == null)
		{
			return false;
		}
		Paragraph paragraph = null;
		Microsoft.Office.Interop.Word.Range range = null;
		Microsoft.Office.Interop.Word.Range range2 = null;
		try
		{
			object Count = Type.Missing;
			paragraph = titleParagraph.Previous(ref Count);
			if (paragraph == null)
			{
				return false;
			}
			range = paragraph.Range;
			if ((range.Text ?? "").IndexOf('\f') >= 0)
			{
				if (IsInsideCompilationTocRegion(document, range.Start))
				{
					return false;
				}
				int index = range.Sections[1].Index;
				range2 = titleParagraph.Range;
				int index2 = range2.Sections[1].Index;
				return index == index2;
			}
			return false;
		}
		catch
		{
			return false;
		}
		finally
		{
			ComObjectRelease.Release(range2, "CompilationSeparator.HasBrTitleRange");
			ComObjectRelease.Release(range, "CompilationSeparator.HasBrPrevRange");
			ComObjectRelease.Release(paragraph, "CompilationSeparator.HasBrPrev");
			ComObjectRelease.Release(titleParagraph, "CompilationSeparator.HasBrTitle");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool HasPageBreakBeforeRange(Microsoft.Office.Interop.Word.Range titleRange)
	{
		Paragraph paragraph = null;
		Paragraph paragraph2 = null;
		Microsoft.Office.Interop.Word.Range range = null;
		try
		{
			paragraph = titleRange.Paragraphs[1];
			Paragraph paragraph3 = paragraph;
			object Count = Type.Missing;
			paragraph2 = paragraph3.Previous(ref Count);
			if (paragraph2 != null)
			{
				range = paragraph2.Range;
				if ((range.Text ?? "").IndexOf('\f') >= 0)
				{
					if (IsInsideCompilationTocRegion(titleRange.Document, range.Start))
					{
						return false;
					}
					int index = range.Sections[1].Index;
					int index2 = titleRange.Sections[1].Index;
					return index == index2;
				}
				return false;
			}
			return false;
		}
		catch
		{
			return false;
		}
		finally
		{
			ComObjectRelease.Release(range, "CompilationSeparator.RangePrevRange");
			ComObjectRelease.Release(paragraph2, "CompilationSeparator.RangePrev");
			ComObjectRelease.Release(paragraph, "CompilationSeparator.RangeTitle");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool IsRangeAtSectionStart(Microsoft.Office.Interop.Word.Range range)
	{
		Section section = null;
		Microsoft.Office.Interop.Word.Range range2 = null;
		try
		{
			section = range.Sections[1];
			range2 = section.Range;
			return Math.Abs(range2.Start - range.Start) <= 1;
		}
		catch
		{
			return false;
		}
		finally
		{
			ComObjectRelease.Release(range2, "CompilationSeparator.RangeSectionRange");
			ComObjectRelease.Release(section, "CompilationSeparator.RangeSection");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool IsAtSectionStart(Document document, CompilationArticleInfo article)
	{
		Microsoft.Office.Interop.Word.Range range = null;
		Section section = null;
		Microsoft.Office.Interop.Word.Range range2 = null;
		try
		{
			if (!document.Bookmarks.Exists(article.BeginBookmarkName))
			{
				return false;
			}
			Bookmarks bookmarks = document.Bookmarks;
			object Index = article.BeginBookmarkName;
			range = bookmarks.get_Item(ref Index).Range;
			section = range.Sections[1];
			range2 = section.Range;
			return Math.Abs(range2.Start - range.Start) <= 1;
		}
		catch
		{
			return false;
		}
		finally
		{
			ComObjectRelease.Release(range2, "CompilationSeparator.SectionRange");
			ComObjectRelease.Release(section, "CompilationSeparator.Section");
			ComObjectRelease.Release(range, "CompilationSeparator.Begin");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void InsertBreakOwned(Document document, CompilationArticleInfo article, bool sectionBreak)
	{
		Paragraph titleParagraph = GetTitleParagraph(document, article);
		if (titleParagraph == null)
		{
			throw new InvalidOperationException("第 " + article.OrderIndex + " 篇标题段不可读，已停止执行。");
		}
		Microsoft.Office.Interop.Word.Range range = null;
		Microsoft.Office.Interop.Word.Range range2 = null;
		try
		{
			range = titleParagraph.Range;
			int start = range.Start;
			object Start = start;
			object End = start;
			range2 = document.Range(ref Start, ref End);
			Microsoft.Office.Interop.Word.Range range3 = range2;
			End = (sectionBreak ? WdBreakType.wdSectionBreakNextPage : WdBreakType.wdPageBreak);
			range3.InsertBreak(ref End);
		}
		finally
		{
			ComObjectRelease.Release(range2, "CompilationSeparator.InsertCursor");
			ComObjectRelease.Release(range, "CompilationSeparator.InsertTitleRange");
			ComObjectRelease.Release(titleParagraph, "CompilationSeparator.InsertTitlePara");
		}
		ReanchorBeginToTitle(document, article);
		Paragraph paragraph = null;
		Paragraph paragraph2 = null;
		Microsoft.Office.Interop.Word.Range range4 = null;
		Microsoft.Office.Interop.Word.Range range5 = null;
		try
		{
			paragraph = GetTitleParagraph(document, article);
			object obj;
			object End;
			if (paragraph != null)
			{
				Paragraph paragraph3 = paragraph;
				End = Type.Missing;
				obj = paragraph3.Previous(ref End);
			}
			else
			{
				obj = null;
			}
			paragraph2 = (Paragraph)obj;
			if (paragraph2 == null)
			{
				throw new InvalidOperationException("第 " + article.OrderIndex + " 篇分隔符段定位失败，已停止执行。");
			}
			range4 = paragraph2.Range;
			End = range4.Start;
			object Start = range4.Start;
			range5 = document.Range(ref End, ref Start);
			string text = AnchorName(article.OrderIndex);
			if (document.Bookmarks.Exists(text))
			{
				Bookmarks bookmarks = document.Bookmarks;
				Start = text;
				bookmarks.get_Item(ref Start).Delete();
			}
			Bookmarks bookmarks2 = document.Bookmarks;
			Start = range5;
			bookmarks2.Add(text, ref Start);
		}
		finally
		{
			ComObjectRelease.Release(range5, "CompilationSeparator.AnchorRange");
			ComObjectRelease.Release(range4, "CompilationSeparator.BreakRange");
			ComObjectRelease.Release(paragraph2, "CompilationSeparator.BreakPara");
			ComObjectRelease.Release(paragraph, "CompilationSeparator.NewTitle");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void RemoveOwnedBreak(Document document, CompilationArticleInfo article, CompilationSeparatorRecord record)
	{
		Microsoft.Office.Interop.Word.Range range = null;
		Paragraph paragraph = null;
		Microsoft.Office.Interop.Word.Range range2 = null;
		try
		{
			Bookmarks bookmarks = document.Bookmarks;
			object Index = record.AnchorBookmark;
			range = bookmarks.get_Item(ref Index).Range;
			paragraph = range.Paragraphs[1];
			range2 = paragraph.Range;
			Microsoft.Office.Interop.Word.Range range3 = range2;
			Index = Type.Missing;
			object Count = Type.Missing;
			range3.Delete(ref Index, ref Count);
			Bookmarks bookmarks2 = document.Bookmarks;
			Count = record.AnchorBookmark;
			bookmarks2.get_Item(ref Count).Delete();
			ReanchorBeginToTitle(document, article);
		}
		finally
		{
			ComObjectRelease.Release(range2, "CompilationSeparator.RemoveRange");
			ComObjectRelease.Release(paragraph, "CompilationSeparator.RemovePara");
			ComObjectRelease.Release(range, "CompilationSeparator.RemoveAnchor");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static List<int> RecordUserBreaksBeforeTitles(Document document, CompilationArticleList articleList, CompilationManifest manifest)
	{
		List<int> list = new List<int>();
		if (document == null || articleList == null)
		{
			return list;
		}
		foreach (CompilationArticleInfo article in articleList.Articles)
		{
			if (GetRecord(manifest, article.OrderIndex) == null && HasPageBreakBeforeTitle(document, article))
			{
				list.Add(article.OrderIndex);
				LogService.Info("汇编排版：记录第 " + article.OrderIndex + " 篇标题前的用户分页段。");
			}
		}
		return list;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void RecreateUserBreaksBeforeTitles(Document document, CompilationArticleList articleList, List<int> orderIndexes)
	{
		if (document == null || articleList == null || orderIndexes == null)
		{
			return;
		}
		foreach (int orderIndex in orderIndexes)
		{
			CompilationArticleInfo compilationArticleInfo = null;
			foreach (CompilationArticleInfo article in articleList.Articles)
			{
				if (article.OrderIndex == orderIndex)
				{
					compilationArticleInfo = article;
					break;
				}
			}
			if (compilationArticleInfo == null || HasPageBreakBeforeTitle(document, compilationArticleInfo))
			{
				continue;
			}
			Paragraph titleParagraph = GetTitleParagraph(document, compilationArticleInfo);
			if (titleParagraph != null)
			{
				Microsoft.Office.Interop.Word.Range range = null;
				Microsoft.Office.Interop.Word.Range range2 = null;
				try
				{
					range = titleParagraph.Range;
					object Start = range.Start;
					object End = range.Start;
					range2 = document.Range(ref Start, ref End);
					Microsoft.Office.Interop.Word.Range range3 = range2;
					End = WdBreakType.wdPageBreak;
					range3.InsertBreak(ref End);
					LogService.Info("汇编排版：重建第 " + orderIndex + " 篇标题前的用户分页段。");
				}
				finally
				{
					ComObjectRelease.Release(range2, "CompilationSeparator.RecreateCursor");
					ComObjectRelease.Release(range, "CompilationSeparator.RecreateTitleRange");
					ComObjectRelease.Release(titleParagraph, "CompilationSeparator.RecreateTitle");
				}
				ReanchorBeginToTitle(document, compilationArticleInfo);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ReanchorBeginToTitle(Document document, CompilationArticleInfo article)
	{
		Paragraph titleParagraph = GetTitleParagraph(document, article);
		if (titleParagraph == null)
		{
			throw new InvalidOperationException("第 " + article.OrderIndex + " 篇无法在分隔符后定位标题段，已停止执行。");
		}
		Microsoft.Office.Interop.Word.Range range = null;
		Microsoft.Office.Interop.Word.Range range2 = null;
		try
		{
			range = titleParagraph.Range;
			int start = range.Start;
			if (!BeginEquals(document, article.BeginBookmarkName, start))
			{
				object Index;
				if (document.Bookmarks.Exists(article.BeginBookmarkName))
				{
					Bookmarks bookmarks = document.Bookmarks;
					Index = article.BeginBookmarkName;
					bookmarks.get_Item(ref Index).Delete();
				}
				Index = start;
				object End = start;
				range2 = document.Range(ref Index, ref End);
				Bookmarks bookmarks2 = document.Bookmarks;
				string beginBookmarkName = article.BeginBookmarkName;
				End = range2;
				bookmarks2.Add(beginBookmarkName, ref End);
			}
		}
		finally
		{
			ComObjectRelease.Release(range2, "CompilationSeparator.FinalBegin");
			ComObjectRelease.Release(range, "CompilationSeparator.TitleRange");
			ComObjectRelease.Release(titleParagraph, "CompilationSeparator.TitlePara");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool BeginEquals(Document document, string name, int position)
	{
		Microsoft.Office.Interop.Word.Range range = null;
		try
		{
			Bookmarks bookmarks = document.Bookmarks;
			object Index = name;
			range = bookmarks.get_Item(ref Index).Range;
			return range.Start == position;
		}
		finally
		{
			ComObjectRelease.Release(range, "CompilationSeparator.BeginEquals");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static Paragraph GetTitleParagraph(Document document, CompilationArticleInfo article)
	{
		if (!document.Bookmarks.Exists(article.BeginBookmarkName))
		{
			throw new InvalidOperationException("第 " + article.OrderIndex + " 篇的内部文章边界已损坏或丢失，已停止执行。");
		}
		Bookmarks bookmarks = document.Bookmarks;
		object Index = article.BeginBookmarkName;
		Microsoft.Office.Interop.Word.Range range = bookmarks.get_Item(ref Index).Range;
		try
		{
			Paragraph paragraph = range.Paragraphs[1];
			int num = 0;
			while (true)
			{
				if (paragraph != null && num++ < 8)
				{
					Microsoft.Office.Interop.Word.Range range2 = paragraph.Range;
					string text = (range2.Text ?? "").TrimEnd('\r', '\a');
					ComObjectRelease.Release(range2, "CompilationSeparator.TitleProbeRange");
					if (!string.IsNullOrWhiteSpace(text) && text.IndexOf('\f') < 0)
					{
						break;
					}
					Paragraph paragraph2 = paragraph;
					Index = Type.Missing;
					Paragraph paragraph3 = paragraph2.Next(ref Index);
					ComObjectRelease.Release(paragraph, "CompilationSeparator.TitleProbePara");
					paragraph = paragraph3;
					continue;
				}
				ComObjectRelease.Release(paragraph, "CompilationSeparator.TitleProbeFinal");
				return null;
			}
			return paragraph;
		}
		finally
		{
			ComObjectRelease.Release(range, "CompilationSeparator.TitleProbeBegin");
		}
	}
}
