using System.Collections.Generic;

namespace DocumentRepository.Services.Conversion.PdfToWord.Tables;

public static class CrossPageTableContinuationVerifier
{
	public static CrossPageTableConservationReport VerifyTextConservation(string sourceText, string outputText, IReadOnlyList<string> provenRepeatedHeaderTexts)
	{
		Dictionary<char, int> dictionary = CharMultiset(sourceText);
		Dictionary<char, int> dictionary2 = CharMultiset(outputText);
		if (provenRepeatedHeaderTexts != null)
		{
			foreach (string provenRepeatedHeaderText in provenRepeatedHeaderTexts)
			{
				AddInto(dictionary2, provenRepeatedHeaderText);
			}
		}
		int num = 0;
		int num2 = 0;
		HashSet<char> hashSet = new HashSet<char>();
		foreach (KeyValuePair<char, int> item in dictionary)
		{
			hashSet.Add(item.Key);
			dictionary2.TryGetValue(item.Key, out var value);
			if (item.Value > value)
			{
				num += item.Value - value;
			}
			else if (value > item.Value)
			{
				num2 += value - item.Value;
			}
		}
		foreach (KeyValuePair<char, int> item2 in dictionary2)
		{
			if (!hashSet.Contains(item2.Key))
			{
				num2 += item2.Value;
			}
		}
		int num3 = 0;
		if (provenRepeatedHeaderTexts != null)
		{
			foreach (string provenRepeatedHeaderText2 in provenRepeatedHeaderTexts)
			{
				num3 += CountNonWhitespace(provenRepeatedHeaderText2);
			}
		}
		return new CrossPageTableConservationReport(CountNonWhitespace(sourceText), CountNonWhitespace(outputText), num3, num, num2);
	}

	private static Dictionary<char, int> CharMultiset(string text)
	{
		Dictionary<char, int> dictionary = new Dictionary<char, int>();
		if (!string.IsNullOrEmpty(text))
		{
			AddInto(dictionary, text);
		}
		return dictionary;
	}

	private static void AddInto(Dictionary<char, int> multiset, string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return;
		}
		foreach (char c in text)
		{
			if (!char.IsWhiteSpace(c) && c != '\u3000')
			{
				multiset.TryGetValue(c, out var value);
				multiset[c] = value + 1;
			}
		}
	}

	private static int CountNonWhitespace(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return 0;
		}
		int num = 0;
		foreach (char c in text)
		{
			if (!char.IsWhiteSpace(c) && c != '\u3000')
			{
				num++;
			}
		}
		return num;
	}
}
