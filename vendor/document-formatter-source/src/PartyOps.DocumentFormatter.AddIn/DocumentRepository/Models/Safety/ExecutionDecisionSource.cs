namespace DocumentRepository.Models.Safety;

public enum ExecutionDecisionSource
{
	Rule,
	Document,
	Host,
	Snapshot,
	Backup,
	User
}
