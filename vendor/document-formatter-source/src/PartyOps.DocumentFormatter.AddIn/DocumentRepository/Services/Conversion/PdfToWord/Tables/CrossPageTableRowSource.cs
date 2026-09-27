namespace DocumentRepository.Services.Conversion.PdfToWord.Tables;

public struct CrossPageTableRowSource
{
	public CrossPageTableId TableId { get; }

	public int SourceRowIndex { get; }

	public CrossPageTableRowSource(CrossPageTableId tableId, int sourceRowIndex)
	{
		TableId = tableId;
		SourceRowIndex = sourceRowIndex;
	}
}
