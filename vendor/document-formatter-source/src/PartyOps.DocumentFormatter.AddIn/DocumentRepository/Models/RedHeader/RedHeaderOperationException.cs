using System;

namespace DocumentRepository.Models.RedHeader;

public sealed class RedHeaderOperationException : InvalidOperationException
{
	public RedHeaderFailureReasonCode ReasonCode { get; private set; }

	public RedHeaderFailureStage Stage { get; private set; }

	private RedHeaderOperationException(RedHeaderFailureReasonCode reasonCode, RedHeaderFailureStage stage, Exception innerException)
		: base(reasonCode.ToString(), innerException)
	{
		ReasonCode = reasonCode;
		Stage = stage;
	}

	public static RedHeaderOperationException Create(RedHeaderFailureReasonCode reasonCode, RedHeaderFailureStage stage, Exception innerException = null)
	{
		return new RedHeaderOperationException(reasonCode, stage, innerException);
	}
}
