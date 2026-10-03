using DocumentRepository.Models.Formatting;

namespace DocumentRepository.Models.CompilationFormatting;

public sealed class CompilationFormatFailure
{
	public CompilationFormatFailureReasonCode ReasonCode { get; private set; }

	public CompilationFormatFailureStage Stage { get; private set; }

	public DocumentSafetyDisposition SafetyDisposition { get; private set; }

	public string StructuredDetail { get; private set; }

	public CompilationFormatFailure(CompilationFormatFailureReasonCode reasonCode, CompilationFormatFailureStage stage, DocumentSafetyDisposition safetyDisposition, string structuredDetail = null)
	{
		ReasonCode = reasonCode;
		Stage = stage;
		SafetyDisposition = safetyDisposition;
		StructuredDetail = structuredDetail;
	}
}
