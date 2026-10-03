using System;

namespace DocumentRepository.Services.Conversion.PdfToWord;

public sealed class PdfReadingException : Exception
{
	public PdfReadingException(string message)
		: base(message)
	{
	}

	public PdfReadingException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
