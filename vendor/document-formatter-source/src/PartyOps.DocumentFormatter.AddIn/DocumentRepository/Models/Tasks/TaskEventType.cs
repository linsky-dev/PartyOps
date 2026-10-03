namespace DocumentRepository.Models.Tasks;

public enum TaskEventType
{
	TaskStarted,
	AdmissionEvaluated,
	SessionPrepared,
	MutationStarted,
	MutationCompleted,
	VerificationPassed,
	VerificationFailed,
	RollbackStarted,
	RollbackSucceeded,
	RollbackFailed,
	TaskCommitted,
	TaskFailed,
	TaskCancelled,
	RecoveryPrepared,
	RecoveryPreparationDenied,
	RecoveryStateChanged
}
