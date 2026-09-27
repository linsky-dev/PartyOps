namespace DocumentRepository.Models.FormattingPlan;

public enum FormatMutationStage
{
	InitializeExecutionAnchors,
	ApplyPageSetup,
	ApplyParagraphStyles,
	ApplyKeywords,
	FormatTables,
	FixOrphanCharacters,
	ApplyEnglishNumberFont,
	ClearTabs,
	InsertSignatureSpacing,
	RefreshAnchors,
	FormatSignature,
	FormatAttachments,
	ApplyPageNumbers,
	NormalizeEastAsianPunctuationFont,
	FormatImages
}
