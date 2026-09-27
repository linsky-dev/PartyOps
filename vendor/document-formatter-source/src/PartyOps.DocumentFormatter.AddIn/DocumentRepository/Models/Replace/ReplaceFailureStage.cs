namespace DocumentRepository.Models.Replace;

public enum ReplaceFailureStage
{
	Entry,
	Prepare,
	ResolveScope,
	ApplyRules,
	Verify,
	Rollback
}
