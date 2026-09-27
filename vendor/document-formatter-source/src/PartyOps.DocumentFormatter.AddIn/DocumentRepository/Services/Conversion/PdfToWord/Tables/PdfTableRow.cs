using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace DocumentRepository.Services.Conversion.PdfToWord.Tables;

public sealed class PdfTableRow
{
	public int Index { get; }

	public float TopY { get; }

	public float BottomY { get; }

	public IReadOnlyList<PdfTableCell> Cells { get; }

	public int NonEmptyCellCount
	{
		get
		{
			int num = 0;
			foreach (PdfTableCell cell in Cells)
			{
				if (!cell.IsMergedContinuation && cell.HasText)
				{
					num++;
				}
			}
			return num;
		}
	}

	public PdfTableRow(int index, float topY, float bottomY, IEnumerable<PdfTableCell> cells)
	{
		Index = index;
		TopY = topY;
		BottomY = bottomY;
		Cells = new ReadOnlyCollection<PdfTableCell>((cells ?? Enumerable.Empty<PdfTableCell>()).ToList());
	}
}
