namespace DocumentRepository.Models;

public class RenameAnalysisResult
{
	public string OriginalPath { get; set; }

	public string OriginalDirectory { get; set; }

	public RenameInfo Info { get; set; }

	public RenameTitleEvidence MainTitleEvidence { get; set; }
}
