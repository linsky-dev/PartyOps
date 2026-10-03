using System.Collections.Generic;

namespace DocumentRepository.Services.Conversion.PdfToWord;

public sealed class LocalPdfToWordResult
{
	public int MeaningfulCharacterCount { get; internal set; }

	public bool LikelyScanned { get; internal set; }

	public int PageCount { get; internal set; }

	public PdfDocumentKind DocumentKind { get; internal set; }

	public IList<int> PagesNeedingOcr { get; internal set; }
}
