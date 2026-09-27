namespace DocumentRepository.Models.Conversion;

public class DocxReplaceCurrentResult
{
	public string SourcePath { get; set; }

	public string TargetPath { get; set; }

	public string CurrentDocumentPath { get; set; }

	public bool SourceDeleted { get; set; }

	public string SourceDeleteError { get; set; }

	public bool HasCleanupWarning
	{
		get
		{
			if (!SourceDeleted)
			{
				return !string.IsNullOrWhiteSpace(SourceDeleteError);
			}
			return false;
		}
	}
}
