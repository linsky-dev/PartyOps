using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace DocumentRepository.Services.Conversion.PdfToWord;

public static class ChinesePunctuationNormalizer
{
	private static readonly Regex[] ProtectedPatterns = new Regex[9]
	{
		new Regex("(?i)\\b(?:https?://|ftp://|www\\.)[A-Z0-9\\-._~:/?#\\[\\]@!$&'()*+,;=%]+", RegexOptions.Compiled),
		new Regex("(?i)\\b[A-Z0-9._%+\\-]+@[A-Z0-9.\\-]+\\.[A-Z]{2,}\\b", RegexOptions.Compiled),
		new Regex("(?i)(?:[A-Z]:\\\\|\\\\\\\\)[^\\s,;，；。！？]+", RegexOptions.Compiled),
		new Regex("(?i)\\b[\\w\\-]+\\.(?:docx?|xlsx?|pptx?|pdf|txt|csv|zip|rar|7z|exe|dll|json|xml|html?|com|cn|net|org)\\b", RegexOptions.Compiled),
		new Regex("\\b(?:\\d{1,3}\\.){3}\\d{1,3}\\b", RegexOptions.Compiled),
		new Regex("\\b\\d{1,3}(?:,\\d{3})+(?:\\.\\d+)?\\b", RegexOptions.Compiled),
		new Regex("\\b\\d+(?:\\.\\d+)+\\b", RegexOptions.Compiled),
		new Regex("\\b\\d{1,2}:\\d{2}(?::\\d{2})?\\b", RegexOptions.Compiled),
		new Regex("(?i)\\b(?:[A-Z]\\.){2,}", RegexOptions.Compiled)
	};

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void NormalizeDocument(ReconstructedDocument document)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		foreach (DocumentBlock block in document.Blocks)
		{
			if (block is ParagraphBlock paragraphBlock)
			{
				NormalizeRuns(paragraphBlock.Runs);
			}
			else
			{
				if (!(block is TableBlock tableBlock))
				{
					continue;
				}
				foreach (TableRow row in tableBlock.Rows)
				{
					foreach (TableCell cell in row.Cells)
					{
						NormalizeRuns(cell.Runs);
					}
				}
			}
		}
	}

	public static string NormalizeText(string text)
	{
		if (!string.IsNullOrEmpty(text))
		{
			bool[] array = BuildProtectedPositions(text);
			char[] array2 = text.ToCharArray();
			for (int i = 0; i < array2.Length; i++)
			{
				if (!array[i] && TryMap(array2[i], out var replacement))
				{
					int num = FindVisible(text, i - 1, -1);
					int num2 = FindVisible(text, i + 1, 1);
					if (((num >= 0 && IsCjk(text[num])) || (num2 >= 0 && IsCjk(text[num2]))) && ((array2[i] != '.' && array2[i] != ',' && array2[i] != ':') || num < 0 || num2 < 0 || !char.IsDigit(text[num]) || !char.IsDigit(text[num2])))
					{
						array2[i] = replacement;
					}
				}
			}
			return new string(array2);
		}
		return text ?? string.Empty;
	}

	private static void NormalizeRuns(IList<TextRun> runs)
	{
		if (runs == null || runs.Count == 0)
		{
			return;
		}
		StringBuilder stringBuilder = new StringBuilder();
		foreach (TextRun run in runs)
		{
			stringBuilder.Append((run == null) ? string.Empty : (run.Text ?? string.Empty));
		}
		string text = stringBuilder.ToString();
		string text2 = NormalizeText(text);
		if (string.Equals(text, text2, StringComparison.Ordinal))
		{
			return;
		}
		int num = 0;
		foreach (TextRun run2 in runs)
		{
			if (run2 != null)
			{
				int length = (run2.Text ?? string.Empty).Length;
				run2.Text = text2.Substring(num, length);
				num += length;
			}
		}
	}

	private static bool[] BuildProtectedPositions(string text)
	{
		bool[] array = new bool[text.Length];
		Regex[] protectedPatterns = ProtectedPatterns;
		for (int i = 0; i < protectedPatterns.Length; i++)
		{
			foreach (Match item in protectedPatterns[i].Matches(text))
			{
				int num = Math.Min(text.Length, item.Index + item.Length);
				for (int j = Math.Max(0, item.Index); j < num; j++)
				{
					array[j] = true;
				}
			}
		}
		return array;
	}

	private static int FindVisible(string text, int start, int step)
	{
		for (int i = start; i >= 0 && i < text.Length; i += step)
		{
			if (!char.IsWhiteSpace(text[i]))
			{
				return i;
			}
		}
		return -1;
	}

	private static bool TryMap(char value, out char replacement)
	{
		switch (value)
		{
		case '.':
			replacement = '。';
			return true;
		case ';':
			replacement = '；';
			return true;
		case '?':
			replacement = '？';
			return true;
		case ',':
			replacement = '，';
			return true;
		default:
			replacement = value;
			return false;
		case ':':
			replacement = '：';
			return true;
		case '!':
			replacement = '！';
			return true;
		}
	}

	private static bool IsCjk(char value)
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
}
