namespace DocumentRepository.Models;

public sealed class RenameTitleParagraphFact
{
	public int ParagraphIndex { get; set; }

	public string Text { get; set; }

	public ElementType Type { get; set; }

	public float FontSize { get; set; }

	public string FontName { get; set; }

	public int PageNumber { get; set; }

	public bool IsBlackFont { get; set; } = true;
}
