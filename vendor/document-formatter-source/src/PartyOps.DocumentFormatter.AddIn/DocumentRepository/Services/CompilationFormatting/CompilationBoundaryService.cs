using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Web.Script.Serialization;
using DocumentRepository.Models.CompilationFormatting;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.Snapshots;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.CompilationFormatting;

public static class CompilationBoundaryService
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string BeginBookmarkName(int orderIndex)
	{
		return "SXCF_ART_" + orderIndex.ToString("000") + "_BEGIN";
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string EndBookmarkName(int orderIndex)
	{
		return "SXCF_ART_" + orderIndex.ToString("000") + "_END";
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static CompilationManifest ConvertMarkersToBoundaries(Document document, CompilationDocumentScanResult scan, CompilationArticleList articleList, string formatTemplateName, CompilationFormatOptions options)
	{
		if (document != null)
		{
			if (scan == null)
			{
				throw new ArgumentNullException("scan");
			}
			if (articleList == null)
			{
				throw new ArgumentNullException("articleList");
			}
			if (options != null)
			{
				int documentBodyEnd = GetDocumentBodyEnd(document);
				for (int i = 0; i < articleList.Articles.Count; i++)
				{
					CompilationArticleInfo compilationArticleInfo = articleList.Articles[i];
					int titleStartParagraphIndex = compilationArticleInfo.TitleStartParagraphIndex;
					if (titleStartParagraphIndex >= 0 && titleStartParagraphIndex < scan.ParagraphStarts.Count && scan.ParagraphStarts[titleStartParagraphIndex] >= 0)
					{
						int position = scan.ParagraphStarts[titleStartParagraphIndex];
						int position2 = ((i + 1 < articleList.Articles.Count) ? scan.ParagraphStarts[scan.BodyMarkerParagraphIndexes[i + 1]] : documentBodyEnd);
						AddBookmarkAt(document, BeginBookmarkName(compilationArticleInfo.OrderIndex), position);
						AddBookmarkAt(document, EndBookmarkName(compilationArticleInfo.OrderIndex), position2);
						continue;
					}
					throw new InvalidOperationException("第 " + compilationArticleInfo.OrderIndex + " 篇文章的主标题位置无法可靠定位。");
				}
				CompilationManifest compilationManifest = new CompilationManifest
				{
					SchemaVersion = 2,
					FormatTemplateName = (formatTemplateName ?? ""),
					GeneratedAt = DateTime.Now.ToString("o"),
					HasFrontMatter = (articleList.FrontMatterEndParagraphIndex >= 0),
					TocMayBeStale = false
				};
				try
				{
					JavaScriptSerializer val = new JavaScriptSerializer();
					compilationManifest.TocOptionsSnapshotJson = val.Serialize((object)options.TocOptions);
					compilationManifest.TocPositionSnapshot = options.TocPosition.ToString();
				}
				catch
				{
					compilationManifest.TocOptionsSnapshotJson = null;
				}
				foreach (CompilationArticleInfo article in articleList.Articles)
				{
					compilationManifest.Articles.Add(new CompilationArticleInfo
					{
						OrderIndex = article.OrderIndex,
						Title = article.Title,
						BeginBookmarkName = BeginBookmarkName(article.OrderIndex),
						EndBookmarkName = EndBookmarkName(article.OrderIndex),
						TitleParagraphCount = article.TitleParagraphCount
					});
				}
				CompilationManifestService.Write(document, compilationManifest);
				for (int num = scan.BodyMarkerParagraphIndexes.Count - 1; num >= 0; num--)
				{
					int num2 = scan.BodyMarkerParagraphIndexes[num];
					if (num >= scan.BodyMarkerParagraphHasPageBreak.Count || !scan.BodyMarkerParagraphHasPageBreak[num] || num >= scan.BodyMarkerTextStarts.Count || num >= scan.BodyMarkerTextEnds.Count)
					{
						int titleStartParagraphIndex2 = articleList.Articles[num].TitleStartParagraphIndex;
						DeleteRange(document, scan.ParagraphStarts[num2], scan.ParagraphStarts[titleStartParagraphIndex2]);
					}
					else
					{
						int titleStartParagraphIndex3 = articleList.Articles[num].TitleStartParagraphIndex;
						if (titleStartParagraphIndex3 > num2 + 1)
						{
							DeleteRange(document, scan.ParagraphEnds[num2], scan.ParagraphStarts[titleStartParagraphIndex3]);
						}
						DeleteRange(document, scan.BodyMarkerTextStarts[num], scan.BodyMarkerTextEnds[num]);
					}
				}
				return compilationManifest;
			}
			throw new ArgumentNullException("options");
		}
		throw new ArgumentNullException("document");
	}

	public static CompilationArticleList TryReadFromBoundaries(Document document, out string failureReason)
	{
		bool titlesReliable;
		return TryReadFromBoundaries(document, new FormatConfig(), out failureReason, out titlesReliable);
	}

	public static CompilationArticleList TryReadFromBoundaries(Document document, FormatConfig config, out string failureReason)
	{
		bool titlesReliable;
		return TryReadFromBoundaries(document, config, out failureReason, out titlesReliable);
	}

	public static CompilationArticleList TryReadFromBoundaries(Document document, out string failureReason, out bool titlesReliable)
	{
		return TryReadFromBoundaries(document, new FormatConfig(), out failureReason, out titlesReliable);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static CompilationArticleList TryReadFromBoundaries(Document document, FormatConfig config, out string failureReason, out bool titlesReliable)
	{
		failureReason = null;
		titlesReliable = true;
		CompilationManifest compilationManifest = CompilationManifestService.Read(document);
		if (compilationManifest == null)
		{
			failureReason = "文档中没有可验证的汇编排版文章清单。";
			return null;
		}
		if (!CompilationManifestService.ValidateManifestStructure(compilationManifest, out var error))
		{
			failureReason = "汇编排版文章清单已损坏：" + error + "请重新建立汇编结构。";
			return null;
		}
		CompilationArticleList compilationArticleList = new CompilationArticleList();
		int num = -1;
		ParagraphTextSnapshot paragraphTextSnapshot = ParagraphTextSnapshot.Capture(document);
		int cursor = 0;
		foreach (CompilationArticleInfo article in compilationManifest.Articles)
		{
			Microsoft.Office.Interop.Word.Range value = GetBookmarkRange(document, article.BeginBookmarkName);
			Microsoft.Office.Interop.Word.Range value2 = GetBookmarkRange(document, article.EndBookmarkName);
			if (value == null || value2 == null)
			{
				ComObjectRelease.Release(ref value, "CompilationBoundary.ReadBegin");
				ComObjectRelease.Release(ref value2, "CompilationBoundary.ReadEnd");
				failureReason = "第 " + article.OrderIndex + " 篇的内部文章边界已损坏或丢失，请重新检查汇编结构。";
				return null;
			}
			if (value2.Start > value.Start && value.Start >= num)
			{
				string title = article.Title;
				int titleParagraphCount = article.TitleParagraphCount;
				List<string> list = (paragraphTextSnapshot.IsReliable ? ReadArticleParagraphTexts(paragraphTextSnapshot, value.Start, value2.Start, ref cursor) : ReadArticleParagraphTextsFromRange(document, value.Start, value2.Start));
				if (list != null && list.Count > 0)
				{
					CompilationArticleTitleResolution compilationArticleTitleResolution = CompilationArticleTitleResolver.Resolve(list, 0, list.Count, config);
					if (compilationArticleTitleResolution.HasTitle)
					{
						title = compilationArticleTitleResolution.Title;
						titleParagraphCount = compilationArticleTitleResolution.TitleParagraphCount;
					}
					else
					{
						titlesReliable = false;
					}
				}
				else
				{
					titlesReliable = false;
				}
				compilationArticleList.Articles.Add(new CompilationArticleInfo
				{
					OrderIndex = article.OrderIndex,
					Title = title,
					BeginBookmarkName = article.BeginBookmarkName,
					EndBookmarkName = article.EndBookmarkName,
					TitleParagraphCount = titleParagraphCount,
					StartPosition = value.Start,
					EndPosition = value2.Start
				});
				num = value2.Start;
				ComObjectRelease.Release(ref value, "CompilationBoundary.ReadBegin");
				ComObjectRelease.Release(ref value2, "CompilationBoundary.ReadEnd");
				continue;
			}
			ComObjectRelease.Release(ref value, "CompilationBoundary.ReadBegin");
			ComObjectRelease.Release(ref value2, "CompilationBoundary.ReadEnd");
			failureReason = "文章边界顺序与清单不一致（第 " + article.OrderIndex + " 篇），请重新建立汇编结构。";
			return null;
		}
		compilationArticleList.FrontMatterEndParagraphIndex = ((!compilationManifest.HasFrontMatter) ? (-1) : 0);
		return compilationArticleList;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static List<string> ReadArticleParagraphTextsFromRange(Document document, int articleStart, int articleEnd)
	{
		if (document == null || articleEnd <= articleStart)
		{
			return null;
		}
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			object Start = articleStart;
			object End = articleEnd;
			value = document.Range(ref Start, ref End);
			return CompilationArticleTitleResolver.SplitParagraphTexts(value.Text);
		}
		catch
		{
			return null;
		}
		finally
		{
			ComObjectRelease.Release(ref value, "CompilationBoundary.ArticleTitleTextRange");
		}
	}

	private static List<string> ReadArticleParagraphTexts(ParagraphTextSnapshot snapshot, int articleStart, int articleEnd, ref int cursor)
	{
		List<string> list = new List<string>();
		if (snapshot == null || !snapshot.IsReliable || articleEnd <= articleStart)
		{
			return list;
		}
		while (cursor < snapshot.Paragraphs.Count && snapshot.Paragraphs[cursor].RangeEnd <= articleStart)
		{
			cursor++;
		}
		int i;
		for (i = cursor; i < snapshot.Paragraphs.Count; i++)
		{
			WordParagraphTextMap.Entry entry = snapshot.Paragraphs[i];
			if (entry.RangeStart >= articleEnd)
			{
				break;
			}
			if (entry.RangeEnd > articleStart)
			{
				list.Add(entry.Text);
			}
		}
		cursor = i;
		return list;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static CompilationTocRangeRelation ClassifyCompilationTocRange(Document document, int selectionStart, int selectionEnd, FormatConfig formatConfig = null)
	{
		Microsoft.Office.Interop.Word.Range value = GetBookmarkRange(document, "SXCF_TOC_BEGIN");
		Microsoft.Office.Interop.Word.Range value2 = GetBookmarkRange(document, "SXCF_TOC_END");
		CompilationTocRangeRelation compilationTocRangeRelation = CompilationTocRangeRelation.Outside;
		if (value != null && value2 != null)
		{
			int start = value.Start;
			int start2 = value2.Start;
			compilationTocRangeRelation = CompilationTocRangeClassifier.Classify(start, start2, selectionStart, selectionEnd);
		}
		ComObjectRelease.Release(ref value, "CompilationBoundary.TocBegin");
		ComObjectRelease.Release(ref value2, "CompilationBoundary.TocEnd");
		if (compilationTocRangeRelation == CompilationTocRangeRelation.Inside)
		{
			return compilationTocRangeRelation;
		}
		try
		{
			CompilationManifest compilationManifest = CompilationManifestService.Read(document);
			if (compilationManifest != null && compilationManifest.Articles != null && compilationManifest.Articles.Count != 0)
			{
				List<CompilationTocEntry> list = new List<CompilationTocEntry>();
				foreach (CompilationArticleInfo article in compilationManifest.Articles)
				{
					if (article != null)
					{
						list.Add(new CompilationTocEntry
						{
							Title = article.Title,
							BookmarkName = article.BeginBookmarkName,
							OrderIndex = article.OrderIndex
						});
					}
				}
				CompilationTocOptions options = ((formatConfig == null || formatConfig.CompilationFormatOptions == null) ? null : formatConfig.CompilationFormatOptions.TocOptions);
				CompilationTocRangeRelation compilationTocRangeRelation2 = CompilationTocOwnershipService.ClassifySelection(document, selectionStart, selectionEnd, list, options);
				if (compilationTocRangeRelation2 != CompilationTocRangeRelation.Inside)
				{
					if (compilationTocRangeRelation == CompilationTocRangeRelation.Overlapping || compilationTocRangeRelation2 == CompilationTocRangeRelation.Overlapping)
					{
						return CompilationTocRangeRelation.Overlapping;
					}
					return CompilationTocRangeRelation.Outside;
				}
				return compilationTocRangeRelation2;
			}
			return compilationTocRangeRelation;
		}
		catch (Exception ex)
		{
			LogService.Warn("CompilationBoundary.ClassifyTocOwnership", ex);
			return compilationTocRangeRelation;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int GetDocumentBodyEnd(Document document)
	{
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			value = document.Content;
			return value.End - 1;
		}
		finally
		{
			ComObjectRelease.Release(ref value, "CompilationBoundary.Content");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void DeleteRange(Document document, int start, int end)
	{
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			object Start = start;
			object End = end;
			value = document.Range(ref Start, ref End);
			Microsoft.Office.Interop.Word.Range range = value;
			End = Type.Missing;
			Start = Type.Missing;
			range.Delete(ref End, ref Start);
		}
		finally
		{
			ComObjectRelease.Release(ref value, "CompilationBoundary.DeleteMarker");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void AddBookmarkAt(Document document, string name, int position)
	{
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			object Start = position;
			object End = position;
			value = document.Range(ref Start, ref End);
			Bookmarks bookmarks = document.Bookmarks;
			End = value;
			bookmarks.Add(name, ref End);
		}
		finally
		{
			ComObjectRelease.Release(ref value, "CompilationBoundary.AddBookmark");
		}
	}

	private static Microsoft.Office.Interop.Word.Range GetBookmarkRange(Document document, string name)
	{
		try
		{
			if (!document.Bookmarks.Exists(name))
			{
				return null;
			}
			Bookmarks bookmarks = document.Bookmarks;
			object Index = name;
			return bookmarks.get_Item(ref Index).Range;
		}
		catch
		{
			return null;
		}
	}
}
