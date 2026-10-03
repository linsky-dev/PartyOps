namespace DocumentRepository.Models.FormattingPlan;

public sealed class PlannedTextRange
{
	public int Start { get; set; }

	public int End { get; set; }

	public ElementType SourceElementType { get; set; }

	public string SourceEastAsianFontName { get; set; }
}
