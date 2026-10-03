using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.RedHeader;
using DocumentRepository.Models.Tasks;

namespace DocumentRepository.Services.RedHeader;

public static class RedHeaderFailurePresentation
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static RedHeaderResult Direct(RedHeaderOperationException failure)
	{
		if (failure == null)
		{
			throw new ArgumentNullException("failure");
		}
		RedHeaderResult result = RedHeaderResult.Fail(Cause(failure.ReasonCode) + Action(failure.ReasonCode), failure);
		ApplyEvidence(result, failure, UserOutcomeKind.ActionRequired);
		return result;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static RedHeaderResult Recovered(RedHeaderOperationException failure)
	{
		if (failure == null)
		{
			throw new ArgumentNullException("failure");
		}
		RedHeaderResult result = RedHeaderResult.Fail(Cause(failure.ReasonCode) + " 本次套红结果未应用，原文档已恢复。" + Action(failure.ReasonCode), failure);
		ApplyEvidence(result, failure, UserOutcomeKind.FailedButRecovered);
		return result;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static RedHeaderResult RecoveryRequired(RedHeaderOperationException failure, string recoveryGuidance, string recoveryId)
	{
		if (failure == null)
		{
			throw new ArgumentNullException("failure");
		}
		RedHeaderResult redHeaderResult = RedHeaderResult.Fail(Cause(failure.ReasonCode) + " 文档回滚后未能确认完整恢复。请立即关闭当前文档并选择“不保存”，避免保留不一致内容。" + (string.IsNullOrWhiteSpace(recoveryGuidance) ? string.Empty : ("\r\n" + recoveryGuidance)), failure);
		redHeaderResult.RecoveryId = recoveryId;
		ApplyEvidence(redHeaderResult, failure, UserOutcomeKind.RecoveryRequired);
		return redHeaderResult;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string Cause(RedHeaderFailureReasonCode reasonCode)
	{
		return reasonCode switch
		{
			RedHeaderFailureReasonCode.OriginalObjectReduced => "套红过程中检测到原有图片、浮动对象或表格数量减少。", 
			RedHeaderFailureReasonCode.RecoveryProtectionUnavailable => "本次套红未能建立文档恢复保护。", 
			RedHeaderFailureReasonCode.TemplateInvalid => "当前红头模板参数不完整或超出允许范围。", 
			RedHeaderFailureReasonCode.AnalysisUnreadable => "无法可靠读取当前文档的节、页数或页面尺寸。", 
			RedHeaderFailureReasonCode.ImprintPaginationFailed => "无法确定版记应落在哪一页或哪个位置。", 
			RedHeaderFailureReasonCode.PlanBindingInvalid => "套红计划与当前文档或模板已经不一致。", 
			RedHeaderFailureReasonCode.OriginalTextChanged => "套红过程中检测到原正文内容发生变化。", 
			RedHeaderFailureReasonCode.TemplateStoreUnavailable => "当前红头模板库暂时无法读取。", 
			RedHeaderFailureReasonCode.PageGeometryInvalid => "当前页面尺寸或页边距无法生成安全的红头、红线和版记位置。", 
			RedHeaderFailureReasonCode.TemplateMissing => "当前没有可用的红头模板。", 
			RedHeaderFailureReasonCode.ConfigMissing => "当前一键排版参数没有加载完成。", 
			RedHeaderFailureReasonCode.VerificationFailed => "套红结果没有通过完整性检查。", 
			RedHeaderFailureReasonCode.HeaderLayoutFailed => "发文机关文字为空，或字号、字体、缩放和单行布局无法应用。", 
			RedHeaderFailureReasonCode.RedLineCreationFailed => "红线未能安全创建或锚定到红线占位位置。", 
			RedHeaderFailureReasonCode.TemplateRecoveryFailed => "红头模板库损坏且未能恢复默认值，原配置没有被覆盖。", 
			RedHeaderFailureReasonCode.NoActiveDocument => "当前没有可套红的文档。", 
			RedHeaderFailureReasonCode.HostGenerationFailed => "Word/WPS 未能完成红头对象生成。", 
			_ => "一键套红遇到未预期的问题。", 
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string Action(RedHeaderFailureReasonCode reasonCode)
	{
		switch (reasonCode)
		{
		case RedHeaderFailureReasonCode.RedLineCreationFailed:
			return "请保存并重新打开文档；如果仍然失败，可在模板设置中改用普通红线后重试。";
		case RedHeaderFailureReasonCode.AnalysisUnreadable:
			return "请保存并重新打开文档，确认文档能正常分页后再试。";
		case RedHeaderFailureReasonCode.NoActiveDocument:
			return "请先打开文档后再试。";
		case RedHeaderFailureReasonCode.OriginalTextChanged:
		case RedHeaderFailureReasonCode.OriginalObjectReduced:
		case RedHeaderFailureReasonCode.VerificationFailed:
			return "请重新打开原文档后重试；如果仍然失败，请将文档副本和日志发送给开发人员。";
		case RedHeaderFailureReasonCode.TemplateMissing:
		case RedHeaderFailureReasonCode.TemplateInvalid:
		case RedHeaderFailureReasonCode.HeaderLayoutFailed:
			return "请打开“模板设置”，检查发文机关、字体、字号、缩放和版记参数后再试。";
		case RedHeaderFailureReasonCode.ConfigMissing:
			return "请打开“自定义参数”，确认当前排版模板后再试。";
		case RedHeaderFailureReasonCode.ImprintPaginationFailed:
			return "请确认文档能够正常分页，删除文末异常空白后重新执行。";
		case RedHeaderFailureReasonCode.PlanBindingInvalid:
			return "请等待当前编辑结束后重新执行套红。";
		case RedHeaderFailureReasonCode.TemplateStoreUnavailable:
		case RedHeaderFailureReasonCode.TemplateRecoveryFailed:
			return "请打开“模板设置”，重新保存或新建一个模板后再试。";
		case RedHeaderFailureReasonCode.RecoveryProtectionUnavailable:
			return "请检查磁盘空间和恢复目录权限后再试。";
		case RedHeaderFailureReasonCode.PageGeometryInvalid:
			return "请检查页面大小和页边距，或先执行一键排版后再套红。";
		default:
			return "请重新打开文档后再试；如果仍然失败，请将日志发送给开发人员。";
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ApplyEvidence(RedHeaderResult result, RedHeaderOperationException failure, UserOutcomeKind outcome)
	{
		result.OutcomeKind = outcome;
		result.FailureReasonCode = "redheader." + failure.ReasonCode.ToString().ToLowerInvariant();
		result.FailureStage = failure.Stage.ToString().ToLowerInvariant();
	}
}
