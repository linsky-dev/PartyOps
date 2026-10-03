namespace DocumentRepository.Models;

public class ReplaceRegexPreset
{
	public string Id { get; set; }

	public string Name { get; set; }

	public string Description { get; set; }

	public string Pattern { get; set; }

	public string Replacement { get; set; }

	public string SampleText { get; set; }
}
