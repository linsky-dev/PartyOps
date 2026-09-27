using System;
using DocumentRepository.Models.CompilationFormatting;

namespace DocumentRepository.Services.CompilationFormatting;

public sealed class CompilationTocUpdateException : Exception
{
	public CompilationFormatFailureReasonCode ReasonCode { get; private set; }

	public CompilationTocUpdateException(CompilationFormatFailureReasonCode code, string message)
		: base(message)
	{
		ReasonCode = code;
	}

	public CompilationTocUpdateException(CompilationFormatFailureReasonCode code, string message, Exception inner)
		: base(message, inner)
	{
		ReasonCode = code;
	}
}
