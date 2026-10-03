using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Models.RedHeader;
using DocumentRepository.Pipelines.RedHeader;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.RedHeader;
using DocumentRepository.Services.Rules;
using DocumentRepository.Services.Ui;

namespace DocumentRepository.Commands;

public class RedHeaderCommand : ICommand
{
	public string Name
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		get
		{
			return "一键套红";
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public CommandResult Execute(OperationContext context)
	{
		if (context == null || context.Document == null)
		{
			return MapFailure(RedHeaderFailurePresentation.Direct(RedHeaderOperationException.Create(RedHeaderFailureReasonCode.NoActiveDocument, RedHeaderFailureStage.Entry)));
		}
		try
		{
			if (context.CurrentConfig == null)
			{
				throw RedHeaderOperationException.Create(RedHeaderFailureReasonCode.ConfigMissing, RedHeaderFailureStage.Template);
			}
			RedHeaderResult redHeaderResult = new RedHeaderPipeline().Execute(new RedHeaderRequest
			{
				Context = context,
				Template = RedHeaderTemplateService.GetActiveTemplate(),
				Config = context.CurrentConfig
			});
			if (redHeaderResult.Cancelled)
			{
				return CommandResult.CancelledResult(redHeaderResult.Message);
			}
			if (redHeaderResult.Success)
			{
				return UserOutcomeMapper.AttachRuleStoreNotice(UserOutcomeMapper.MapSuccessWithWarnings(redHeaderResult.Message, redHeaderResult.Warnings), RedHeaderTemplateService.LastLoadResult);
			}
			return MapFailure(redHeaderResult);
		}
		catch (RuleStoreUnavailableException innerException)
		{
			if (context.IsBatchMode || context.SuppressUserDialogs)
			{
				return MapFailure(RedHeaderFailurePresentation.Direct(RedHeaderOperationException.Create(RedHeaderFailureReasonCode.TemplateStoreUnavailable, RedHeaderFailureStage.Template, innerException)));
			}
			switch (RuleStoreRecoveryUi.ResolveInteractive(UserInteraction.Resolve(context), "套红模板库", () => RedHeaderTemplateService.ResetToDefault().Usable, RuleStoreRecoveryUi.OpenRedHeaderSettingsAndRecheck))
			{
			case RuleStoreRecoveryUi.Resolution.Retry:
			{
				RedHeaderResult redHeaderResult2 = new RedHeaderPipeline().Execute(new RedHeaderRequest
				{
					Context = context,
					Template = RedHeaderTemplateService.GetActiveTemplate(),
					Config = context.CurrentConfig
				});
				if (!redHeaderResult2.Cancelled)
				{
					if (redHeaderResult2.Success)
					{
						return UserOutcomeMapper.AttachRuleStoreNotice(UserOutcomeMapper.MapSuccessWithWarnings(redHeaderResult2.Message, redHeaderResult2.Warnings), RedHeaderTemplateService.LastLoadResult);
					}
					return MapFailure(redHeaderResult2);
				}
				return CommandResult.CancelledResult(redHeaderResult2.Message);
			}
			case RuleStoreRecoveryUi.Resolution.Cancelled:
				return CommandResult.CancelledResult("已取消。").WithHandledPresentation();
			default:
				return MapFailure(RedHeaderFailurePresentation.Direct(RedHeaderOperationException.Create(RedHeaderFailureReasonCode.TemplateRecoveryFailed, RedHeaderFailureStage.Template, innerException)));
			}
		}
		catch (Exception ex)
		{
			LogService.Error("RedHeaderCommand.Execute", ex);
			return MapFailure(RedHeaderFailurePresentation.Direct(RedHeaderFailureClassifier.Classify(ex, RedHeaderFailureStage.Entry)));
		}
	}

	CommandResult ICommand.Execute(OperationContext context)
	{
		return this.Execute(context);
	}

	private static CommandResult MapFailure(RedHeaderResult result)
	{
		CommandResult commandResult = CommandResult.FailResult(result.Message, result.Error);
		commandResult.OutcomeKind = result.OutcomeKind;
		commandResult.RecoveryId = result.RecoveryId;
		commandResult.FailureReasonCode = result.FailureReasonCode;
		commandResult.FailureStage = result.FailureStage;
		return commandResult;
	}
}
