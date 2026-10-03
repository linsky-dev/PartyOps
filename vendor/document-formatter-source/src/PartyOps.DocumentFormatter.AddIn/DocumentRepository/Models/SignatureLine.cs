namespace DocumentRepository.Models;

public sealed class SignatureLine
{
	public int ParagraphIndex { get; internal set; }

	public int RangeStart { get; internal set; }

	public int RangeEnd { get; internal set; }

	public string Text { get; internal set; }
}
