using System;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using DocumentRepository.Models.Safety;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.RedHeader;
using DocumentRepository.Services.Replace;

namespace DocumentRepository.Services.Ui;

public static class RuleStoreRecoveryUi
{
	public enum Resolution
	{
		Retry,
		Cancelled,
		Failed
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static Resolution ResolveInteractive(IUserInteractionPort port, string displayName, Func<bool> resetAction, Func<bool> settingsOpener)
	{
		if (port != null)
		{
			if (resetAction == null)
			{
				throw new ArgumentNullException("resetAction");
			}
			UserConfirmationRequest request = new UserConfirmationRequest
			{
				Kind = UserConfirmationKind.RuleStoreResetToDefault,
				OfferCancel = true
			};
			UserChoiceResult userChoiceResult = port.Ask(request);
			LogService.Info("rule-store-recovery choice=" + userChoiceResult.ToString() + ", store=" + displayName);
			switch (userChoiceResult)
			{
			case UserChoiceResult.Primary:
			{
				bool flag2 = false;
				try
				{
					flag2 = resetAction();
				}
				catch (Exception ex2)
				{
					LogService.Warn("rule-store-recovery reset failed", ex2);
				}
				if (flag2)
				{
					return Resolution.Retry;
				}
				return Resolution.Failed;
			}
			case UserChoiceResult.Secondary:
				if (settingsOpener != null)
				{
					bool flag = false;
					try
					{
						flag = settingsOpener();
					}
					catch (Exception ex)
					{
						LogService.Warn("rule-store-recovery settings failed", ex);
					}
					if (!flag)
					{
						return Resolution.Failed;
					}
					return Resolution.Retry;
				}
				break;
			}
			return Resolution.Cancelled;
		}
		throw new ArgumentNullException("port");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool OpenSettingsAndRecheck(Func<Form> formFactory, Func<bool> recheck)
	{
		if (formFactory != null)
		{
			if (recheck == null)
			{
				throw new ArgumentNullException("recheck");
			}
			Form val = formFactory();
			try
			{
				val.ShowDialog();
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
			return recheck();
		}
		throw new ArgumentNullException("formFactory");
	}

	public static bool OpenFormatSettingsAndRecheck()
	{
		return OpenSettingsAndRecheck(() => (Form)(object)new SettingsForm(), delegate
		{
			try
			{
				ConfigManager.EnsureStoreAvailableForExecution();
				return true;
			}
			catch
			{
				return false;
			}
		});
	}

	public static bool OpenReplaceSettingsAndRecheck()
	{
		return OpenSettingsAndRecheck(() => (Form)(object)new ReplacePlanSettingsForm(), delegate
		{
			try
			{
				return ReplacePlanService.Load() != null;
			}
			catch
			{
				return false;
			}
		});
	}

	public static bool OpenRedHeaderSettingsAndRecheck()
	{
		return OpenSettingsAndRecheck(() => (Form)(object)new RedHeaderTemplateSettingsForm(), delegate
		{
			try
			{
				return RedHeaderTemplateService.Load() != null;
			}
			catch
			{
				return false;
			}
		});
	}

	public static bool OpenRenameSettingsAndRecheck()
	{
		return OpenSettingsAndRecheck(() => (Form)(object)new RenameRuleSettingsForm(), delegate
		{
			try
			{
				return RenameRuleManager.Load() != null;
			}
			catch
			{
				return false;
			}
		});
	}
}
