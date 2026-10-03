using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Services.Conversion.PdfToWord.Tables;

public sealed class PdfTableCandidate
{
	public PdfTableRegion Region { get; set; }

	public IList<PdfTableRow> Rows { get; private set; }

	public IList<PdfTableColumn> Columns { get; private set; }

	public IList<PdfTableDetectionEvidence> Evidence { get; private set; }

	public PdfTableConfidence Confidence { get; set; }

	public bool HasWireframe { get; set; }

	public bool ClosedGridFrame { get; set; }

	public int UnassignedElementCount { get; set; }

	public int RowCount => Rows.Count;

	public int ColumnCount => Columns.Count;

	public bool HasCounterEvidence => PdfTableEvidenceList.HasCounter(Evidence);

	public PdfTableCandidate()
	{
		Rows = new List<PdfTableRow>();
		Columns = new List<PdfTableColumn>();
		Evidence = new List<PdfTableDetectionEvidence>();
		Confidence = PdfTableConfidence.Low;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public override string ToString()
	{
		return $"{Confidence} {RowCount}行x{ColumnCount}列 wireframe={HasWireframe} {Region}";
	}
}
