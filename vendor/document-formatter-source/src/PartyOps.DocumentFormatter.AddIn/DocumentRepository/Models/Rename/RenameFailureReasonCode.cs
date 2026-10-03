namespace DocumentRepository.Models.Rename;

public enum RenameFailureReasonCode
{
	DocumentMissing = 0,
	DocumentNeedsSave = 1,
	StablePathUnavailable = 2,
	ActiveRuleMissing = 3,
	RuleInvalid = 4,
	RuleRecoveryFailed = 16,
	MainTitleMissing = 5,
	DocumentNumberMissing = 6,
	SubtitleMissing = 7,
	OutputDirectoryMissing = 8,
	OutputDirectoryUnavailable = 9,
	FilenameInvalid = 10,
	OutputFileInUse = 11,
	OutputValidationFailed = 12,
	RollbackVerified = 13,
	RecoveryRequired = 14,
	UnexpectedFailure = 15
}
