using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Models.Rename;
using DocumentRepository.Models.Tasks;
using DocumentRepository.Pipelines.Rename;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.Rename;
using DocumentRepository.Services.Rules;
using DocumentRepository.Services.Ui;

namespace DocumentRepository.Commands;

public class RenameCommand : ICommand
{
	public string Name
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		get
		{
			return "一键命名";
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public CommandResult Execute(OperationContext context)
	{
		bool flag = false;
		while (true)
		{
			try
			{
				return UserOutcomeMapper.AttachRuleStoreNotice(CommandResult.SuccessResult(new RenamePipeline().Execute(context)), RenameRuleManager.LastLoadResult);
			}
			catch (UserCancelledException)
			{
				return CommandResult.CancelledResult("已取消。");
			}
			catch (RuleStoreUnavailableException innerException)
			{
				if (context.IsBatchMode || context.SuppressUserDialogs || flag)
				{
					return RenameFailurePresentation.ToCommandResult(RenameOperationException.Create(RenameFailureReasonCode.RuleInvalid, RenameFailureStage.RuleLoad, innerException));
				}
				switch (RuleStoreRecoveryUi.ResolveInteractive(UserInteraction.Resolve(context), "命名规则库", () => RenameRuleManager.ResetToDefault().Usable, RuleStoreRecoveryUi.OpenRenameSettingsAndRecheck))
				{
				case RuleStoreRecoveryUi.Resolution.Cancelled:
					return CommandResult.CancelledResult("已取消。").WithHandledPresentation();
				default:
					return RenameFailurePresentation.ToCommandResult(RenameOperationException.Create(RenameFailureReasonCode.RuleRecoveryFailed, RenameFailureStage.RuleLoad, innerException));
				case RuleStoreRecoveryUi.Resolution.Retry:
					flag = true;
					break;
				}
			}
			catch (RenameOperationException ex2)
			{
				LogService.Error("RenameCommand.Structured reason=" + ex2.ReasonCode.ToString() + ", stage=" + ex2.Stage, ex2);
				return RenameFailurePresentation.ToCommandResult(ex2);
			}
			catch (Exception ex3)
			{
				LogService.Error("RenameCommand.Execute", ex3);
				return RenameFailurePresentation.ToCommandResult(RenameFailureClassifier.Classify(ex3, RenameFailureStage.Unknown));
			}
		}
	}

	CommandResult ICommand.Execute(OperationContext context)
	{
		return this.Execute(context);
	}
}
