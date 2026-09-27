using System.Runtime.CompilerServices;

namespace DocumentRepository.Services.Detection;

public static class MainTitleCandidateDetector
{
	public const int MaximumMainTitleLength = 70;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool IsMainTitleCandidate(string text, FormatConfig cfg = null)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		text = text.Trim();
		if (!HeadingLevelDetector.IsConfiguredHeading(text, cfg))
		{
			if (text.StartsWith("——") || text.EndsWith("——"))
			{
				return false;
			}
			if (IsValidMainTitleContent(text) && IsValidMainTitleLength(text))
			{
				return !text.EndsWith("，");
			}
			return false;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool IsSubTitleCandidate(string text, FormatConfig cfg = null)
	{
		if (!string.IsNullOrWhiteSpace(text))
		{
			text = text.Trim();
			if (HeadingLevelDetector.IsConfiguredHeading(text, cfg))
			{
				return false;
			}
			if ((text.Contains("——") || text.Contains("--")) && IsValidMainTitleContent(text) && IsValidMainTitleLength(text))
			{
				return IsValidTitleEnding(text[text.Length - 1]);
			}
			return false;
		}
		return false;
	}

	public static bool IsParenthesizedSubTitleCandidate(string text, FormatConfig cfg = null)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		string text2 = text.Trim();
		if (text2.Length < 3)
		{
			return false;
		}
		if (HeadingLevelDetector.IsConfiguredHeading(text2, cfg))
		{
			return false;
		}
		bool num = text2[0] == '（' && text2[text2.Length - 1] == '）';
		bool flag = text2[0] == '(' && text2[text2.Length - 1] == ')';
		if (!num && !flag)
		{
			return false;
		}
		if (!string.IsNullOrWhiteSpace(text2.Substring(1, text2.Length - 2).Trim()) && IsValidMainTitleContent(text2))
		{
			return IsValidMainTitleLength(text2);
		}
		return false;
	}

	private static bool IsValidMainTitleContent(string text)
	{
		for (int i = 0; i < text.Length; i++)
		{
			char c = text[i];
			if ((c < '一' || c > '鿿') && !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c) && c != '《' && c != '》' && c != '、' && c != '，' && c != '·' && c != '×' && c != 'X' && c != 'x' && c != '"' && c != '\'' && c != '“' && c != '”' && c != '‘' && c != '’' && c != '（' && c != '）' && c != '(' && c != ')' && c != '【' && c != '】' && c != '〔' && c != '〕' && ((c != '-' && c != '－') || !IsBetweenDigits(text, i)) && c != '—' && c != '–')
			{
				return false;
			}
		}
		return true;
	}

	private static bool IsBetweenDigits(string text, int index)
	{
		if (string.IsNullOrEmpty(text) || index <= 0 || index >= text.Length - 1)
		{
			return false;
		}
		if (char.IsDigit(text[index - 1]))
		{
			return char.IsDigit(text[index + 1]);
		}
		return false;
	}

	private static bool IsValidMainTitleLength(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return false;
		}
		if (text.Length <= 70)
		{
			char c = text[text.Length - 1];
			if ((c == '"' || c == '\'' || c == '”' || c == '’') && text.Length > 20)
			{
				return false;
			}
			return true;
		}
		return false;
	}

	private static bool IsValidTitleEnding(char c)
	{
		if (c < '一' || c > '鿿')
		{
			if (char.IsLetterOrDigit(c))
			{
				return true;
			}
			if (c == '》' || c == '"' || c == '\'' || c == '”' || c == '’' || c == ')' || c == '）' || c == '、' || c == '·')
			{
				return true;
			}
			return false;
		}
		return true;
	}
}
