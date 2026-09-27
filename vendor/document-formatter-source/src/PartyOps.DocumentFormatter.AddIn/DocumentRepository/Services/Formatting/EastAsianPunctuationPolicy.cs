namespace DocumentRepository.Services.Formatting;

internal static class EastAsianPunctuationPolicy
{
	internal static int FindReferenceIndex(string text, int punctuationIndex)
	{
		if (!string.IsNullOrEmpty(text) && punctuationIndex >= 0 && punctuationIndex < text.Length)
		{
			switch (text[punctuationIndex])
			{
			case '’':
			case '”':
			{
				int num3 = FindHanCharacter(text, punctuationIndex, -1);
				if (num3 < 0)
				{
					return FindHanCharacter(text, punctuationIndex, 1);
				}
				return num3;
			}
			default:
			{
				int num2 = FindHanCharacter(text, punctuationIndex, -1);
				if (num2 < 0)
				{
					return FindHanCharacter(text, punctuationIndex, 1);
				}
				return num2;
			}
			case '‘':
			case '“':
			{
				int num = FindHanCharacter(text, punctuationIndex, 1);
				if (num >= 0)
				{
					return num;
				}
				return FindHanCharacter(text, punctuationIndex, -1);
			}
			}
		}
		return -1;
	}

	internal static bool RequiresFormatting(string text, int index)
	{
		if (string.IsNullOrEmpty(text) || index < 0 || index >= text.Length)
		{
			return false;
		}
		switch (text[index])
		{
		case '·':
		case '—':
		case '―':
		case '‘':
		case '’':
		case '“':
		case '”':
		case '…':
			return !IsInsideLatinWord(text, index);
		default:
			return false;
		}
	}

	private static int FindHanCharacter(string text, int startIndex, int direction)
	{
		for (int i = startIndex + direction; i >= 0 && i < text.Length; i += direction)
		{
			char c = text[i];
			if (c == '\r' || c == '\a' || c == '\v' || c == '\f')
			{
				return -1;
			}
			if (IsHanCharacter(c))
			{
				return i;
			}
		}
		return -1;
	}

	private static bool IsInsideLatinWord(string text, int index)
	{
		if (index <= 0 || index >= text.Length - 1)
		{
			return false;
		}
		if (IsLatinOrDigit(text[index - 1]))
		{
			return IsLatinOrDigit(text[index + 1]);
		}
		return false;
	}

	private static bool IsLatinOrDigit(char value)
	{
		if ((value < '0' || value > '9') && (value < 'A' || value > 'Z'))
		{
			if (value < 'a')
			{
				return false;
			}
			return value <= 'z';
		}
		return true;
	}

	private static bool IsHanCharacter(char value)
	{
		if ((value < '㐀' || value > '䶿') && (value < '一' || value > '鿿'))
		{
			if (value >= '豈')
			{
				return value <= '\ufaff';
			}
			return false;
		}
		return true;
	}
}
