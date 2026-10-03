using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace DocumentRepository.Services.Conversion.PdfToWord.Tables;

public sealed class TableFragmentFacts
{
	public CrossPageTableId Id { get; }

	public PdfTableConfidence Confidence { get; }

	public bool HasWireframe { get; }

	public bool ClosedGridFrame { get; }

	public float LeftX { get; }

	public float RightX { get; }

	public float TopY { get; }

	public float BottomY { get; }

	public int ColumnCount { get; }

	public IReadOnlyList<float> NormalizedColumnBoundaries { get; }

	public int RowCount { get; }

	public int HeaderRowIndex { get; }

	public CrossPageTableRowSignature HeaderSignature { get; }

	public CrossPageTableRowSignature FirstRowSignature { get; }

	public bool HasMeaningfulContentBefore { get; }

	public bool HasMeaningfulContentAfter { get; }

	public bool HasMeaningfulContentBeside { get; }

	public SequenceColumnEvidence Sequence { get; }

	public TableFragmentFacts(CrossPageTableId id, PdfTableConfidence confidence, bool hasWireframe, bool closedGridFrame, float leftX, float rightX, float topY, float bottomY, int columnCount, IEnumerable<float> normalizedColumnBoundaries, int rowCount, int headerRowIndex, CrossPageTableRowSignature headerSignature, CrossPageTableRowSignature firstRowSignature, bool hasMeaningfulContentBefore, bool hasMeaningfulContentAfter, bool hasMeaningfulContentBeside, SequenceColumnEvidence sequence = default(SequenceColumnEvidence))
	{
		Id = id;
		Confidence = confidence;
		HasWireframe = hasWireframe;
		ClosedGridFrame = closedGridFrame;
		LeftX = leftX;
		RightX = rightX;
		TopY = topY;
		BottomY = bottomY;
		ColumnCount = columnCount;
		NormalizedColumnBoundaries = new ReadOnlyCollection<float>((normalizedColumnBoundaries ?? Enumerable.Empty<float>()).ToList());
		RowCount = rowCount;
		HeaderRowIndex = headerRowIndex;
		HeaderSignature = headerSignature;
		FirstRowSignature = firstRowSignature;
		HasMeaningfulContentBefore = hasMeaningfulContentBefore;
		HasMeaningfulContentAfter = hasMeaningfulContentAfter;
		HasMeaningfulContentBeside = hasMeaningfulContentBeside;
		Sequence = ((sequence.ColumnIndex == 0 && sequence.FirstDataValue == 0) ? SequenceColumnEvidence.None : sequence);
	}

	public static SequenceColumnEvidence ComputeSequenceEvidence(PdfTableLayoutPlan plan, int headerRowIndex)
	{
		if (plan != null && plan.ColumnCount > 0 && plan.Rows.Count > 1)
		{
			int num = Math.Max(headerRowIndex + 1, 1);
			if (num >= plan.Rows.Count)
			{
				return SequenceColumnEvidence.None;
			}
			int num2 = plan.Rows.Count - num;
			if (num2 >= 2)
			{
				int num3 = -1;
				SequenceColumnEvidence result = SequenceColumnEvidence.None;
				int num4 = 0;
				while (true)
				{
					if (num4 >= plan.ColumnCount)
					{
						if (num3 < 0)
						{
							break;
						}
						return result;
					}
					int[] array = new int[num2];
					bool flag = true;
					for (int i = 0; i < num2; i++)
					{
						PdfTableCell pdfTableCell = plan.Rows[num + i].Cells[num4];
						if (pdfTableCell != null && !pdfTableCell.IsMergedContinuation && pdfTableCell.ColumnSpan == 1)
						{
							if (TryParsePureInteger(CellPlainText(pdfTableCell), out var value))
							{
								array[i] = value;
								continue;
							}
							flag = false;
							break;
						}
						flag = false;
						break;
					}
					if (flag)
					{
						bool flag2 = true;
						for (int j = 1; j < num2; j++)
						{
							if (array[j] != array[j - 1] + 1)
							{
								flag2 = false;
								break;
							}
						}
						if (flag2)
						{
							if (num3 >= 0)
							{
								return new SequenceColumnEvidence
								{
									ColumnIndex = -1,
									FirstDataValue = -1,
									LastDataValue = -1,
									Ambiguous = true
								};
							}
							num3 = num4;
							result = new SequenceColumnEvidence
							{
								ColumnIndex = num4,
								FirstDataValue = array[0],
								LastDataValue = array[num2 - 1]
							};
						}
					}
					num4++;
				}
				return SequenceColumnEvidence.None;
			}
			return SequenceColumnEvidence.None;
		}
		return SequenceColumnEvidence.None;
	}

	private static string CellPlainText(PdfTableCell cell)
	{
		if (cell == null)
		{
			return string.Empty;
		}
		StringBuilder stringBuilder = new StringBuilder();
		foreach (IReadOnlyList<PdfTextElement> textLine in cell.TextLines)
		{
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

	private static bool TryParsePureInteger(string text, out int value)
	{
		value = 0;
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		string text2 = CrossPageTableRowSignature.NormalizeCellText(text);
		if (text2.Length == 0 || text2.Length > 9)
		{
			return false;
		}
		string text3 = text2;
		foreach (char c in text3)
		{
			if (c < '0' || c > '9')
			{
				return false;
			}
		}
		return int.TryParse(text2, out value);
	}
}
