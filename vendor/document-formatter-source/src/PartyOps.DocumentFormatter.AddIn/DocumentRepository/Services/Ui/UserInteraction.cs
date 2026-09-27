using DocumentRepository.Models;

namespace DocumentRepository.Services.Ui;

public static class UserInteraction
{
	private static IUserInteractionPort overrideForTesting;

	public static IUserInteractionPort Resolve(bool isBatchMode)
	{
		if (overrideForTesting != null)
		{
			return overrideForTesting;
		}
		if (!isBatchMode)
		{
			return WinFormsInteractionPort.Instance;
		}
		return HeadlessInteractionPort.Instance;
	}

	public static IUserInteractionPort Resolve(OperationContext context)
	{
		if (overrideForTesting != null)
		{
			return overrideForTesting;
		}
		if (context != null && (context.IsBatchMode || context.SuppressUserDialogs))
		{
			return HeadlessInteractionPort.Instance;
		}
		return WinFormsInteractionPort.Instance;
	}

	internal static void SetPortOverrideForTesting(IUserInteractionPort port)
	{
		overrideForTesting = port;
	}
}
