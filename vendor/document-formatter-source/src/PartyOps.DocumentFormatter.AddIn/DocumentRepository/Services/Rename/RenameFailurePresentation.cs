using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Models.Rename;
using DocumentRepository.Models.Tasks;

namespace DocumentRepository.Services.Rename;

public static class RenameFailurePresentation
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static CommandResult ToCommandResult(RenameOperationException failure)
	{
		if (failure == null)
		{
			throw new ArgumentNullException("failure");
		}
		CommandResult commandResult = CommandResult.FailResult(UserMessage(failure.ReasonCode), failure);
		switch (failure.ReasonCode)
		{
		case RenameFailureReasonCode.RecoveryRequired:
			commandResult.OutcomeKind = UserOutcomeKind.RecoveryRequired;
			break;
		case RenameFailureReasonCode.RollbackVerified:
			commandResult.OutcomeKind = UserOutcomeKind.FailedButRecovered;
			break;
		case RenameFailureReasonCode.OutputValidationFailed:
		case RenameFailureReasonCode.UnexpectedFailure:
			commandResult.OutcomeKind = UserOutcomeKind.Failed;
			break;
		default:
			commandResult.OutcomeKind = UserOutcomeKind.ActionRequired;
			break;
		}
		commandResult.FailureReasonCode = "rename." + failure.ReasonCode.ToString().ToLowerInvariant();
		commandResult.FailureStage = failure.Stage.ToString().ToLowerInvariant();
		return commandResult;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string UserMessage(RenameFailureReasonCode reasonCode)
	{
		return reasonCode switch
		{
			RenameFailureReasonCode.RuleRecoveryFailed => "命名方案损坏且未能恢复默认值，原配置没有被覆盖。请打开方案设置，重新保存或新建一个方案后再试。", 
			RenameFailureReasonCode.ActiveRuleMissing => "当前命名方案不存在。请打开方案设置，选择或新建一个命名方案。", 
			RenameFailureReasonCode.OutputDirectoryMissing => "保存目录不存在。请在方案设置中重新选择保存目录。", 
			RenameFailureReasonCode.OutputValidationFailed => "新文件未通过完整性检查，本次命名没有提交。请重试；如果仍然失败，请将日志发送给开发人员。", 
			RenameFailureReasonCode.OutputDirectoryUnavailable => "当前保存目录不可用。请检查目录权限，或重新选择保存目录。", 
			RenameFailureReasonCode.DocumentNeedsSave => "当前文档需要先保存。请完成保存后再使用一键命名。", 
			RenameFailureReasonCode.RecoveryRequired => "一键命名未完成，且未能确认原文档已经恢复。请不要继续保存，关闭文档后重新打开原文件检查。", 
			RenameFailureReasonCode.DocumentMissing => "没有找到当前文档，请打开需要命名的文档后再试。", 
			RenameFailureReasonCode.OutputFileInUse => "目标文件正在被其他程序使用。请关闭同名文件后再试。", 
			RenameFailureReasonCode.StablePathUnavailable => "保存后仍无法确认文档位置。请关闭并重新打开该文档后再试。", 
			RenameFailureReasonCode.SubtitleMissing => "未识别到副标题。请检查主标题后的副标题，或从当前命名方案中移除“副标题”。", 
			RenameFailureReasonCode.FilenameInvalid => "无法生成有效的文件名。请检查命名方案中的组成项和自定义文字。", 
			RenameFailureReasonCode.RollbackVerified => "本次一键命名未完成，原文档已恢复，内容没有丢失。", 
			RenameFailureReasonCode.DocumentNumberMissing => "未识别到发文字号。请检查文档开头的发文字号，或从当前命名方案中移除“发文字号”。", 
			RenameFailureReasonCode.RuleInvalid => "当前命名方案设置不完整。请打开方案设置检查并保存后再试。", 
			RenameFailureReasonCode.MainTitleMissing => "未识别到主标题。请检查文档开头的主标题，或从当前命名方案中移除“主标题”。", 
			_ => "一键命名未能确认完成。请先确认原文档仍能正常打开，且保存目录可以写入；然后重新打开文档再试。如果仍然失败，请将日志发送给开发人员。", 
		};
	}
}
