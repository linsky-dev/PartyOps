using System;

namespace DocumentRepository.Models.Conversion;

public sealed class ConvertOperationException : InvalidOperationException
{
	public ConvertFailureReasonCode ReasonCode { get; private set; }

	public ConvertFailureStage Stage { get; private set; }

	private ConvertOperationException(ConvertFailureReasonCode reasonCode, ConvertFailureStage stage, Exception innerException)
		: base(reasonCode.ToString(), innerException)
	{
		ReasonCode = reasonCode;
		Stage = stage;
	}

	public static ConvertOperationException Create(ConvertFailureReasonCode reasonCode, ConvertFailureStage stage, Exception innerException = null)
	{
		return new ConvertOperationException(reasonCode, stage, innerException);
	}
}
