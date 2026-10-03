using System;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using DocumentRepository.Models.Safety;
using DocumentRepository.Services.Logging;

namespace DocumentRepository.Services.Ui;

public sealed class WinFormsInteractionPort : IUserInteractionPort
{
	public static readonly WinFormsInteractionPort Instance = new WinFormsInteractionPort();

	private WinFormsInteractionPort()
	{
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public unsafe UserChoiceResult Ask(UserConfirmationRequest request)
	{
		if (request == null)
		{
			throw new ArgumentNullException("request");
		}
		MessageBoxIcon icon = (MessageBoxIcon)((request.Kind == UserConfirmationKind.RuleStoreResetToDefault) ? 48 : 32);
		DialogResult val = AppleMessageDialog.ShowChoices(null, UserInteractionText.ResolveMessage(request), UserInteractionText.ResolveTitle(request), UserInteractionText.ResolvePrimaryAction(request), UserInteractionText.ResolveSecondaryAction(request), UserInteractionText.ResolveCancelAction(request), icon);
		LogService.Info("user-confirmation kind=" + request.Kind.ToString() + ", result=" + ((object)(*(DialogResult*)(&val))/*cast due to .constrained prefix*/).ToString());
		if ((int)val == 6)
		{
			return UserChoiceResult.Primary;
		}
		if ((int)val == 7)
		{
			return UserChoiceResult.Secondary;
		}
		return UserChoiceResult.Cancelled;
	}

	UserChoiceResult IUserInteractionPort.Ask(UserConfirmationRequest request)
	{
		return this.Ask(request);
	}
}
