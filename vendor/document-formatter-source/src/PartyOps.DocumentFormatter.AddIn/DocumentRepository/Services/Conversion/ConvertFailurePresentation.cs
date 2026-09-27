using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Models.Conversion;
using DocumentRepository.Models.Tasks;

namespace DocumentRepository.Services.Conversion;

public static class ConvertFailurePresentation
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ConvertResult ToResult(ConvertOperationException failure)
	{
		if (failure == null)
		{
			throw new ArgumentNullException("failure");
		}
		ConvertResult convertResult = ConvertResult.Fail(UserMessage(failure.ReasonCode));
		convertResult.FailureReasonCode = "convert." + ToToken(failure.ReasonCode);
		convertResult.FailureStage = failure.Stage.ToString().ToLowerInvariant();
		convertResult.OutcomeKind = (IsActionRequired(failure.ReasonCode) ? UserOutcomeKind.ActionRequired : UserOutcomeKind.Failed);
		return convertResult;
	}

	public static CommandResult ToCommandResult(ConvertOperationException failure)
	{
		ConvertResult convertResult = ToResult(failure);
		CommandResult commandResult = CommandResult.FailResult(convertResult.Message, failure);
		commandResult.OutcomeKind = convertResult.OutcomeKind;
		commandResult.FailureReasonCode = convertResult.FailureReasonCode;
		commandResult.FailureStage = convertResult.FailureStage;
		return commandResult;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string UserMessage(ConvertFailureReasonCode reasonCode)
	{
		return reasonCode switch
		{
			ConvertFailureReasonCode.PageRangeOrderInvalid => "页码范围不正确。起始页必须不大于结束页，并且页码要从 1 开始。", 
			ConvertFailureReasonCode.ConfigurationUnavailable => "转换参数暂时无法读取。请打开“转换设置”，恢复默认参数或重新保存参数后再试。", 
			ConvertFailureReasonCode.ScannedPdfUnsupported => "本插件暂不支持扫描型、图片型PDF转换（该类型需云端文字识别，违背本插件所有功能单机运行原则）", 
			ConvertFailureReasonCode.PageRangeFormatInvalid => "页码范围格式不正确。连续页请填写“2-8”，指定页请填写“1,3,9”。", 
			ConvertFailureReasonCode.ImageResourceBudgetExceeded => "本次图片转换需要的内存过大。请改用逐页图片、减少页数或降低图片清晰度后再试。", 
			ConvertFailureReasonCode.UnsupportedFormat => "当前转换方式无法识别。请打开“转换设置”重新选择 DOCX、PDF、TXT 或图片。", 
			ConvertFailureReasonCode.DocumentNeedsSave => "当前文档还没有保存位置。一键转换需要先确定输出位置，请先保存文档后再试。", 
			ConvertFailureReasonCode.OutputValidationFailed => "输出文件没有通过完整性检查，本次结果未提交。请关闭同名输出文件、检查磁盘空间后重试。", 
			ConvertFailureReasonCode.PageSelectionNotNumeric => "指定页码中包含非数字内容。请使用类似“1,3,9”的格式后再试。", 
			ConvertFailureReasonCode.OutputFolderInvalid => "保存位置无效或已经不存在。请在“转换设置”中重新选择输出目录后再试。", 
			ConvertFailureReasonCode.ImagePageReadFailed => "Word/WPS 未能读取需要转换的页面，未生成完整图片。请确认文档能正常翻页；长图可改为逐页图片后重试。", 
			ConvertFailureReasonCode.PageOutsideDocument => "指定页码超过了当前文档页数。请重新填写文档实际存在的页码。", 
			ConvertFailureReasonCode.HostExportFailed => "Word/WPS 未能导出当前文档。原文档内容没有被排版修改；请保存并重新打开文档后再试。", 
			ConvertFailureReasonCode.DocumentAnalysisUnavailable => "无法读取当前文档的页数或文件状态。请确认文档能正常翻页，保存后重新打开再试。", 
			ConvertFailureReasonCode.DocxReplacementFailed => "替换为 DOCX 没有完整完成。请先检查当前文档标题栏和原文件是否存在，不要立即覆盖旧文件；确认后改用“另存为新文件”重试。", 
			ConvertFailureReasonCode.NoActiveDocument => "当前没有可转换的文档。请先打开文档后再试。", 
			ConvertFailureReasonCode.PageSelectionEmpty => "尚未填写要转换的页码。请填写类似“1,3,9”的页码后再试。", 
			ConvertFailureReasonCode.PageCountUnavailable => "无法获取当前文档页数。请确认文档可以正常分页，保存并重新打开后再试。", 
			ConvertFailureReasonCode.TaskContextUnavailable => "转换任务没有正常启动，尚未生成输出文件。请关闭并重新打开文档后再试。", 
			ConvertFailureReasonCode.PdfEncryptedOrProtected => "该 PDF 已加密或受密码保护，暂时无法转换。请先解除密码，或另存为未加密的 PDF 后再试。", 
			ConvertFailureReasonCode.OutputWriteFailed => "输出文件无法写入。请关闭同名文件，并检查保存目录是否可写、磁盘空间是否充足后再试。", 
			_ => "一键转换暂时未能完成。原文档内容没有被排版修改；请保存并重新打开文档后再试。如果仍然失败，请将日志发送给开发人员。", 
		};
	}

	private static bool IsActionRequired(ConvertFailureReasonCode reasonCode)
	{
		if (reasonCode != ConvertFailureReasonCode.HostExportFailed && reasonCode != ConvertFailureReasonCode.OutputValidationFailed)
		{
			return reasonCode != ConvertFailureReasonCode.UnexpectedFailure;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string ToToken(ConvertFailureReasonCode reasonCode)
	{
		return reasonCode.ToString().Replace("_", "-").ToLowerInvariant();
	}
}
