namespace DocumentRepository.Models.Protection;

public enum DocumentRiskState
{
	NewBlank,
	NewWithContent,
	SavedClean,
	SavedDirty,
	ReadOnly,
	Protected,
	PathUnavailable
}
