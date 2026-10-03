using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.CompilationFormatting;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.CompilationFormatting;

public static class CompilationTocService
{
	private const string BookmarkPrefix = "SXCF_";

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void InsertTocRegion(Document document, IList<CompilationTocEntry> entries, CompilationTocOptions options, int insertPosition)
	{
		if (document == null)
		{
			return;
		}
		if (entries == null || entries.Count == 0)
		{
			throw new InvalidOperationException("汇编目录没有可用条目，请先确认文章标题识别结果。");
		}
		CompilationTocOptions options2 = options ?? new CompilationTocOptions();
		try
		{
			InsertBookmarkTocRegion(document, entries, options2, insertPosition);
		}
		catch (Exception ex)
		{
			LogService.Error("CompilationTocService.InsertTocRegion", ex);
			throw;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void RefreshRegionPageRefs(Document document)
	{
		if (document == null)
		{
			return;
		}
		try
		{
			document.Repaginate();
		}
		catch (Exception ex)
		{
			LogService.Warn("CompilationTocService.RefreshRegionPageRefs.Repaginate", ex);
		}
		ForcePaginate(document);
		List<Field> list = new List<Field>();
		foreach (Field field in document.Fields)
		{
			Microsoft.Office.Interop.Word.Range range = null;
			try
			{
				range = field.Code;
				if (((range == null) ? "" : (range.Text ?? "")).IndexOf("PAGEREF SXCF_", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					list.Add(field);
				}
			}
			catch
			{
			}
			finally
			{
				if (range != null)
				{
					ComObjectRelease.ReleaseOwned(range, "RefreshRegionPageRefs", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\Services\\CompilationFormatting\\CompilationTocService.cs", 69);
				}
			}
		}
		foreach (Field item in list)
		{
			item.Update();
		}
		ForcePaginate(document);
		foreach (Field item2 in list)
		{
			item2.Update();
		}
	}

	public static bool ShouldInsertAutoToc(Document document, CompilationTocOptions options)
	{
		if (options != null && options.ExistingTocPolicy == CompilationExistingTocPolicy.Cancel)
		{
			return CountExistingTocFields(document) == 0;
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CountExistingTocFields(Document document)
	{
		if (document == null)
		{
			return 0;
		}
		Fields fields = null;
		try
		{
			fields = document.Fields;
			int num = 0;
			for (int i = 1; i <= fields.Count; i++)
			{
				Field field = null;
				try
				{
					field = fields[i];
					if (field.Type == WdFieldType.wdFieldTOC)
					{
						num++;
					}
				}
				finally
				{
					if (field != null)
					{
						ComObjectRelease.ReleaseOwned(field, "CountExistingTocFields", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\Services\\CompilationFormatting\\CompilationTocService.cs", 108);
					}
				}
			}
			return num;
		}
		finally
		{
			if (fields != null)
			{
				ComObjectRelease.ReleaseOwned(fields, "CountExistingTocFields", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\Services\\CompilationFormatting\\CompilationTocService.cs", 115);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void DeleteExistingTocFields(Document document)
	{
		if (document == null)
		{
			return;
		}
		while (CountExistingTocFields(document) > 0)
		{
			Fields fields = null;
			Field field = null;
			try
			{
				fields = document.Fields;
				for (int num = fields.Count; num >= 1; num--)
				{
					Field field2 = fields[num];
					if (field2.Type == WdFieldType.wdFieldTOC)
					{
						field = field2;
						break;
					}
					ComObjectRelease.ReleaseOwned(field2, "DeleteExistingTocFields", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\Services\\CompilationFormatting\\CompilationTocService.cs", 141);
				}
				if (field == null)
				{
					break;
				}
				Microsoft.Office.Interop.Word.Range range = null;
				try
				{
					range = field.Result;
					Microsoft.Office.Interop.Word.Range range2 = range;
					object Unit = Type.Missing;
					object Count = Type.Missing;
					range2.Delete(ref Unit, ref Count);
				}
				finally
				{
					if (range != null)
					{
						ComObjectRelease.ReleaseOwned(range, "DeleteExistingTocFields", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\Services\\CompilationFormatting\\CompilationTocService.cs", 153);
					}
				}
				field.Delete();
			}
			finally
			{
				if (field != null)
				{
					ComObjectRelease.ReleaseOwned(field, "DeleteExistingTocFields", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\Services\\CompilationFormatting\\CompilationTocService.cs", 159);
				}
				if (fields != null)
				{
					ComObjectRelease.ReleaseOwned(fields, "DeleteExistingTocFields", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\Services\\CompilationFormatting\\CompilationTocService.cs", 160);
				}
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void InsertBookmarkTocRegion(Document document, IList<CompilationTocEntry> entries, CompilationTocOptions options, int insertPosition)
	{
		string text = "SXCF_TOC_BEGIN";
		string text2 = "SXCF_TOC_END";
		Microsoft.Office.Interop.Word.Range value = null;
		Microsoft.Office.Interop.Word.Range value2 = null;
		Microsoft.Office.Interop.Word.Range value3 = null;
		Microsoft.Office.Interop.Word.Range value4 = null;
		string bookmarkName = null;
		try
		{
			value4 = CaptureFirstArticleAnchor(document, entries, out bookmarkName);
			if (value4 != null && !string.IsNullOrWhiteSpace(bookmarkName))
			{
				EnsureBookmarkAt(document, bookmarkName, GetAnchoredCharacterPosition(value4));
			}
			bool flag = BookmarkExists(document, text);
			bool flag2 = BookmarkExists(document, text2);
			int num;
			if (!(flag && flag2))
			{
				if (flag)
				{
					DeleteBookmark(document, text);
				}
				if (flag2)
				{
					DeleteBookmark(document, text2);
				}
				num = insertPosition;
				EnsureBookmarkAt(document, text, insertPosition);
			}
			else
			{
				num = RebuildExistingRegion(document, text, text2);
			}
			object Start = num;
			object End = num;
			value3 = document.Range(ref Start, ref End);
			Microsoft.Office.Interop.Word.Range range = value3;
			End = WdCollapseDirection.wdCollapseEnd;
			range.Collapse(ref End);
			value3.InsertAfter(options.TitleText + "\r");
			Microsoft.Office.Interop.Word.Range range2 = value3;
			End = WdCollapseDirection.wdCollapseEnd;
			range2.Collapse(ref End);
			int start = value3.Start;
			string switches = (options.HyperlinkEntries ? " \\h" : string.Empty);
			foreach (CompilationTocEntry entry in entries)
			{
				if (entry == null)
				{
					continue;
				}
				string text3 = CleanTitle(entry.Title);
				if (!string.IsNullOrWhiteSpace(text3))
				{
					string bookmarkName2 = EnsureArticleBookmark(document, entry);
					int end = value3.End;
					value3.InsertAfter(text3);
					int end2 = value3.End;
					int num2 = end2;
					if (options.HyperlinkEntries)
					{
						num2 = AddEntryTitleHyperlink(document, end, end2, bookmarkName2);
					}
					value3.Start = num2;
					value3.End = num2;
					value3.InsertAfter("\t");
					Microsoft.Office.Interop.Word.Range range3 = value3;
					End = WdCollapseDirection.wdCollapseEnd;
					range3.Collapse(ref End);
					AddPageRefField(document, value3, bookmarkName2, switches);
					Microsoft.Office.Interop.Word.Range range4 = value3;
					End = WdCollapseDirection.wdCollapseEnd;
					range4.Collapse(ref End);
					value3.InsertAfter("\r");
					Microsoft.Office.Interop.Word.Range range5 = value3;
					End = WdCollapseDirection.wdCollapseEnd;
					range5.Collapse(ref End);
				}
			}
			int end3 = value3.End;
			if (options.PageBreakAfterToc)
			{
				Microsoft.Office.Interop.Word.Range range6 = value3;
				End = WdBreakType.wdPageBreak;
				range6.InsertBreak(ref End);
			}
			int end4 = value3.End;
			DeleteBookmark(document, text2);
			EnsureBookmarkAt(document, text2, end4);
			if (start > num)
			{
				End = num;
				Start = start;
				value = document.Range(ref End, ref Start);
				FormatTitleParagraph(value, options);
			}
			if (end3 > start)
			{
				Start = start;
				End = end3;
				value2 = document.Range(ref Start, ref End);
				FormatEntryBody(document, value2, options);
			}
			RepositionEntryBookmarks(document, entries, end4, bookmarkName, value4);
			try
			{
				document.Repaginate();
			}
			catch (Exception ex)
			{
				LogService.Warn("CompilationTocService.Repaginate", ex);
			}
			ForcePaginate(document);
			List<Field> list = new List<Field>();
			foreach (Field field in document.Fields)
			{
				Microsoft.Office.Interop.Word.Range range7 = null;
				try
				{
					range7 = field.Code;
					if (((range7 == null) ? "" : (range7.Text ?? "")).IndexOf("PAGEREF SXCF_", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						list.Add(field);
					}
				}
				catch
				{
				}
				finally
				{
					if (range7 != null)
					{
						ComObjectRelease.ReleaseOwned(range7, "InsertBookmarkTocRegion", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\Services\\CompilationFormatting\\CompilationTocService.cs", 308);
					}
				}
			}
			ForcePaginateEntries(document, entries);
			try
			{
				foreach (Field item in list)
				{
					item.Update();
				}
			}
			catch (Exception ex2)
			{
				LogService.Error("CompilationTocService.InsertTocRegion.UpdateFields", ex2);
				throw;
			}
			ForcePaginateEntries(document, entries);
			try
			{
				foreach (Field item2 in list)
				{
					item2.Update();
				}
			}
			catch (Exception ex3)
			{
				LogService.Error("CompilationTocService.InsertTocRegion.UpdateFields2", ex3);
				throw;
			}
			VerifyTocEntries(document, entries, num, end4, options.HyperlinkEntries);
		}
		finally
		{
			ComObjectRelease.Release(ref value4, "CompilationTocService.Insert.FirstArticleAnchor");
			ComObjectRelease.Release(ref value3, "CompilationTocService.Insert.Cursor");
			ComObjectRelease.Release(ref value2, "CompilationTocService.Insert.BodyRange");
			ComObjectRelease.Release(ref value, "CompilationTocService.Insert.TitleRange");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int RebuildExistingRegion(Document document, string beginMarker, string endMarker)
	{
		Microsoft.Office.Interop.Word.Range value = null;
		Microsoft.Office.Interop.Word.Range value2 = null;
		Microsoft.Office.Interop.Word.Range value3 = null;
		try
		{
			value = GetBookmarkRange(document, beginMarker);
			value2 = GetBookmarkRange(document, endMarker);
			int end = value.End;
			int start = value2.Start;
			if (start > end)
			{
				object Start = end;
				object End = start;
				value3 = document.Range(ref Start, ref End);
				Microsoft.Office.Interop.Word.Range range = value3;
				End = Type.Missing;
				Start = Type.Missing;
				range.Delete(ref End, ref Start);
				DeleteResidualSectionBreakAt(document, end);
			}
			DeleteBookmark(document, endMarker);
			if (!BookmarkExists(document, beginMarker))
			{
				EnsureBookmarkAt(document, beginMarker, end);
			}
			else
			{
				Microsoft.Office.Interop.Word.Range value4 = GetBookmarkRange(document, beginMarker);
				try
				{
					end = value4.End;
				}
				finally
				{
					ComObjectRelease.Release(ref value4, "CompilationTocService.Rebuild.FreshBegin");
				}
			}
			return end;
		}
		finally
		{
			if (value3 != null)
			{
				ComObjectRelease.Release(ref value3, "CompilationTocService.Rebuild.Body");
			}
			if (value2 != null)
			{
				ComObjectRelease.Release(ref value2, "CompilationTocService.Rebuild.EndRange");
			}
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "CompilationTocService.Rebuild.BeginRange");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void FormatTitleParagraph(Microsoft.Office.Interop.Word.Range titleRange, CompilationTocOptions options)
	{
		ParagraphFormat value = null;
		Font value2 = null;
		try
		{
			value = titleRange.ParagraphFormat;
			switch (options.TitleAlignment)
			{
			default:
				value.Alignment = WdParagraphAlignment.wdAlignParagraphCenter;
				break;
			case CompilationTocTitleAlignment.Right:
				value.Alignment = WdParagraphAlignment.wdAlignParagraphRight;
				break;
			case CompilationTocTitleAlignment.Left:
				value.Alignment = WdParagraphAlignment.wdAlignParagraphLeft;
				break;
			}
			value.SpaceBefore = options.TitleSpaceBefore;
			value.SpaceAfter = options.TitleSpaceAfter;
			value.FirstLineIndent = 0f;
			value.LeftIndent = 0f;
			value.RightIndent = 0f;
			value2 = titleRange.Font;
			value2.NameFarEast = options.TitleFontName;
			value2.Name = options.TitleFontName;
			value2.Size = options.TitleFontSize;
			value2.Bold = (options.TitleBold ? 1 : 0);
		}
		finally
		{
			if (value2 != null)
			{
				ComObjectRelease.Release(ref value2, "CompilationTocService.Title.Font");
			}
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "CompilationTocService.Title.Format");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void FormatEntryBody(Document document, Microsoft.Office.Interop.Word.Range bodyRange, CompilationTocOptions options)
	{
		Font value = null;
		ParagraphFormat value2 = null;
		TabStops value3 = null;
		Section value4 = null;
		PageSetup value5 = null;
		TabStop value6 = null;
		try
		{
			value = bodyRange.Font;
			value.NameFarEast = options.EntryFontNameFarEast;
			value.Name = options.EntryFontNameAscii;
			value.Size = options.EntryFontSize;
			value.Bold = 0;
			value2 = bodyRange.ParagraphFormat;
			value2.Alignment = WdParagraphAlignment.wdAlignParagraphLeft;
			value2.FirstLineIndent = 0f;
			value2.LeftIndent = 0f;
			value2.RightIndent = 0f;
			value2.SpaceBefore = 0f;
			value2.SpaceAfter = 0f;
			value2.LineSpacingRule = WdLineSpacing.wdLineSpaceExactly;
			value2.LineSpacing = options.EntryLineSpacingPoints;
			value3 = value2.TabStops;
			value3.ClearAll();
			value4 = document.Sections[1];
			value5 = value4.PageSetup;
			float num = value5.PageWidth - value5.LeftMargin - value5.RightMargin;
			if (num < 10f)
			{
				num = 10f;
			}
			if (!(num <= 1584f))
			{
				num = 1584f;
			}
			WdTabLeader wdTabLeader = options.LeaderStyle switch
			{
				CompilationTocLeaderStyle.Dashes => WdTabLeader.wdTabLeaderDashes, 
				CompilationTocLeaderStyle.None => WdTabLeader.wdTabLeaderSpaces, 
				_ => WdTabLeader.wdTabLeaderDots, 
			};
			TabStops tabStops = value3;
			float position = num;
			object Alignment = WdTabAlignment.wdAlignTabRight;
			object Leader = wdTabLeader;
			value6 = tabStops.Add(position, ref Alignment, ref Leader);
		}
		finally
		{
			if (value6 != null)
			{
				ComObjectRelease.Release(ref value6, "CompilationTocService.Format.TabStop");
			}
			if (value4 != null)
			{
				ComObjectRelease.Release(ref value4, "CompilationTocService.Format.Section");
			}
			if (value5 != null)
			{
				ComObjectRelease.Release(ref value5, "CompilationTocService.Format.PageSetup");
			}
			if (value3 != null)
			{
				ComObjectRelease.Release(ref value3, "CompilationTocService.Format.TabStops");
			}
			if (value2 != null)
			{
				ComObjectRelease.Release(ref value2, "CompilationTocService.Format.ParagraphFormat");
			}
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "CompilationTocService.Format.Font");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void AddPageRefField(Document document, Microsoft.Office.Interop.Word.Range cursor, string bookmarkName, string switches)
	{
		object Type = WdFieldType.wdFieldEmpty;
		object Text = "PAGEREF " + bookmarkName + switches;
		object PreserveFormatting = false;
		Fields value = null;
		Field value2 = null;
		Microsoft.Office.Interop.Word.Range value3 = null;
		Microsoft.Office.Interop.Word.Range value4 = null;
		try
		{
			value = document.Fields;
			int end = cursor.End;
			object Start = end;
			object End = end;
			value3 = document.Range(ref Start, ref End);
			value3.InsertAfter(" ");
			value3.Start = end;
			value3.End = end + 1;
			value2 = value.Add(value3, ref Type, ref Text, ref PreserveFormatting);
			if (value2 != null)
			{
				value4 = value2.Result;
				cursor.Start = value4.End + 1;
				cursor.End = value4.End + 1;
				End = WdCollapseDirection.wdCollapseEnd;
				cursor.Collapse(ref End);
				return;
			}
			throw new InvalidOperationException("宿主未能建立目录页码引用域。");
		}
		finally
		{
			if (value4 != null)
			{
				ComObjectRelease.Release(ref value4, "CompilationTocService.Insert.Field.ResultRange");
			}
			if (value2 != null)
			{
				ComObjectRelease.Release(ref value2, "CompilationTocService.Insert.Field");
			}
			if (value3 != null)
			{
				ComObjectRelease.Release(ref value3, "CompilationTocService.Insert.Field.Anchor");
			}
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "CompilationTocService.Insert.Field.Fields");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void DeleteResidualSectionBreakAt(Document document, int position)
	{
		Microsoft.Office.Interop.Word.Range value = null;
		Microsoft.Office.Interop.Word.Range value2 = null;
		try
		{
			value = document.Content;
			if (position >= 0 && position < value.End)
			{
				object Start = position;
				object End = Math.Min(position + 1, value.End);
				value2 = document.Range(ref Start, ref End);
				if ((value2.Text ?? string.Empty).IndexOf('\f') >= 0)
				{
					Microsoft.Office.Interop.Word.Range range = value2;
					End = Type.Missing;
					Start = Type.Missing;
					range.Delete(ref End, ref Start);
				}
			}
		}
		finally
		{
			ComObjectRelease.Release(ref value2, "CompilationTocService.Rebuild.ResidualBreak");
			ComObjectRelease.Release(ref value, "CompilationTocService.Rebuild.Content");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int AddEntryTitleHyperlink(Document document, int titleStart, int titleEnd, string bookmarkName)
	{
		if (titleEnd > titleStart)
		{
			Microsoft.Office.Interop.Word.Range value = null;
			Hyperlinks value2 = null;
			Hyperlink value3 = null;
			Microsoft.Office.Interop.Word.Range value4 = null;
			object Address = string.Empty;
			object SubAddress = bookmarkName;
			object ScreenTip = "按住 Ctrl 并单击可跳转";
			object TextToDisplay = Type.Missing;
			object Target = Type.Missing;
			try
			{
				object Start = titleStart;
				object End = titleEnd;
				value = document.Range(ref Start, ref End);
				value2 = document.Hyperlinks;
				value3 = value2.Add(value, ref Address, ref SubAddress, ref ScreenTip, ref TextToDisplay, ref Target);
				if (value3 != null)
				{
					value4 = value3.Range;
					return value4.End;
				}
				throw new InvalidOperationException("宿主未能建立目录标题超链接。");
			}
			finally
			{
				ComObjectRelease.Release(ref value4, "CompilationTocService.TitleLink.Range");
				ComObjectRelease.Release(ref value3, "CompilationTocService.TitleLink.Hyperlink");
				ComObjectRelease.Release(ref value2, "CompilationTocService.TitleLink.Hyperlinks");
				ComObjectRelease.Release(ref value, "CompilationTocService.TitleLink.Anchor");
			}
		}
		return titleEnd;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void VerifyTocEntries(Document document, IList<CompilationTocEntry> entries, int regionStart, int regionEnd, bool hyperlinkEntries)
	{
		int num = 0;
		foreach (CompilationTocEntry entry in entries)
		{
			if (entry != null && !string.IsNullOrWhiteSpace(CleanTitle(entry.Title)))
			{
				num++;
				if (!string.IsNullOrWhiteSpace(entry.BookmarkName) && !BookmarkExists(document, entry.BookmarkName))
				{
					throw new InvalidOperationException("目录页码引用缺少文章书签：" + entry.BookmarkName);
				}
				if (!string.IsNullOrWhiteSpace(entry.BookmarkName))
				{
					VerifyArticleBookmarkTarget(document, entry.BookmarkName);
				}
			}
		}
		int num2 = CountPagerRefFields(document, regionStart, regionEnd);
		if (num2 != num)
		{
			throw new InvalidOperationException("目录页码引用域数量与条目数不一致：域=" + num2 + "，条目=" + num);
		}
		if (hyperlinkEntries)
		{
			VerifyTitleHyperlinkFields(document, entries, regionStart, regionEnd, num);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void VerifyTitleHyperlinkFields(Document document, IList<CompilationTocEntry> entries, int start, int end, int expectedCount)
	{
		Dictionary<string, int> dictionary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		foreach (CompilationTocEntry entry in entries)
		{
			if (entry != null && !string.IsNullOrWhiteSpace(entry.BookmarkName) && !string.IsNullOrWhiteSpace(CleanTitle(entry.Title)))
			{
				dictionary[entry.BookmarkName] = 0;
			}
		}
		Microsoft.Office.Interop.Word.Range value = null;
		Fields value2 = null;
		try
		{
			object Start = start;
			object End = end;
			value = document.Range(ref Start, ref End);
			value2 = value.Fields;
			for (int i = 1; i <= value2.Count; i++)
			{
				Field value3 = null;
				Microsoft.Office.Interop.Word.Range value4 = null;
				try
				{
					value3 = value2[i];
					if (value3.Type == WdFieldType.wdFieldHyperlink)
					{
						value4 = value3.Code;
						if (CompilationTocOwnershipAnalyzer.TryParseArticleHyperlinkBookmark((value4 == null) ? string.Empty : value4.Text, out var bookmarkName) && dictionary.ContainsKey(bookmarkName))
						{
							dictionary[bookmarkName]++;
						}
					}
				}
				finally
				{
					ComObjectRelease.Release(ref value4, "CompilationTocService.Verify.LinkCode");
					ComObjectRelease.Release(ref value3, "CompilationTocService.Verify.LinkField");
				}
			}
		}
		finally
		{
			ComObjectRelease.Release(ref value2, "CompilationTocService.Verify.LinkFields");
			ComObjectRelease.Release(ref value, "CompilationTocService.Verify.LinkRegion");
		}
		int num = 0;
		foreach (KeyValuePair<string, int> item in dictionary)
		{
			num += item.Value;
			if (item.Value != 1)
			{
				throw new InvalidOperationException("目录标题超链接目标不是唯一值：" + item.Key);
			}
		}
		if (num != expectedCount)
		{
			throw new InvalidOperationException("目录标题超链接数量与条目数不一致：链接=" + num + "，条目=" + expectedCount);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int CountPagerRefFields(Document document, int start, int end)
	{
		if (end <= start)
		{
			return 0;
		}
		Microsoft.Office.Interop.Word.Range value = null;
		Fields value2 = null;
		try
		{
			object Start = start;
			object End = end;
			value = document.Range(ref Start, ref End);
			value2 = value.Fields;
			int num = 0;
			foreach (Field item in value2)
			{
				try
				{
					if (item.Type == WdFieldType.wdFieldPageRef)
					{
						num++;
					}
				}
				catch
				{
				}
			}
			return num;
		}
		finally
		{
			if (value2 != null)
			{
				ComObjectRelease.Release(ref value2, "CompilationTocService.Verify.Fields");
			}
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "CompilationTocService.Verify.Range");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void RepositionEntryBookmarks(Document document, IList<CompilationTocEntry> entries, int tocEnd, string firstArticleBookmarkName, Microsoft.Office.Interop.Word.Range firstArticleAnchor)
	{
		Bookmarks value = null;
		try
		{
			value = document.Bookmarks;
			foreach (CompilationTocEntry entry in entries)
			{
				if (entry == null)
				{
					continue;
				}
				string bookmarkName = entry.BookmarkName;
				if (string.IsNullOrWhiteSpace(bookmarkName) || !value.Exists(bookmarkName))
				{
					continue;
				}
				bool flag = false;
				Microsoft.Office.Interop.Word.Range value2 = null;
				try
				{
					Bookmarks bookmarks = value;
					object Index = bookmarkName;
					value2 = bookmarks.get_Item(ref Index).Range;
					flag = value2.Start <= tocEnd && value2.End <= tocEnd;
				}
				catch
				{
				}
				finally
				{
					if (value2 != null)
					{
						ComObjectRelease.Release(ref value2, "CompilationTocService.Reposition.BookmarkRange");
					}
				}
				if (!flag)
				{
					continue;
				}
				Bookmark value3 = null;
				Microsoft.Office.Interop.Word.Range value4 = null;
				try
				{
					Bookmarks bookmarks2 = value;
					object Index = bookmarkName;
					value3 = bookmarks2.get_Item(ref Index);
					value3.Delete();
					int num = tocEnd;
					num = ((firstArticleAnchor == null || !string.Equals(bookmarkName, firstArticleBookmarkName, StringComparison.OrdinalIgnoreCase)) ? FindFirstVisiblePosition(document, tocEnd) : GetAnchoredCharacterPosition(firstArticleAnchor));
					Index = num;
					object End = num;
					value4 = document.Range(ref Index, ref End);
					Bookmarks bookmarks3 = value;
					End = value4;
					bookmarks3.Add(bookmarkName, ref End);
				}
				finally
				{
					if (value4 != null)
					{
						ComObjectRelease.Release(ref value4, "CompilationTocService.Reposition.Range");
					}
					if (value3 != null)
					{
						ComObjectRelease.Release(ref value3, "CompilationTocService.Reposition.Existing");
					}
				}
			}
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "CompilationTocService.Reposition.Bookmarks");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void VerifyArticleBookmarkTarget(Document document, string bookmarkName)
	{
		Bookmarks value = null;
		Bookmark value2 = null;
		Microsoft.Office.Interop.Word.Range value3 = null;
		Microsoft.Office.Interop.Word.Range value4 = null;
		Microsoft.Office.Interop.Word.Range value5 = null;
		try
		{
			value = document.Bookmarks;
			if (!value.Exists(bookmarkName))
			{
				throw new InvalidOperationException("目录跳转目标书签已丢失：" + bookmarkName);
			}
			Bookmarks bookmarks = value;
			object Index = bookmarkName;
			value2 = bookmarks.get_Item(ref Index);
			value3 = value2.Range;
			value4 = document.Content;
			if (value3.Start >= value4.Start && value3.Start < value4.End)
			{
				Index = value3.Start;
				object End = Math.Min(value3.Start + 1, value4.End);
				value5 = document.Range(ref Index, ref End);
				string text = value5.Text ?? string.Empty;
				if (text.Length == 0 || text[0] == '\r' || text[0] == '\a' || text[0] == '\f' || text[0] == '\v')
				{
					throw new InvalidOperationException("目录跳转目标未对准文章标题：" + bookmarkName);
				}
				return;
			}
			throw new InvalidOperationException("目录跳转目标超出正文范围：" + bookmarkName);
		}
		finally
		{
			ComObjectRelease.Release(ref value5, "CompilationTocService.Verify.TargetProbe");
			ComObjectRelease.Release(ref value4, "CompilationTocService.Verify.TargetContent");
			ComObjectRelease.Release(ref value3, "CompilationTocService.Verify.TargetRange");
			ComObjectRelease.Release(ref value2, "CompilationTocService.Verify.TargetBookmark");
			ComObjectRelease.Release(ref value, "CompilationTocService.Verify.TargetBookmarks");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static Microsoft.Office.Interop.Word.Range CaptureFirstArticleAnchor(Document document, IList<CompilationTocEntry> entries, out string bookmarkName)
	{
		bookmarkName = null;
		if (document != null && entries != null)
		{
			Bookmarks value = null;
			Bookmark value2 = null;
			Microsoft.Office.Interop.Word.Range value3 = null;
			Bookmark value4 = null;
			Microsoft.Office.Interop.Word.Range value5 = null;
			try
			{
				value = document.Bookmarks;
				foreach (CompilationTocEntry entry in entries)
				{
					if (entry != null && !string.IsNullOrWhiteSpace(entry.BookmarkName) && value.Exists(entry.BookmarkName))
					{
						bookmarkName = entry.BookmarkName;
						Bookmarks bookmarks = value;
						object Index = bookmarkName;
						value2 = bookmarks.get_Item(ref Index);
						value3 = value2.Range;
						break;
					}
				}
				if (value3 != null)
				{
					int start = value3.Start;
					if (value.Exists("SXCF_TOC_END"))
					{
						Bookmarks bookmarks2 = value;
						object Index = "SXCF_TOC_END";
						value4 = bookmarks2.get_Item(ref Index);
						value5 = value4.Range;
						if (start <= value5.Start)
						{
							start = value5.Start;
						}
					}
					int num = FindFirstVisiblePosition(document, start);
					Microsoft.Office.Interop.Word.Range value6 = null;
					try
					{
						value6 = document.Content;
						if (num >= value6.Start && num < value6.End)
						{
							object Index = num;
							object End = Math.Min(num + 1, value6.End);
							return document.Range(ref Index, ref End);
						}
						return null;
					}
					finally
					{
						ComObjectRelease.Release(ref value6, "CompilationTocService.Anchor.Content");
					}
				}
				return null;
			}
			finally
			{
				ComObjectRelease.Release(ref value5, "CompilationTocService.Anchor.TocEndRange");
				ComObjectRelease.Release(ref value4, "CompilationTocService.Anchor.TocEndBookmark");
				ComObjectRelease.Release(ref value3, "CompilationTocService.Anchor.ArticleRange");
				ComObjectRelease.Release(ref value2, "CompilationTocService.Anchor.ArticleBookmark");
				ComObjectRelease.Release(ref value, "CompilationTocService.Anchor.Bookmarks");
			}
		}
		return null;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int FindFirstVisiblePosition(Document document, int start)
	{
		Microsoft.Office.Interop.Word.Range value = null;
		Microsoft.Office.Interop.Word.Range value2 = null;
		try
		{
			value = document.Content;
			int num = Math.Max(value.Start, Math.Min(start, Math.Max(value.Start, value.End - 1)));
			int num2 = Math.Min(value.End, num + 1024);
			object Start = num;
			object End = num2;
			value2 = document.Range(ref Start, ref End);
			string text = value2.Text ?? string.Empty;
			int num3 = 0;
			while (true)
			{
				if (num3 < text.Length)
				{
					char c = text[num3];
					if (c >= ' ' && !char.IsWhiteSpace(c) && c != '\u3000' && c != '\u00a0')
					{
						break;
					}
					num3++;
					continue;
				}
				return num;
			}
			return num + num3;
		}
		finally
		{
			ComObjectRelease.Release(ref value2, "CompilationTocService.Anchor.Probe");
			ComObjectRelease.Release(ref value, "CompilationTocService.Anchor.FindContent");
		}
	}

	private static int GetAnchoredCharacterPosition(Microsoft.Office.Interop.Word.Range anchor)
	{
		if (anchor == null)
		{
			return 0;
		}
		return Math.Max(anchor.Start, anchor.End - 1);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ForcePaginate(Document document)
	{
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			object Start = 0;
			object End = 0;
			value = document.Range(ref Start, ref End);
			dynamic val = value;
			val.Information(WdInformation.wdActiveEndPageNumber);
		}
		catch (Exception ex)
		{
			LogService.Warn("CompilationTocService.ForcePaginate", ex);
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "CompilationTocService.ForcePaginate.Probe");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ForcePaginateEntries(Document document, IList<CompilationTocEntry> entries)
	{
		if (document == null || entries == null)
		{
			return;
		}
		Bookmarks value = null;
		try
		{
			value = document.Bookmarks;
			foreach (CompilationTocEntry entry in entries)
			{
				if (entry == null)
				{
					continue;
				}
				string bookmarkName = entry.BookmarkName;
				if (string.IsNullOrWhiteSpace(bookmarkName) || !value.Exists(bookmarkName))
				{
					continue;
				}
				Microsoft.Office.Interop.Word.Range value2 = null;
				try
				{
					Bookmarks bookmarks = value;
					object Index = bookmarkName;
					value2 = bookmarks.get_Item(ref Index).Range;
					dynamic val = value2;
					val.Information(WdInformation.wdActiveEndPageNumber);
				}
				catch
				{
				}
				finally
				{
					if (value2 != null)
					{
						ComObjectRelease.Release(ref value2, "CompilationTocService.ForcePaginateEntries.BookmarkRange");
					}
				}
			}
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "CompilationTocService.ForcePaginateEntries.Bookmarks");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string EnsureArticleBookmark(Document document, CompilationTocEntry entry)
	{
		string bookmarkName = entry.BookmarkName;
		if (string.IsNullOrWhiteSpace(bookmarkName) || !BookmarkExists(document, bookmarkName))
		{
			throw new InvalidOperationException("目录页码引用缺少文章书签：" + (bookmarkName ?? "(空)"));
		}
		return bookmarkName;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void EnsureBookmarkAt(Document document, string name, int position)
	{
		Bookmarks value = null;
		Bookmark value2 = null;
		Microsoft.Office.Interop.Word.Range value3 = null;
		Microsoft.Office.Interop.Word.Range value4 = null;
		try
		{
			value = document.Bookmarks;
			object Index;
			if (value.Exists(name))
			{
				Bookmarks bookmarks = value;
				Index = name;
				value2 = bookmarks.get_Item(ref Index);
				value2.Delete();
			}
			value3 = document.Content;
			int num = Math.Max(value3.Start, Math.Min(position, Math.Max(value3.Start, value3.End - 1)));
			Index = num;
			object End = num;
			value4 = document.Range(ref Index, ref End);
			Bookmarks bookmarks2 = value;
			End = value4;
			bookmarks2.Add(name, ref End);
		}
		finally
		{
			if (value2 != null)
			{
				ComObjectRelease.Release(ref value2, "CompilationTocService.EnsureMarker.Existing");
			}
			if (value4 != null)
			{
				ComObjectRelease.Release(ref value4, "CompilationTocService.EnsureMarker.Range");
			}
			if (value3 != null)
			{
				ComObjectRelease.Release(ref value3, "CompilationTocService.EnsureMarker.Content");
			}
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "CompilationTocService.EnsureMarker.Bookmarks");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool BookmarkExists(Document document, string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return false;
		}
		Bookmarks value = null;
		try
		{
			value = document.Bookmarks;
			return value.Exists(name);
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "CompilationTocService.BookmarkExists.Bookmarks");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void DeleteBookmark(Document document, string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return;
		}
		Bookmarks value = null;
		Bookmark value2 = null;
		try
		{
			value = document.Bookmarks;
			if (value.Exists(name))
			{
				Bookmarks bookmarks = value;
				object Index = name;
				value2 = bookmarks.get_Item(ref Index);
				value2.Delete();
			}
		}
		finally
		{
			if (value2 != null)
			{
				ComObjectRelease.Release(ref value2, "CompilationTocService.DeleteBookmark.Bookmark");
			}
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "CompilationTocService.DeleteBookmark.Bookmarks");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static Microsoft.Office.Interop.Word.Range GetBookmarkRange(Document document, string name)
	{
		Bookmarks value = null;
		Bookmark value2 = null;
		try
		{
			value = document.Bookmarks;
			if (value.Exists(name))
			{
				Bookmarks bookmarks = value;
				object Index = name;
				value2 = bookmarks.get_Item(ref Index);
				return value2.Range;
			}
			throw new InvalidOperationException("汇编目录缺少区域标记书签：" + name);
		}
		finally
		{
			if (value2 != null)
			{
				ComObjectRelease.Release(ref value2, "CompilationTocService.GetBookmarkRange.Bookmark");
			}
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "CompilationTocService.GetBookmarkRange.Bookmarks");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string CleanTitle(string text)
	{
		if (text == null)
		{
			return "";
		}
		return text.Replace("\r", "").Replace("\n", "").Replace("\a", "")
			.Replace("\v", "")
			.Replace("\f", "")
			.Trim();
	}
}
