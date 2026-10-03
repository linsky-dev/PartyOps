namespace DocumentRepository.Services.Formatting.Planning;

public sealed class FormatWorkloadEstimate
{
	public int CharacterCount { get; set; }

	public int ParagraphCount { get; set; }

	public bool ShowProgress { get; set; }
}
