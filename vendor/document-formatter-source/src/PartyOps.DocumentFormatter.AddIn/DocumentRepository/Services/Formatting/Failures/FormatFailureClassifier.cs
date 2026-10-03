using System;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using DocumentRepository.Models.Formatting;
using DocumentRepository.Models.Safety;
using DocumentRepository.Models.Snapshots;

namespace DocumentRepository.Services.Formatting.Failures;

internal static class FormatFailureClassifier
{
	private const int RpcCallRejected = -2147418111;

	private const int RpcDisconnected = -2147417848;

	private const int RpcServerUnavailable = -2147023174;

	internal static FormatOperationException Classify(Exception error, FormatFailureStage stage, DocumentSafetyDisposition safety = DocumentSafetyDisposition.Unchanged)
	{
		if (error is FormatStageException ex)
		{
			return Classify(ex.InnerException, ex.Stage, safety);
		}
		if (!(error is FormatOperationException ex2))
		{
			if (error is AggregateException ex3)
			{
				ReadOnlyCollection<Exception> innerExceptions = ex3.Flatten().InnerExceptions;
				foreach (Exception item in innerExceptions)
				{
					if (item is FormatOperationException ex4)
					{
						return (ex4.SafetyDisposition == safety) ? ex4 : ex4.WithSafety(safety);
					}
				}
				foreach (Exception item2 in innerExceptions)
				{
					FormatStageException ex5 = item2 as FormatStageException;
					DocumentSnapshotMismatchException ex6 = item2 as DocumentSnapshotMismatchException;
					COMException ex7 = item2 as COMException;
					if (ex5 != null || ex6 != null || (ex7 != null && (ex7.HResult == -2147418111 || ex7.HResult == -2147417848 || ex7.HResult == -2147023174)))
					{
						return Classify(item2, stage, safety);
					}
				}
			}
			if (!(error is DocumentSnapshotMismatchException ex8))
			{
				COMException ex9 = error as COMException;
				if (ex9 != null && ex9.HResult == -2147418111)
				{
					return FormatOperationException.Create(FormatFailureReasonCode.HostBusy, stage, safety, error);
				}
				if (ex9 == null || (ex9.HResult != -2147417848 && ex9.HResult != -2147023174))
				{
					return FormatOperationException.Create(FormatFailureReasonCode.UnexpectedFailure, stage, safety, error);
				}
				return FormatOperationException.Create(FormatFailureReasonCode.HostDocumentUnavailable, stage, safety, error);
			}
			if (ex8.Kind == DocumentSnapshotMismatchKind.OutsideScope)
			{
				return FormatOperationException.Create(FormatFailureReasonCode.OutsideSelectionChanged, FormatFailureStage.Verify, (safety == DocumentSafetyDisposition.Unchanged) ? DocumentSafetyDisposition.PendingRecovery : safety, error);
			}
			return FormatOperationException.Create((ex8.Kind == DocumentSnapshotMismatchKind.NormalizedContent && (safety == DocumentSafetyDisposition.RecoveredVerified || safety == DocumentSafetyDisposition.RecoveryUnconfirmed || safety == DocumentSafetyDisposition.PendingRecovery)) ? FormatFailureReasonCode.OriginalTextChanged : FormatFailureReasonCode.DocumentChangedAfterPlan, stage, safety, error);
		}
		if (ex2.SafetyDisposition != safety)
		{
			return ex2.WithSafety(safety);
		}
		return ex2;
	}

	internal static FormatOperationException FromRecoveryReason(ExecutionDecisionReasonCode reasonCode)
	{
		return FormatOperationException.Create(reasonCode switch
		{
			ExecutionDecisionReasonCode.RecoveryManifestWriteFailed => FormatFailureReasonCode.RecoveryManifestWriteFailed, 
			ExecutionDecisionReasonCode.RecoverySourceChangedDuringCopy => FormatFailureReasonCode.RecoverySourceChanged, 
			ExecutionDecisionReasonCode.RecoveryQuotaExceeded => FormatFailureReasonCode.RecoveryQuotaExceeded, 
			ExecutionDecisionReasonCode.RecoveryCopyVerificationFailed => FormatFailureReasonCode.RecoveryCopyVerificationFailed, 
			ExecutionDecisionReasonCode.RecoveryRootUnavailable => FormatFailureReasonCode.RecoveryRootUnavailable, 
			ExecutionDecisionReasonCode.RecoveryDiskSpaceInsufficient => FormatFailureReasonCode.RecoveryDiskSpaceInsufficient, 
			_ => FormatFailureReasonCode.RecoveryCopyVerificationFailed, 
		}, FormatFailureStage.Protection);
	}
}
