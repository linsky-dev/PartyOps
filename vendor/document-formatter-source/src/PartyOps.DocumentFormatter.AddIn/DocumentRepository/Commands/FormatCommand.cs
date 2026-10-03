using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Models.Formatting;
using DocumentRepository.Pipelines.Format;
using DocumentRepository.Services.Formatting.Failures;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.Rules;
using DocumentRepository.Services.Ui;

namespace DocumentRepository.Commands;

public class FormatCommand : ICommand
{
	public string Name
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		get
		{
			return "一键排版";
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public CommandResult Execute(OperationContext context)
	{
		if (context == null || context.Document == null)
		{
			return FormatFailurePresentation.ToCommandResult(FormatOperationException.Create(FormatFailureReasonCode.NoActiveDocument, FormatFailureStage.Entry), context?.TaskId);
		}
		try
		{
			return ExecuteCore(context);
		}
		catch (RuleStoreUnavailableException error)
		{
			if (context.IsBatchMode || context.SuppressUserDialogs)
			{
				return FormatFailurePresentation.ToCommandResult(FormatFailureClassifier.Classify(error, FormatFailureStage.Entry), context.TaskId);
			}
			switch (RuleStoreRecoveryUi.ResolveInteractive(UserInteraction.Resolve(context), "排版模板库", () => ConfigManager.ResetStoreToDefault().Usable, RuleStoreRecoveryUi.OpenFormatSettingsAndRecheck))
			{
			case RuleStoreRecoveryUi.Resolution.Retry:
				try
				{
					return ExecuteCore(context);
				}
				catch (Exception ex)
				{
					LogService.Error("FormatCommand.Execute.retry", ex);
					return FormatFailurePresentation.ToCommandResult(FormatFailureClassifier.Classify(ex, FormatFailureStage.Entry), context.TaskId);
				}
			case RuleStoreRecoveryUi.Resolution.Cancelled:
				return CommandResult.CancelledResult("已取消。").WithHandledPresentation();
			default:
				return CommandResult.FailResult("恢复默认配置失败，原文件未改动。", error);
			}
		}
		catch (FormatOperationException ex2)
		{
			LogService.Error("FormatCommand.Execute", ex2);
			return FormatFailurePresentation.ToCommandResult(ex2, context.TaskId);
		}
		catch (Exception ex3)
		{
			LogService.Error("FormatCommand.Execute", ex3);
			return FormatFailurePresentation.ToCommandResult(FormatFailureClassifier.Classify(ex3, FormatFailureStage.Entry), context.TaskId);
		}
	}

	CommandResult ICommand.Execute(OperationContext context)
	{
		return this.Execute(context);
	}

	private static CommandResult ExecuteCore(OperationContext context)
	{
		ConfigManager.EnsureStoreAvailableForExecution();
		CommandResult commandResult = UserOutcomeMapper.AttachRuleStoreNotice(new FormatPipeline().Execute(context), ConfigManager.LastStoreLoadResult);
		if (commandResult.Success && !ShouldSuppressUiSideEffects(context))
		{
			ScheduleVersionCheck();
		}
		return commandResult;
	}

	private static void ScheduleVersionCheck()
	{
		// WPS 通过原生 COM 桥启动时没有 VSTO ThisAddIn 实例；排版本身必须保持可用。
	}

	private static bool ShouldSuppressUiSideEffects(OperationContext context)
	{
		if (context != null)
		{
			if (context.IsBatchMode)
			{
				return true;
			}
			return context.SuppressUserDialogs;
		}
		return false;
	}
}
