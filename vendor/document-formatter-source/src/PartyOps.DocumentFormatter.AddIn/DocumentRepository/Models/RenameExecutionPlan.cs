namespace DocumentRepository.Models;

public class RenameExecutionPlan
{
	public string OriginalPath { get; set; }

	public string TargetPath { get; set; }

	public bool CopyMode { get; set; }

	public RenameRule Rule { get; set; }

	public RenameInfo Info { get; set; }

	public RenameRuleManager.RotateWordSelection RotateSelection { get; set; }
}
