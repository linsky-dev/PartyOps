using DocumentRepository.Models;

namespace DocumentRepository.Services.Detection;

public static class KeywordCandidatePolicy
{
	public static bool IsEligible(ElementType type, string text, bool bodyPostProcessAllowed)
	{
		if (bodyPostProcessAllowed)
		{
			return true;
		}
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		if (type == ElementType.MainTitle || (uint)(type - 3) <= 2u)
		{
			int num = MixedContentDetector.FindTitleBodyBoundary(text);
			if (num <= 0)
			{
				return false;
			}
			return num < text.Length - 1;
		}
		return false;
	}
}
