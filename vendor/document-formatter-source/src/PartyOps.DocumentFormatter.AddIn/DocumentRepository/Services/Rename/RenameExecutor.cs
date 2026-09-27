using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Models.Rename;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Rename;

public static class RenameExecutor
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static SaveRenameResult Execute(Document document, RenameExecutionPlan plan)
	{
		if (document != null)
		{
			if (plan == null)
			{
				throw RenameOperationException.Create(RenameFailureReasonCode.UnexpectedFailure, RenameFailureStage.Planning, new ArgumentNullException("plan"));
			}
			if (!string.IsNullOrWhiteSpace(plan.OriginalPath))
			{
				if (string.IsNullOrWhiteSpace(plan.TargetPath))
				{
					throw RenameOperationException.Create(RenameFailureReasonCode.FilenameInvalid, RenameFailureStage.Save);
				}
				return SaveRenameService.Execute(document, plan.OriginalPath, plan.TargetPath, plan.CopyMode);
			}
			throw RenameOperationException.Create(RenameFailureReasonCode.StablePathUnavailable, RenameFailureStage.Save);
		}
		throw RenameOperationException.Create(RenameFailureReasonCode.DocumentMissing, RenameFailureStage.Save, new ArgumentNullException("document"));
	}
}
