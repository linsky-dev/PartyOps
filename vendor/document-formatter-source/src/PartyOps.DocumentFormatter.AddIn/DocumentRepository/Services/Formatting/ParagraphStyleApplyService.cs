using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Normalization;
using DocumentRepository.Services.Performance;
using DocumentRepository.Services.Snapshots;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Formatting;

internal static class ParagraphStyleApplyService
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string ApplyParagraph(Paragraph para, ElementType type, ElementType[] allTypes, int index, string text, FormatTextStyleDefinition styleDefinition, FirstFormatDiagnosticsSession diagnostics = null)
	{
		if (para != null)
		{
			if (styleDefinition == null)
			{
				throw new ArgumentNullException("styleDefinition");
			}
			long startTimestamp = FirstFormatDiagnosticsSession.Timestamp();
			long startTimestamp2 = FirstFormatDiagnosticsSession.Timestamp();
			text = HeadingSpacingNormalizer.NormalizeIfNeeded(para, type, text, styleDefinition.Config);
			diagnostics?.Accumulate("apply.heading-spacing", startTimestamp2);
			DocumentStyleManager.ApplyPreparedStyle(para, type, diagnostics);
			ApplyParagraphExceptions(para, type, allTypes, index, styleDefinition, diagnostics);
			diagnostics?.Accumulate("apply.full-rebuild", startTimestamp);
			return text;
		}
		throw new ArgumentNullException("para");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string ApplyParagraphToRange(Microsoft.Office.Interop.Word.Range range, ElementType type, ElementType[] allTypes, int index, string text, FormatTextStyleDefinition styleDefinition, FirstFormatDiagnosticsSession diagnostics = null)
	{
		if (range == null)
		{
			throw new ArgumentNullException("range");
		}
		if (styleDefinition != null)
		{
			long startTimestamp = FirstFormatDiagnosticsSession.Timestamp();
			long startTimestamp2 = FirstFormatDiagnosticsSession.Timestamp();
			text = HeadingSpacingNormalizer.NormalizeIfNeeded(range, type, text, styleDefinition.Config);
			diagnostics?.Accumulate("apply.heading-spacing", startTimestamp2);
			DocumentStyleManager.ApplyPreparedStyle(range, type, diagnostics);
			ApplyParagraphExceptions(range, type, allTypes, index, styleDefinition, diagnostics);
			diagnostics?.Accumulate("apply.full-rebuild", startTimestamp);
			return text;
		}
		throw new ArgumentNullException("styleDefinition");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ApplyParagraphExceptions(Microsoft.Office.Interop.Word.Range range, ElementType type, ElementType[] allTypes, int index, FormatTextStyleDefinition styleDefinition, FirstFormatDiagnosticsSession diagnostics = null)
	{
		if (range != null)
		{
			if (styleDefinition != null)
			{
				if (!ParagraphTypeSequenceNormalizer.IsTitleSpacingType(type))
				{
					return;
				}
				long startTimestamp = FirstFormatDiagnosticsSession.Timestamp();
				ParagraphFormat value = null;
				try
				{
					value = range.ParagraphFormat;
					value.LineUnitBefore = 0f;
					value.LineUnitAfter = 0f;
					value.SpaceBeforeAuto = 0;
					value.SpaceAfterAuto = 0;
					value.SpaceBefore = 0f;
					value.SpaceAfter = (ParagraphTypeSequenceNormalizer.ShouldKeepTitleAfterSpacing(type, allTypes, index) ? Math.Max(0f, styleDefinition.MainTitleSpaceAfter) : 0f);
				}
				finally
				{
					if (value != null)
					{
						ComObjectRelease.Release(ref value, "ParagraphStyleApplyService.pf");
					}
				}
				diagnostics?.Accumulate("apply.paragraph-exceptions", startTimestamp);
				return;
			}
			throw new ArgumentNullException("styleDefinition");
		}
		throw new ArgumentNullException("range");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string ApplySelectionParagraph(Paragraph para, ElementType type, ElementType[] allTypes, int index, string text, FormatTextStyleDefinition styleDefinition)
	{
		if (para != null)
		{
			if (styleDefinition == null)
			{
				throw new ArgumentNullException("styleDefinition");
			}
			text = HeadingSpacingNormalizer.NormalizeIfNeeded(para, type, text, styleDefinition.Config);
			DocumentStyleManager.ApplyStyle(para, type);
			ApplyParagraphExceptions(para, type, allTypes, index, styleDefinition);
			return text;
		}
		throw new ArgumentNullException("para");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ApplyMergedStyle(Document doc, Paragraphs paragraphs, int start, int end, ElementType type, FirstFormatDiagnosticsSession diagnostics = null)
	{
		if (doc == null)
		{
			throw new ArgumentNullException("doc");
		}
		if (paragraphs == null)
		{
			throw new ArgumentNullException("paragraphs");
		}
		Microsoft.Office.Interop.Word.Range value = null;
		Paragraph value2 = null;
		Paragraph value3 = null;
		try
		{
			value2 = paragraphs[start + 1];
			value3 = paragraphs[end + 1];
			long startTimestamp = FirstFormatDiagnosticsSession.Timestamp();
			object Start = value2.Range.Start;
			object End = value3.Range.End;
			value = doc.Range(ref Start, ref End);
			diagnostics?.Accumulate("apply.range-create", startTimestamp);
			DocumentStyleManager.ApplyPreparedStyle(value, type, diagnostics);
		}
		finally
		{
			if (value2 != null)
			{
				ComObjectRelease.Release(ref value2, "ParagraphStyleApplyService.sp");
			}
			if (value3 != null)
			{
				ComObjectRelease.Release(ref value3, "ParagraphStyleApplyService.ep");
			}
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "ParagraphStyleApplyService.mergeRng");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void RefreshPreparedStyle(Paragraph paragraph, ElementType type, FirstFormatDiagnosticsSession diagnostics = null)
	{
		if (paragraph == null)
		{
			throw new ArgumentNullException("paragraph");
		}
		DocumentStyleManager.ApplyPreparedStyle(paragraph, type, diagnostics);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PrepareDirectFormattingRuns(Document doc, Paragraphs paragraphs, ParagraphComplianceState[] complianceStates, FirstFormatDiagnosticsSession diagnostics = null)
	{
		if (doc == null)
		{
			throw new ArgumentNullException("doc");
		}
		if (paragraphs == null)
		{
			throw new ArgumentNullException("paragraphs");
		}
		if (complianceStates == null)
		{
			throw new ArgumentNullException("complianceStates");
		}
		int num = 0;
		for (int i = 0; i < complianceStates.Length; i++)
		{
			for (; i < complianceStates.Length && complianceStates[i] != ParagraphComplianceState.NeedsFullRebuild; i++)
			{
			}
			if (i >= complianceStates.Length)
			{
				break;
			}
			int num2 = i;
			for (; i + 1 < complianceStates.Length && complianceStates[i + 1] == ParagraphComplianceState.NeedsFullRebuild; i++)
			{
			}
			int num3 = i;
			Paragraph value = null;
			Paragraph value2 = null;
			Microsoft.Office.Interop.Word.Range value3 = null;
			try
			{
				value = paragraphs[num2 + 1];
				value2 = paragraphs[num3 + 1];
				object Start = value.Range.Start;
				object End = value2.Range.End;
				value3 = doc.Range(ref Start, ref End);
				long startTimestamp = FirstFormatDiagnosticsSession.Timestamp();
				DocumentStyleManager.PrepareDirectFormatting(value3);
				diagnostics?.Accumulate("apply.prepare-direct-format", startTimestamp);
				num++;
			}
			finally
			{
				ComObjectRelease.Release(ref value3, "ParagraphStyleApplyService.PrepareDirectFormatting.Range");
				ComObjectRelease.Release(ref value2, "ParagraphStyleApplyService.PrepareDirectFormatting.Last");
				ComObjectRelease.Release(ref value, "ParagraphStyleApplyService.PrepareDirectFormatting.First");
			}
		}
		return num;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PrepareDirectFormattingRuns(Document doc, ParagraphTextSnapshot snapshot, ParagraphComplianceState[] complianceStates, FirstFormatDiagnosticsSession diagnostics = null)
	{
		if (doc != null)
		{
			if (snapshot != null && snapshot.IsReliable)
			{
				if (complianceStates == null)
				{
					throw new ArgumentNullException("complianceStates");
				}
				int num = 0;
				for (int i = 0; i < complianceStates.Length; i++)
				{
					for (; i < complianceStates.Length && complianceStates[i] != ParagraphComplianceState.NeedsFullRebuild; i++)
					{
					}
					if (i >= complianceStates.Length)
					{
						break;
					}
					int index = i;
					for (; i + 1 < complianceStates.Length && complianceStates[i + 1] == ParagraphComplianceState.NeedsFullRebuild; i++)
					{
					}
					int index2 = i;
					Microsoft.Office.Interop.Word.Range value = null;
					try
					{
						object Start = snapshot.Paragraphs[index].RangeStart;
						object End = snapshot.Paragraphs[index2].RangeEnd;
						value = doc.Range(ref Start, ref End);
						long startTimestamp = FirstFormatDiagnosticsSession.Timestamp();
						DocumentStyleManager.PrepareDirectFormatting(value);
						diagnostics?.Accumulate("apply.prepare-direct-format", startTimestamp);
						num++;
					}
					finally
					{
						ComObjectRelease.Release(ref value, "ParagraphStyleApplyService.PrepareDirectFormatting.Range");
					}
				}
				return num;
			}
			throw new ArgumentException("快照不可用。", "snapshot");
		}
		throw new ArgumentNullException("doc");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PrepareDirectFormattingRuns(Document doc, IList<int> paragraphRangeStarts, IList<int> paragraphRangeEnds, ParagraphComplianceState[] complianceStates, FirstFormatDiagnosticsSession diagnostics = null)
	{
		if (doc != null)
		{
			if (complianceStates != null)
			{
				if (paragraphRangeStarts == null || paragraphRangeEnds == null || paragraphRangeStarts.Count < complianceStates.Length || paragraphRangeEnds.Count < complianceStates.Length)
				{
					throw new ArgumentException("段落偏移表不可用或与段落数不一致。", "paragraphRangeStarts");
				}
				int num = 0;
				for (int i = 0; i < complianceStates.Length; i++)
				{
					for (; i < complianceStates.Length && complianceStates[i] != ParagraphComplianceState.NeedsFullRebuild; i++)
					{
					}
					if (i >= complianceStates.Length)
					{
						break;
					}
					int index = i;
					for (; i + 1 < complianceStates.Length && complianceStates[i + 1] == ParagraphComplianceState.NeedsFullRebuild; i++)
					{
					}
					int index2 = i;
					Microsoft.Office.Interop.Word.Range value = null;
					try
					{
						object Start = paragraphRangeStarts[index];
						object End = paragraphRangeEnds[index2];
						value = doc.Range(ref Start, ref End);
						long startTimestamp = FirstFormatDiagnosticsSession.Timestamp();
						DocumentStyleManager.PrepareDirectFormatting(value);
						diagnostics?.Accumulate("apply.prepare-direct-format", startTimestamp);
						num++;
					}
					finally
					{
						ComObjectRelease.Release(ref value, "ParagraphStyleApplyService.PrepareDirectFormatting.Range");
					}
				}
				return num;
			}
			throw new ArgumentNullException("complianceStates");
		}
		throw new ArgumentNullException("doc");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool AreAllParagraphsAlreadyStyle(Document doc, int start, int end, ElementType type)
	{
		if (doc == null)
		{
			throw new ArgumentNullException("doc");
		}
		for (int i = start; i <= end; i++)
		{
			Paragraph value = null;
			try
			{
				value = doc.Paragraphs[i + 1];
				if (DocumentStyleManager.IsParagraphStyle(value, type))
				{
					continue;
				}
				return false;
			}
			finally
			{
				if (value != null)
				{
					ComObjectRelease.Release(ref value, "ParagraphStyleApplyService.para");
				}
			}
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ApplyParagraphExceptions(Paragraph para, ElementType type, ElementType[] allTypes, int index, FormatTextStyleDefinition styleDefinition, FirstFormatDiagnosticsSession diagnostics = null)
	{
		if (para != null)
		{
			if (styleDefinition != null)
			{
				if (!ParagraphTypeSequenceNormalizer.IsTitleSpacingType(type))
				{
					return;
				}
				long startTimestamp = FirstFormatDiagnosticsSession.Timestamp();
				ParagraphFormat value = null;
				try
				{
					value = para.Range.ParagraphFormat;
					value.LineUnitBefore = 0f;
					value.LineUnitAfter = 0f;
					value.SpaceBeforeAuto = 0;
					value.SpaceAfterAuto = 0;
					value.SpaceBefore = 0f;
					value.SpaceAfter = (ParagraphTypeSequenceNormalizer.ShouldKeepTitleAfterSpacing(type, allTypes, index) ? Math.Max(0f, styleDefinition.MainTitleSpaceAfter) : 0f);
				}
				finally
				{
					if (value != null)
					{
						ComObjectRelease.Release(ref value, "ParagraphStyleApplyService.pf");
					}
				}
				diagnostics?.Accumulate("apply.paragraph-exceptions", startTimestamp);
				return;
			}
			throw new ArgumentNullException("styleDefinition");
		}
		throw new ArgumentNullException("para");
	}
}
