using System;

namespace DocumentRepository.Models.Replace;

public sealed class ReplaceOperationException : InvalidOperationException
{
	public ReplaceFailureReasonCode ReasonCode { get; private set; }

	public ReplaceFailureStage Stage { get; private set; }

	private ReplaceOperationException(ReplaceFailureReasonCode reasonCode, ReplaceFailureStage stage, Exception innerException)
		: base(reasonCode.ToString(), innerException)
	{
		ReasonCode = reasonCode;
		Stage = stage;
	}

	public static ReplaceOperationException Create(ReplaceFailureReasonCode reasonCode, ReplaceFailureStage stage, Exception innerException = null)
	{
		return new ReplaceOperationException(reasonCode, stage, innerException);
	}
}
