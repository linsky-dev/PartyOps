using System;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using DocumentRepository.Models;
using DocumentRepository.Models.Formatting;
using DocumentRepository.Models.Tasks;

namespace DocumentRepository.Services.Formatting.Failures;

public static class FormatFailurePresentation
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static CommandResult ToCommandResult(FormatOperationException failure, string taskId = null, string recoveryGuidance = null, string recoveryId = null)
	{
		if (failure != null)
		{
			if (failure.SafetyDisposition == DocumentSafetyDisposition.PendingRecovery)
			{
				throw new InvalidOperationException("回滚结论尚未产生，禁止提前生成用户结果。");
			}
			string text = UserMessage(failure, taskId);
			if (failure.SafetyDisposition == DocumentSafetyDisposition.RecoveryUnconfirmed && !string.IsNullOrWhiteSpace(recoveryGuidance))
			{
				text = text + "\r\n" + recoveryGuidance.Trim();
			}
			CommandResult commandResult;
			if (failure.ReasonCode == FormatFailureReasonCode.Cancelled)
			{
				commandResult = CommandResult.CancelledResult(text);
				commandResult.OutcomeKind = UserOutcomeKind.Cancelled;
				return commandResult;
			}
			commandResult = CommandResult.FailResult(text, failure);
			if (failure.ReasonCode != FormatFailureReasonCode.TaskAlreadyRunning)
			{
				if (failure.SafetyDisposition == DocumentSafetyDisposition.RecoveryUnconfirmed || failure.ReasonCode == FormatFailureReasonCode.RecoveryRequired)
				{
					commandResult.OutcomeKind = UserOutcomeKind.RecoveryRequired;
					commandResult.RecoveryId = recoveryId;
				}
				else if (failure.SafetyDisposition != DocumentSafetyDisposition.RecoveredVerified && failure.ReasonCode != FormatFailureReasonCode.RollbackVerified)
				{
					if (!IsActionRequired(failure.ReasonCode))
					{
						commandResult.OutcomeKind = UserOutcomeKind.Failed;
					}
					else
					{
						commandResult.OutcomeKind = UserOutcomeKind.ActionRequired;
					}
				}
				else
				{
					commandResult.OutcomeKind = UserOutcomeKind.FailedButRecovered;
				}
			}
			else
			{
				commandResult.OutcomeKind = UserOutcomeKind.Busy;
			}
			return commandResult;
		}
		throw new ArgumentNullException("failure");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string UserMessage(FormatOperationException failure, string taskId = null)
	{
		if (failure != null)
		{
			if (failure.SafetyDisposition == DocumentSafetyDisposition.PendingRecovery)
			{
				throw new InvalidOperationException("回滚结论尚未产生，禁止提前生成用户文案。");
			}
			if (failure.SafetyDisposition != DocumentSafetyDisposition.RecoveryUnconfirmed && failure.ReasonCode != FormatFailureReasonCode.RecoveryRequired)
			{
				bool flag = failure.SafetyDisposition == DocumentSafetyDisposition.RecoveredVerified;
				string text;
				switch (failure.ReasonCode)
				{
				case FormatFailureReasonCode.DocumentReadOnly:
					text = "当前文档处于只读状态。请创建可编辑副本后继续，原文件不会被修改。";
					break;
				case FormatFailureReasonCode.Cancelled:
					text = (flag ? "已取消排版，本次修改已撤销，原文档已恢复。" : "已取消排版，文档未发生变化。");
					break;
				case FormatFailureReasonCode.BuiltInDefaultInvalid:
					text = "内置排版参数无法使用。请重新启动 Word/WPS；如果仍然失败，请反馈问题编号。";
					break;
				case FormatFailureReasonCode.RecoveryDiskSpaceInsufficient:
					text = "磁盘剩余空间不足，无法创建本次恢复副本。请清理磁盘空间，或使用备用位置继续。";
					break;
				case FormatFailureReasonCode.SelectionOutsideMainStory:
					text = "当前选择的内容不在文档正文中。请重新选择正文段落，或取消选区后使用全文排版。";
					break;
				case FormatFailureReasonCode.ImageObjectChanged:
					text = RecoveredMessage("图片数量或图片对象出现异常变化");
					break;
				case FormatFailureReasonCode.RollbackVerified:
					text = "本次排版未应用，原文档已恢复。请重新打开文档后再试；如果仍然失败，请反馈问题编号。";
					break;
				case FormatFailureReasonCode.UndoUnsupported:
				case FormatFailureReasonCode.UndoStartFailed:
					text = "Word/WPS 没有成功建立本次排版的撤销记录，因此排版尚未开始。请关闭其他弹窗，重新打开文档后再试。";
					break;
				case FormatFailureReasonCode.ConfigFieldInvalid:
				case FormatFailureReasonCode.PageGeometryInvalid:
				case FormatFailureReasonCode.FontParameterInvalid:
					text = ParameterMessage(failure.ParameterField);
					break;
				case FormatFailureReasonCode.TableStructureChanged:
					text = RecoveredMessage("表格结构出现异常变化");
					break;
				case FormatFailureReasonCode.DocumentStructureUnreadable:
					text = (flag ? "Word/WPS 没有完整返回当前文档的结构信息，本次排版结果未保留，原文档已恢复。请保存文档，关闭后重新打开，再进行排版。" : "Word/WPS 没有完整返回当前文档的结构信息，本次排版未开始。请保存文档，关闭后重新打开，再进行排版。");
					break;
				case FormatFailureReasonCode.TaskAlreadyRunning:
					text = "当前已有功能正在执行，请等待当前任务完成。";
					break;
				case FormatFailureReasonCode.EditableCopyFailed:
					text = "未能创建可编辑副本。请先使用“另存为”保存到可写位置，再重新排版。";
					break;
				case FormatFailureReasonCode.PlanInvariantViolation:
					text = "排版计划无法安全执行，本次没有保留排版结果。请重新打开文档后再试；如果仍然失败，请反馈问题编号。";
					break;
				case FormatFailureReasonCode.RecoveryManifestWriteFailed:
					text = "恢复副本已经准备，但无法完成安全登记。当前文档尚未修改，可以仅使用撤销保护继续，或取消本次排版。";
					break;
				case FormatFailureReasonCode.TableContentChanged:
					text = RecoveredMessage("表格文字内容出现异常变化");
					break;
				case FormatFailureReasonCode.SelectionBoundaryUnreliable:
					text = "无法确定当前选区的完整段落边界。请重新选择完整段落，或改用全文排版。";
					break;
				case FormatFailureReasonCode.OutsideSelectionChanged:
					text = "检测到选区外的内容或格式也发生了变化，本次选中排版未保留，原文档已恢复。请重新选择完整段落后再试，或改用全文排版。";
					break;
				case FormatFailureReasonCode.OriginalTextChanged:
					text = RecoveredMessage("正文内容出现异常变化");
					break;
				case FormatFailureReasonCode.RecoveryRootUnavailable:
					text = "默认备份位置暂时不可用。可以使用备用位置继续，原文档仍会先备份再修改。";
					break;
				case FormatFailureReasonCode.SelectionEmpty:
					text = "请先选中正文中的一段或多段文字；若要排版整篇文档，请取消选区后再点击“一键排版”。";
					break;
				case FormatFailureReasonCode.HostBusy:
					text = "Word/WPS 正在处理其他操作。请关闭未完成的弹窗或等待当前操作结束后重试。";
					break;
				case FormatFailureReasonCode.DocumentChangedAfterPlan:
					text = (flag ? "排版准备后文档又发生了变化，本次排版结果未保留，原文档已恢复。请停止输入或等待同步完成后重新排版。" : "排版计划生成后，文档内容又发生了变化，本次排版尚未开始。请停止输入或等待同步完成后重新排版。");
					break;
				case FormatFailureReasonCode.HostDocumentUnavailable:
					text = "当前文档已经关闭、切换或暂时不可用。请重新打开需要排版的文档后再试。";
					break;
				case FormatFailureReasonCode.RecoverySourceChanged:
					text = "Word/WPS 在准备备份时检测到文档仍在变化，本次排版尚未开始。请等待保存、同步或输入完成后重新排版。";
					break;
				case FormatFailureReasonCode.DocumentProtected:
					text = "当前文档禁止修改。请在 Word/WPS 的“审阅”中找到“限制编辑”或“保护文档”，停止保护后重新排版。";
					break;
				case FormatFailureReasonCode.CredentialBindingMismatch:
					text = (flag ? "本次排版的安全凭证不匹配，排版结果未保留，原文档已恢复。" : "本次排版的安全凭证不匹配，已在修改文档前停止。");
					break;
				case FormatFailureReasonCode.NoActiveDocument:
					text = "请先新建或打开一个 Word/WPS 文档，再点击“一键排版”。";
					break;
				case FormatFailureReasonCode.RecoveryCopyVerificationFailed:
					text = "已尝试创建恢复副本，但无法确认副本完整。当前文档尚未修改，可以仅使用撤销保护继续，或取消本次排版。";
					break;
				case FormatFailureReasonCode.RecoveryQuotaExceeded:
					text = "恢复副本空间已满且暂时无法清理。请清理磁盘空间，或使用备用位置继续。";
					break;
				default:
					text = (flag ? "本次排版未应用，原文档已恢复。请重新打开文档后再试；如果仍然失败，请反馈问题编号。" : "本次排版未能开始，文档没有保留本次排版结果。请重新打开文档后再试；如果仍然失败，请反馈问题编号。");
					break;
				}
				if (NeedsIssueCode(failure.ReasonCode))
				{
					text = text + "\r\n问题编号：" + CreateIssueCode(taskId, failure.ReasonCode, failure.Stage);
				}
				return text;
			}
			return "排版未能完成，而且无法确认文档已经完整恢复。请立即关闭当前文档，并在 Word/WPS 询问时选择“不保存”；然后重新打开原文件，或使用恢复助手查找排版前的副本。";
		}
		throw new ArgumentNullException("failure");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string CreateIssueCode(string taskId, FormatFailureReasonCode reasonCode, FormatFailureStage stage)
	{
		string s = (taskId ?? "unknown") + "|" + reasonCode.ToString() + "|" + stage;
		using SHA256 sHA = SHA256.Create();
		byte[] array = sHA.ComputeHash(Encoding.UTF8.GetBytes(s));
		StringBuilder stringBuilder = new StringBuilder("F-");
		for (int i = 0; i < 3; i++)
		{
			stringBuilder.Append(array[i].ToString("X2"));
		}
		return stringBuilder.ToString();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string RecoveredMessage(string problem)
	{
		return "排版过程中检测到" + problem + "，本次排版结果未保留，原文档已恢复。建议先把文档另存为新的 DOCX 文件后重试；如果仍然出现，请把原文档发给我们检查。";
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string ParameterMessage(FormatParameterField field)
	{
		return field switch
		{
			FormatParameterField.PageNumberFontSize => "页码字号参数为空或不可用。请打开自定义参数，重新选择页码字号。", 
			FormatParameterField.PageNumberFont => "页码字体参数为空或不可用。请打开自定义参数，重新选择页码字体。", 
			FormatParameterField.BodyFontSize => "正文字号参数为空或不可用。请打开自定义参数，重新选择正文字号。", 
			FormatParameterField.PageMargins => "页面边距超过了当前纸张可用范围。请打开自定义参数，调整上下左右页边距后重试。", 
			FormatParameterField.TableStyle => "表格样式参数不完整。请打开自定义参数，检查表格设置后重试。", 
			FormatParameterField.PageGeometry => "页面尺寸设置不合理。请打开自定义参数，检查纸张大小和页面尺寸后重试。", 
			FormatParameterField.EnglishNumberFont => "英文和数字字体参数为空或不可用。请打开自定义参数，重新选择该字体。", 
			FormatParameterField.BodyFont => "正文字体参数为空或不可用。请打开自定义参数，重新选择正文字体。", 
			_ => "当前排版参数不完整或无法使用。请打开自定义参数检查并保存后重试。", 
		};
	}

	private static bool NeedsIssueCode(FormatFailureReasonCode reasonCode)
	{
		if (reasonCode != FormatFailureReasonCode.BuiltInDefaultInvalid && reasonCode != FormatFailureReasonCode.PlanInvariantViolation && reasonCode != FormatFailureReasonCode.RollbackVerified)
		{
			return reasonCode == FormatFailureReasonCode.UnexpectedFailure;
		}
		return true;
	}

	private static bool IsActionRequired(FormatFailureReasonCode reasonCode)
	{
		switch (reasonCode)
		{
		case FormatFailureReasonCode.NoActiveDocument:
		case FormatFailureReasonCode.DocumentProtected:
		case FormatFailureReasonCode.DocumentReadOnly:
		case FormatFailureReasonCode.EditableCopyFailed:
		case FormatFailureReasonCode.SelectionEmpty:
		case FormatFailureReasonCode.SelectionOutsideMainStory:
		case FormatFailureReasonCode.SelectionBoundaryUnreliable:
		case FormatFailureReasonCode.ConfigFieldInvalid:
		case FormatFailureReasonCode.UndoUnsupported:
		case FormatFailureReasonCode.UndoStartFailed:
		case FormatFailureReasonCode.RecoveryRootUnavailable:
		case FormatFailureReasonCode.RecoveryDiskSpaceInsufficient:
		case FormatFailureReasonCode.RecoveryQuotaExceeded:
		case FormatFailureReasonCode.RecoverySourceChanged:
		case FormatFailureReasonCode.RecoveryCopyVerificationFailed:
		case FormatFailureReasonCode.RecoveryManifestWriteFailed:
		case FormatFailureReasonCode.DocumentChangedAfterPlan:
		case FormatFailureReasonCode.DocumentStructureUnreadable:
		case FormatFailureReasonCode.HostBusy:
		case FormatFailureReasonCode.HostDocumentUnavailable:
		case FormatFailureReasonCode.PageGeometryInvalid:
		case FormatFailureReasonCode.FontParameterInvalid:
		case FormatFailureReasonCode.CredentialBindingMismatch:
			return true;
		default:
			return false;
		}
	}
}
