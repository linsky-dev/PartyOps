using System;
using DocumentRepository.Models.RedHeader;

namespace DocumentRepository.Services.RedHeader;

public static class RedHeaderFailureClassifier
{
	public static RedHeaderOperationException Classify(Exception error, RedHeaderFailureStage stage)
	{
		RedHeaderOperationException ex = FindControlled(error);
		if (ex == null)
		{
			return stage switch
			{
				RedHeaderFailureStage.Template => RedHeaderOperationException.Create(RedHeaderFailureReasonCode.TemplateInvalid, stage, error), 
				RedHeaderFailureStage.Analyze => RedHeaderOperationException.Create(RedHeaderFailureReasonCode.AnalysisUnreadable, stage, error), 
				RedHeaderFailureStage.Plan => RedHeaderOperationException.Create(RedHeaderFailureReasonCode.PageGeometryInvalid, stage, error), 
				RedHeaderFailureStage.Generate => RedHeaderOperationException.Create(RedHeaderFailureReasonCode.HostGenerationFailed, stage, error), 
				RedHeaderFailureStage.Prepare => RedHeaderOperationException.Create(RedHeaderFailureReasonCode.RecoveryProtectionUnavailable, stage, error), 
				RedHeaderFailureStage.Verify => RedHeaderOperationException.Create(RedHeaderFailureReasonCode.VerificationFailed, stage, error), 
				_ => RedHeaderOperationException.Create(RedHeaderFailureReasonCode.UnexpectedFailure, stage, error), 
			};
		}
		return ex;
	}

	private static RedHeaderOperationException FindControlled(Exception error)
	{
		if (error == null)
		{
			return null;
		}
		if (error is RedHeaderOperationException result)
		{
			return result;
		}
		if (error is AggregateException ex)
		{
			foreach (Exception innerException in ex.Flatten().InnerExceptions)
			{
				RedHeaderOperationException ex2 = FindControlled(innerException);
				if (ex2 != null)
				{
					return ex2;
				}
			}
		}
		return FindControlled(error.InnerException);
	}
}
