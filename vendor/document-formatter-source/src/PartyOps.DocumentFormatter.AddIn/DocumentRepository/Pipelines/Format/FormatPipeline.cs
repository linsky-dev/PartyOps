using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Models.CompilationFormatting;
using DocumentRepository.Models.Formatting;
using DocumentRepository.Models.FormattingPlan;
using DocumentRepository.Models.Mutations;
using DocumentRepository.Models.Protection;
using DocumentRepository.Models.Tasks;
using DocumentRepository.Pipelines.CompilationFormatting;
using DocumentRepository.Services.CompilationFormatting;
using DocumentRepository.Services.Formatting;
using DocumentRepository.Services.Formatting.Failures;
using DocumentRepository.Services.Formatting.Planning;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.Performance;
using DocumentRepository.Services.Protection;
using DocumentRepository.Services.Safety;
using DocumentRepository.Services.Tasks;
using DocumentRepository.Services.Ui;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Pipelines.Format;

public class FormatPipeline
{
	public string Name
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		get
		{
			return "FormatPipeline";
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public CommandResult Execute(OperationContext context)
	{
		if (context == null)
		{
			throw new ArgumentNullException("context");
		}
		if (context.Application != null)
		{
			if (context.Document != null)
			{
				if (context.CurrentConfig != null)
				{
					CompilationRouteResolution compilationRouteResolution = CompilationFormatRouter.Resolve(context);
					if (compilationRouteResolution == null)
					{
						if (context.IsBatchMode || !context.HasMeaningfulSelection || context.Selection == null || IsWholeDocumentSelection(context.Document, context.Selection))
						{
							return ExecuteFull(context);
						}
						return ExecuteSelection(context);
					}
					return compilationRouteResolution.Decision.Kind switch
					{
						CompilationRouteKind.CompilationTocUpdate => CompilationTocUpdateExecutor.Execute(context, (context.CurrentConfig == null) ? null : context.CurrentConfig.CompilationFormatOptions), 
						CompilationRouteKind.Reject => CompilationFailurePresentation.ToCommandResult(new CompilationFormatFailure(compilationRouteResolution.Decision.RejectReasonCode, compilationRouteResolution.Decision.RejectStage, DocumentSafetyDisposition.Unchanged, compilationRouteResolution.Decision.RejectStructuredDetail)), 
						_ => CompilationFormattingPipeline.Execute(context, compilationRouteResolution), 
					};
				}
				throw FormatOperationException.Create(FormatFailureReasonCode.BuiltInDefaultInvalid, FormatFailureStage.Entry);
			}
			throw FormatOperationException.Create(FormatFailureReasonCode.NoActiveDocument, FormatFailureStage.Entry);
		}
		throw FormatOperationException.Create(FormatFailureReasonCode.HostDocumentUnavailable, FormatFailureStage.Entry);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public CommandResult ExecuteFull(OperationContext context)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		string runId = "full-" + DateTime.Now.ToString("HHmmssfff");
		List<string> performanceLines = new List<string>();
		long previousMs = 0L;
		previousMs = LogPerf(performanceLines, runId, "enter full format", stopwatch, previousMs);
		Application application = context.Application;
		Document document = context.Document;
		bool quietMode = context.IsBatchMode || context.SuppressUserDialogs;
		bool completedSuccessfully = false;
		bool wasCancelled = false;
		bool gridCompatibilityFallbackApplied = false;
		string progressMessage = null;
		int pageCount = 0;
		TaskProgressSession taskProgressSession = null;
		DocumentSession documentSession = null;
		FormatFailureStage stage = FormatFailureStage.Entry;
		FirstFormatDiagnosticsSession firstFormatDiagnosticsSession = null;
		firstFormatDiagnosticsSession = new FirstFormatDiagnosticsSession(context.TaskId);
		firstFormatDiagnosticsSession.Mark("entry", 1, reliable: true, "FormatPipeline.ExecuteFull start");
		try
		{
			stage = FormatFailureStage.Plan;
			FormatConfigurationValidator.Validate(context.CurrentConfig);
			stage = FormatFailureStage.Protection;
			EnsureFormatAllowed(document);
			FormatWorkloadEstimate formatWorkloadEstimate = FormatWorkloadEstimator.Estimate(document);
			if (!quietMode && formatWorkloadEstimate.ShowProgress)
			{
				taskProgressSession = CreateAndStartProgress(context, "正在读取文档", "准备分析");
				firstFormatDiagnosticsSession?.Mark("progress-shown", 1, reliable: true, "large workload at pipeline entry");
				previousMs = LogPerf(performanceLines, runId, "progress window shown for large workload", stopwatch, previousMs);
			}
			else if (!quietMode)
			{
				firstFormatDiagnosticsSession?.Mark("progress-deferred", 1, reliable: true, "small workload pending elapsed-time check");
			}
			else
			{
				firstFormatDiagnosticsSession?.Mark("progress-skipped", 1, reliable: true, "quiet mode");
			}
			previousMs = LogPerf(performanceLines, runId, "workload characters=" + formatWorkloadEstimate.CharacterCount + ", paragraphs=" + formatWorkloadEstimate.ParagraphCount + ", showProgress=" + formatWorkloadEstimate.ShowProgress, stopwatch, previousMs);
			DocumentSessionOptions documentSessionOptions = DocumentSessionOptions.Create("format", "一键排版");
			documentSessionOptions.UseUndoRecord = true;
			documentSessionOptions.SuppressAlerts = false;
			documentSessionOptions.RefreshVisibleLayoutOnSuccess = false;
			documentSessionOptions.RequireRecoveryCopy = !context.IsBatchMode;
			documentSessionOptions.SelectionRestoreMode = ((!quietMode) ? SelectionRestoreMode.DocumentStart : SelectionRestoreMode.None);
			DocumentRiskProfile documentRiskProfile = DocumentRiskProfiler.Capture(document);
			InteractiveProtectionCoordinator.ConfigureBeforeSession(documentSessionOptions, context, documentRiskProfile);
			documentSession = DocumentSession.Begin(context, documentSessionOptions);
			documentSession.SetStage("prepare");
			EnsureTransactionalHost(documentSession);
			firstFormatDiagnosticsSession?.Mark("session-ready", 1, reliable: true, "DocumentSession begun and undo verified");
			InteractiveProtectionCoordinator.Outcome outcome = InteractiveProtectionCoordinator.ResolveBeforeWrite(documentSessionOptions, documentSession, context, documentRiskProfile);
			if (outcome.Resolution != InteractiveProtectionCoordinator.Resolution.Cancelled)
			{
				if (outcome.Resolution == InteractiveProtectionCoordinator.Resolution.Failed)
				{
					return FormatFailurePresentation.ToCommandResult(FormatFailureClassifier.FromRecoveryReason(outcome.ReasonCode), context.TaskId);
				}
				stage = FormatFailureStage.Plan;
				documentSession.SetStage("snapshot-and-plan");
				FormatExecutionPlan formatExecutionPlan = FormatExecutionPlanBuilder.PrepareAndBuild(context, null, isSelectionMode: false, documentSession, taskProgressSession, firstFormatDiagnosticsSession);
				if (firstFormatDiagnosticsSession != null)
				{
					firstFormatDiagnosticsSession.PlanId = formatExecutionPlan.PlanId;
				}
				firstFormatDiagnosticsSession?.Mark("plan-built", formatExecutionPlan.Operations.Count, reliable: true, "FormatExecutionPlan prepared and sealed");
				previousMs = LogPerf(performanceLines, runId, "format plan built operations=" + formatExecutionPlan.Operations.Count, stopwatch, previousMs);
				pageCount = document.Content.ComputeStatistics(WdStatistic.wdStatisticPages);
				previousMs = LogPerf(performanceLines, runId, "page count=" + pageCount, stopwatch, previousMs);
				if (FormatProgressPolicy.ShouldShowAfterPlanning(quietMode, taskProgressSession != null, pageCount, stopwatch.ElapsedMilliseconds))
				{
					taskProgressSession = CreateAndStartProgress(context, "正在排版", "准备执行");
					string progressReason = ((pageCount >= 30) ? "page count" : "elapsed planning time");
					firstFormatDiagnosticsSession?.Mark("progress-shown", 1, reliable: true, "deferred after " + progressReason);
					previousMs = LogPerf(performanceLines, runId, "progress window shown after " + progressReason + " fallback", stopwatch, previousMs);
				}
				taskProgressSession?.Report(TaskProgressInfo.Create("一键排版", 34, 100, "排版计划已就绪", "准备执行"));
				stage = FormatFailureStage.Execute;
				documentSession.SetStage("plan-execute");
				taskProgressSession?.Cancellation.ThrowIfCancellationRequested("执行排版计划前");
				MutationPlanExecutionResult mutationPlanExecutionResult = FormatPlanExecutor.Execute(formatExecutionPlan, application, document, null, documentSession, taskProgressSession, firstFormatDiagnosticsSession);
				firstFormatDiagnosticsSession?.Mark("execute-done", mutationPlanExecutionResult.AppliedCount, mutationPlanExecutionResult.Verified, "FormatPlanExecutor completed");
				previousMs = LogPerf(performanceLines, runId, "format plan executed operations=" + mutationPlanExecutionResult.AppliedCount + ", verified=" + mutationPlanExecutionResult.Verified, stopwatch, previousMs);
				gridCompatibilityFallbackApplied = formatExecutionPlan.FormatContext != null && formatExecutionPlan.FormatContext.DocumentGridCompatibilityFallbackApplied;
				LogPerf(performanceLines, runId, "full format success", stopwatch, previousMs);
				completedSuccessfully = true;
				documentSession.Complete();
				firstFormatDiagnosticsSession?.Mark("committed", 1, reliable: true, "session committed");
				List<VerificationFinding> list = new List<VerificationFinding>(ExecutionWarningCollector.Snapshot());
				if (gridCompatibilityFallbackApplied)
				{
					bool hasGridCompatibilityWarning = false;
					for (int i = 0; i < list.Count; i++)
					{
						if (string.Equals(list[i].Code, "document-grid-compatibility", StringComparison.Ordinal))
						{
							hasGridCompatibilityWarning = true;
							break;
						}
					}
					if (!hasGridCompatibilityWarning)
					{
						list.Add(new VerificationFinding("document-grid-compatibility", VerificationSeverity.Warning, "page-setup", "warn.documentgrid.compat"));
					}
				}
				return UserOutcomeMapper.MapSuccessWithWarnings("排版完成", list);
			}
			return CommandResult.CancelledResult(outcome.Message);
		}
		catch (OperationCanceledException ex)
		{
			wasCancelled = true;
			progressMessage = "正在安全取消本次排版。";
			LogPerf(performanceLines, runId, "full format canceled at stage=" + stage, stopwatch, previousMs);
			LogService.Warn("FormatPipeline.ExecuteFull canceled", ex);
			bool rollbackAttempted;
			Exception ex2 = TryRollbackChangedSession(documentSession, "FormatPipeline.ExecuteFull.Rollback", out rollbackAttempted);
			if (ex2 != null)
			{
				wasCancelled = false;
				CommandResult commandResult = FormatFailurePresentation.ToCommandResult(FormatOperationException.Create(FormatFailureReasonCode.RecoveryRequired, FormatFailureStage.Rollback, DocumentSafetyDisposition.RecoveryUnconfirmed, new AggregateException(ex, ex2)), context.TaskId, documentSession?.GetRecoveryGuidanceForUser(), RecoveryId(documentSession));
				progressMessage = commandResult.Message;
				return commandResult;
			}
			CommandResult commandResult2 = FormatFailurePresentation.ToCommandResult(FormatOperationException.Create(FormatFailureReasonCode.Cancelled, stage, rollbackAttempted ? DocumentSafetyDisposition.RecoveredVerified : DocumentSafetyDisposition.Unchanged, ex), context.TaskId);
			progressMessage = commandResult2.Message;
			return commandResult2;
		}
		catch (Exception ex3)
		{
			progressMessage = "正在安全结束本次排版。";
			LogPerf(performanceLines, runId, "full format failed at stage=" + stage.ToString() + ", type=" + ex3.GetType().Name, stopwatch, previousMs);
			LogService.Error("FormatPipeline.ExecuteFull", ex3);
			CommandResult commandResult3 = MapFailure(documentSession, ex3, stage, context.TaskId, "FormatPipeline.ExecuteFull.Rollback");
			progressMessage = commandResult3.Message;
			return commandResult3;
		}
		finally
		{
			if (taskProgressSession != null)
			{
				try
				{
					if (wasCancelled)
					{
						taskProgressSession.Cancel(string.IsNullOrWhiteSpace(progressMessage) ? "排版已取消" : progressMessage);
					}
					else if (completedSuccessfully)
					{
						taskProgressSession.Complete("排版完成");
					}
					else
					{
						taskProgressSession.Fail(string.IsNullOrWhiteSpace(progressMessage) ? "排版失败" : progressMessage);
					}
					taskProgressSession.Dispose();
				}
				catch (Exception ex4)
				{
					LogService.Warn("FormatPipeline.ExecuteFull.CloseProgress", ex4);
				}
			}
			documentSession?.Dispose();
			LogPerf(performanceLines, runId, "cleanup complete", stopwatch, previousMs);
			FlushPerf(performanceLines);
			string outcome2 = (wasCancelled ? "cancelled" : (completedSuccessfully ? "success" : "failed"));
			firstFormatDiagnosticsSession?.Flush(outcome2);
			firstFormatDiagnosticsSession?.Dispose();
			if (!quietMode && completedSuccessfully)
			{
				try
				{
					if (gridCompatibilityFallbackApplied)
					{
						stopwatch.Stop();
					}
					else
					{
						ShowCompletionMessage(context, stopwatch, pageCount);
					}
				}
				catch (Exception ex5)
				{
					LogService.Warn("FormatPipeline.ShowSuccessMessage", ex5);
				}
			}
			else
			{
				stopwatch.Stop();
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public CommandResult ExecuteSelection(OperationContext context)
	{
		Stopwatch sw = Stopwatch.StartNew();
		string runId = "selection-" + DateTime.Now.ToString("HHmmssfff");
		List<string> lines = new List<string>();
		long previousMs = 0L;
		previousMs = LogPerf(lines, runId, "enter selection format", sw, previousMs);
		Application application = context.Application;
		Document document = context.Document;
		Selection selection = context.Selection;
		Microsoft.Office.Interop.Word.Range value = null;
		DocumentSession documentSession = null;
		FormatFailureStage stage = FormatFailureStage.Entry;
		try
		{
			stage = FormatFailureStage.Plan;
			FormatConfigurationValidator.Validate(context.CurrentConfig);
			stage = FormatFailureStage.Protection;
			EnsureFormatAllowed(document);
			if (selection == null || selection.Range == null || selection.Range.Start == selection.Range.End)
			{
				throw FormatOperationException.Create(FormatFailureReasonCode.SelectionEmpty, FormatFailureStage.Entry);
			}
			DocumentSessionOptions documentSessionOptions = DocumentSessionOptions.Create("format-selection", "选中快排");
			documentSessionOptions.UseUndoRecord = true;
			documentSessionOptions.SuppressAlerts = false;
			documentSessionOptions.RequireRecoveryCopy = true;
			documentSessionOptions.SelectionRestoreMode = SelectionRestoreMode.OriginalSelection;
			DocumentRiskProfile documentRiskProfile = DocumentRiskProfiler.Capture(document);
			InteractiveProtectionCoordinator.ConfigureBeforeSession(documentSessionOptions, context, documentRiskProfile);
			documentSession = DocumentSession.Begin(context, documentSessionOptions);
			documentSession.SetStage("selection-prepare");
			EnsureTransactionalHost(documentSession);
			InteractiveProtectionCoordinator.Outcome outcome = InteractiveProtectionCoordinator.ResolveBeforeWrite(documentSessionOptions, documentSession, context, documentRiskProfile);
			if (outcome.Resolution != InteractiveProtectionCoordinator.Resolution.Cancelled)
			{
				if (outcome.Resolution != InteractiveProtectionCoordinator.Resolution.Failed)
				{
					stage = FormatFailureStage.Plan;
					value = SelectionFormattingScopeResolver.Resolve(document, selection.Range);
					documentSession.SetStage("selection-snapshot-and-plan");
					FormatExecutionPlan formatExecutionPlan = FormatExecutionPlanBuilder.PrepareAndBuild(context, value, isSelectionMode: true, documentSession, null);
					previousMs = LogPerf(lines, runId, "selection plan built operations=" + formatExecutionPlan.Operations.Count, sw, previousMs);
					stage = FormatFailureStage.Execute;
					documentSession.SetStage("selection-plan-execute");
					MutationPlanExecutionResult mutationPlanExecutionResult = FormatPlanExecutor.Execute(formatExecutionPlan, application, document, value, documentSession, null);
					previousMs = LogPerf(lines, runId, "selection plan executed operations=" + mutationPlanExecutionResult.AppliedCount + ", verified=" + mutationPlanExecutionResult.Verified, sw, previousMs);
					LogPerf(lines, runId, "selection format success", sw, previousMs);
					documentSession.Complete();
					return UserOutcomeMapper.MapSuccessWithWarnings("排版完成", ExecutionWarningCollector.Snapshot());
				}
				return FormatFailurePresentation.ToCommandResult(FormatFailureClassifier.FromRecoveryReason(outcome.ReasonCode), context.TaskId);
			}
			return CommandResult.CancelledResult(outcome.Message);
		}
		catch (Exception ex)
		{
			LogPerf(lines, runId, "selection format failed at stage=" + stage.ToString() + ", type=" + ex.GetType().Name, sw, previousMs);
			LogService.Error("FormatPipeline.ExecuteSelection", ex);
			return MapFailure(documentSession, ex, stage, context.TaskId, "FormatPipeline.ExecuteSelection.Rollback");
		}
		finally
		{
			documentSession?.Dispose();
			ComObjectRelease.Release(ref value, "FormatPipeline.ExecuteSelection.WorkRange");
			LogPerf(lines, runId, "cleanup complete", sw, previousMs);
			FlushPerf(lines);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static Exception TryRollbackChangedSession(DocumentSession session, string logContext, out bool rollbackAttempted)
	{
		rollbackAttempted = false;
		if (session == null || session.State != DocumentSessionState.RolledBack)
		{
			if (session != null && session.State == DocumentSessionState.RecoveryRequired)
			{
				rollbackAttempted = true;
				return new InvalidOperationException("session-recovery-required");
			}
			if (session == null || (session.State != DocumentSessionState.Mutating && session.State != DocumentSessionState.Verified))
			{
				return null;
			}
			try
			{
				rollbackAttempted = true;
				session.RollbackChanges();
				return null;
			}
			catch (Exception ex)
			{
				LogService.Error(logContext, ex);
				return ex;
			}
		}
		rollbackAttempted = true;
		return null;
	}

	private static CommandResult MapFailure(DocumentSession session, Exception error, FormatFailureStage stage, string taskId, string rollbackLogContext)
	{
		bool rollbackAttempted;
		Exception ex = TryRollbackChangedSession(session, rollbackLogContext, out rollbackAttempted);
		if (ex == null)
		{
			DocumentSafetyDisposition safety = (rollbackAttempted ? DocumentSafetyDisposition.RecoveredVerified : DocumentSafetyDisposition.Unchanged);
			FormatOperationException ex2 = FormatFailureClassifier.Classify(error, stage, safety);
			if (!rollbackAttempted && ex2.SafetyDisposition == DocumentSafetyDisposition.PendingRecovery)
			{
				return FormatFailurePresentation.ToCommandResult(FormatOperationException.Create(FormatFailureReasonCode.RecoveryRequired, FormatFailureStage.Rollback, DocumentSafetyDisposition.RecoveryUnconfirmed, error), taskId, session?.GetRecoveryGuidanceForUser(), RecoveryId(session));
			}
			return FormatFailurePresentation.ToCommandResult(ex2, taskId);
		}
		return FormatFailurePresentation.ToCommandResult(FormatOperationException.Create(FormatFailureReasonCode.RecoveryRequired, FormatFailureStage.Rollback, DocumentSafetyDisposition.RecoveryUnconfirmed, new AggregateException(error, ex)), taskId, session?.GetRecoveryGuidanceForUser(), RecoveryId(session));
	}

	private static string RecoveryId(DocumentSession session)
	{
		if (session != null && session.RecoveryReceipt != null)
		{
			return session.RecoveryReceipt.RecoveryId;
		}
		return null;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static TaskProgressSession CreateAndStartProgress(OperationContext context, string step, string stage)
	{
		TaskProgressSession taskProgressSession = new TaskProgressSession(context.UserInterface.CreateProgress("format", "正在排版..."), "format", "一键排版");
		taskProgressSession.Start("一键排版", 100, step);
		taskProgressSession.Report(TaskProgressInfo.Create("一键排版", 1, 100, step, stage));
		return taskProgressSession;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void EnsureFormatAllowed(Document doc)
	{
		if (doc == null)
		{
			throw new ArgumentNullException("doc");
		}
		if (doc.ProtectionType == WdProtectionType.wdNoProtection)
		{
			if (doc.ReadOnly)
			{
				throw FormatOperationException.Create(FormatFailureReasonCode.DocumentReadOnly, FormatFailureStage.Protection);
			}
			return;
		}
		throw FormatOperationException.Create(FormatFailureReasonCode.DocumentProtected, FormatFailureStage.Protection);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void EnsureTransactionalHost(DocumentSession session)
	{
		if (session == null)
		{
			throw new ArgumentNullException("session");
		}
		if (session.Capabilities.SupportsUndoRecord)
		{
			if (!session.IsUndoRecordActive)
			{
				throw FormatOperationException.Create(FormatFailureReasonCode.UndoStartFailed, FormatFailureStage.Protection, DocumentSafetyDisposition.Unchanged, session.UndoRecordStartFailure);
			}
			return;
		}
		throw FormatOperationException.Create(FormatFailureReasonCode.UndoUnsupported, FormatFailureStage.Protection);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool IsWholeDocumentSelection(Document doc, Selection sel)
	{
		if (doc == null)
		{
			throw new ArgumentNullException("doc");
		}
		if (sel == null || sel.Range == null)
		{
			return false;
		}
		Microsoft.Office.Interop.Word.Range value = null;
		Microsoft.Office.Interop.Word.Range value2 = null;
		try
		{
			value = doc.Content;
			value2 = sel.Range;
			return value2.Start <= value.Start + 1 && value2.End >= value.End - 1;
		}
		finally
		{
			ComObjectRelease.Release(ref value2, "FormatPipeline.IsWholeDocumentSelection.SelectionRange");
			ComObjectRelease.Release(ref value, "FormatPipeline.IsWholeDocumentSelection.ContentRange");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static long LogPerf(List<string> lines, string runId, string step, Stopwatch sw, long previousMs)
	{
		long num = sw?.ElapsedMilliseconds ?? 0;
		long num2 = Math.Max(0L, num - previousMs);
		lines?.Add("[FORMAT-PERF] " + runId + " | " + step + " | step=" + num2 + "ms | total=" + num + "ms");
		return num;
	}

	private static void FlushPerf(List<string> lines)
	{
		if (lines != null && lines.Count != 0)
		{
			LogService.Info(string.Join(Environment.NewLine, lines));
			lines.Clear();
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ShowCompletionMessage(OperationContext context, Stopwatch sw, int pageCount)
	{
		sw.Stop();
		if (pageCount >= 30)
		{
			int num = (int)Math.Ceiling(sw.Elapsed.TotalSeconds);
			if (context.UserInterface == null)
			{
				throw new InvalidOperationException("一键排版缺少用户交互服务。入口必须注入 IFeatureUiService。");
			}
			context.UserInterface.ShowCompletionMessage("排版完成", "本次长文档排版已全部完成", "总耗时 " + num + " 秒", 2000);
		}
	}
}
