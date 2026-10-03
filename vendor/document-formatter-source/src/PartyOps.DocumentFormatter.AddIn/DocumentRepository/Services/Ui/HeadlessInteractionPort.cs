using System.Runtime.CompilerServices;
using DocumentRepository.Models.Safety;
using DocumentRepository.Services.Logging;

namespace DocumentRepository.Services.Ui;

public sealed class HeadlessInteractionPort : IUserInteractionPort
{
	public static readonly HeadlessInteractionPort Instance = new HeadlessInteractionPort();

	private HeadlessInteractionPort()
	{
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public UserChoiceResult Ask(UserConfirmationRequest request)
	{
		LogService.Info("user-confirmation-headless kind=" + ((request == null) ? "null" : request.Kind.ToString()) + ", result=auto-cancel");
		return UserChoiceResult.Cancelled;
	}

	UserChoiceResult IUserInteractionPort.Ask(UserConfirmationRequest request)
	{
		return this.Ask(request);
	}
}
