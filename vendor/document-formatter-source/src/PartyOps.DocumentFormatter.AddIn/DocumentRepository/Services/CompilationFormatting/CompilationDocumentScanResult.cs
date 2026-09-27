using System.Collections.Generic;

namespace DocumentRepository.Services.CompilationFormatting;

public sealed class CompilationDocumentScanResult
{
	public List<string> ParagraphTexts { get; private set; }

	public List<int> ParagraphStarts { get; private set; }

	public List<int> ParagraphEnds { get; private set; }

	public int ParagraphCount => ParagraphTexts.Count;

	public List<string> UnsupportedRegionMarkers { get; private set; }

	public List<int> BodyMarkerParagraphIndexes { get; private set; }

	public List<int> BodyMarkerTextStarts { get; private set; }

	public List<int> BodyMarkerTextEnds { get; private set; }

	public List<bool> BodyMarkerParagraphHasPageBreak { get; private set; }

	public List<string> ScanErrors { get; private set; }

	public List<string> MalformedBodyMarkers { get; private set; }

	public CompilationDocumentScanResult()
	{
		ParagraphTexts = new List<string>();
		ParagraphStarts = new List<int>();
		ParagraphEnds = new List<int>();
		UnsupportedRegionMarkers = new List<string>();
		BodyMarkerParagraphIndexes = new List<int>();
		BodyMarkerTextStarts = new List<int>();
		BodyMarkerTextEnds = new List<int>();
		BodyMarkerParagraphHasPageBreak = new List<bool>();
		ScanErrors = new List<string>();
		MalformedBodyMarkers = new List<string>();
	}
}
