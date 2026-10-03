namespace DocumentRepository.Models.Tasks;

public enum UserOutcomeKind
{
	Completed,
	CompletedWithWarnings,
	NoChanges,
	Busy,
	ActionRequired,
	Cancelled,
	FailedButRecovered,
	RecoveryRequired,
	Failed
}
