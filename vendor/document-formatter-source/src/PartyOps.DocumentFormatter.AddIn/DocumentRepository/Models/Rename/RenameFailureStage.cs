namespace DocumentRepository.Models.Rename;

public enum RenameFailureStage
{
	Admission,
	RuleLoad,
	Analysis,
	Planning,
	OutputPreparation,
	Save,
	Verification,
	Commit,
	Rollback,
	Unknown
}
