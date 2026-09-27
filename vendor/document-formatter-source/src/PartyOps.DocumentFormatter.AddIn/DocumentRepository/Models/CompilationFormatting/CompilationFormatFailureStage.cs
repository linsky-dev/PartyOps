namespace DocumentRepository.Models.CompilationFormatting;

public enum CompilationFormatFailureStage
{
	None,
	Entry,
	StructureScan,
	Manifest,
	Boundary,
	Confirmation,
	SeparatorPrecheck,
	PageSetup,
	ArticleFormat,
	Toc,
	PageNumbers,
	SeparatorReconcile,
	OrphanFix,
	Verify,
	Recovery,
	Unknown
}
