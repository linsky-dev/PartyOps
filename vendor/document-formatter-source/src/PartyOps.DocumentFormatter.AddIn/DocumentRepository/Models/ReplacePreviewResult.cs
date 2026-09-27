namespace DocumentRepository.Models;

public class ReplacePreviewResult
{
	public bool Success { get; set; }

	public int MatchCount { get; set; }

	public string BeforeText { get; set; }

	public string AfterText { get; set; }

	public string Message { get; set; }
}
