using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Services.Cleanup;

internal static class CleanupStagePrecheck
{
	public static bool ContainsAnyChar(string text, string candidates)
	{
		if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(candidates))
		{
			return false;
		}
		for (int i = 0; i < text.Length; i++)
		{
			if (candidates.IndexOf(text[i]) >= 0)
			{
				return true;
			}
		}
		return false;
	}

	public static bool HasAnyConfiguredSpace(string text, ISet<char> configuredSpaceCharacters)
	{
		if (string.IsNullOrEmpty(text) || configuredSpaceCharacters == null)
		{
			return false;
		}
		for (int i = 0; i < text.Length; i++)
		{
			if (configuredSpaceCharacters.Contains(text[i]))
			{
				return true;
			}
		}
		return false;
	}

	public static bool HasAnySymbolCleanupWork(string text, bool deleteAiSymbols)
	{
		if (string.IsNullOrEmpty(text))
		{
			return false;
		}
		for (int i = 0; i < text.Length; i++)
		{
			char c = text[i];
			switch (c)
			{
			case '\u00ad':
			case '\u200b':
			case '\u200c':
			case '\u200d':
			case '\ufeff':
				return true;
			case '!':
			case '(':
			case ')':
			case ',':
			case '.':
			case ':':
			case ';':
			case '<':
			case '>':
			case '?':
			case '[':
			case ']':
				return true;
			case '"':
			case '\'':
			case '＂':
			case '＇':
				return true;
			}
			if (!deleteAiSymbols || !IsContextualAiNoBreakSpace(text, i))
			{
				if (deleteAiSymbols && (c == '#' || c == '*' || c == '-' || c == '【'))
				{
					return true;
				}
				continue;
			}
			return true;
		}
		return false;
	}

	internal static bool IsContextualAiNoBreakSpace(string text, int index)
	{
		if (string.IsNullOrEmpty(text) || index <= 0 || index >= text.Length - 1 || text[index] != '\u00a0')
		{
			return false;
		}
		char ch = text[index - 1];
		char ch2 = text[index + 1];
		bool flag = IsHanCharacter(ch);
		bool flag2 = IsHanCharacter(ch2);
		if (!(flag && flag2) && !(IsChinesePunctuation(ch) && flag2))
		{
			if (flag)
			{
				return IsChinesePunctuation(ch2);
			}
			return false;
		}
		return true;
	}

	private static bool IsHanCharacter(char ch)
	{
		if ((ch < '㐀' || ch > '䶿') && (ch < '一' || ch > '鿿'))
		{
			if (ch < '豈')
			{
				return false;
			}
			return ch <= '\ufaff';
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool IsChinesePunctuation(char ch)
	{
		return "，。！？；：、（）《》【】「」『』“”‘’".IndexOf(ch) >= 0;
	}

	public static bool HasDigitFullStopDigit(string text)
	{
		if (!string.IsNullOrEmpty(text) && text.Length >= 3)
		{
			for (int i = 1; i < text.Length - 1; i++)
			{
				if (text[i] == '。' && char.IsDigit(text[i - 1]) && char.IsDigit(text[i + 1]))
				{
					return true;
				}
			}
			return false;
		}
		return false;
	}
}
