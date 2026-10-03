using System.Collections.Generic;

namespace DocumentRepository.Services.Conversion.PdfToWord;

public sealed class ReconstructedDocument
{
	public IList<DocumentBlock> Blocks { get; private set; }

	public ReconstructedDocument()
	{
		Blocks = new List<DocumentBlock>();
	}
}
