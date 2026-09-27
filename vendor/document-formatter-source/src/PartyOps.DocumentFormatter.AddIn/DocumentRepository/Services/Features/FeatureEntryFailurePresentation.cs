using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Models.Features;
using DocumentRepository.Models.Tasks;

namespace DocumentRepository.Services.Features;

public static class FeatureEntryFailurePresentation
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static CommandResult ToCommandResult(FeatureEntryFailureReasonCode reasonCode, string featureDisplayName, Exception error = null)
	{
		string featureDisplayName2 = (string.IsNullOrWhiteSpace(featureDisplayName) ? "当前功能" : featureDisplayName.Trim());
		CommandResult commandResult = CommandResult.FailResult(UserMessage(reasonCode, featureDisplayName2), error);
		commandResult.FailureReasonCode = "entry." + ToToken(reasonCode);
		commandResult.FailureStage = "entry";
		commandResult.OutcomeKind = (IsActionRequired(reasonCode) ? UserOutcomeKind.ActionRequired : UserOutcomeKind.Failed);
		return commandResult;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static CommandResult FromException(FeatureEntryOperationException failure, string featureDisplayName)
	{
		if (failure == null)
		{
			throw new ArgumentNullException("failure");
		}
		return ToCommandResult(failure.ReasonCode, featureDisplayName, failure);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string UserMessage(FeatureEntryFailureReasonCode reasonCode, string featureDisplayName)
	{
		return reasonCode switch
		{
			FeatureEntryFailureReasonCode.CommandUnavailable => featureDisplayName + "组件未能加载，功能尚未开始，文档没有被修改。请重启 Word/WPS；如果仍然失败，请重新安装插件。", 
			FeatureEntryFailureReasonCode.NoActiveDocument => "当前没有可处理的文档。请先打开一个 Word/WPS 文档，再使用" + featureDisplayName + "。", 
			FeatureEntryFailureReasonCode.UserInterfaceUnavailable => featureDisplayName + "的操作窗口未能加载，功能尚未开始，文档没有被修改。请重启 Word/WPS 后再试。", 
			FeatureEntryFailureReasonCode.HostUnavailable => "Word/WPS 当前尚未准备好。请关闭并重新打开文档，再使用" + featureDisplayName + "。", 
			FeatureEntryFailureReasonCode.UnexpectedDuringCommand => featureDisplayName + "未能完成。请先检查当前文档是否符合预期；如有异常请立即撤销。重新打开文档后仍然失败，请将日志发送给开发人员。", 
			FeatureEntryFailureReasonCode.CommandReturnedNoResult => featureDisplayName + "没有返回执行结果。请先检查当前文档是否符合预期；如有异常请立即撤销，然后重新打开文档再试。", 
			FeatureEntryFailureReasonCode.ContextUnavailable => featureDisplayName + "未能读取当前文档状态，功能尚未开始，文档没有被修改。请重新打开文档后再试。", 
			_ => featureDisplayName + "暂时无法启动，功能尚未开始，文档没有被修改。请关闭并重新打开文档后再试；如果仍然失败，请重启 Word/WPS。", 
		};
	}

	private static bool IsActionRequired(FeatureEntryFailureReasonCode reasonCode)
	{
		if (reasonCode != FeatureEntryFailureReasonCode.NoActiveDocument && reasonCode != FeatureEntryFailureReasonCode.HostUnavailable && reasonCode != FeatureEntryFailureReasonCode.ContextUnavailable && reasonCode != FeatureEntryFailureReasonCode.UserInterfaceUnavailable)
		{
			return reasonCode == FeatureEntryFailureReasonCode.CommandUnavailable;
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string ToToken(FeatureEntryFailureReasonCode reasonCode)
	{
		return reasonCode switch
		{
			FeatureEntryFailureReasonCode.CommandUnavailable => "command-unavailable", 
			FeatureEntryFailureReasonCode.HostUnavailable => "host-unavailable", 
			FeatureEntryFailureReasonCode.UserInterfaceUnavailable => "ui-unavailable", 
			FeatureEntryFailureReasonCode.CommandReturnedNoResult => "command-no-result", 
			FeatureEntryFailureReasonCode.UnexpectedDuringCommand => "unexpected-during-command", 
			FeatureEntryFailureReasonCode.NoActiveDocument => "no-active-document", 
			FeatureEntryFailureReasonCode.ContextUnavailable => "context-unavailable", 
			_ => "unexpected-before-start", 
		};
	}
}
