namespace DocumentRepository.Services.Detection.Tables;

public class TableElementInfo
{
	public int AnalysisOrdinal { get; set; }

	public int RangeStart { get; set; }

	public int RangeEnd { get; set; }

	public int StoryTypeCode { get; set; }

	public int ContentLength { get; set; }

	public string ContentHash { get; set; }

	public bool ContentIdentityReliable { get; set; }

	public int RowCount { get; set; }

	public bool RowCountReliable { get; set; }

	public int ColumnCount { get; set; }

	public bool ColumnCountReliable { get; set; }

	public int CellCount { get; set; }

	public bool CellCountReliable { get; set; }

	public int NestedTableCount { get; set; }

	public bool NestedTableCountReliable { get; set; }

	public string StructureFingerprint { get; set; }

	public bool HasHeaderCandidate { get; set; }
}
