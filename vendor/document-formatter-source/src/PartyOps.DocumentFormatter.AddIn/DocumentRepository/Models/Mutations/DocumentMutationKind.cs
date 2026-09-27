namespace DocumentRepository.Models.Mutations;

public enum DocumentMutationKind
{
	CharacterText,
	CharacterFormat,
	ParagraphFormat,
	Style,
	PageSetup,
	Structure,
	FileOutput,
	VerificationCheckpoint
}
