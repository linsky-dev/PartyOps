using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace DocumentRepository.Services.Cleanup;

internal static class ChineseContextPunctuationNormalizer
{
	private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250.0);

	private static readonly Regex[] ProtectedTokenPatterns = new Regex[14]
	{
		CreateRegex("\\b(?:https?|ftp)://[^\\s\\u3400-\\u9fff，。！？；：“”‘’（）【】《》]+"),
		CreateRegex("\\bwww\\.[^\\s\\u3400-\\u9fff，。！？；：“”‘’（）【】《》]+"),
		CreateRegex("\\b[A-Z0-9._%+\\-]+@[A-Z0-9.\\-]+\\.[A-Z]{2,}\\b"),
		CreateRegex("\\b(?:\\d{1,3}\\.){3}\\d{1,3}\\b"),
		CreateRegex("\\b[vV]?\\d+(?:\\.\\d+){1,}\\b"),
		CreateRegex("\\b\\d{1,2}:\\d{2}(?::\\d{2})?\\b"),
		CreateRegex("\\b\\d+(?:\\.\\d+)?\\s*:\\s*\\d+(?:\\.\\d+)?\\b"),
		CreateRegex("\\b[A-Za-z]+(?:'[A-Za-z]+)+\\b"),
		CreateRegex("\\b(?:[A-Za-z]\\.){2,}"),
		CreateRegex("\\b[A-Za-z0-9_\\-]+\\.(?:docx?|xlsx?|pptx?|pdf|txt|rtf|wps|et|dps|zip|rar|7z|exe|dll|json|xml|html?|cs|js|ts)\\b"),
		CreateRegex("\\b[A-Za-z0-9\\-]+(?:\\.[A-Za-z0-9\\-]+)+\\b"),
		CreateRegex("(?:[A-Za-z]:\\\\|\\\\\\\\)[^\\s，。！？；：“”‘’（）【】《》]+"),
		CreateRegex("`[^`\\r\\n]*`"),
		CreateRegex("</?[A-Za-z][^>\\r\\n]*>")
	};

	private static readonly Regex JsonLikePattern = CreateRegex("^\\s*[\\{\\[]?.*\"[A-Za-z_][A-Za-z0-9_\\-]*\"\\s*:");

	public static ChinesePunctuationNormalizationResult Normalize(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return new ChinesePunctuationNormalizationResult(text ?? string.Empty, 0, 0, 0, 0);
		}
		StringBuilder stringBuilder = new StringBuilder(text.Length);
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		int num4 = 0;
		int num5 = 0;
		for (int i = 0; i <= text.Length; i++)
		{
			if (i == text.Length || text[i] == '\r' || text[i] == '\a')
			{
				if (i > num5)
				{
					ChinesePunctuationNormalizationResult chinesePunctuationNormalizationResult = NormalizeParagraph(text.Substring(num5, i - num5));
					stringBuilder.Append(chinesePunctuationNormalizationResult.Text);
					num += chinesePunctuationNormalizationResult.ConvertedCount;
					num2 += chinesePunctuationNormalizationResult.PreservedStraightQuoteCount;
					num3 += chinesePunctuationNormalizationResult.ProtectedTokenCount;
					num4 += chinesePunctuationNormalizationResult.ConservativeSkipCount;
				}
				if (i < text.Length)
				{
					stringBuilder.Append(text[i]);
				}
				num5 = i + 1;
			}
		}
		return new ChinesePunctuationNormalizationResult(stringBuilder.ToString(), num, num2, num3, num4);
	}

	private static ChinesePunctuationNormalizationResult NormalizeParagraph(string paragraph)
	{
		if (string.IsNullOrEmpty(paragraph))
		{
			return new ChinesePunctuationNormalizationResult(paragraph, 0, 0, 0, 0);
		}
		bool[] array = new bool[paragraph.Length];
		if (TryMarkProtectedTokens(paragraph, array, out var protectedTokenCount))
		{
			char[] array2 = paragraph.ToCharArray();
			int num = 0;
			int num2 = 0;
			for (int i = 0; i < paragraph.Length; i++)
			{
				if (IsHanCharacter(paragraph[i]))
				{
					num++;
				}
				else if (IsLatinLetter(paragraph[i]))
				{
					num2++;
				}
			}
			bool chineseDominant = num > 0 && num * 2 >= num2;
			int num3 = 0;
			num3 += NormalizeBracketPairs(paragraph, array2, array, chineseDominant);
			for (int j = 0; j < paragraph.Length; j++)
			{
				if (!array[j] && TryGetSimpleReplacement(paragraph[j], out var replacement) && ShouldConvertSimplePunctuation(paragraph, array, j, chineseDominant))
				{
					array2[j] = replacement;
					num3++;
				}
			}
			num3 += NormalizeQuotePairs(paragraph, array2, array, chineseDominant, out var preservedQuoteCount);
			return new ChinesePunctuationNormalizationResult(new string(array2), num3, preservedQuoteCount, protectedTokenCount, 0);
		}
		return new ChinesePunctuationNormalizationResult(paragraph, 0, CountStraightQuotes(paragraph), 0, 1);
	}

	private static bool TryMarkProtectedTokens(string text, bool[] protectedCharacters, out int protectedTokenCount)
	{
		protectedTokenCount = 0;
		try
		{
			if (!JsonLikePattern.IsMatch(text))
			{
				Regex[] protectedTokenPatterns = ProtectedTokenPatterns;
				for (int i = 0; i < protectedTokenPatterns.Length; i++)
				{
					foreach (Match item in protectedTokenPatterns[i].Matches(text))
					{
						if (!item.Success || item.Length <= 0)
						{
							continue;
						}
						int num = Math.Min(protectedCharacters.Length, item.Index + item.Length);
						while (num > item.Index && IsTrailingWesternDelimiter(text[num - 1]))
						{
							num--;
						}
						if (num > item.Index)
						{
							protectedTokenCount++;
							for (int j = Math.Max(0, item.Index); j < num; j++)
							{
								protectedCharacters[j] = true;
							}
						}
					}
				}
				for (int k = 0; k < text.Length; k++)
				{
					if ((text[k] == '\'' || text[k] == '＇') && k > 0 && k + 1 < text.Length && IsLatinOrDigit(text[k - 1]) && IsLatinOrDigit(text[k + 1]))
					{
						protectedCharacters[k] = true;
					}
					if ((text[k] == '\'' || text[k] == '"' || text[k] == '＇' || text[k] == '＂') && k > 0 && text[k - 1] == '\\')
					{
						protectedCharacters[k] = true;
					}
				}
				return true;
			}
			for (int l = 0; l < protectedCharacters.Length; l++)
			{
				protectedCharacters[l] = true;
			}
			protectedTokenCount = 1;
			return true;
		}
		catch (RegexMatchTimeoutException)
		{
			return false;
		}
	}

	private static int NormalizeBracketPairs(string text, char[] corrected, bool[] protectedCharacters, bool chineseDominant)
	{
		return 0 + NormalizeBracketPairType(text, corrected, protectedCharacters, chineseDominant, '(', ')', '（', '）') + NormalizeBracketPairType(text, corrected, protectedCharacters, chineseDominant, '[', ']', '【', '】') + NormalizeBracketPairType(text, corrected, protectedCharacters, chineseDominant, '<', '>', '《', '》');
	}

	private static int NormalizeBracketPairType(string text, char[] corrected, bool[] protectedCharacters, bool chineseDominant, char opening, char closing, char replacementOpening, char replacementClosing)
	{
		Stack<int> stack = new Stack<int>();
		int num = 0;
		for (int i = 0; i < text.Length; i++)
		{
			if (protectedCharacters[i])
			{
				continue;
			}
			if (text[i] != opening)
			{
				if (text[i] == closing && stack.Count != 0)
				{
					int num2 = stack.Pop();
					if (ShouldConvertBracketPair(text, num2, i, chineseDominant))
					{
						corrected[num2] = replacementOpening;
						corrected[i] = replacementClosing;
						num += 2;
					}
				}
			}
			else
			{
				stack.Push(i);
			}
		}
		return num;
	}

	private static bool ShouldConvertBracketPair(string text, int openingIndex, int closingIndex, bool chineseDominant)
	{
		if (closingIndex > openingIndex + 1)
		{
			if (openingIndex <= 0 || char.IsWhiteSpace(text[openingIndex - 1]) || (!IsLatinOrDigit(text[openingIndex - 1]) && text[openingIndex - 1] != '_'))
			{
				int num = FindSignificant(text, openingIndex, -1);
				int num2 = FindSignificant(text, closingIndex, 1);
				if ((num < 0 || !IsHanCharacter(text[num])) && (num2 < 0 || !IsHanCharacter(text[num2])))
				{
					if (!chineseDominant)
					{
						return false;
					}
					for (int i = openingIndex + 1; i < closingIndex; i++)
					{
						if (IsHanCharacter(text[i]))
						{
							return true;
						}
					}
					if (!HasHanWithin(text, openingIndex, 16))
					{
						return HasHanWithin(text, closingIndex, 16);
					}
					return true;
				}
				return true;
			}
			return false;
		}
		return false;
	}

	private static bool ShouldConvertSimplePunctuation(string text, bool[] protectedCharacters, int index, bool chineseDominant)
	{
		char c = text[index];
		int num = FindSignificant(text, index, -1);
		int num2 = FindSignificant(text, index, 1);
		char c2 = ((num >= 0) ? text[num] : '\0');
		char c3 = ((num2 >= 0) ? text[num2] : '\0');
		if (c == '.' && ((index > 0 && text[index - 1] == '.') || (index + 1 < text.Length && text[index + 1] == '.')))
		{
			return false;
		}
		if (c == '.' && (char.IsDigit(c2) || char.IsDigit(c3)))
		{
			return false;
		}
		if (c != ',' || !char.IsDigit(c2) || !char.IsDigit(c3))
		{
			if (c != ':' || !char.IsDigit(c2) || !char.IsDigit(c3))
			{
				if (num >= 0 && num2 >= 0 && !protectedCharacters[num] && !protectedCharacters[num2] && IsLatinOrDigit(c2) && IsLatinOrDigit(c3))
				{
					return false;
				}
				if (!IsHanCharacter(c2) && !IsHanCharacter(c3))
				{
					if (chineseDominant)
					{
						if (!IsChinesePunctuation(c2) && !IsChinesePunctuation(c3))
						{
							return HasHanWithin(text, index, 16);
						}
						return true;
					}
					return false;
				}
				return true;
			}
			return false;
		}
		return false;
	}

	private static int NormalizeQuotePairs(string text, char[] corrected, bool[] protectedCharacters, bool chineseDominant, out int preservedQuoteCount)
	{
		int num = 0;
		int candidateCount = 0;
		num += NormalizeQuotePairType(text, corrected, protectedCharacters, chineseDominant, doubleQuote: true, out candidateCount);
		num += NormalizeQuotePairType(text, corrected, protectedCharacters, chineseDominant, doubleQuote: false, out var candidateCount2);
		candidateCount += candidateCount2;
		preservedQuoteCount = Math.Max(0, candidateCount - num);
		return num;
	}

	private static int NormalizeQuotePairType(string text, char[] corrected, bool[] protectedCharacters, bool chineseDominant, bool doubleQuote, out int candidateCount)
	{
		List<int> candidates = new List<int>();
		for (int i = 0; i < text.Length; i++)
		{
			if (protectedCharacters[i])
			{
				continue;
			}

			char c = text[i];
			bool isQuote = doubleQuote ? (c == '"' || c == '＂') : (c == '\'' || c == '＇');
			if (isQuote)
			{
				candidates.Add(i);
			}
		}
		candidateCount = candidates.Count;
		List<int> list2 = new List<int>(candidates.Count);
		for (int j = 0; j < candidates.Count; j++)
		{
			int num2 = candidates[j];
			if (!IsNumericMeasurementQuote(text, candidates, j, num2))
			{
				list2.Add(num2);
			}
		}
		int num3 = 0;
		for (int k = 0; k + 1 < list2.Count; k += 2)
		{
			int num4 = list2[k];
			int num5 = list2[k + 1];
			if (IsPlausibleChineseQuotePair(text, num4, num5, chineseDominant))
			{
				corrected[num4] = (doubleQuote ? '“' : '‘');
				corrected[num5] = (doubleQuote ? '”' : '’');
				num3 += 2;
			}
		}
		return num3;
	}

	private static bool IsNumericMeasurementQuote(string text, IList<int> sameTypeQuotes, int quotePosition, int quoteIndex)
	{
		if (quoteIndex > 0 && char.IsDigit(text[quoteIndex - 1]))
		{
			if (quotePosition > 0)
			{
				int num = sameTypeQuotes[quotePosition - 1];
				if (num < quoteIndex - 1)
				{
					for (int i = num + 1; i < quoteIndex; i++)
					{
						char c = text[i];
						if (!char.IsDigit(c) && c != '.' && c != ',' && !char.IsWhiteSpace(c))
						{
							return true;
						}
					}
					return false;
				}
				return true;
			}
			return true;
		}
		return false;
	}

	private static bool IsPlausibleChineseQuotePair(string text, int openingIndex, int closingIndex, bool chineseDominant)
	{
		if (closingIndex <= openingIndex + 1)
		{
			return false;
		}
		int i;
		for (i = openingIndex + 1; i < closingIndex && char.IsWhiteSpace(text[i]); i++)
		{
		}
		if (i < closingIndex)
		{
			int num = FindSignificant(text, openingIndex, -1);
			int num2 = FindSignificant(text, closingIndex, 1);
			if ((num >= 0 && IsHanCharacter(text[num])) || (num2 >= 0 && IsHanCharacter(text[num2])))
			{
				return true;
			}
			if (chineseDominant)
			{
				if (!HasHanWithin(text, openingIndex, 20))
				{
					return HasHanWithin(text, closingIndex, 20);
				}
				return true;
			}
			return false;
		}
		return false;
	}

	private static int FindSignificant(string text, int index, int direction)
	{
		for (int i = index + direction; i >= 0 && i < text.Length; i += direction)
		{
			char c = text[i];
			if (c == '\r' || c == '\a' || c == '\v' || c == '\f')
			{
				return -1;
			}
			if (!char.IsWhiteSpace(c))
			{
				return i;
			}
		}
		return -1;
	}

	private static bool HasHanWithin(string text, int center, int radius)
	{
		int num = Math.Max(0, center - Math.Max(1, radius));
		int num2 = Math.Min(text.Length - 1, center + Math.Max(1, radius));
		for (int i = num; i <= num2; i++)
		{
			if (IsHanCharacter(text[i]))
			{
				return true;
			}
		}
		return false;
	}

	private static bool TryGetSimpleReplacement(char value, out char replacement)
	{
		switch (value)
		{
		case '.':
			replacement = '。';
			return true;
		case '!':
			replacement = '！';
			return true;
		case ':':
			replacement = '：';
			return true;
		default:
			replacement = value;
			return false;
		case ';':
			replacement = '；';
			return true;
		case '?':
			replacement = '？';
			return true;
		case ',':
			replacement = '，';
			return true;
		}
	}

	private static int CountStraightQuotes(string text)
	{
		int num = 0;
		foreach (char c in text)
		{
			if (c == '"' || c == '\'' || c == '＂' || c == '＇')
			{
				num++;
			}
		}
		return num;
	}

	private static bool IsHanCharacter(char value)
	{
		if ((value < '㐀' || value > '䶿') && (value < '一' || value > '鿿'))
		{
			if (value < '豈')
			{
				return false;
			}
			return value <= '\ufaff';
		}
		return true;
	}

	private static bool IsLatinLetter(char value)
	{
		if (value < 'A' || value > 'Z')
		{
			if (value >= 'a')
			{
				return value <= 'z';
			}
			return false;
		}
		return true;
	}

	private static bool IsLatinOrDigit(char value)
	{
		if (!IsLatinLetter(value))
		{
			if (value >= '0')
			{
				return value <= '9';
			}
			return false;
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool IsChinesePunctuation(char value)
	{
		return "，。！？；：、（）《》【】「」『』“”‘’—―…·".IndexOf(value) >= 0;
	}

	private static bool IsTrailingWesternDelimiter(char value)
	{
		if (value != ',' && value != ';' && value != '!')
		{
			return value == '?';
		}
		return true;
	}

	private static Regex CreateRegex(string pattern)
	{
		return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
	}
}
