namespace DocumentRepository.Services.Analysis;

public sealed class ParagraphClassificationOptions
{
	public bool ForceFirstParagraphAsTitle { get; set; }

	public bool PreferSignaturePair { get; set; }

	public bool NextParagraphIsDateLine { get; set; }

	public bool PreviousParagraphIsSignatureLine { get; set; }
}
