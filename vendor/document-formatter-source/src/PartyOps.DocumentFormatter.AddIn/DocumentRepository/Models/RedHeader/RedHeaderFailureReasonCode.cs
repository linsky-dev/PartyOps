namespace DocumentRepository.Models.RedHeader;

public enum RedHeaderFailureReasonCode
{
	NoActiveDocument,
	ConfigMissing,
	TemplateMissing,
	TemplateInvalid,
	AnalysisUnreadable,
	PageGeometryInvalid,
	RecoveryProtectionUnavailable,
	PlanBindingInvalid,
	HeaderLayoutFailed,
	RedLineCreationFailed,
	ImprintPaginationFailed,
	OriginalTextChanged,
	OriginalObjectReduced,
	VerificationFailed,
	HostGenerationFailed,
	UnexpectedFailure,
	TemplateStoreUnavailable,
	TemplateRecoveryFailed
}
