using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Models.CompilationFormatting;
using DocumentRepository.Models.Formatting;
using DocumentRepository.Models.Mutations;
using DocumentRepository.Models.Protection;
using DocumentRepository.Models.Snapshots;
using DocumentRepository.Services.Formatting.Planning;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.Protection;
using DocumentRepository.Services.Snapshots;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.CompilationFormatting;

public static class CompilationTocUpdateExecutor
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static CommandResult Execute(OperationContext context, CompilationFormatOptions fallbackOptions)
	{
		if (context != null)
		{
			Document document = context.Document;
			if (document == null)
			{
				return CompilationFailurePresentation.PreWriteResult(CompilationFormatFailureReasonCode.Unknown);
			}
			DocumentSession documentSession = null;
			DocumentSnapshot documentSnapshot = null;
			DocumentWriteLease writeLease = null;
			CompilationFormatFailureStage compilationFormatFailureStage = CompilationFormatFailureStage.Manifest;
			try
			{
				CompilationManifest compilationManifest = CompilationManifestService.Read(document);
				if (!CompilationManifestService.ValidateManifestStructure(compilationManifest, out var _))
				{
					return CompilationFailurePresentation.PreWriteResult(CompilationFormatFailureReasonCode.ManifestUnreadable);
				}
				DocumentSessionOptions documentSessionOptions = DocumentSessionOptions.Create("compilation-toc", "汇编目录更新");
				documentSessionOptions.UseUndoRecord = true;
				documentSessionOptions.SuppressAlerts = false;
				documentSessionOptions.RequireRecoveryCopy = true;
				documentSessionOptions.SelectionRestoreMode = SelectionRestoreMode.OriginalSelection;
				DocumentRiskProfile documentRiskProfile = DocumentRiskProfiler.Capture(document);
				InteractiveProtectionCoordinator.ConfigureBeforeSession(documentSessionOptions, context, documentRiskProfile);
				documentSession = DocumentSession.Begin(context, documentSessionOptions);
				documentSession.SetStage("compilation-toc-update");
				if (!documentSession.Capabilities.SupportsUndoRecord || !documentSession.IsUndoRecordActive)
				{
					return CompilationFailurePresentation.PreWriteResult(CompilationFormatFailureReasonCode.UndoUnavailable);
				}
				InteractiveProtectionCoordinator.Outcome outcome = InteractiveProtectionCoordinator.ResolveBeforeWrite(documentSessionOptions, documentSession, context, documentRiskProfile);
				if (outcome.Resolution == InteractiveProtectionCoordinator.Resolution.Cancelled)
				{
					return CommandResult.CancelledResult(outcome.Message ?? "已取消目录更新，未对文档做任何修改。");
				}
				if (outcome.Resolution != InteractiveProtectionCoordinator.Resolution.Failed)
				{
					documentSnapshot = DocumentSnapshotService.Capture(document);
					documentSession.RegisterRestoreSnapshot(documentSnapshot);
					string planId = "compilation-toc-" + Guid.NewGuid().ToString("N");
					string ruleContentHash = FormatConfigFingerprint.Build(fallbackOptions);
					writeLease = documentSession.BeginMutation(planId, documentSnapshot.SnapshotId, ruleContentHash);
					CompilationMutationWarmupService.Execute(document, writeLease);
					compilationFormatFailureStage = CompilationFormatFailureStage.Toc;
					CompilationTocOutcome compilationTocOutcome;
					string usedOptionsSource;
					try
					{
						compilationTocOutcome = CompilationTocUpdateService.UpdateToc(document, fallbackOptions, context.CurrentConfig, out usedOptionsSource);
					}
					catch (CompilationTocUpdateException ex)
					{
						LogService.Warn("CompilationTocUpdateExecutor.UpdateToc", ex);
						bool flag = RollbackQuietly(documentSession, documentSnapshot, writeLease);
						return CompilationFailurePresentation.ToCommandResult(new CompilationFormatFailure(ex.ReasonCode, flag ? compilationFormatFailureStage : CompilationFormatFailureStage.Recovery, flag ? DocumentSafetyDisposition.RecoveredVerified : DocumentSafetyDisposition.RecoveryUnconfirmed), (flag || documentSession == null) ? null : documentSession.GetRecoveryGuidanceForUser());
					}
					if (!string.IsNullOrEmpty(usedOptionsSource))
					{
						LogService.Info("汇编目录更新：" + usedOptionsSource);
					}
					if (compilationTocOutcome == CompilationTocOutcome.SkippedByExistingTocPolicy)
					{
						compilationFormatFailureStage = CompilationFormatFailureStage.Verify;
						VerificationReceipt receipt = CompilationOperationVerificationService.Verify(context.TaskId, planId, documentSnapshot, ruleContentHash, document, compilationManifest.Articles.Count, fallbackOptions, fullDocument: true, compilationTocOutcome, context.CurrentConfig);
						documentSession.MarkVerified(receipt);
						documentSession.Commit(receipt);
						return CommandResult.SuccessResult("按策略未生成自动目录：检测到用户已有目录，保留不动。");
					}
					compilationFormatFailureStage = CompilationFormatFailureStage.Verify;
					VerificationReceipt receipt2 = CompilationOperationVerificationService.Verify(context.TaskId, planId, documentSnapshot, ruleContentHash, document, compilationManifest.Articles.Count, fallbackOptions, fullDocument: true, compilationTocOutcome, context.CurrentConfig);
					documentSession.MarkVerified(receipt2);
					documentSession.Commit(receipt2);
					return CommandResult.SuccessResult("汇编目录已更新。");
				}
				return CompilationFailurePresentation.PreWriteResult(CompilationFormatFailureReasonCode.ProtectionUnavailable);
			}
			catch (Exception ex2)
			{
				LogService.Error("CompilationTocUpdateExecutor.Execute", ex2);
				if (documentSession == null || documentSession.State == DocumentSessionState.Prepared)
				{
					return CompilationFailurePresentation.ToCommandResult(new CompilationFormatFailure(CompilationFormatFailureReasonCode.Unknown, compilationFormatFailureStage, DocumentSafetyDisposition.Unchanged));
				}
				bool flag2 = RollbackQuietly(documentSession, documentSnapshot, writeLease);
				return CompilationFailurePresentation.ToCommandResult(new CompilationFormatFailure((compilationFormatFailureStage == CompilationFormatFailureStage.Verify) ? CompilationFormatFailureReasonCode.VerificationFailed : CompilationFormatFailureReasonCode.ExecutionInterruptedRolledBack, flag2 ? compilationFormatFailureStage : CompilationFormatFailureStage.Recovery, flag2 ? DocumentSafetyDisposition.RecoveredVerified : DocumentSafetyDisposition.RecoveryUnconfirmed), (flag2 || documentSession == null) ? null : documentSession.GetRecoveryGuidanceForUser());
			}
			finally
			{
				documentSession?.Dispose();
			}
		}
		throw new ArgumentNullException("context");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool RollbackQuietly(DocumentSession session, DocumentSnapshot sourceSnapshot, DocumentWriteLease writeLease)
	{
		if (session != null)
		{
			if (session.State == DocumentSessionState.RolledBack)
			{
				return true;
			}
			if (session.State == DocumentSessionState.RecoveryRequired)
			{
				return false;
			}
			if (session.State != DocumentSessionState.Mutating && session.State != DocumentSessionState.Verified)
			{
				return true;
			}
			try
			{
				if (sourceSnapshot != null)
				{
					if (writeLease == null || !writeLease.HasConfirmedWrite)
					{
						session.AbortMutationWithoutWrites(sourceSnapshot);
					}
					else
					{
						session.RollbackChanges(sourceSnapshot);
					}
					return true;
				}
				return false;
			}
			catch (Exception ex)
			{
				LogService.Error("CompilationTocUpdateExecutor.Rollback", ex);
				return false;
			}
		}
		return true;
	}
}
