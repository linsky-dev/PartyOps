namespace DocumentRepository.Services.Conversion.PdfToWord;

public enum PdfToWordFailureReason
{
	Encrypted,
	Unsupported,
	NoText,
	Internal,
	Output
}
