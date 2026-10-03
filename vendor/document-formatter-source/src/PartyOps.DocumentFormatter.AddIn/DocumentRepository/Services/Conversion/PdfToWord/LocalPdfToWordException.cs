using System;

namespace DocumentRepository.Services.Conversion.PdfToWord;

public sealed class LocalPdfToWordException : Exception
{
	public PdfToWordFailureReason Reason { get; private set; }

	public LocalPdfToWordException(PdfToWordFailureReason reason, string message)
		: base(message)
	{
		Reason = reason;
	}

	public LocalPdfToWordException(PdfToWordFailureReason reason, string message, Exception innerException)
		: base(message, innerException)
	{
		Reason = reason;
	}
}
