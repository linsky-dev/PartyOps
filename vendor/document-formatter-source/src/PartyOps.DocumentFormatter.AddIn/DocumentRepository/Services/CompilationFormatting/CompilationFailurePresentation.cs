using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Models.CompilationFormatting;
using DocumentRepository.Models.Formatting;

namespace DocumentRepository.Services.CompilationFormatting;

public static class CompilationFailurePresentation
{
	private const string MissingMarkerMessage = "该模版启用了汇编排版功能，未检测到识别标记。请切换模版或重新插入标记后排版。";

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static CommandResult ToCommandResult(CompilationFormatFailure failure, string recoveryGuidance = null)
	{
		if (failure == null)
		{
			failure = new CompilationFormatFailure(CompilationFormatFailureReasonCode.Unknown, CompilationFormatFailureStage.Unknown, DocumentSafetyDisposition.Unchanged);
		}
		if (failure.SafetyDisposition == DocumentSafetyDisposition.PendingRecovery)
		{
			throw new InvalidOperationException("恢复结论尚未产生，禁止向用户展示失败结果。");
		}
		CommandResult commandResult = ((failure.SafetyDisposition == DocumentSafetyDisposition.RecoveredVerified) ? PostWriteResult(recoveredVerified: true, recoveryGuidance) : ((failure.SafetyDisposition != DocumentSafetyDisposition.RecoveryUnconfirmed) ? PreWriteResult(failure.ReasonCode, failure.StructuredDetail) : PostWriteResult(recoveredVerified: false, recoveryGuidance)));
		commandResult.FailureReasonCode = failure.ReasonCode.ToString();
		commandResult.FailureStage = failure.Stage.ToString();
		return commandResult;
	}

	public static CommandResult PreWriteResult(CompilationFormatFailureReasonCode code, string structuredDetail = null)
	{
		CommandResult commandResult = ((code != CompilationFormatFailureReasonCode.UserCancelled && code != CompilationFormatFailureReasonCode.ExpandNotConfirmed && code != CompilationFormatFailureReasonCode.ProgressCancelled) ? CommandResult.FailResult(UserMessage(code, structuredDetail)) : CommandResult.CancelledResult(CancelMessage(code)));
		commandResult.FailureReasonCode = code.ToString();
		return commandResult;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static CommandResult PostWriteResult(bool recoveredVerified, string recoveryGuidance = null)
	{
		if (!recoveredVerified)
		{
			string text = "汇编排版未能完成，而且无法确认文档已经完整恢复。请立即关闭当前文档，并在 Word/WPS 询问时选择“不保存”；然后重新打开原文件。";
			if (!string.IsNullOrWhiteSpace(recoveryGuidance))
			{
				text = text + "\r\n" + recoveryGuidance.Trim();
			}
			CommandResult commandResult = CommandResult.FailResult(text);
			commandResult.FailureReasonCode = CompilationFormatFailureReasonCode.RecoveryUnconfirmed.ToString();
			commandResult.FailureStage = CompilationFormatFailureStage.Recovery.ToString();
			return commandResult;
		}
		CommandResult commandResult2 = CommandResult.FailResult("汇编排版执行中断，文档已自动恢复到排版前状态，内容未受影响。请重试；若反复失败，请检查文档结构后联系技术支持。");
		commandResult2.FailureReasonCode = CompilationFormatFailureReasonCode.ExecutionInterruptedRolledBack.ToString();
		return commandResult2;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string UserMessage(CompilationFormatFailureReasonCode code, string structuredDetail = null)
	{
		if (code == CompilationFormatFailureReasonCode.MarkerStructureInvalid && IsMissingMarkerDetail(structuredDetail))
		{
			return "该模版启用了汇编排版功能，未检测到识别标记。请切换模版或重新插入标记后排版。";
		}

		string message;
		switch (code)
		{
		case CompilationFormatFailureReasonCode.ManifestUnreadable:
			message = "该模版启用了汇编排版功能，未检测到识别标记。请切换模版或重新插入标记后排版。";
			break;
		case CompilationFormatFailureReasonCode.MalformedMarker:
			message = "检测到汇编标记没有独占一行。请在“@@汇编@@”后按回车，将文章标题移到下一行后重试。文档尚未修改。";
			break;
		case CompilationFormatFailureReasonCode.SeparatorConflict:
			message = "检测到文章之间的分隔符与插件记录不一致，为保护您原有的分页/分节设置，已停止执行。请检查后重试。";
			break;
		case CompilationFormatFailureReasonCode.UiServiceMissing:
			message = "汇编排版缺少用户交互服务，请通过功能入口重新发起。";
			break;
		case CompilationFormatFailureReasonCode.UnsupportedRegionMarker:
			message = "在表格、页眉页脚或其他不支持区域发现“@@汇编@@”标记，这些区域中的标记不会作为文章起点，请移除后重试。";
			break;
		case CompilationFormatFailureReasonCode.ExecutionInterruptedRolledBack:
			message = "汇编排版执行中断，文档已自动恢复到排版前状态，内容未受影响。请重试；若反复失败，请检查文档结构后联系技术支持。";
			break;
		case CompilationFormatFailureReasonCode.NonInteractiveMode:
			message = "汇编排版需要交互确认，当前处于非交互模式，已拒绝执行。";
			break;
		case CompilationFormatFailureReasonCode.VerificationFailed:
			message = "汇编排版结果未通过完整性验证，文档已恢复到排版前状态。";
			break;
		case CompilationFormatFailureReasonCode.ConfigInvalid:
			message = "汇编参数未通过校验，请打开“汇编参数”检查各项设置。";
			break;
		case CompilationFormatFailureReasonCode.FrontMatterRejected:
			message = "当前模板设置为拒绝前置内容，但第一个“@@汇编@@”标记之前存在内容。请移除前置内容或调整模板参数。";
			break;
		case CompilationFormatFailureReasonCode.TitlesUnreliable:
			message = "无法可靠读取部分文章的当前标题，目录更新已停止。请打开文档核实各篇标题后重试。";
			break;
		case CompilationFormatFailureReasonCode.SelectionSpansTocAndBody:
			message = "选区同时包含汇编目录和正文，请只选择目录区域后重试。";
			break;
		case CompilationFormatFailureReasonCode.ProtectionUnavailable:
			message = "无法在修改前建立可靠的恢复保护，已停止汇编排版。请先保存文档或检查恢复目录后重试。";
			break;
		case CompilationFormatFailureReasonCode.BoundaryBroken:
			message = "内部文章边界已损坏或丢失，请重新插入标记并执行全文汇编排版。";
			break;
		case CompilationFormatFailureReasonCode.SelectionCoversNoArticle:
			message = "选区未覆盖任何文章，请调整选择范围后重试。";
			break;
		case CompilationFormatFailureReasonCode.UndoUnavailable:
			message = "当前 Word/WPS 未能创建撤销事务，已在修改文档前停止汇编排版。";
			break;
		case CompilationFormatFailureReasonCode.UserCancelled:
		case CompilationFormatFailureReasonCode.ExpandNotConfirmed:
		case CompilationFormatFailureReasonCode.ProgressCancelled:
			return CancelMessage(code);
		case CompilationFormatFailureReasonCode.MarkerStructureInvalid:
			message = "文档中的“@@汇编@@”标记不符合要求。";
			break;
		case CompilationFormatFailureReasonCode.ScanFailed:
			message = "文档部分区域读取失败，无法确认是否遗漏标记。请检查文档后重试。";
			break;
		case CompilationFormatFailureReasonCode.InsufficientArticles:
			message = "全文汇编排版至少需要两篇文章（两个合法“@@汇编@@”标记）。请检查是否遗漏标记。";
			break;
		case CompilationFormatFailureReasonCode.RecoveryUnconfirmed:
			message = "汇编排版未能完成，而且无法确认文档已经完整恢复。请立即关闭当前文档，并在 Word/WPS 询问时选择“不保存”；然后重新打开原文件。";
			break;
		case CompilationFormatFailureReasonCode.TocSnapshotCorrupt:
			message = "文档保存的目录参数快照已损坏，无法更新目录。请重新执行全文汇编排版重建目录配置。";
			break;
		default:
			message = "汇编排版未能完成。请重试；若反复失败，请联系技术支持。";
			break;
		}

		if (!string.IsNullOrWhiteSpace(structuredDetail))
		{
			message = message + "（" + structuredDetail.Trim() + "）";
		}
		return message;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool IsMissingMarkerDetail(string structuredDetail)
	{
		if (!string.IsNullOrWhiteSpace(structuredDetail))
		{
			return structuredDetail.IndexOf("未找到合法标记", StringComparison.Ordinal) >= 0;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string CancelMessage(CompilationFormatFailureReasonCode code)
	{
		return code switch
		{
			CompilationFormatFailureReasonCode.ExpandNotConfirmed => "选区首尾截在文章内部，未确认扩展到完整范围，已取消且未修改文档。", 
			CompilationFormatFailureReasonCode.ProgressCancelled => "已按您的要求取消汇编排版，文档已恢复到排版前状态。", 
			_ => "已取消汇编排版，未对文档做任何修改。", 
		};
	}
}
