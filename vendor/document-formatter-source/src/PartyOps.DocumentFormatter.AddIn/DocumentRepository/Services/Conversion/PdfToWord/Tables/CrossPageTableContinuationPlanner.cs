using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace DocumentRepository.Services.Conversion.PdfToWord.Tables;

public static class CrossPageTableContinuationPlanner
{
	public const string ReasonConfirmed = "ContinuationConfirmed";

	public const string ReasonConfidenceNotHigh = "ConfidenceNotHigh";

	public const string ReasonRotationMismatch = "RotationMismatch";

	public const string ReasonPageSizeMismatch = "PageSizeMismatch";

	public const string ReasonLineFrameStructureMismatch = "LineFrameStructureMismatch";

	public const string ReasonInterveningAfterPrevious = "InterveningContentAfterPreviousTable";

	public const string ReasonInterveningBeforeNext = "InterveningContentBeforeNextTable";

	public const string ReasonPreviousNotAtBottom = "PreviousTableNotAtPageBottom";

	public const string ReasonNextNotAtTop = "NextTableNotAtPageTop";

	public const string ReasonColumnCountMismatch = "ColumnCountMismatch";

	public const string ReasonHorizontalPositionMismatch = "HorizontalPositionMismatch";

	public const string ReasonColumnBoundariesMismatch = "ColumnBoundariesMismatch";

	public const string ReasonNoHeaderOnPrevious = "NoReliableHeaderOnPrevious";

	public const string ReasonNextFirstRowNotHeader = "NextFirstRowNotHeader";

	public const string ReasonRepeatedHeaderMismatch = "RepeatedHeaderMismatch";

	public const string ReasonHeaderStyleMismatch = "HeaderStyleMismatch";

	public const string ReasonContinuationIdentityEvidenceMissing = "ContinuationIdentityEvidenceMissing";

	public const string ReasonSequenceColumnAmbiguous = "SequenceColumnAmbiguous";

	public const string ReasonSequenceNotContinuous = "SequenceNotContinuous";

	public const string ReasonContinuationMarkerMismatch = "ContinuationMarkerMismatch";

	public static CrossPageTableContinuationResult Plan(IReadOnlyList<PageTableFragmentFacts> pages, CrossPageTableContinuationOptions options = null)
	{
		if (options == null)
		{
			options = new CrossPageTableContinuationOptions();
		}
		List<CrossPageTableContinuationPlan> list = new List<CrossPageTableContinuationPlan>();
		List<CrossPageTableContinuationEvidence> list2 = new List<CrossPageTableContinuationEvidence>();
		if (pages == null || pages.Count < 2)
		{
			return new CrossPageTableContinuationResult(list, list2);
		}
		for (int i = 0; i < pages.Count - 1; i++)
		{
			PageTableFragmentFacts pageTableFragmentFacts = pages[i];
			PageTableFragmentFacts pageTableFragmentFacts2 = pages[i + 1];
			if (pageTableFragmentFacts == null || pageTableFragmentFacts2 == null || pageTableFragmentFacts2.PageIndex != pageTableFragmentFacts.PageIndex + 1 || pageTableFragmentFacts.Fragments.Count == 0 || pageTableFragmentFacts2.Fragments.Count == 0)
			{
				continue;
			}
			TableFragmentFacts tableFragmentFacts = pageTableFragmentFacts.Fragments[pageTableFragmentFacts.Fragments.Count - 1];
			TableFragmentFacts tableFragmentFacts2 = pageTableFragmentFacts2.Fragments[0];
			if (tableFragmentFacts != null && tableFragmentFacts2 != null)
			{
				CrossPageTableContinuationEvidence crossPageTableContinuationEvidence = Evaluate(pageTableFragmentFacts, tableFragmentFacts, pageTableFragmentFacts2, tableFragmentFacts2, options);
				list2.Add(crossPageTableContinuationEvidence);
				if (crossPageTableContinuationEvidence.Confidence == PdfTableConfidence.High)
				{
					list.Add(BuildPlan(pageTableFragmentFacts, tableFragmentFacts, pageTableFragmentFacts2, tableFragmentFacts2));
				}
			}
		}
		return new CrossPageTableContinuationResult(list, list2);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static CrossPageTableContinuationEvidence Evaluate(PageTableFragmentFacts previousPage, TableFragmentFacts previous, PageTableFragmentFacts nextPage, TableFragmentFacts next, CrossPageTableContinuationOptions options)
	{
		bool num = previous.Confidence == PdfTableConfidence.High && next.Confidence == PdfTableConfidence.High;
		bool flag = previousPage.Rotation == nextPage.Rotation;
		float num2 = Math.Max(options.PageWidthTolerancePoints, previousPage.Width * options.PageWidthToleranceRatio);
		bool flag2 = Math.Abs(previousPage.Width - nextPage.Width) <= num2;
		bool flag3 = previous.HasWireframe && previous.ClosedGridFrame && next.HasWireframe && next.ClosedGridFrame;
		bool geometryReliable = flag && flag2 && flag3;
		bool flag4 = !previous.HasMeaningfulContentAfter && !previous.HasMeaningfulContentBeside;
		bool flag5 = !next.HasMeaningfulContentBefore && !next.HasMeaningfulContentBeside;
		bool hasInterveningMeaningfulContent = !(flag4 && flag5);
		bool flag6 = previousPage.Height > 0f && previous.BottomY <= previousPage.Height * options.PageEdgeTouchRatio;
		bool flag7 = nextPage.Height > 0f && next.TopY >= nextPage.Height * (1f - options.PageEdgeTouchRatio);
		bool flag8 = previous.ColumnCount == next.ColumnCount && previous.ColumnCount > 0;
		float num3 = Math.Max(options.HorizontalPositionTolerancePoints, previousPage.Width * options.HorizontalPositionToleranceRatio);
		bool flag9 = Math.Abs(previous.LeftX - next.LeftX) <= num3 && Math.Abs(previous.RightX - next.RightX) <= num3;
		bool flag10 = flag8 && NormalizedBoundariesEqual(previous.NormalizedColumnBoundaries, next.NormalizedColumnBoundaries, options.ColumnBoundaryToleranceRatio);
		bool flag11 = previous.HeaderRowIndex >= 0 && previous.HeaderSignature != null;
		bool flag12 = next.HeaderRowIndex == 0 && next.FirstRowSignature != null;
		bool flag13 = flag11 && flag12 && RowStructureAndTextEqual(previous.HeaderSignature, next.FirstRowSignature);
		bool flag14 = flag13 && RowStyleEqual(previous.HeaderSignature, next.FirstRowSignature, options.HeaderFontSizeTolerance);
		SequenceColumnEvidence sequence = previous.Sequence;
		SequenceColumnEvidence sequence2 = next.Sequence;
		bool flag15 = sequence.IsReliable && sequence2.IsReliable && sequence.ColumnIndex == sequence2.ColumnIndex;
		bool flag16 = flag15 && sequence2.FirstDataValue == sequence.LastDataValue + 1;
		string text = ((!num) ? "ConfidenceNotHigh" : ((!flag) ? "RotationMismatch" : (flag2 ? ((!flag3) ? "LineFrameStructureMismatch" : ((!flag4) ? "InterveningContentAfterPreviousTable" : (flag5 ? (flag6 ? ((!flag7) ? "NextTableNotAtPageTop" : ((!flag8) ? "ColumnCountMismatch" : ((!flag9) ? "HorizontalPositionMismatch" : (flag10 ? (flag11 ? ((!flag12) ? "NextFirstRowNotHeader" : (flag13 ? ((!flag14) ? "HeaderStyleMismatch" : ((sequence.Ambiguous || sequence2.Ambiguous) ? "SequenceColumnAmbiguous" : ((!flag15) ? "ContinuationIdentityEvidenceMissing" : (flag16 ? "ContinuationConfirmed" : "SequenceNotContinuous")))) : "RepeatedHeaderMismatch")) : "NoReliableHeaderOnPrevious") : "ColumnBoundariesMismatch")))) : "PreviousTableNotAtPageBottom") : "InterveningContentBeforeNextTable"))) : "PageSizeMismatch")));
		return new CrossPageTableContinuationEvidence(previousPage.PageIndex + 1, nextPage.PageIndex + 1, previous.Id, next.Id, flag6, flag7, flag8, flag10, flag13 && flag14, hasInterveningMeaningfulContent, geometryReliable, (text == "ContinuationConfirmed") ? PdfTableConfidence.High : PdfTableConfidence.Low, text);
	}

	private static CrossPageTableContinuationPlan BuildPlan(PageTableFragmentFacts previousPage, TableFragmentFacts previous, PageTableFragmentFacts nextPage, TableFragmentFacts next)
	{
		List<CrossPageTableRowSource> list = new List<CrossPageTableRowSource>();
		for (int i = 0; i < previous.RowCount; i++)
		{
			list.Add(new CrossPageTableRowSource(previous.Id, i));
		}
		for (int j = 1; j < next.RowCount; j++)
		{
			list.Add(new CrossPageTableRowSource(next.Id, j));
		}
		return new CrossPageTableContinuationPlan(previous.Id, next.Id, 1, previous.RowCount, next.RowCount, previous.HeaderRowIndex, list, new int[2]
		{
			previousPage.PageIndex + 1,
			nextPage.PageIndex + 1
		}, ComputeStructureFingerprint(previous));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string ComputeStructureFingerprint(TableFragmentFacts previous)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append("cols=").Append(previous.ColumnCount).Append("|b=");
		foreach (float normalizedColumnBoundary in previous.NormalizedColumnBoundaries)
		{
			stringBuilder.Append(normalizedColumnBoundary.ToString("F4", CultureInfo.InvariantCulture)).Append(',');
		}
		stringBuilder.Append("|h=");
		if (previous.HeaderSignature != null)
		{
			foreach (string cellText in previous.HeaderSignature.CellTexts)
			{
				stringBuilder.Append(cellText).Append('\u0001');
			}
		}
		return PrivacySanitizer.ShortHash(stringBuilder.ToString());
	}

	private static bool NormalizedBoundariesEqual(IReadOnlyList<float> a, IReadOnlyList<float> b, float tolerance)
	{
		if (a == null || b == null)
		{
			return false;
		}
		if (a.Count == b.Count && a.Count >= 2)
		{
			for (int i = 0; i < a.Count; i++)
			{
				if (Math.Abs(a[i] - b[i]) > tolerance)
				{
					return false;
				}
			}
			return true;
		}
		return false;
	}

	private static bool RowStructureAndTextEqual(CrossPageTableRowSignature a, CrossPageTableRowSignature b)
	{
		if (a == null || b == null)
		{
			return false;
		}
		if (a.RealCellCount != b.RealCellCount || a.RealCellCount == 0)
		{
			return false;
		}
		for (int i = 0; i < a.RealCellCount; i++)
		{
			if (a.CellSpans[i] != b.CellSpans[i])
			{
				return false;
			}
			if (!string.Equals(a.CellTexts[i], b.CellTexts[i], StringComparison.Ordinal))
			{
				return false;
			}
		}
		return true;
	}

	private static bool RowStyleEqual(CrossPageTableRowSignature a, CrossPageTableRowSignature b, float fontSizeTolerance)
	{
		if (a != null && b != null)
		{
			if (a.RealCellCount != b.RealCellCount)
			{
				return false;
			}
			for (int i = 0; i < a.RealCellCount; i++)
			{
				if (a.CellBold[i] != b.CellBold[i])
				{
					return false;
				}
				if (Math.Abs(a.CellFontSizes[i] - b.CellFontSizes[i]) > fontSizeTolerance)
				{
					return false;
				}
			}
			return true;
		}
		return false;
	}
}
