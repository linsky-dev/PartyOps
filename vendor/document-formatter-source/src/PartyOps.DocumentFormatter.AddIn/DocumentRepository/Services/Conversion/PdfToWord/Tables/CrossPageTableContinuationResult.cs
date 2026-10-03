using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace DocumentRepository.Services.Conversion.PdfToWord.Tables;

public sealed class CrossPageTableContinuationResult
{
	public IReadOnlyList<CrossPageTableContinuationPlan> Plans { get; }

	public IReadOnlyList<CrossPageTableContinuationEvidence> Evaluations { get; }

	public CrossPageTableContinuationResult(IEnumerable<CrossPageTableContinuationPlan> plans, IEnumerable<CrossPageTableContinuationEvidence> evaluations)
	{
		Plans = new ReadOnlyCollection<CrossPageTableContinuationPlan>((plans ?? Enumerable.Empty<CrossPageTableContinuationPlan>()).ToList());
		Evaluations = new ReadOnlyCollection<CrossPageTableContinuationEvidence>((evaluations ?? Enumerable.Empty<CrossPageTableContinuationEvidence>()).ToList());
	}
}
