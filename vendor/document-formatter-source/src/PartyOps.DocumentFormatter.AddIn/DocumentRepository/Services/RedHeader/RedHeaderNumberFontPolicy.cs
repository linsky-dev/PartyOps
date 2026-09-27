using System.Collections.Generic;

namespace DocumentRepository.Services.RedHeader;

public static class RedHeaderNumberFontPolicy
{
	public const string ArabicNumberFontName = "Times New Roman";

	public static IReadOnlyList<RedHeaderDigitRun> FindArabicDigitRuns(string text)
	{
		List<RedHeaderDigitRun> list = new List<RedHeaderDigitRun>();
		if (string.IsNullOrEmpty(text))
		{
			return list;
		}
		int i = 0;
		while (i < text.Length)
		{
			if (!IsAsciiDigit(text[i]))
			{
				i++;
				continue;
			}
			int num = i;
			for (; i < text.Length && IsAsciiDigit(text[i]); i++)
			{
			}
			list.Add(new RedHeaderDigitRun(num, i - num));
		}
		return list;
	}

	private static bool IsAsciiDigit(char value)
	{
		if (value >= '0')
		{
			return value <= '9';
		}
		return false;
	}
}
