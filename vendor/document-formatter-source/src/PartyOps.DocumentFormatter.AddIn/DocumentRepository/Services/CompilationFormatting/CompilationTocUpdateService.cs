using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Web.Script.Serialization;
using DocumentRepository.Models.CompilationFormatting;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.CompilationFormatting;

public static class CompilationTocUpdateService
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static CompilationTocOutcome UpdateToc(Document document, CompilationFormatOptions fallbackOptions, FormatConfig formatConfig, out string usedOptionsSource)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		string failureReason;
		bool titlesReliable;
		CompilationArticleList obj = CompilationBoundaryService.TryReadFromBoundaries(document, formatConfig, out failureReason, out titlesReliable) ?? throw new CompilationTocUpdateException(CompilationFormatFailureReasonCode.BoundaryBroken, "无法更新目录：" + (failureReason ?? "文章边界不可用。"));
		if (!titlesReliable)
		{
			throw new CompilationTocUpdateException(CompilationFormatFailureReasonCode.TitlesUnreliable, "无法可靠读取部分文章的当前标题，目录更新已停止。请打开文档核实各篇标题后重试。");
		}
		CompilationManifest compilationManifest = CompilationManifestService.Read(document);
		ResolveTocConfiguration(compilationManifest, fallbackOptions, out var tocOptions, out var tocPosition, out usedOptionsSource);
		List<CompilationTocEntry> list = new List<CompilationTocEntry>();
		foreach (CompilationArticleInfo article in obj.Articles)
		{
			list.Add(new CompilationTocEntry
			{
				Title = article.Title,
				BookmarkName = article.BeginBookmarkName,
				OrderIndex = article.OrderIndex
			});
		}
		CompilationTocOutcome compilationTocOutcome = InsertOrUpdateTocRegion(document, list, tocOptions, tocPosition);
		if ((compilationTocOutcome == CompilationTocOutcome.Updated || compilationTocOutcome == CompilationTocOutcome.Created) && compilationManifest != null && compilationManifest.TocMayBeStale)
		{
			compilationManifest.TocMayBeStale = false;
			CompilationManifestService.Write(document, compilationManifest);
		}
		return compilationTocOutcome;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static CompilationTocOutcome InsertOrUpdateTocRegion(Document document, List<CompilationTocEntry> entries, CompilationTocOptions tocOptions, CompilationTocPosition tocPosition)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		if (tocOptions == null)
		{
			throw new ArgumentNullException("tocOptions");
		}
		bool num = HasCompilationToc(document);
		int pluginFieldCount;
		int num2 = CompilationTocOwnershipService.RemoveOrphanRegions(document, entries, tocOptions, out pluginFieldCount);
		bool flag = num || pluginFieldCount > 0 || num2 > 0;
		if (flag || tocOptions.ExistingTocPolicy != CompilationExistingTocPolicy.Cancel || CompilationTocService.ShouldInsertAutoToc(document, tocOptions))
		{
			if (tocOptions.ExistingTocPolicy == CompilationExistingTocPolicy.Replace)
			{
				CompilationTocService.DeleteExistingTocFields(document);
			}
			int insertPosition = ((tocPosition != CompilationTocPosition.DocumentStart) ? ResolveFirstArticlePosition(document) : 0);
			CompilationTocService.InsertTocRegion(document, entries, tocOptions, insertPosition);
			DedupeTrailingBreakAfterToc(document);
			CompilationTocOwnershipService.VerifyCanonicalState(document, entries, tocOptions);
			if (!flag)
			{
				return CompilationTocOutcome.Created;
			}
			return CompilationTocOutcome.Updated;
		}
		LogService.Info("汇编排版目录：检测到用户既有目录，按策略取消首次自动生成。");
		return CompilationTocOutcome.SkippedByExistingTocPolicy;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void EnsureTocSectionBreaks(Document document)
	{
		if (document == null)
		{
			return;
		}
		Microsoft.Office.Interop.Word.Range value = null;
		Microsoft.Office.Interop.Word.Range value2 = null;
		try
		{
			if (document.Bookmarks.Exists("SXCF_TOC_BEGIN") && document.Bookmarks.Exists("SXCF_TOC_END"))
			{
				Bookmarks bookmarks = document.Bookmarks;
				object Index = "SXCF_TOC_BEGIN";
				value = bookmarks.get_Item(ref Index).Range;
				Bookmarks bookmarks2 = document.Bookmarks;
				Index = "SXCF_TOC_END";
				value2 = bookmarks2.get_Item(ref Index).Range;
				int start = value.Start;
				int start2 = value2.Start;
				EnsureSectionBreakAt(document, start2);
				if (start > 0)
				{
					EnsureSectionBreakAt(document, start);
				}
			}
		}
		finally
		{
			ComObjectRelease.Release(ref value, "CompilationToc.TocBegin");
			ComObjectRelease.Release(ref value2, "CompilationToc.TocEnd");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void FinalizeTocNumberingExclusion(Document document, bool restartBody)
	{
		if (document == null)
		{
			return;
		}
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			if (!document.Bookmarks.Exists("SXCF_TOC_BEGIN"))
			{
				return;
			}
			Bookmarks bookmarks = document.Bookmarks;
			object Index = "SXCF_TOC_BEGIN";
			value = bookmarks.get_Item(ref Index).Range;
			Section value2 = value.Sections[1];
			int index = value2.Index;
			ComObjectRelease.Release(ref value2, "CompilationToc.TocSection");
			Section value3 = document.Sections[index];
			PageSetupManager.DeletePageNumbersInSection(value3);
			ComObjectRelease.Release(ref value3, "CompilationToc.TocSectionFresh");
			if (!restartBody || index >= document.Sections.Count)
			{
				return;
			}
			Section section = document.Sections[index + 1];
			HeadersFooters footers = section.Footers;
			foreach (HeaderFooter item in footers)
			{
				if (item != null)
				{
					try
					{
						item.PageNumbers.RestartNumberingAtSection = true;
						item.PageNumbers.StartingNumber = 1;
					}
					finally
					{
						ComObjectRelease.Release(item, "CompilationToc.BodyFooter");
					}
				}
			}
			ComObjectRelease.Release(footers, "CompilationToc.BodyFooters");
			ComObjectRelease.Release(section, "CompilationToc.BodySection");
		}
		finally
		{
			ComObjectRelease.Release(ref value, "CompilationToc.TocBegin");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void DedupeTrailingBreakAfterToc(Document document)
	{
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			if (!document.Bookmarks.Exists("SXCF_TOC_END"))
			{
				return;
			}
			Bookmarks bookmarks = document.Bookmarks;
			object Index = "SXCF_TOC_END";
			value = bookmarks.get_Item(ref Index).Range;
			Paragraph paragraph = value.Paragraphs[1];
			int num = 0;
			int num2 = 0;
			bool flag = false;
			while (paragraph != null && num2++ < 8)
			{
				Microsoft.Office.Interop.Word.Range range = paragraph.Range;
				if ((range.Text ?? "").Replace("\f", "").Replace("\f", "").Replace("\r", "")
					.Trim()
					.Length != 0 || (range.Text ?? "").IndexOf('\f') < 0)
				{
					ComObjectRelease.Release(range, "CompilationToc.DedupeRange");
					ComObjectRelease.Release(paragraph, "CompilationToc.DedupePara");
					break;
				}
				Paragraph paragraph2 = paragraph;
				Index = Type.Missing;
				Paragraph paragraph3 = paragraph2.Previous(ref Index);
				if (flag)
				{
					Index = Type.Missing;
					object Count = Type.Missing;
					range.Delete(ref Index, ref Count);
					num++;
				}
				else
				{
					flag = true;
				}
				ComObjectRelease.Release(range, "CompilationToc.DedupeRange");
				ComObjectRelease.Release(paragraph, "CompilationToc.DedupePara");
				paragraph = paragraph3;
			}
			if (num > 0)
			{
				LogService.Info("汇编排版目录：移除重复的分页段 " + num + " 个。");
			}
		}
		catch (Exception ex)
		{
			LogService.Warn("CompilationTocUpdateService.DedupeTrailingBreakAfterToc", ex);
		}
		finally
		{
			ComObjectRelease.Release(ref value, "CompilationToc.DedupeTocEnd");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool HasCompilationToc(Document document)
	{
		if (document != null)
		{
			Bookmarks value = null;
			Bookmark value2 = null;
			Bookmark value3 = null;
			Microsoft.Office.Interop.Word.Range value4 = null;
			Microsoft.Office.Interop.Word.Range value5 = null;
			try
			{
				value = document.Bookmarks;
				if (!value.Exists("SXCF_TOC_BEGIN") || !value.Exists("SXCF_TOC_END"))
				{
					return false;
				}
				Bookmarks bookmarks = value;
				object Index = "SXCF_TOC_BEGIN";
				value2 = bookmarks.get_Item(ref Index);
				Bookmarks bookmarks2 = value;
				Index = "SXCF_TOC_END";
				value3 = bookmarks2.get_Item(ref Index);
				value4 = value2.Range;
				value5 = value3.Range;
				return value5.Start > value4.Start;
			}
			catch
			{
				return false;
			}
			finally
			{
				ComObjectRelease.Release(ref value5, "CompilationTocUpdate.Has.EndRange");
				ComObjectRelease.Release(ref value4, "CompilationTocUpdate.Has.BeginRange");
				ComObjectRelease.Release(ref value3, "CompilationTocUpdate.Has.EndBookmark");
				ComObjectRelease.Release(ref value2, "CompilationTocUpdate.Has.BeginBookmark");
				ComObjectRelease.Release(ref value, "CompilationTocUpdate.Has.Bookmarks");
			}
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ResolveTocConfiguration(CompilationManifest manifest, CompilationFormatOptions fallbackOptions, out CompilationTocOptions tocOptions, out CompilationTocPosition tocPosition, out string usedOptionsSource)
	{
		tocOptions = null;
		tocPosition = CompilationTocPosition.BeforeFirstArticle;
		usedOptionsSource = null;
		if (manifest != null && !string.IsNullOrWhiteSpace(manifest.TocOptionsSnapshotJson))
		{
			try
			{
				JavaScriptSerializer val = new JavaScriptSerializer();
				val.MaxJsonLength = int.MaxValue;
				tocOptions = val.Deserialize<CompilationTocOptions>(manifest.TocOptionsSnapshotJson);
			}
			catch (Exception inner)
			{
				throw new CompilationTocUpdateException(CompilationFormatFailureReasonCode.TocSnapshotCorrupt, "文档保存的目录参数快照已损坏，无法更新目录。请重新执行全文汇编排版重建目录配置。", inner);
			}
			if (tocOptions == null)
			{
				throw new CompilationTocUpdateException(CompilationFormatFailureReasonCode.TocSnapshotCorrupt, "文档保存的目录参数快照为空，无法更新目录。");
			}
			if (!string.IsNullOrWhiteSpace(manifest.TocPositionSnapshot))
			{
				if (!Enum.TryParse<CompilationTocPosition>(manifest.TocPositionSnapshot, out var result) || !Enum.IsDefined(typeof(CompilationTocPosition), result))
				{
					throw new CompilationTocUpdateException(CompilationFormatFailureReasonCode.TocSnapshotCorrupt, "文档保存的目录位置快照未知，无法更新目录。");
				}
				tocPosition = result;
			}
			usedOptionsSource = "文档保存的原目录参数";
		}
		else
		{
			if (fallbackOptions == null || fallbackOptions.TocOptions == null)
			{
				throw new CompilationTocUpdateException(CompilationFormatFailureReasonCode.TocSnapshotCorrupt, "文档未保存目录参数快照，且当前模板没有可用目录参数。");
			}
			tocOptions = fallbackOptions.TocOptions;
			tocPosition = fallbackOptions.TocPosition;
			usedOptionsSource = "当前模板（文档未保存原目录参数，已回退并记录）";
			LogService.Info("汇编排版目录更新：文档缺少目录参数快照，已回退当前模板参数。");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ResolveFirstArticlePosition(Document document)
	{
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			string text = CompilationBoundaryService.BeginBookmarkName(1);
			if (document.Bookmarks.Exists(text))
			{
				Bookmarks bookmarks = document.Bookmarks;
				object Index = text;
				value = bookmarks.get_Item(ref Index).Range;
				return value.Start;
			}
		}
		finally
		{
			ComObjectRelease.Release(ref value, "CompilationTocUpdateService.FirstBegin");
		}
		return 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void EnsureSectionBreakAt(Document document, int position)
	{
		Microsoft.Office.Interop.Word.Range value = null;
		Microsoft.Office.Interop.Word.Range value2 = null;
		try
		{
			object Start = position;
			object End = position;
			value = document.Range(ref Start, ref End);
			Section value3 = value.Sections[1];
			Microsoft.Office.Interop.Word.Range value4 = value3.Range;
			int start = value4.Start;
			int end = value4.End;
			int index = value3.Index;
			ComObjectRelease.Release(ref value4, "CompilationToc.SectionRange");
			ComObjectRelease.Release(ref value3, "CompilationToc.SectionProbe");
			if (Math.Abs(start - position) <= 1)
			{
				return;
			}
			Paragraph value5 = null;
			Microsoft.Office.Interop.Word.Range value6 = null;
			try
			{
				value5 = value.Paragraphs[1];
				value6 = value5.Range;
				if (value6.End == end && index < document.Sections.Count)
				{
					return;
				}
			}
			finally
			{
				ComObjectRelease.Release(ref value6, "CompilationToc.BreakParaRange");
				ComObjectRelease.Release(ref value5, "CompilationToc.BreakPara");
			}
			End = position;
			Start = position;
			value2 = document.Range(ref End, ref Start);
			Microsoft.Office.Interop.Word.Range range = value2;
			Start = WdBreakType.wdSectionBreakNextPage;
			range.InsertBreak(ref Start);
		}
		finally
		{
			ComObjectRelease.Release(ref value2, "CompilationToc.BreakCursor");
			ComObjectRelease.Release(ref value, "CompilationToc.BreakProbe");
		}
	}
}
