using System.Collections.Generic;

namespace DocumentRepository.Services.Conversion.PdfToWord;

public sealed class TableBlock : DocumentBlock
{
	public IList<TableRow> Rows { get; private set; }

	public int ColumnCount { get; set; }

	public TableBorderStyle BorderStyle { get; set; }

	internal bool NeedsFollowingBoundary { get; set; }

	public IList<float> ColumnWidths { get; set; }

	public TableBlock()
	{
		Rows = new List<TableRow>();
	}
}
