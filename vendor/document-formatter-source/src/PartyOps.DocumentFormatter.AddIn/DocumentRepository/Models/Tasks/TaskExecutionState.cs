namespace DocumentRepository.Models.Tasks;

public enum TaskExecutionState
{
	NotStarted,
	Running,
	Cancelling,
	Succeeded,
	SucceededWithWarnings,
	Cancelled,
	Failed
}
