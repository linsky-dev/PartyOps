namespace DocumentRepository.Models.Rules;

public enum RuleLoadStatus
{
	Loaded,
	CreatedDefault,
	InMemoryFallback,
	Corrupt,
	ReadFailed,
	WriteFailed
}
