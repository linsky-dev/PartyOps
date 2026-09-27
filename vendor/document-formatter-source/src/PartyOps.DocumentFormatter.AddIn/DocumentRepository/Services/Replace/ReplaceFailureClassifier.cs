using System;
using DocumentRepository.Models.Replace;

namespace DocumentRepository.Services.Replace;

public static class ReplaceFailureClassifier
{
	public static ReplaceOperationException Classify(Exception error, ReplaceFailureStage stage)
	{
		if (!(error is ReplaceOperationException result))
		{
			return stage switch
			{
				ReplaceFailureStage.ResolveScope => ReplaceOperationException.Create(ReplaceFailureReasonCode.ScopeUnavailable, stage, error), 
				ReplaceFailureStage.Prepare => ReplaceOperationException.Create(ReplaceFailureReasonCode.UndoUnavailable, stage, error), 
				ReplaceFailureStage.ApplyRules => ReplaceOperationException.Create(ReplaceFailureReasonCode.HostOperationFailed, stage, error), 
				ReplaceFailureStage.Verify => ReplaceOperationException.Create(ReplaceFailureReasonCode.VerificationFailed, stage, error), 
				_ => ReplaceOperationException.Create(ReplaceFailureReasonCode.UnexpectedFailure, stage, error), 
			};
		}
		return result;
	}
}
