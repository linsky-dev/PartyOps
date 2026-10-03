using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace DocumentRepository.Services.Conversion.PdfToWord.Tables;

public sealed class PdfTableCell
{
	public int RowIndex { get; }

	public int ColumnIndex { get; }

	public int ColumnSpan { get; }

	public bool IsMergedContinuation { get; }

	public PdfCellVerticalMerge VerticalMerge { get; }

	public IReadOnlyList<IReadOnlyList<PdfTextElement>> TextLines { get; }

	public string PlainText
	{
		get
		{
			StringBuilder stringBuilder = new StringBuilder();
			foreach (IReadOnlyList<PdfTextElement> textLine in TextLines)
			{
				if (stringBuilder.Length > 0)
				{
					stringBuilder.Append('\n');
				}
				foreach (PdfTextElement item in textLine)
				{
					if (item != null && !string.IsNullOrEmpty(item.Text))
					{
						stringBuilder.Append(item.Text);
					}
				}
			}
			return stringBuilder.ToString();
		}
	}

	public bool HasText
	{
		get
		{
			foreach (IReadOnlyList<PdfTextElement> textLine in TextLines)
			{
				foreach (PdfTextElement item in textLine)
				{
					if (item == null || string.IsNullOrWhiteSpace(item.Text))
					{
						continue;
					}
					return true;
				}
			}
			return false;
		}
	}

	public PdfTableCell(int rowIndex, int columnIndex, int columnSpan, bool isMergedContinuation, IEnumerable<IEnumerable<PdfTextElement>> textLines, PdfCellVerticalMerge verticalMerge = PdfCellVerticalMerge.None)
	{
		RowIndex = rowIndex;
		ColumnIndex = columnIndex;
		ColumnSpan = ((columnSpan <= 0) ? 1 : columnSpan);
		IsMergedContinuation = isMergedContinuation;
		VerticalMerge = verticalMerge;
		TextLines = new ReadOnlyCollection<IReadOnlyList<PdfTextElement>>((textLines ?? Enumerable.Empty<IEnumerable<PdfTextElement>>()).Select((Func<IEnumerable<PdfTextElement>, IReadOnlyList<PdfTextElement>>)((IEnumerable<PdfTextElement> line) => new ReadOnlyCollection<PdfTextElement>((line ?? Enumerable.Empty<PdfTextElement>()).Select((PdfTextElement e) => (e != null) ? new PdfTextElement(e) : null).ToList()))).ToList());
	}
}
