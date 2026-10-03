namespace DocumentRepository.Services.Formatting.Orphans;

public sealed class OrphanVisualLineSnapshot
{
	public int ParagraphStart { get; internal set; }

	public int ParagraphEnd { get; internal set; }

	public int LastLineStart { get; internal set; }

	public int AdjustmentStart { get; internal set; }

	public string LastLineText { get; internal set; }

	public bool IsOrphan { get; internal set; }
}
