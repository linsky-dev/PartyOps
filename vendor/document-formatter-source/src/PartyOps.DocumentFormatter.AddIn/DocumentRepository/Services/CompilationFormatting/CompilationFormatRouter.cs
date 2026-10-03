using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Models.CompilationFormatting;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.CompilationFormatting;

public static class CompilationFormatRouter
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static CompilationRouteResolution Resolve(OperationContext context)
	{
		if (context != null)
		{
			FormatConfig currentConfig = context.CurrentConfig;
			bool flag = currentConfig != null && currentConfig.EnableCompilationFormatting && currentConfig.CompilationFormatOptions != null;
			Document document = context.Document;
			if (document != null)
			{
				if (TryGetSelectionRange(context, out var start, out var end))
				{
					switch (CompilationBoundaryService.ClassifyCompilationTocRange(document, start, end, currentConfig))
					{
					case CompilationTocRangeRelation.Overlapping:
						return CompilationRouteResolution.Rejected(CompilationFormatFailureReasonCode.SelectionSpansTocAndBody);
					case CompilationTocRangeRelation.Inside:
						return new CompilationRouteResolution
						{
							Decision = new CompilationRouteDecision
							{
								Kind = CompilationRouteKind.CompilationTocUpdate
							}
						};
					}
				}
				if (flag)
				{
					CompilationDocumentScanResult compilationDocumentScanResult = null;
					CompilationArticleList compilationArticleList = null;
					bool usesVisibleMarkers = false;
					string failureReason = null;
					try
					{
						compilationDocumentScanResult = CompilationDocumentScanner.Scan(document);
						if (compilationDocumentScanResult.ScanErrors != null && compilationDocumentScanResult.ScanErrors.Count > 0)
						{
							return CompilationRouteResolution.Rejected(CompilationFormatFailureReasonCode.ScanFailed, string.Join("；", compilationDocumentScanResult.ScanErrors), CompilationFormatFailureStage.StructureScan);
						}
						if (compilationDocumentScanResult.MalformedBodyMarkers != null && compilationDocumentScanResult.MalformedBodyMarkers.Count > 0)
						{
							return CompilationRouteResolution.Rejected(CompilationFormatFailureReasonCode.MalformedMarker, string.Join("、", compilationDocumentScanResult.MalformedBodyMarkers), CompilationFormatFailureStage.StructureScan);
						}
						if (compilationDocumentScanResult.BodyMarkerParagraphIndexes.Count > 0)
						{
							usesVisibleMarkers = true;
							compilationArticleList = CompilationArticleListBuilder.BuildFromParagraphTextsWithConfig(compilationDocumentScanResult.ParagraphTexts, currentConfig);
							if (!string.IsNullOrEmpty(compilationArticleList.ErrorMessage))
							{
								failureReason = compilationArticleList.ErrorMessage;
								compilationArticleList = null;
							}
						}
						else
						{
							compilationArticleList = CompilationBoundaryService.TryReadFromBoundaries(document, currentConfig, out failureReason);
						}
					}
					catch (Exception ex)
					{
						LogService.Error("CompilationFormatRouter.Resolve.Scan", ex);
						return CompilationRouteResolution.Rejected(CompilationFormatFailureReasonCode.ScanFailed, null, CompilationFormatFailureStage.StructureScan);
					}
					CompilationRouteDecision compilationRouteDecision = CompilationFormatEntryDispatcher.Decide(BuildFacts(context, compilationDocumentScanResult, compilationArticleList, usesVisibleMarkers));
					switch (compilationRouteDecision.Kind)
					{
					case CompilationRouteKind.Reject:
						return CompilationRouteResolution.Rejected(compilationRouteDecision.RejectReasonCode, compilationRouteDecision.RejectStructuredDetail);
					case CompilationRouteKind.CompilationTocUpdate:
						return new CompilationRouteResolution
						{
							Decision = compilationRouteDecision
						};
					case CompilationRouteKind.CompilationFull:
					case CompilationRouteKind.CompilationPartial:
						return new CompilationRouteResolution
						{
							Decision = compilationRouteDecision,
							ArticleList = compilationArticleList,
							Scan = compilationDocumentScanResult,
							UsesVisibleMarkers = usesVisibleMarkers,
							StructureFailure = failureReason
						};
					case CompilationRouteKind.LegacyFull:
					case CompilationRouteKind.LegacySelection:
						return null;
					default:
						return CompilationRouteResolution.Rejected(CompilationFormatFailureReasonCode.Unknown);
					}
				}
				return null;
			}
			return null;
		}
		throw new ArgumentNullException("context");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool TryGetSelectionRange(OperationContext context, out int start, out int end)
	{
		start = 0;
		end = 0;
		Selection selection = context.Selection;
		if (selection == null)
		{
			return false;
		}
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			value = selection.Range;
			if (value != null)
			{
				start = value.Start;
				end = value.End;
				return true;
			}
			return false;
		}
		catch
		{
			return false;
		}
		finally
		{
			ComObjectRelease.Release(ref value, "CompilationFormatRouter.SelectionRange");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static CompilationRouteFacts BuildFacts(OperationContext context, CompilationDocumentScanResult scan, CompilationArticleList articleList, bool usesVisibleMarkers)
	{
		CompilationRouteFacts compilationRouteFacts = new CompilationRouteFacts
		{
			CompilationEnabled = true,
			HasArticleStructure = (articleList != null && articleList.Articles.Count > 0),
			UsesVisibleMarkers = usesVisibleMarkers,
			ArticleCount = (articleList?.Articles.Count ?? 0),
			HasFrontMatter = (articleList != null && articleList.FrontMatterEndParagraphIndex >= 0),
			HasMeaningfulSelection = context.HasMeaningfulSelection,
			IsWholeDocumentSelection = false
		};
		if (!TryGetSelectionRange(context, out var start, out var end))
		{
			return compilationRouteFacts;
		}
		Document document = context.Document;
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			value = document.Content;
			compilationRouteFacts.IsWholeDocumentSelection = start <= value.Start + 1 && end >= value.End - 1;
		}
		finally
		{
			ComObjectRelease.Release(ref value, "CompilationFormatRouter.Content");
		}
		if (compilationRouteFacts.HasArticleStructure && compilationRouteFacts.HasMeaningfulSelection)
		{
			List<int[]> articleRanges = GetArticleRanges(document, articleList, scan, usesVisibleMarkers);
			compilationRouteFacts.FirstSelectedArticleIndex = FindArticleIndex(articleRanges, start);
			int position = ((end > start) ? (end - 1) : end);
			compilationRouteFacts.LastSelectedArticleIndex = FindArticleIndex(articleRanges, position);
			if (compilationRouteFacts.FirstSelectedArticleIndex >= 0 && compilationRouteFacts.FirstSelectedArticleIndex == compilationRouteFacts.LastSelectedArticleIndex && start > articleRanges[compilationRouteFacts.FirstSelectedArticleIndex][0])
			{
				compilationRouteFacts.SelectionInsideSingleArticle = true;
			}
			if (compilationRouteFacts.FirstSelectedArticleIndex >= 0 && compilationRouteFacts.LastSelectedArticleIndex >= 0)
			{
				compilationRouteFacts.SelectionTruncatedAtStart = start > articleRanges[compilationRouteFacts.FirstSelectedArticleIndex][0];
				compilationRouteFacts.SelectionTruncatedAtEnd = end < articleRanges[compilationRouteFacts.LastSelectedArticleIndex][1];
			}
			return compilationRouteFacts;
		}
		return compilationRouteFacts;
	}

	private static List<int[]> GetArticleRanges(Document doc, CompilationArticleList articleList, CompilationDocumentScanResult scan, bool usesVisibleMarkers)
	{
		List<int[]> list = new List<int[]>();
		if (usesVisibleMarkers)
		{
			int documentBodyEnd = GetDocumentBodyEnd(doc);
			for (int i = 0; i < articleList.Articles.Count; i++)
			{
				int index = scan.BodyMarkerParagraphIndexes[i];
				int num = scan.ParagraphStarts[index];
				int num2 = ((i + 1 < articleList.Articles.Count) ? scan.ParagraphStarts[scan.BodyMarkerParagraphIndexes[i + 1]] : documentBodyEnd);
				list.Add(new int[2] { num, num2 });
			}
		}
		else
		{
			foreach (CompilationArticleInfo article in articleList.Articles)
			{
				list.Add(new int[2] { article.StartPosition, article.EndPosition });
			}
		}
		return list;
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
			ComObjectRelease.Release(ref value, "CompilationFormatRouter.BodyEnd");
		}
	}

	private static int FindArticleIndex(List<int[]> ranges, int position)
	{
		for (int i = 0; i < ranges.Count; i++)
		{
			if (position >= ranges[i][0] && position < ranges[i][1])
			{
				return i;
			}
		}
		return -1;
	}
}
