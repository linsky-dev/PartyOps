using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Models.Replace;
using DocumentRepository.Models.Tasks;

namespace DocumentRepository.Services.Replace;

public static class ReplaceFailurePresentation
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ReplaceExecutionResult Direct(ReplaceOperationException failure)
	{
		if (failure == null)
		{
			throw new ArgumentNullException("failure");
		}
		ReplaceExecutionResult result = ReplaceExecutionResult.Fail(CauseAndAction(failure.ReasonCode), failure);
		ApplyEvidence(result, failure, UserOutcomeKind.ActionRequired);
		return result;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ReplaceExecutionResult Recovered(ReplaceOperationException failure)
	{
		if (failure == null)
		{
			throw new ArgumentNullException("failure");
		}
		ReplaceExecutionResult result = ReplaceExecutionResult.Fail(Cause(failure.ReasonCode) + " 本次替换结果未应用，原文档已恢复。" + Action(failure.ReasonCode), failure);
		ApplyEvidence(result, failure, UserOutcomeKind.FailedButRecovered);
		return result;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ReplaceExecutionResult RecoveryRequired(ReplaceOperationException failure, string recoveryGuidance, string recoveryId, Exception rollbackError)
	{
		if (failure == null)
		{
			throw new ArgumentNullException("failure");
		}
		string message = Cause(failure.ReasonCode) + " 文档回滚后未能确认完整恢复。请立即关闭当前文档并选择“不保存”，避免保留不一致内容。" + (string.IsNullOrWhiteSpace(recoveryGuidance) ? string.Empty : ("\r\n" + recoveryGuidance));
		Exception error = ((rollbackError == null) ? ((Exception)failure) : ((Exception)new AggregateException(failure, rollbackError)));
		ReplaceExecutionResult replaceExecutionResult = ReplaceExecutionResult.Fail(message, error);
		replaceExecutionResult.RecoveryId = recoveryId;
		ApplyEvidence(replaceExecutionResult, failure, UserOutcomeKind.RecoveryRequired);
		return replaceExecutionResult;
	}

	public static string CauseAndAction(ReplaceFailureReasonCode reasonCode)
	{
		return Cause(reasonCode) + Action(reasonCode);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string Cause(ReplaceFailureReasonCode reasonCode)
	{
		return reasonCode switch
		{
			ReplaceFailureReasonCode.RuleConfigurationInvalid => "当前替换条目设置相互冲突或不完整。", 
			ReplaceFailureReasonCode.RegularExpressionInvalid => "当前方案中存在无效的正则表达式。", 
			ReplaceFailureReasonCode.OverlappingTargets => "同一条目产生了相互重叠的替换目标，无法安全执行。", 
			ReplaceFailureReasonCode.WildcardExpressionInvalid => "当前方案中的 WPS 通配符无效，或当前 WPS 不支持该表达式。", 
			ReplaceFailureReasonCode.RuleStoreUnavailable => "当前替换方案暂时无法读取。", 
			ReplaceFailureReasonCode.RecoveryProtectionUnavailable => "本次替换未能建立文档保护。", 
			ReplaceFailureReasonCode.VerificationFailed => "替换结果没有通过范围和数量检查。", 
			ReplaceFailureReasonCode.NoExecutableItems => "当前替换方案没有启用的有效条目。", 
			ReplaceFailureReasonCode.TableBoundaryUnsafe => "替换目标跨越表格单元格边界，为避免破坏表格结构已停止。", 
			ReplaceFailureReasonCode.ScopeUnavailable => "无法确定本次替换应处理全文还是当前选区。", 
			ReplaceFailureReasonCode.NoActiveDocument => "当前没有可替换的文档。", 
			ReplaceFailureReasonCode.UndoUnavailable => "Word/WPS 未能建立本次替换所需的撤销保护。", 
			ReplaceFailureReasonCode.HostOperationFailed => "Word/WPS 未能执行当前替换条目。", 
			ReplaceFailureReasonCode.TargetChanged => "文档内容在规划后发生变化，原替换目标已经失效。", 
			ReplaceFailureReasonCode.FormatConditionInvalid => "仅修改格式的条目缺少有效的查找格式。", 
			ReplaceFailureReasonCode.CaptureGroupInvalid => "替换内容引用了不存在或不完整的捕获组。", 
			ReplaceFailureReasonCode.RuleRecoveryFailed => "替换方案损坏且未能恢复默认值，原配置没有被覆盖。", 
			ReplaceFailureReasonCode.ZeroLengthMatch => "当前表达式可能匹配空内容，无法安全替换。", 
			_ => "一键替换遇到未预期的问题。", 
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string Action(ReplaceFailureReasonCode reasonCode)
	{
		switch (reasonCode)
		{
		case ReplaceFailureReasonCode.RecoveryProtectionUnavailable:
			return "请检查磁盘空间和恢复目录权限后再试。";
		case ReplaceFailureReasonCode.RuleConfigurationInvalid:
		case ReplaceFailureReasonCode.RegularExpressionInvalid:
		case ReplaceFailureReasonCode.ZeroLengthMatch:
		case ReplaceFailureReasonCode.CaptureGroupInvalid:
		case ReplaceFailureReasonCode.FormatConditionInvalid:
			return "请打开“方案设置”检查对应条目的查找和替换内容后再试。";
		case ReplaceFailureReasonCode.UndoUnavailable:
		case ReplaceFailureReasonCode.ScopeUnavailable:
		case ReplaceFailureReasonCode.HostOperationFailed:
			return "请保存并重新打开文档后再试。";
		case ReplaceFailureReasonCode.NoExecutableItems:
			return "请打开“方案设置”，添加或启用至少一个有效条目。";
		case ReplaceFailureReasonCode.WildcardExpressionInvalid:
			return "请打开“方案设置”检查通配符语法，或改用正则表达式。";
		case ReplaceFailureReasonCode.TableBoundaryUnsafe:
		case ReplaceFailureReasonCode.OverlappingTargets:
			return "请缩小查找范围或拆分该条目后再试。";
		case ReplaceFailureReasonCode.TargetChanged:
			return "请等待当前编辑结束后重新执行。";
		case ReplaceFailureReasonCode.VerificationFailed:
			return "请重新打开文档后重试；如果仍然失败，请将日志发送给开发人员。";
		case ReplaceFailureReasonCode.NoActiveDocument:
			return "请先打开文档后再试。";
		case ReplaceFailureReasonCode.RuleStoreUnavailable:
		case ReplaceFailureReasonCode.RuleRecoveryFailed:
			return "请打开“方案设置”，重新保存或新建一个方案后再试。";
		default:
			return "请重新打开文档后再试；如果仍然失败，请将日志发送给开发人员。";
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ApplyEvidence(ReplaceExecutionResult result, ReplaceOperationException failure, UserOutcomeKind outcome)
	{
		result.OutcomeKind = outcome;
		result.FailureReasonCode = "replace." + failure.ReasonCode.ToString().ToLowerInvariant();
		result.FailureStage = failure.Stage.ToString().ToLowerInvariant();
	}
}
