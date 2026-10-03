using DocumentRepository.Services.FileSafety;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Rename;

public sealed class SaveRenameResult
{
	public string Message { get; internal set; }

	public Document ActiveDocument { get; internal set; }

	public OutputIntegrityReceipt IntegrityReceipt { get; internal set; }

	public string ValidatedOutputPath { get; internal set; }

	internal string OriginalPath { get; set; }

	internal string TargetPath { get; set; }

	internal bool CopyMode { get; set; }

	internal bool PathSwitched { get; set; }

	internal bool OutputCreated { get; set; }

	internal bool IsCommitted { get; set; }
}
