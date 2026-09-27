using System.Collections.Generic;

namespace DocumentRepository.Services.Conversion.PdfToWord;

internal sealed class ReconstructedLine
{
	public List<PdfTextElement> Elements { get; set; }

	public string Text { get; set; }

	public List<TextRun> Runs { get; set; }

	public float Baseline { get; set; }

	public float StartX { get; set; }

	public float EndX { get; set; }

	public float FontSize { get; set; }

	public string WordFontName { get; set; }
}
