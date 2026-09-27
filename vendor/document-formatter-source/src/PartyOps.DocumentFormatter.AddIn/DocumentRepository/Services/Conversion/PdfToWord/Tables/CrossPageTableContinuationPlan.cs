using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Services.Conversion.PdfToWord.Tables;

public sealed class CrossPageTableContinuationPlan
{
	public CrossPageTableId PreviousTable { get; }

	public CrossPageTableId NextTable { get; }

	public int PreviousPageNumber { get; }

	public int NextPageNumber { get; }

	public int RepeatedHeaderRowCount { get; }

	public int PreviousRowCount { get; }

	public int NextRowCount { get; }

	public int MergedRowCount { get; }

	public int PreviousHeaderRowIndex { get; }

	public IReadOnlyList<CrossPageTableRowSource> MergedRowOrder { get; }

	public IReadOnlyList<int> SourcePageNumbers { get; }

	public string StructureFingerprint { get; }

	[MethodImpl(MethodImplOptions.NoInlining)]
	public CrossPageTableContinuationPlan(CrossPageTableId previousTable, CrossPageTableId nextTable, int repeatedHeaderRowCount, int previousRowCount, int nextRowCount, int previousHeaderRowIndex, IEnumerable<CrossPageTableRowSource> mergedRowOrder, IEnumerable<int> sourcePageNumbers, string structureFingerprint)
	{
		PreviousTable = previousTable;
		NextTable = nextTable;
		PreviousPageNumber = previousTable.PageIndex + 1;
		NextPageNumber = nextTable.PageIndex + 1;
		RepeatedHeaderRowCount = repeatedHeaderRowCount;
		PreviousRowCount = previousRowCount;
		NextRowCount = nextRowCount;
		MergedRowCount = previousRowCount + nextRowCount - repeatedHeaderRowCount;
		PreviousHeaderRowIndex = previousHeaderRowIndex;
		MergedRowOrder = new ReadOnlyCollection<CrossPageTableRowSource>((mergedRowOrder ?? Enumerable.Empty<CrossPageTableRowSource>()).ToList());
		SourcePageNumbers = new ReadOnlyCollection<int>((sourcePageNumbers ?? Enumerable.Empty<int>()).ToList());
		StructureFingerprint = structureFingerprint ?? string.Empty;
		if (MergedRowOrder.Count == MergedRowCount)
		{
			return;
		}
		throw new ArgumentException("MergedRowOrder 长度必须等于合并后行数。");
	}
}
