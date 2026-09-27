using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Services.Conversion.PdfToWord.Tables;

public sealed class PdfTableLayoutPlan
{
	public int PageIndex { get; }

	public float AnchorY { get; }

	public PdfTableRegion Region { get; }

	public int ColumnCount { get; }

	public IReadOnlyList<float> ColumnWidths { get; }

	public IReadOnlyList<PdfTableRow> Rows { get; }

	public IReadOnlyList<PdfTableDetectionEvidence> Evidence { get; }

	[MethodImpl(MethodImplOptions.NoInlining)]
	public PdfTableLayoutPlan(int pageIndex, float anchorY, PdfTableRegion region, int columnCount, IEnumerable<float> columnWidths, IEnumerable<PdfTableRow> rows, IEnumerable<PdfTableDetectionEvidence> evidence)
	{
		if (region == null)
		{
			throw new ArgumentNullException("region");
		}
		if (columnCount <= 0)
		{
			throw new ArgumentOutOfRangeException("columnCount");
		}
		PageIndex = pageIndex;
		AnchorY = anchorY;
		Region = region;
		ColumnCount = columnCount;
		ColumnWidths = new ReadOnlyCollection<float>((columnWidths ?? Enumerable.Empty<float>()).ToList());
		Rows = new ReadOnlyCollection<PdfTableRow>((rows ?? Enumerable.Empty<PdfTableRow>()).ToList());
		Evidence = new ReadOnlyCollection<PdfTableDetectionEvidence>((evidence ?? Enumerable.Empty<PdfTableDetectionEvidence>()).ToList());
		if (ColumnWidths.Count != ColumnCount)
		{
			throw new ArgumentException("ColumnWidths 数量必须等于 ColumnCount。", "columnWidths");
		}
		int columnCount2 = ColumnCount;
		foreach (PdfTableRow row in Rows)
		{
			if (row == null)
			{
				throw new ArgumentException("Rows 中包含 null 行。", "rows");
			}
			if (row.Cells.Count != columnCount2)
			{
				throw new ArgumentException($"第 {row.Index} 行单元格数 {row.Cells.Count} 不等于列数 {columnCount2}。", "rows");
			}
			bool[] array = new bool[ColumnCount];
			foreach (PdfTableCell cell in row.Cells)
			{
				if (cell.IsMergedContinuation)
				{
					if (cell.ColumnSpan != 1)
					{
						throw new ArgumentException($"第 {row.Index} 行第 {cell.ColumnIndex} 列为被合并占位格，ColumnSpan 必须为 1。", "rows");
					}
					continue;
				}
				if (cell.VerticalMerge == PdfCellVerticalMerge.Continue)
				{
					if (cell.ColumnSpan != 1 || cell.HasText)
					{
						throw new ArgumentException($"第 {row.Index} 行第 {cell.ColumnIndex} 列纵向续接格必须为空且不得横向合并。", "rows");
					}
					if (row.Index <= 0)
					{
						throw new ArgumentException("首行不能是纵向续接格。", "rows");
					}
					PdfTableCell pdfTableCell = Rows[row.Index - 1].Cells[cell.ColumnIndex];
					if (pdfTableCell.VerticalMerge != PdfCellVerticalMerge.Restart && pdfTableCell.VerticalMerge != PdfCellVerticalMerge.Continue)
					{
						throw new ArgumentException("纵向续接格缺少上方起始/续接格。", "rows");
					}
				}
				if (cell.ColumnSpan < 1)
				{
					throw new ArgumentException($"第 {row.Index} 行第 {cell.ColumnIndex} 列 ColumnSpan {cell.ColumnSpan} 无效。", "rows");
				}
				if (cell.ColumnIndex + cell.ColumnSpan > ColumnCount)
				{
					throw new ArgumentException($"第 {row.Index} 行第 {cell.ColumnIndex} 列跨 {cell.ColumnSpan} 列后超出列数 {ColumnCount}。", "rows");
				}
				for (int i = cell.ColumnIndex; i < cell.ColumnIndex + cell.ColumnSpan; i++)
				{
					if (array[i])
					{
						throw new ArgumentException($"第 {row.Index} 行列 {i} 被多个单元格覆盖。", "rows");
					}
					array[i] = true;
				}
			}
		}
	}

	public static PdfTableLayoutPlan FromCandidate(PdfTableCandidate candidate)
	{
		if (candidate == null)
		{
			return null;
		}
		if (candidate.Confidence != PdfTableConfidence.High)
		{
			return null;
		}
		if (candidate.Region == null)
		{
			return null;
		}
		PdfTableRegion region = new PdfTableRegion(candidate.Region.PageIndex, candidate.Region.LeftX, candidate.Region.RightX, candidate.Region.TopY, candidate.Region.BottomY);
		List<float> columnWidths = candidate.Columns.Select((PdfTableColumn c) => c.RightX - c.LeftX).ToList();
		List<PdfTableRow> rows = candidate.Rows.Select((PdfTableRow r) => new PdfTableRow(r.Index, r.TopY, r.BottomY, r.Cells.Select((PdfTableCell c) => new PdfTableCell(c.RowIndex, c.ColumnIndex, c.ColumnSpan, c.IsMergedContinuation, c.TextLines, c.VerticalMerge)))).ToList();
		List<PdfTableDetectionEvidence> evidence = candidate.Evidence.Select((PdfTableDetectionEvidence e) => new PdfTableDetectionEvidence(e.Kind, e.Name, e.Detail)).ToList();
		return new PdfTableLayoutPlan(candidate.Region.PageIndex, candidate.Region.TopY, region, candidate.ColumnCount, columnWidths, rows, evidence);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public override string ToString()
	{
		return $"plan page={PageIndex} {Rows.Count}行x{ColumnCount}列 anchorY={AnchorY:F1}";
	}
}
