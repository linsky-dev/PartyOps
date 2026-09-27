namespace DocumentRepository.Services.Cleanup;

internal static class ExactTextMutationPolicy
{
	internal static bool TouchesBookmarkBoundary(int targetStart, int targetEnd, int bookmarkStart, int bookmarkEnd)
	{
		if (targetStart >= 0 && targetEnd >= targetStart)
		{
			if (bookmarkStart < 0 || bookmarkEnd < bookmarkStart)
			{
				return true;
			}
			if (targetStart == targetEnd)
			{
				return false;
			}
			bool num = bookmarkStart >= targetStart && bookmarkStart < targetEnd;
			bool flag = bookmarkEnd > targetStart && bookmarkEnd <= targetEnd;
			return num || flag;
		}
		return true;
	}
}
