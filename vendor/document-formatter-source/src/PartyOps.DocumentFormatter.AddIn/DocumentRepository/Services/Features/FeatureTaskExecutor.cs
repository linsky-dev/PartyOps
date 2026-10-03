using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DocumentRepository.Commands;
using DocumentRepository.Models;
using DocumentRepository.Models.Features;
using DocumentRepository.Models.Formatting;
using DocumentRepository.Models.Protection;
using DocumentRepository.Models.Safety;
using DocumentRepository.Models.Tasks;
using DocumentRepository.Services.Formatting.Failures;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.Protection;
using DocumentRepository.Services.Safety;
using DocumentRepository.Services.Tasks;
using DocumentRepository.Services.Ui;

namespace DocumentRepository.Services.Features;

public static class FeatureTaskExecutor
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static CommandResult Execute(string featureId, OperationContext context, FeatureExecutionOptions options = null)
	{
		options = options ?? FeatureExecutionOptions.External("unknown");
		Stopwatch stopwatch = Stopwatch.StartNew();
		IDisposable lease = null;
		FeatureDescriptor featureDescriptor = null;
		bool flag = false;
		try
		{
			featureDescriptor = FeatureRegistry.GetById(featureId);
			ValidateContext(featureDescriptor, context);
			if (InteractiveFeatureExecutionGate.TryEnter(context.IsBatchMode, out lease))
			{
				PrepareContext(featureDescriptor, context, options);
				TaskEventReporter.Report(JsonlTaskEventJournal.Shared, TaskEventType.TaskStarted, context.TaskId, featureDescriptor.Id, options.InvocationSource, null, null, null, null, 0L);
				DocumentRiskProfile documentRiskProfile = null;
				if (featureDescriptor.RequiresDocument && !context.IsBatchMode)
				{
					documentRiskProfile = DocumentRiskProfiler.Capture(context.Document);
					if (documentRiskProfile.State == DocumentRiskState.Protected)
					{
						UserInteraction.Resolve(context).Ask(new UserConfirmationRequest
						{
							Kind = UserConfirmationKind.ProtectedRetryAfterUnprotect,
							OfferCancel = false
						});
						TaskEventReporter.Report(JsonlTaskEventJournal.Shared, TaskEventType.TaskCancelled, context.TaskId, featureDescriptor.Id, options.InvocationSource, null, "cancelled", "document-protected", null, stopwatch.ElapsedMilliseconds);
						return CommandResult.CancelledResult("文档处于保护状态，已取消。请先在 Word/WPS 中解除保护后重试。").WithHandledPresentation();
					}
				}
				if (documentRiskProfile != null && documentRiskProfile.State == DocumentRiskState.ReadOnly)
				{
					if (UserInteraction.Resolve(context).Ask(new UserConfirmationRequest
					{
						Kind = UserConfirmationKind.ReadOnlyCreateEditableCopy,
						OfferCancel = true
					}) != UserChoiceResult.Primary)
					{
						TaskEventReporter.Report(JsonlTaskEventJournal.Shared, TaskEventType.TaskCancelled, context.TaskId, featureDescriptor.Id, options.InvocationSource, null, "cancelled", "document-readonly", null, stopwatch.ElapsedMilliseconds);
						return CommandResult.CancelledResult("文档为只读，已取消。").WithHandledPresentation();
					}
					context = EditableCopyService.CreateEditableCopyAndContext(context, out var failureMessage);
					if (context == null)
					{
						return CommandResult.CancelledResult(failureMessage ?? "无法创建可编辑副本，已取消。").WithHandledPresentation();
					}
					DocumentRiskProfile documentRiskProfile2 = DocumentRiskProfiler.Capture(context.Document);
					if (documentRiskProfile2.State == DocumentRiskState.ReadOnly || documentRiskProfile2.State == DocumentRiskState.Protected)
					{
						return CommandResult.CancelledResult("副本仍不可编辑，已取消。").WithHandledPresentation();
					}
				}
				ICommand command;
				try
				{
					command = FeatureCommandFactory.Create(featureDescriptor);
				}
				catch (Exception innerException)
				{
					throw FeatureEntryOperationException.Create(FeatureEntryFailureReasonCode.CommandUnavailable, innerException);
				}
				flag = true;
				CommandResult commandResult = CommandExecutor.Execute(command, context, featureDescriptor.CommandType);
				TaskEventReporter.Report(JsonlTaskEventJournal.Shared, (commandResult != null && commandResult.Cancelled) ? TaskEventType.TaskCancelled : ((commandResult != null && commandResult.Success) ? TaskEventType.TaskCommitted : TaskEventType.TaskFailed), context.TaskId, featureDescriptor.Id, options.InvocationSource, null, (commandResult != null) ? TaskEventReporter.MapOutcome(commandResult.Status) : "failed", null, null, stopwatch.ElapsedMilliseconds);
				return commandResult;
			}
			CommandResult commandResult2 = CommandResult.FailResult("已有功能正在执行，请等待当前任务完成后再试。");
			commandResult2.OutcomeKind = UserOutcomeKind.Busy;
			return commandResult2;
		}
		catch (FeatureEntryOperationException ex)
		{
			LogService.Error("FeatureTaskExecutor.Entry feature=" + (featureId ?? "null") + ", reason=" + ex.ReasonCode, ex);
			TaskEventReporter.Report(JsonlTaskEventJournal.Shared, TaskEventType.TaskFailed, context?.TaskId, featureId, options.InvocationSource, null, "controlled-failure", "entry-" + ex.ReasonCode, null, stopwatch.ElapsedMilliseconds, ex);
			return FeatureEntryFailurePresentation.FromException(ex, featureDescriptor?.DisplayName);
		}
		catch (FormatOperationException ex2)
		{
			LogService.Error("FeatureTaskExecutor.Execute feature=" + (featureId ?? "null"), ex2);
			TaskEventReporter.Report(JsonlTaskEventJournal.Shared, TaskEventType.TaskFailed, context?.TaskId, featureId, options.InvocationSource, null, "controlled-failure", ex2.ReasonCode.ToString(), null, stopwatch.ElapsedMilliseconds, ex2);
			return FormatFailurePresentation.ToCommandResult(ex2, context?.TaskId);
		}
		catch (Exception ex3)
		{
			LogService.Error("FeatureTaskExecutor.Execute feature=" + (featureId ?? "null"), ex3);
			TaskEventReporter.Report(JsonlTaskEventJournal.Shared, TaskEventType.TaskFailed, context?.TaskId, featureId, options.InvocationSource, null, "exception", null, null, stopwatch.ElapsedMilliseconds, ex3);
			if (!string.Equals(featureId, "format", StringComparison.OrdinalIgnoreCase))
			{
				return FeatureEntryFailurePresentation.ToCommandResult(flag ? FeatureEntryFailureReasonCode.UnexpectedDuringCommand : FeatureEntryFailureReasonCode.UnexpectedBeforeStart, featureDescriptor?.DisplayName, ex3);
			}
			return FormatFailurePresentation.ToCommandResult(FormatFailureClassifier.Classify(ex3, FormatFailureStage.Entry), context?.TaskId);
		}
		finally
		{
			lease?.Dispose();
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ValidateContext(FeatureDescriptor feature, OperationContext context)
	{
		bool flag = feature != null && string.Equals(feature.Id, "format", StringComparison.OrdinalIgnoreCase);
		if (context == null)
		{
			if (flag)
			{
				throw FormatOperationException.Create(FormatFailureReasonCode.HostDocumentUnavailable, FormatFailureStage.Entry);
			}
			throw FeatureEntryOperationException.Create(FeatureEntryFailureReasonCode.ContextUnavailable);
		}
		if (context.Application == null)
		{
			if (flag)
			{
				throw FormatOperationException.Create(FormatFailureReasonCode.HostDocumentUnavailable, FormatFailureStage.Entry);
			}
			throw FeatureEntryOperationException.Create(FeatureEntryFailureReasonCode.HostUnavailable);
		}
		if (feature.RequiresDocument && context.Document == null)
		{
			if (flag)
			{
				throw FormatOperationException.Create(FormatFailureReasonCode.NoActiveDocument, FormatFailureStage.Entry);
			}
			throw FeatureEntryOperationException.Create(FeatureEntryFailureReasonCode.NoActiveDocument);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void PrepareContext(FeatureDescriptor feature, OperationContext context, FeatureExecutionOptions options)
	{
		context.FeatureId = feature.Id;
		context.InvocationSource = (string.IsNullOrWhiteSpace(options.InvocationSource) ? "unknown" : options.InvocationSource.Trim());
		if (string.IsNullOrWhiteSpace(context.TaskId))
		{
			context.TaskId = Guid.NewGuid().ToString("N");
		}
		if (options.SuppressUserDialogs)
		{
			context.SuppressUserDialogs = true;
		}
		if (context.UserInterface == null)
		{
			if (!context.SuppressUserDialogs)
			{
				throw FeatureEntryOperationException.Create(FeatureEntryFailureReasonCode.UserInterfaceUnavailable);
			}
			context.UserInterface = NonInteractiveFeatureUiService.Instance;
		}
	}
}
