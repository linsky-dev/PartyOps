using System.Runtime.CompilerServices;

namespace DocumentRepository.Services.Conversion.PdfToWord.Tables;

public sealed class CrossPageTableContinuationEvidence
{
	public int PreviousPageNumber { get; }

	public int NextPageNumber { get; }

	public CrossPageTableId PreviousTableId { get; }

	public CrossPageTableId NextTableId { get; }

	public bool PreviousTouchesBottom { get; }

	public bool NextTouchesTop { get; }

	public bool ColumnCountMatches { get; }

	public bool NormalizedColumnBoundariesMatch { get; }

	public bool RepeatedHeaderMatches { get; }

	public bool HasInterveningMeaningfulContent { get; }

	public bool GeometryReliable { get; }

	public PdfTableConfidence Confidence { get; }

	public string ReasonCode { get; }

	public CrossPageTableContinuationEvidence(int previousPageNumber, int nextPageNumber, CrossPageTableId previousTableId, CrossPageTableId nextTableId, bool previousTouchesBottom, bool nextTouchesTop, bool columnCountMatches, bool normalizedColumnBoundariesMatch, bool repeatedHeaderMatches, bool hasInterveningMeaningfulContent, bool geometryReliable, PdfTableConfidence confidence, string reasonCode)
	{
		PreviousPageNumber = previousPageNumber;
		NextPageNumber = nextPageNumber;
		PreviousTableId = previousTableId;
		NextTableId = nextTableId;
		PreviousTouchesBottom = previousTouchesBottom;
		NextTouchesTop = nextTouchesTop;
		ColumnCountMatches = columnCountMatches;
		NormalizedColumnBoundariesMatch = normalizedColumnBoundariesMatch;
		RepeatedHeaderMatches = repeatedHeaderMatches;
		HasInterveningMeaningfulContent = hasInterveningMeaningfulContent;
		GeometryReliable = geometryReliable;
		Confidence = confidence;
		ReasonCode = reasonCode ?? string.Empty;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public override string ToString()
	{
		return $"{ReasonCode} p{PreviousPageNumber}->{NextPageNumber} bottom={PreviousTouchesBottom} top={NextTouchesTop} cols={ColumnCountMatches} bounds={NormalizedColumnBoundariesMatch} header={RepeatedHeaderMatches} intervening={HasInterveningMeaningfulContent} geo={GeometryReliable}";
	}
}
