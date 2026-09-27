namespace DocumentRepository.Services.Conversion.PdfToWord;

public sealed class PdfPageClassification
{
	public int PageIndex { get; set; }

	public PdfPageType Type { get; set; }

	public string ReasonCode { get; set; }

	public int CharacterCount { get; set; }

	public int ImageCount { get; set; }

	public float ImageCoverageRatio { get; set; }

	public int VectorPathCount { get; set; }

	public float GarbledRatio { get; set; }
}
