using System;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using DocumentRepository.Models.Rules;
using DocumentRepository.Services.Logging;

namespace DocumentRepository.Services.Rules;

public static class RuleStoreResetPrompt
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool ConfirmAndReset<T>(IWin32Window owner, RuleLoadResult<T> loadResult, string displayName, Func<RuleLoadResult<T>> resetAction)
	{
		if (loadResult != null && !loadResult.Usable)
		{
			if (resetAction != null)
			{
				string text = (string.IsNullOrWhiteSpace(displayName) ? "规则库" : displayName);
				if ((int)AppleMessageDialog.Show(owner, text + "的配置文件已损坏或无法读取，本次内容未做任何改动。\n\n是否重置为默认配置？重置前会自动备份原文件。", "规则库恢复", (MessageBoxButtons)4, (MessageBoxIcon)48) != 6)
				{
					return false;
				}
				RuleLoadResult<T> ruleLoadResult;
				try
				{
					ruleLoadResult = resetAction();
				}
				catch (Exception ex)
				{
					LogService.Warn("RuleStoreResetPrompt reset action failed. StoreId=" + loadResult.StoreId, ex);
					ShowResetFailure(owner);
					return false;
				}
				if (ruleLoadResult == null || !ruleLoadResult.Usable)
				{
					ShowResetFailure(owner);
					return false;
				}
				AppleMessageDialog.Show(owner, "已重置为默认配置。" + (string.IsNullOrWhiteSpace(ruleLoadResult.ArchivedPath) ? "" : ("\n原文件已备份：\n" + ruleLoadResult.ArchivedPath)), "规则库恢复", (MessageBoxButtons)0, (MessageBoxIcon)64);
				return true;
			}
			throw new ArgumentNullException("resetAction");
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ShowResetFailure(IWin32Window owner)
	{
		AppleMessageDialog.Show(owner, "重置未能完成，原文件保持未改动。\n请检查磁盘和权限后重试。", "规则库恢复", (MessageBoxButtons)0, (MessageBoxIcon)16);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ShowUnavailable(IWin32Window owner, RuleStoreUnavailableException exception)
	{
		AppleMessageDialog.Show(owner, (exception == null) ? "规则库已损坏或无法读取。" : exception.Message, "规则库恢复", (MessageBoxButtons)0, (MessageBoxIcon)48);
	}
}
