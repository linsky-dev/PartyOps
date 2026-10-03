using DocumentRepository.Models.CompilationFormatting;

namespace DocumentRepository.Services.CompilationFormatting;

public sealed class CompilationRouteResolution
{
	public CompilationRouteDecision Decision { get; set; }

	public CompilationArticleList ArticleList { get; set; }

	public CompilationDocumentScanResult Scan { get; set; }

	public bool UsesVisibleMarkers { get; set; }

	public string StructureFailure { get; set; }

	public static CompilationRouteResolution Rejected(CompilationFormatFailureReasonCode reasonCode, string structuredDetail = null, CompilationFormatFailureStage stage = CompilationFormatFailureStage.Entry)
	{
		return new CompilationRouteResolution
		{
			Decision = CompilationRouteDecision.Reject(reasonCode, structuredDetail, stage)
		};
	}
}
