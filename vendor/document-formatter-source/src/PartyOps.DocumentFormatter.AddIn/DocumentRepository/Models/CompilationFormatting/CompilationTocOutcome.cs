namespace DocumentRepository.Models.CompilationFormatting;

public enum CompilationTocOutcome
{
	Created,
	Updated,
	SkippedByExistingTocPolicy,
	Failed
}
