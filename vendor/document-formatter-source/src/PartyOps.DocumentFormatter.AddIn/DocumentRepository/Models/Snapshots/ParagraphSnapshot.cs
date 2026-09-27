namespace DocumentRepository.Models.Snapshots;

public sealed class ParagraphSnapshot
{
	public int Index { get; set; }

	public int RangeStart { get; set; }

	public int RangeEnd { get; set; }

	public string Text { get; set; }

	public string TextFingerprint { get; set; }

	public string FormatFingerprint { get; set; }

	public string StyleName { get; set; }

	public bool IsInTable { get; set; }

	public ParagraphSnapshot DeepCopy()
	{
		return new ParagraphSnapshot
		{
			Index = Index,
			RangeStart = RangeStart,
			RangeEnd = RangeEnd,
			Text = Text,
			TextFingerprint = TextFingerprint,
			FormatFingerprint = FormatFingerprint,
			StyleName = StyleName,
			IsInTable = IsInTable
		};
	}
}
