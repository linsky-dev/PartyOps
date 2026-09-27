using System.Collections.Generic;

namespace DocumentRepository.Services.Conversion.PdfToWord;

public sealed class TableCell
{
	public IList<TextRun> Runs { get; private set; }

	public int GridSpan { get; set; }

	public VerticalMerge VerticalMerge { get; set; }

	public CellVerticalAlignment VerticalAlign { get; set; }

	public TableCell()
	{
		Runs = new List<TextRun>();
		GridSpan = 1;
		VerticalMerge = VerticalMerge.None;
		VerticalAlign = CellVerticalAlignment.Top;
	}
}
