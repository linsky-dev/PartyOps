namespace DocumentRepository.Models;

public class DocumentElement
{
	public int ParagraphIndex { get; set; }

	public int ScopeParagraphIndex { get; set; }

	public int RangeStart { get; set; }

	public int RangeEnd { get; set; }

	public string Text { get; set; }

	public ElementType Type { get; set; }

	public bool IsEmpty { get; set; }

	public bool IsInTable { get; set; }

	public bool HasInlineShape { get; set; }

	public bool HasShape { get; set; }

	public bool AllowsInlinePostProcess { get; set; }

	public bool AllowsBodyPostProcess { get; set; }

	public bool HasEnglishNumbers { get; set; }

	public bool HasKeywordCandidate { get; set; }

	public bool HasSemicolonCandidate { get; set; }

	public bool HasTabs { get; set; }

	public bool HasOrphanCandidate { get; set; }
}
