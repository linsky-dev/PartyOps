using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Models.Replace;
using DocumentRepository.Pipelines.Replace;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.Replace;
using DocumentRepository.Services.Rules;
using DocumentRepository.Services.Ui;

namespace DocumentRepository.Commands;

public class ReplaceCommand : ICommand
{
	public string Name
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		get
		{
			return "一键替换";
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public CommandResult Execute(OperationContext context)
	{
		if (context == null || context.Application == null || context.Document == null)
		{
			return MapFailure(ReplaceFailurePresentation.Direct(ReplaceOperationException.Create(ReplaceFailureReasonCode.NoActiveDocument, ReplaceFailureStage.Entry)));
		}
		try
		{
			ReplaceExecutionResult replaceExecutionResult = new ReplacePipeline().Execute(context);
			if (replaceExecutionResult.Cancelled)
			{
				return CommandResult.CancelledResult(replaceExecutionResult.Message);
			}
			CommandResult obj = (replaceExecutionResult.Success ? CommandResult.SuccessResult(replaceExecutionResult.Message) : CommandResult.FailResult(replaceExecutionResult.Message, replaceExecutionResult.Error));
			obj.OutcomeKind = replaceExecutionResult.OutcomeKind;
			obj.RecoveryId = replaceExecutionResult.RecoveryId;
			obj.FailureReasonCode = replaceExecutionResult.FailureReasonCode;
			obj.FailureStage = replaceExecutionResult.FailureStage;
			return UserOutcomeMapper.AttachRuleStoreNotice(obj, ReplacePlanService.LastLoadResult);
		}
		catch (RuleStoreUnavailableException innerException)
		{
			if (context.IsBatchMode || context.SuppressUserDialogs)
			{
				return MapFailure(ReplaceFailurePresentation.Direct(ReplaceOperationException.Create(ReplaceFailureReasonCode.RuleStoreUnavailable, ReplaceFailureStage.Entry, innerException)));
			}
			switch (RuleStoreRecoveryUi.ResolveInteractive(UserInteraction.Resolve(context), "替换规则库", () => ReplacePlanService.ResetToDefault().Usable, RuleStoreRecoveryUi.OpenReplaceSettingsAndRecheck))
			{
			default:
				return MapFailure(ReplaceFailurePresentation.Direct(ReplaceOperationException.Create(ReplaceFailureReasonCode.RuleRecoveryFailed, ReplaceFailureStage.Entry, innerException)));
			case RuleStoreRecoveryUi.Resolution.Cancelled:
				return CommandResult.CancelledResult("已取消。").WithHandledPresentation();
			case RuleStoreRecoveryUi.Resolution.Retry:
			{
				ReplaceExecutionResult replaceExecutionResult2 = new ReplacePipeline().Execute(context);
				if (!replaceExecutionResult2.Cancelled)
				{
					CommandResult obj2 = (replaceExecutionResult2.Success ? CommandResult.SuccessResult(replaceExecutionResult2.Message) : CommandResult.FailResult(replaceExecutionResult2.Message, replaceExecutionResult2.Error));
					obj2.OutcomeKind = replaceExecutionResult2.OutcomeKind;
					obj2.RecoveryId = replaceExecutionResult2.RecoveryId;
					obj2.FailureReasonCode = replaceExecutionResult2.FailureReasonCode;
					obj2.FailureStage = replaceExecutionResult2.FailureStage;
					return UserOutcomeMapper.AttachRuleStoreNotice(obj2, ReplacePlanService.LastLoadResult);
				}
				return CommandResult.CancelledResult(replaceExecutionResult2.Message);
			}
			}
		}
		catch (Exception ex)
		{
			LogService.Error("ReplaceCommand.Execute", ex);
			ReplaceExecutionResult replaceExecutionResult3 = ReplaceFailurePresentation.Direct(ReplaceFailureClassifier.Classify(ex, ReplaceFailureStage.Entry));
			CommandResult commandResult = CommandResult.FailResult(replaceExecutionResult3.Message, ex);
			commandResult.OutcomeKind = replaceExecutionResult3.OutcomeKind;
			commandResult.FailureReasonCode = replaceExecutionResult3.FailureReasonCode;
			commandResult.FailureStage = replaceExecutionResult3.FailureStage;
			return commandResult;
		}
	}

	CommandResult ICommand.Execute(OperationContext context)
	{
		return this.Execute(context);
	}

	private static CommandResult MapFailure(ReplaceExecutionResult structured)
	{
		CommandResult commandResult = CommandResult.FailResult(structured.Message, structured.Error);
		commandResult.OutcomeKind = structured.OutcomeKind;
		commandResult.RecoveryId = structured.RecoveryId;
		commandResult.FailureReasonCode = structured.FailureReasonCode;
		commandResult.FailureStage = structured.FailureStage;
		return commandResult;
	}
}
