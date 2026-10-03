using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Models.Protection;
using DocumentRepository.Models.Replace;
using DocumentRepository.Models.Snapshots;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.Protection;
using DocumentRepository.Services.Replace;
using DocumentRepository.Services.Snapshots;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Pipelines.Replace;

public class ReplacePipeline
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public ReplaceExecutionResult Execute(OperationContext context)
	{
		if (context != null && context.Application != null && context.Document != null)
		{
			ReplacePlan activePlan = ReplacePlanService.GetActivePlan();
			List<ReplaceRule> executableRules = GetExecutableRules(activePlan);
			if (executableRules.Count != 0)
			{
				Microsoft.Office.Interop.Word.Range value = null;
				int num = 0;
				DocumentSession documentSession = null;
				DocumentSnapshot sourceSnapshot = null;
				OutsideScopeSnapshot outsideScopeSnapshot = null;
				bool flag = false;
				bool writeConfirmed = false;
				bool flag2 = false;
				ReplaceFailureStage stage = ReplaceFailureStage.Prepare;
				try
				{
					DocumentSessionOptions documentSessionOptions = DocumentSessionOptions.Create("replace", "一键替换");
					documentSessionOptions.UseUndoRecord = !context.IsBatchMode;
					documentSessionOptions.RequireRecoveryCopy = !context.IsBatchMode;
					documentSessionOptions.SelectionRestoreMode = ((!context.IsBatchMode) ? SelectionRestoreMode.OriginalSelection : SelectionRestoreMode.None);
					DocumentRiskProfile documentRiskProfile = DocumentRiskProfiler.Capture(context.Document);
					InteractiveProtectionCoordinator.ConfigureBeforeSession(documentSessionOptions, context, documentRiskProfile);
					documentSession = DocumentSession.Begin(context, documentSessionOptions);
					if (!context.IsBatchMode && !documentSession.IsUndoRecordActive)
					{
						throw ReplaceOperationException.Create(ReplaceFailureReasonCode.UndoUnavailable, ReplaceFailureStage.Prepare);
					}
					InteractiveProtectionCoordinator.Outcome outcome = InteractiveProtectionCoordinator.ResolveBeforeWrite(documentSessionOptions, documentSession, context, documentRiskProfile);
					if (outcome.Resolution != InteractiveProtectionCoordinator.Resolution.Cancelled)
					{
						if (outcome.Resolution != InteractiveProtectionCoordinator.Resolution.Failed)
						{
							stage = ReplaceFailureStage.ResolveScope;
							documentSession.SetStage("resolve-scope");
							if (!context.IsBatchMode)
							{
								sourceSnapshot = DocumentSnapshotService.Capture(context.Document);
								documentSession.RegisterRestoreSnapshot(sourceSnapshot);
							}
							value = ReplaceScopeService.GetExecutionScope(context.Application);
							outsideScopeSnapshot = DocumentSnapshotService.CaptureOutsideScope(context.Document, value);
							documentSession.SetStage("apply-rules");
							stage = ReplaceFailureStage.ApplyRules;
							if (!context.IsBatchMode)
							{
								documentSession.MarkChangesStarted();
							}
							flag = true;
							Action confirmWriteOccurred = delegate
							{
								writeConfirmed = true;
							};
							for (int num2 = 0; num2 < executableRules.Count; num2++)
							{
								num += ExecuteRule(value, executableRules[num2], num2, confirmWriteOccurred);
							}
							if (num == 0)
							{
								documentSession.Complete();
								return ReplaceExecutionResult.NoChanges(activePlan.Name);
							}
							documentSession.SetStage("verify-result");
							stage = ReplaceFailureStage.Verify;
							ReplaceVerifier.Verify(context.Document, value, outsideScopeSnapshot, num);
							documentSession.Complete();
							return ReplaceExecutionResult.Ok(activePlan.Name, num);
						}
						return ReplaceFailurePresentation.Direct(ReplaceOperationException.Create(ReplaceFailureReasonCode.RecoveryProtectionUnavailable, ReplaceFailureStage.Prepare));
					}
					return ReplaceExecutionResult.CancelledResult(outcome.Message);
				}
				catch (Exception ex)
				{
					ReplaceOperationException failure = ReplaceFailureClassifier.Classify(ex, stage);
					Exception ex2 = null;
					if (flag && documentSession != null && documentSession.IsUndoRecordActive)
					{
						try
						{
							if (writeConfirmed)
							{
								documentSession.RollbackChanges(sourceSnapshot);
							}
							else
							{
								documentSession.AbortMutationWithoutWrites(sourceSnapshot);
							}
							flag2 = true;
						}
						catch (Exception ex3)
						{
							ex2 = ex3;
							LogService.Error("ReplacePipeline.Rollback", ex3);
						}
					}
					LogService.Error("ReplacePipeline.Execute", ex);
					if (ex2 != null)
					{
						string recoveryGuidance = documentSession?.GetRecoveryGuidanceForUser();
						return ReplaceFailurePresentation.RecoveryRequired(failure, recoveryGuidance, (documentSession == null || documentSession.RecoveryReceipt == null) ? null : documentSession.RecoveryReceipt.RecoveryId, ex2);
					}
					if (flag2)
					{
						return ReplaceFailurePresentation.Recovered(failure);
					}
					return ReplaceFailurePresentation.Direct(failure);
				}
				finally
				{
					documentSession?.Dispose();
					ComObjectRelease.Release(ref value, "ReplacePipeline.Scope");
				}
			}
			return ReplaceFailurePresentation.Direct(ReplaceOperationException.Create(ReplaceFailureReasonCode.NoExecutableItems, ReplaceFailureStage.Entry));
		}
		return ReplaceFailurePresentation.Direct(ReplaceOperationException.Create(ReplaceFailureReasonCode.NoActiveDocument, ReplaceFailureStage.Entry));
	}

	private static List<ReplaceRule> GetExecutableRules(ReplacePlan plan)
	{
		List<ReplaceRule> list = new List<ReplaceRule>();
		if (plan == null || plan.Rules == null)
		{
			return list;
		}
		foreach (ReplaceRule rule in plan.Rules)
		{
			ReplaceRuleNormalizer.Normalize(rule);
			if (rule.Enabled && rule.HasWork)
			{
				list.Add(rule);
			}
		}
		return list;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ExecuteRule(Microsoft.Office.Interop.Word.Range scope, ReplaceRule rule, int ruleIndex, Action confirmWriteOccurred)
	{
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			value = scope.Duplicate;
			return rule.FormatOnly ? FormatReplaceService.ApplyFormatOnly(value, rule, confirmWriteOccurred) : (rule.UseWildcard ? WildcardReplaceService.Apply(value, rule, confirmWriteOccurred) : TextReplaceService.Apply(value, rule, ruleIndex, confirmWriteOccurred));
		}
		finally
		{
			ComObjectRelease.Release(ref value, "ReplacePipeline.RuleRange");
		}
	}
}
