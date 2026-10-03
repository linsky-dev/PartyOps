using System.Runtime.CompilerServices;
using DocumentRepository.Models.Safety;

namespace DocumentRepository.Services.Ui;

internal static class UserInteractionText
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static string ResolveWarning(string userMessageKey)
	{
		return userMessageKey switch
		{
			"warn.format.table" => "部分表格外观未完全符合设置，表格内容和结构未受影响", 
			"warn.documentgrid.compat" => "文档网格使用了宿主兼容模式，排版结果仍然有效", 
			"warn.format.quality" => "部分格式可能未完全符合设置，可先使用，必要时手工微调或反馈", 
			"warn.rule-store.memory" => "参数文件暂时无法保存，本次已使用默认参数继续", 
			"warn.format.image" => "部分图片外观未完全符合设置，图片对象未丢失", 
			"warn.format.paragraph" => "部分段落样式未完全符合设置，文字内容未受影响", 
			"warn.format.page-number" => "页码部分格式未完全符合设置，可在页脚中手工调整", 
			"warn.format.english-number-font" => "部分英文或数字字体未完全套用，可手工调整", 
			"warn.rule-store.recovered" => "参数文件曾损坏，已恢复为默认参数，原文件已安全备份", 
			_ => "存在宿主兼容性差异，结果仍然有效", 
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static string ResolveTitle(UserConfirmationRequest request)
	{
		switch (request.Kind)
		{
		case UserConfirmationKind.AuthorizationAdmission:
			return "需要确认";
		case UserConfirmationKind.ReadOnlyCreateEditableCopy:
			return "文档为只读";
		case UserConfirmationKind.ProtectedRetryAfterUnprotect:
			return "文档受保护";
		case UserConfirmationKind.UnsavedContentSaveContinue:
		case UserConfirmationKind.UnsavedContentUndoOnly:
			return "文档尚未保存";
		case UserConfirmationKind.RenameSaveContinue:
			return "命名前需要保存";
		case UserConfirmationKind.RecoveryFallbackUndoOnly:
			return "无法创建恢复副本";
		case UserConfirmationKind.RecoveryFallbackDirectory:
			return "使用备用恢复目录";
		case UserConfirmationKind.RuleStoreResetToDefault:
			return "规则文件损坏";
		default:
			return "需要确认";
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static string ResolveMessage(UserConfirmationRequest request)
	{
		return request.Kind switch
		{
			UserConfirmationKind.RenameSaveContinue => "命名会改变文件身份，需要先保存当前文档。选择「保存并继续」会打开保存对话框，保存成功后自动继续命名。", 
			UserConfirmationKind.UnsavedContentSaveContinue => "当前文档还没有保存。选择「保存并继续」会先打开保存对话框；选择「仅用撤销保护继续」则本次修改没有独立文件副本，仅由撤销功能保护。", 
			UserConfirmationKind.ReadOnlyCreateEditableCopy => "当前文档是只读的，不能直接修改。可以创建一个可编辑副本后继续，原件不会被改动。", 
			UserConfirmationKind.RecoveryFallbackDirectory => "主恢复目录暂时不可用或空间不足。可以把本次恢复副本保存到受控备用目录后继续，文档仍会先备份再修改。", 
			UserConfirmationKind.RuleStoreResetToDefault => "规则文件已损坏，无法读取。可以先把损坏文件备份后恢复为默认配置，或取消本次操作。不会直接覆盖原文件。", 
			UserConfirmationKind.AuthorizationAdmission => "执行前需要你的确认。是否继续？", 
			UserConfirmationKind.ProtectedRetryAfterUnprotect => "当前文档处于保护状态，插件不会猜测密码或绕过保护。可在「审阅」里的限制编辑/保护文档区域停止保护；完成后再点一次本功能。", 
			UserConfirmationKind.RecoveryFallbackUndoOnly => "无法为本次修改创建文件恢复副本。可以仅用撤销保护继续，或取消本次操作。文档当前是安全的。", 
			UserConfirmationKind.UnsavedContentUndoOnly => "继续吗？", 
			_ => "执行前需要你的确认。是否继续？", 
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static string ResolvePrimaryAction(UserConfirmationRequest request)
	{
		return request.Kind switch
		{
			UserConfirmationKind.ReadOnlyCreateEditableCopy => "创建可编辑副本", 
			UserConfirmationKind.RecoveryFallbackUndoOnly => "仅用撤销保护继续", 
			UserConfirmationKind.RuleStoreResetToDefault => "备份并恢复默认", 
			UserConfirmationKind.AuthorizationAdmission => "继续", 
			UserConfirmationKind.ProtectedRetryAfterUnprotect => "我知道了", 
			UserConfirmationKind.RecoveryFallbackDirectory => "使用备用目录继续", 
			UserConfirmationKind.RenameSaveContinue => "保存并继续", 
			UserConfirmationKind.UnsavedContentSaveContinue => "保存并继续", 
			_ => "继续", 
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static string ResolveSecondaryAction(UserConfirmationRequest request)
	{
		return request.Kind switch
		{
			UserConfirmationKind.RuleStoreResetToDefault => "打开设置", 
			UserConfirmationKind.UnsavedContentSaveContinue => "仅用撤销保护继续", 
			_ => null, 
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static string ResolveCancelAction(UserConfirmationRequest request)
	{
		if (!request.OfferCancel)
		{
			return null;
		}
		return "取消";
	}
}
