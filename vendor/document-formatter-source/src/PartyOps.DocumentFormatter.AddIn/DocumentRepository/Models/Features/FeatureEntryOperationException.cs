using System;

namespace DocumentRepository.Models.Features;

public sealed class FeatureEntryOperationException : Exception
{
	public FeatureEntryFailureReasonCode ReasonCode { get; private set; }

	private FeatureEntryOperationException(FeatureEntryFailureReasonCode reasonCode, Exception innerException)
		: base(reasonCode.ToString(), innerException)
	{
		ReasonCode = reasonCode;
	}

	public static FeatureEntryOperationException Create(FeatureEntryFailureReasonCode reasonCode, Exception innerException = null)
	{
		return new FeatureEntryOperationException(reasonCode, innerException);
	}
}
