namespace DocumentRepository.Services.Conversion.PdfToWord.Tables;

public sealed class PdfTableColumn
{
	public int Index { get; }

	public float LeftX { get; }

	public float RightX { get; }

	public PdfTableColumn(int index, float leftX, float rightX)
	{
		Index = index;
		LeftX = leftX;
		RightX = rightX;
	}
}
