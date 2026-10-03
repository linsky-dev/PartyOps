using System.Collections.Generic;

namespace DocumentRepository.Services.Conversion.PdfToWord;

internal sealed class ColumnReadingSegment
{
	public List<ReconstructedLine> Lines { get; set; }

	public int ColumnOrdinal { get; set; }

	public float ColumnZoneTop { get; set; }

	public ColumnReadingSegment()
	{
		Lines = new List<ReconstructedLine>();
		ColumnOrdinal = -1;
	}
}
