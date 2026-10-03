namespace DocumentRepository.Services.CompilationFormatting;

public sealed class CompilationTocFieldFact
{
	public string BookmarkName { get; set; }

	public int FieldStart { get; set; }

	public int ParagraphStart { get; set; }

	public int ParagraphEnd { get; set; }

	public bool IsOwnedEntryParagraph { get; set; }
}
