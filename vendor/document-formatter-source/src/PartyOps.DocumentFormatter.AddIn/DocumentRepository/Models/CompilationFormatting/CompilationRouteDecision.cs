using System.Collections.Generic;

namespace DocumentRepository.Models.CompilationFormatting;

public class CompilationRouteDecision
{
	public CompilationRouteKind Kind { get; set; }

	public CompilationFormatFailureReasonCode RejectReasonCode { get; set; }

	public CompilationFormatFailureStage RejectStage { get; set; }

	public string RejectStructuredDetail { get; set; }

	public List<int> SelectedArticleIndexes { get; set; }

	public bool NeedsExpandConfirmation { get; set; }

	public CompilationRouteDecision()
	{
		SelectedArticleIndexes = new List<int>();
	}

	public static CompilationRouteDecision Reject(CompilationFormatFailureReasonCode reasonCode, string structuredDetail = null, CompilationFormatFailureStage stage = CompilationFormatFailureStage.Entry)
	{
		return new CompilationRouteDecision
		{
			Kind = CompilationRouteKind.Reject,
			RejectReasonCode = reasonCode,
			RejectStage = stage,
			RejectStructuredDetail = structuredDetail
		};
	}
}
