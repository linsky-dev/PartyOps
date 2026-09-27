using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace DocumentRepository.Services.Conversion.PdfToWord.Tables;

public sealed class CrossPageTableRowSignature
{
	public IReadOnlyList<string> CellTexts { get; }

	public IReadOnlyList<int> CellSpans { get; }

	public IReadOnlyList<bool> CellBold { get; }

	public IReadOnlyList<float> CellFontSizes { get; }

	public int RealCellCount => CellTexts.Count;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public CrossPageTableRowSignature(IEnumerable<string> cellTexts, IEnumerable<int> cellSpans, IEnumerable<bool> cellBold, IEnumerable<float> cellFontSizes)
	{
		CellTexts = new ReadOnlyCollection<string>((cellTexts ?? Enumerable.Empty<string>()).ToList());
		CellSpans = new ReadOnlyCollection<int>((cellSpans ?? Enumerable.Empty<int>()).ToList());
		CellBold = new ReadOnlyCollection<bool>((cellBold ?? Enumerable.Empty<bool>()).ToList());
		CellFontSizes = new ReadOnlyCollection<float>((cellFontSizes ?? Enumerable.Empty<float>()).ToList());
		if (CellSpans.Count != CellTexts.Count || CellBold.Count != CellTexts.Count || CellFontSizes.Count != CellTexts.Count)
		{
			throw new ArgumentException("签名各序列长度必须一致。");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static CrossPageTableRowSignature FromRow(PdfTableRow row)
	{
		if (row == null)
		{
			throw new ArgumentNullException("row");
		}
		List<string> list = new List<string>();
		List<int> list2 = new List<int>();
		List<bool> list3 = new List<bool>();
		List<float> list4 = new List<float>();
		foreach (PdfTableCell cell in row.Cells)
		{
			if (cell != null && !cell.IsMergedContinuation)
			{
				list.Add(NormalizeCellText(cell.PlainText));
				list2.Add(cell.ColumnSpan);
				PdfTextElement pdfTextElement = null;
				if (cell.TextLines.Count > 0 && cell.TextLines[0].Count > 0)
				{
					pdfTextElement = cell.TextLines[0][0];
				}
				list3.Add(pdfTextElement?.Bold ?? false);
				list4.Add(pdfTextElement?.FontSize ?? 0f);
			}
		}
		return new CrossPageTableRowSignature(list, list2, list3, list4);
	}

	public static string NormalizeCellText(string text)
	{
		if (!string.IsNullOrEmpty(text))
		{
			StringBuilder stringBuilder = new StringBuilder(text.Length);
			foreach (char c in text)
			{
				if (!char.IsWhiteSpace(c) && c != '\u3000')
				{
					stringBuilder.Append(c);
				}
			}
			return stringBuilder.ToString();
		}
		return string.Empty;
	}
}
