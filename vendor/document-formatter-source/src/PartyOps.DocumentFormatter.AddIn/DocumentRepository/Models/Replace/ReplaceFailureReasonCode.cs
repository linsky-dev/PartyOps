namespace DocumentRepository.Models.Replace;

public enum ReplaceFailureReasonCode
{
	NoActiveDocument,
	NoExecutableItems,
	UndoUnavailable,
	RecoveryProtectionUnavailable,
	ScopeUnavailable,
	RuleConfigurationInvalid,
	RegularExpressionInvalid,
	WildcardExpressionInvalid,
	ZeroLengthMatch,
	CaptureGroupInvalid,
	FormatConditionInvalid,
	TableBoundaryUnsafe,
	OverlappingTargets,
	TargetChanged,
	VerificationFailed,
	HostOperationFailed,
	UnexpectedFailure,
	RuleStoreUnavailable,
	RuleRecoveryFailed
}
