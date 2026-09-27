using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.FileSafety;

public sealed class OfficeDocumentOutputResult
{
	public AtomicFileWriteResult FileResult { get; internal set; }

	public OutputIntegrityReceipt IntegrityReceipt { get; internal set; }

	public Document ReopenedDocument { get; internal set; }
}
