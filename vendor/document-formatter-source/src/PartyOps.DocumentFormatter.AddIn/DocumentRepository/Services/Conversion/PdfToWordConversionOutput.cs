using System.Collections.Generic;
using DocumentRepository.Services.FileSafety;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Conversion;

public sealed class PdfToWordConversionOutput
{
	public string TargetPath { get; internal set; }

	public OutputIntegrityReceipt IntegrityReceipt { get; internal set; }

	public Document ReopenedDocument { get; internal set; }

	public bool LikelyScannedPdf { get; internal set; }

	public IList<int> PagesNeedingOcr { get; internal set; }
}
