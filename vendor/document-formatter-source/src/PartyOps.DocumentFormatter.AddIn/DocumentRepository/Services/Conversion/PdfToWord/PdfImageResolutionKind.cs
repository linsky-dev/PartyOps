namespace DocumentRepository.Services.Conversion.PdfToWord;

public enum PdfImageResolutionKind
{
	None,
	PassthroughJpeg,
	PassthroughPng,
	RecodedPng,
	TooLarge,
	Unsupported
}
