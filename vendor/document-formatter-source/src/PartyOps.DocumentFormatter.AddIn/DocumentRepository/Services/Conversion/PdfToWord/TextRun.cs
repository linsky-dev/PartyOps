namespace DocumentRepository.Services.Conversion.PdfToWord;

public sealed class TextRun
{
	public string Text { get; set; }

	public string FontName { get; set; }

	public float FontSize { get; set; }

	public bool Bold { get; set; }

	public bool Italic { get; set; }
}
