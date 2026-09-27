using System.Collections.Generic;

namespace DocumentRepository.Services.Conversion.PdfToWord;

public sealed class TableRow
{
	public IList<TableCell> Cells { get; private set; }

	public bool IsHeader { get; set; }

	public TableRow()
	{
		Cells = new List<TableCell>();
	}
}
