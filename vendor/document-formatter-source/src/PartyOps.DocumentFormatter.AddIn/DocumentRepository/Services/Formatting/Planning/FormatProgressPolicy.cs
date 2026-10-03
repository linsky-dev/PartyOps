namespace DocumentRepository.Services.Formatting.Planning;

public static class FormatProgressPolicy
{
	public const int ImmediateCharacterThreshold = 15000;

	public const int ImmediateParagraphThreshold = 250;

	public const int DeferredPageThreshold = 30;

	public const long DeferredElapsedThresholdMilliseconds = 1500L;

	public static bool ShouldShowImmediately(int characterCount, int paragraphCount)
	{
		int num = ((characterCount >= 0) ? characterCount : 0);
		int num2 = ((paragraphCount >= 0) ? paragraphCount : 0);
		if (num < 15000)
		{
			return num2 >= 250;
		}
		return true;
	}

	public static bool ShouldShowAfterPlanning(bool quietMode, bool progressAlreadyVisible, int pageCount, long elapsedMilliseconds)
	{
		if (quietMode || progressAlreadyVisible)
		{
			return false;
		}
		if (pageCount < 30)
		{
			return elapsedMilliseconds >= 1500;
		}
		return true;
	}
}
