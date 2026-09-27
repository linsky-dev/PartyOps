using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Services.Cleanup;
using DocumentRepository.Services.Interop;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.Normalization;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Formatting;

public static class FormatDocumentCleanupService
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void CleanDocument(Document doc, FormatContext fctx)
	{
		if (doc == null)
		{
			throw new ArgumentNullException("doc");
		}
		if (fctx == null)
		{
			throw new ArgumentNullException("fctx");
		}
		FormatConfig config = fctx.Config;
		if (config == null)
		{
			throw new InvalidOperationException("Format context is missing config.");
		}
		bool hasTables = fctx.HasTables;
		bool hasImages = fctx.HasImages;
		bool flag = hasTables || hasImages;
		Stopwatch stopwatch = Stopwatch.StartNew();
		long previous = 0L;
		List<string> list = new List<string>();
		LogStep("line-breaks", "start");
		int num = TextCleanupService.NormalizeManualLineBreaks(doc, hasTables, hasImages);
		previous = AddTiming(list, "line-breaks", stopwatch, previous);
		LogStep("line-breaks", "complete, changed=" + num);
		LogStep("head-noise", "start");
		TextCleanupService.NormalizeParagraphHeadNoise(doc, hasTables, hasImages);
		previous = AddTiming(list, "head-noise", stopwatch, previous);
		LogStep("head-noise", "complete");
		LogStep("full-width", "start");
		TextCleanupService.ConvertFullWidthLettersAndDigits(doc, hasTables, hasImages);
		previous = AddTiming(list, "full-width", stopwatch, previous);
		LogStep("full-width", "complete");
		LogStep("tabs", "start");
		int num2 = TextCleanupService.RemoveTabs(doc, hasTables, hasImages);
		previous = AddTiming(list, "tabs", stopwatch, previous);
		LogStep("tabs", "complete, changed=" + num2);
		LogStep("punctuation", "start");
		if (flag)
		{
			TextCleanupService.CleanSymbolsAndPunctuationSkipTables(doc, hasTables, hasImages, config);
		}
		else
		{
			TextCleanupService.CleanSymbolsAndPunctuation(doc, config);
		}
		previous = AddTiming(list, "punctuation", stopwatch, previous);
		LogStep("punctuation", "complete");
		LogStep("empty-paragraphs", "start");
		StructureCleanupService.RemoveEmptyParagraphs(doc, hasTables, hasImages);
		previous = AddTiming(list, "empty-paragraphs", stopwatch, previous);
		LogStep("empty-paragraphs", "complete");
		LogStep("spaces", "start");
		if (config.DeleteSpaces)
		{
			if (flag)
			{
				TextCleanupService.RemoveConfiguredSpacesSkipTables(doc, hasTables, hasImages);
			}
			else
			{
				TextCleanupService.RemoveConfiguredSpaces(doc);
			}
		}
		previous = AddTiming(list, "spaces", stopwatch, previous);
		LogStep("spaces", "complete");
		LogStep("decimal-points", "start");
		int num3 = TextCleanupService.NormalizeDecimalPoints(doc, hasTables, hasImages);
		previous = AddTiming(list, "decimal-points", stopwatch, previous);
		LogStep("decimal-points", "complete, changed=" + num3);
		LogStep("document-number", "start");
		TextCleanupService.ReplaceDocumentNumberBrackets(doc, hasTables, hasImages);
		previous = AddTiming(list, "document-number", stopwatch, previous);
		LogStep("document-number", "complete");
		LogStep("hyperlinks", "start");
		if (config.RemoveHyperlinks && (fctx.Analysis == null || fctx.Analysis.HasHyperlinks))
		{
			StructureCleanupService.RemoveDocumentHyperlinks(doc);
		}
		AddTiming(list, "hyperlinks", stopwatch, previous);
		LogStep("hyperlinks", "complete");
		LogService.Info("[FORMAT-CLEANUP-PERF] complex=" + flag + ", total=" + stopwatch.ElapsedMilliseconds + "ms, " + string.Join(", ", list));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void CleanSelectionRange(Document doc, Microsoft.Office.Interop.Word.Range range, FormatContext fctx)
	{
		if (doc != null)
		{
			if (range == null)
			{
				throw new ArgumentNullException("range");
			}
			if (fctx != null)
			{
				FormatConfig config = fctx.Config;
				if (config == null)
				{
					throw new InvalidOperationException("Format context is missing config.");
				}
				bool hasTables = RangeHasTables(range);
				bool hasImages = RangeHasImages(range);
				TextCleanupService.CleanSymbolsAndPunctuation(range, config);
				if (config.DeleteSpaces)
				{
					TextCleanupService.RemoveConfiguredSpaces(range);
				}
				TextCleanupService.NormalizeDecimalPoints(range);
				TextCleanupService.ReplaceDocumentNumberBrackets(range, hasTables, hasImages);
				if (config.RemoveHyperlinks)
				{
					StructureCleanupService.RemoveDocumentHyperlinks(range);
				}
				StructureCleanupService.RemoveEmptyParagraphs(range, fctx.PreserveSectionBreaksInCleanup);
				return;
			}
			throw new ArgumentNullException("fctx");
		}
		throw new ArgumentNullException("doc");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void NormalizeAttachmentListsForDocument(FormatContext fctx)
	{
		if (fctx == null)
		{
			throw new ArgumentNullException("fctx");
		}
		if (fctx.Document == null)
		{
			throw new InvalidOperationException("Format context is missing document.");
		}
		if (fctx.Config != null)
		{
			if (fctx.Config.EnableAttachmentFormatting)
			{
				AttachmentListNormalizationService.NormalizeDocument(fctx.Document);
			}
			return;
		}
		throw new InvalidOperationException("Format context is missing config.");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void NormalizeAttachmentListsForSelection(FormatContext fctx)
	{
		if (fctx != null)
		{
			if (fctx.Document != null)
			{
				if (fctx.SelectionRange == null)
				{
					throw new InvalidOperationException("Format context is missing selection range.");
				}
				if (fctx.Config != null)
				{
					if (fctx.Config.EnableAttachmentFormatting && FormatRangeInspector.RangeHasAttachmentText(fctx.SelectionRange))
					{
						AttachmentListNormalizationService.NormalizeRange(fctx.Document, fctx.SelectionRange);
					}
					return;
				}
				throw new InvalidOperationException("Format context is missing config.");
			}
			throw new InvalidOperationException("Format context is missing document.");
		}
		throw new ArgumentNullException("fctx");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool RangeHasTables(Microsoft.Office.Interop.Word.Range range)
	{
		if (range == null)
		{
			throw new ArgumentNullException("range");
		}
		if (range.Tables != null)
		{
			return range.Tables.Count > 0;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool RangeHasImages(Microsoft.Office.Interop.Word.Range range)
	{
		if (range == null)
		{
			throw new ArgumentNullException("range");
		}
		return WordRangeInspector.HasInlineOrAnchoredShape(range);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static long AddTiming(ICollection<string> timings, string name, Stopwatch stopwatch, long previous)
	{
		long elapsedMilliseconds = stopwatch.ElapsedMilliseconds;
		timings.Add(name + "=" + Math.Max(0L, elapsedMilliseconds - previous) + "ms");
		return elapsedMilliseconds;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void LogStep(string step, string state)
	{
		LogService.Info("[FORMAT-CLEANUP] step=" + step + ", state=" + state);
	}
}
