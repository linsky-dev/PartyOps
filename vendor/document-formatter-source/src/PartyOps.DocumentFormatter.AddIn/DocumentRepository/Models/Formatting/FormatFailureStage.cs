namespace DocumentRepository.Models.Formatting;

public enum FormatFailureStage
{
	Entry,
	Authorization,
	Protection,
	Snapshot,
	Normalize,
	Analyze,
	Plan,
	Execute,
	Verify,
	Rollback,
	Unknown
}
