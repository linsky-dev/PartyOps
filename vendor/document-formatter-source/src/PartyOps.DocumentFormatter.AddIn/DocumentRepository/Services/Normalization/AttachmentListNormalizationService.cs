using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DocumentRepository.Services.Cleanup;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Interop;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.Snapshots;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Normalization;

internal static class AttachmentListNormalizationService
{
	internal sealed class ParagraphSnapshot
	{
		public int Start { get; set; }

		public int End { get; set; }

		public int VisibleStart { get; set; }

		public int VisibleEnd { get; set; }

		public string VisibleText { get; set; }

		public bool IsInTable { get; set; }
	}

	internal sealed class TextEdit
	{
		public int Start { get; set; }

		public int End { get; set; }

		public string Replacement { get; set; }
	}

	private static readonly Regex MarkerAloneRegex = new Regex("^附件[：:]\\s*$", RegexOptions.Compiled);

	private static readonly Regex FirstInlineRegex = new Regex("^(?<prefix>附件[：:])\\s*1[、\\.．]?\\s*(?<body>.+)$", RegexOptions.Compiled);

	private static readonly Regex FirstErrorRegex = new Regex("^1[、\\.．]?\\s*.+", RegexOptions.Compiled);

	private static readonly Regex SingleRegex = new Regex("^附件[：:]\\s*.+", RegexOptions.Compiled);

	private static readonly Regex NextRegex = new Regex("^[1-9]\\d*[\\.．、]\\s*.+", RegexOptions.Compiled);

	private static readonly Regex ItemRegex = new Regex("^(?<num>[1-9]\\d*)[、\\.．]?\\s*(?<body>.+)$", RegexOptions.Compiled);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void NormalizeDocument(Document document)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		Normalize(document, null);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void NormalizeRange(Document document, Microsoft.Office.Interop.Word.Range scopeRange)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		if (scopeRange == null)
		{
			throw new ArgumentNullException("scopeRange");
		}
		Normalize(document, scopeRange);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void Normalize(Document document, Microsoft.Office.Interop.Word.Range scopeRange)
	{
		List<TextEdit> list = BuildPlan(CaptureSnapshots(document, scopeRange));
		if (list.Count == 0)
		{
			return;
		}
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			value = ((scopeRange == null) ? document.Content : scopeRange.Duplicate);
			list.Sort(delegate(TextEdit left, TextEdit right)
			{
				int num = right.Start.CompareTo(left.Start);
				return (num == 0) ? right.End.CompareTo(left.End) : num;
			});
			foreach (TextEdit item in list)
			{
				SafeTextMutationService.TryReplace(value, item.Start - value.Start, item.End - item.Start, item.Replacement, "附件说明规范化");
			}
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "AttachmentListNormalizationService.container");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static List<ParagraphSnapshot> CaptureSnapshots(Document document, Microsoft.Office.Interop.Word.Range scopeRange)
	{
		if (scopeRange == null)
		{
			ParagraphTextSnapshot paragraphTextSnapshot = ParagraphTextSnapshot.Capture(document);
			if (paragraphTextSnapshot.IsReliable)
			{
				List<ParagraphSnapshot> list = new List<ParagraphSnapshot>(paragraphTextSnapshot.Paragraphs.Count);
				for (int i = 0; i < paragraphTextSnapshot.Paragraphs.Count; i++)
				{
					WordParagraphTextMap.Entry entry = paragraphTextSnapshot.Paragraphs[i];
					Microsoft.Office.Interop.Word.Range value = null;
					try
					{
						object Start = entry.RangeStart;
						object End = entry.RangeEnd;
						value = document.Range(ref Start, ref End);
						list.Add(CreateSnapshot(entry.RangeStart, entry.RangeEnd, entry.Text, WordRangeInspector.IsInTable(value)));
					}
					finally
					{
						if (value != null)
						{
							ComObjectRelease.Release(ref value, "AttachmentListNormalizationService.probeRange");
						}
					}
				}
				return list;
			}
			LogService.Warn("AttachmentListNormalizationService text snapshot unavailable, fallback: " + paragraphTextSnapshot.FailureReason);
		}
		List<ParagraphSnapshot> list2 = new List<ParagraphSnapshot>();
		Paragraphs value2 = null;
		try
		{
			value2 = ((scopeRange == null) ? document.Paragraphs : scopeRange.Paragraphs);
			int val = scopeRange?.Start ?? 0;
			int val2 = scopeRange?.End ?? int.MaxValue;
			int num = value2?.Count ?? 0;
			for (int j = 1; j <= num; j++)
			{
				Paragraph value3 = null;
				Microsoft.Office.Interop.Word.Range value4 = null;
				try
				{
					value3 = value2[j];
					value4 = value3.Range;
					int num2 = Math.Max(value4.Start, val);
					int num3 = Math.Min(value4.End, val2);
					if (num3 <= num2)
					{
						continue;
					}
					Microsoft.Office.Interop.Word.Range value5 = null;
					try
					{
						object End = num2;
						object Start = num3;
						value5 = document.Range(ref End, ref Start);
						list2.Add(CreateSnapshot(num2, num3, value5.Text, WordRangeInspector.IsInTable(value3)));
					}
					finally
					{
						if (value5 != null)
						{
							ComObjectRelease.Release(ref value5, "AttachmentListNormalizationService.clipped");
						}
					}
				}
				finally
				{
					if (value4 != null)
					{
						ComObjectRelease.Release(ref value4, "AttachmentListNormalizationService.range");
					}
					if (value3 != null)
					{
						ComObjectRelease.Release(ref value3, "AttachmentListNormalizationService.paragraph");
					}
				}
			}
			return list2;
		}
		finally
		{
			if (value2 != null)
			{
				ComObjectRelease.Release(ref value2, "AttachmentListNormalizationService.paragraphs");
			}
		}
	}

	internal static ParagraphSnapshot CreateSnapshot(int start, int end, string rawText, bool isInTable)
	{
		string text = rawText ?? string.Empty;
		int i;
		for (i = 0; i < text.Length && IsTrimCharacter(text[i]); i++)
		{
		}
		int num = text.Length;
		while (num > i && IsTrimCharacter(text[num - 1]))
		{
			num--;
		}
		return new ParagraphSnapshot
		{
			Start = start,
			End = end,
			VisibleStart = start + i,
			VisibleEnd = start + num,
			VisibleText = text.Substring(i, num - i),
			IsInTable = isInTable
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static List<TextEdit> BuildPlan(IList<ParagraphSnapshot> paragraphs)
	{
		List<TextEdit> list = new List<TextEdit>();
		bool flag = false;
		for (int i = 0; i < paragraphs.Count; i++)
		{
			ParagraphSnapshot paragraphSnapshot = paragraphs[i];
			if (paragraphSnapshot != null && !paragraphSnapshot.IsInTable && !string.IsNullOrEmpty(paragraphSnapshot.VisibleText))
			{
				string visibleText = paragraphSnapshot.VisibleText;
				if (MarkerAloneRegex.IsMatch(visibleText) && i + 1 < paragraphs.Count)
				{
					ParagraphSnapshot paragraphSnapshot2 = paragraphs[i + 1];
					if (paragraphSnapshot2 != null && !paragraphSnapshot2.IsInTable && FirstErrorRegex.IsMatch(paragraphSnapshot2.VisibleText ?? string.Empty))
					{
						AddMinimalTextEdit(list, paragraphSnapshot2, NormalizeItem(paragraphSnapshot2.VisibleText));
						if (paragraphSnapshot2.VisibleStart > paragraphSnapshot.VisibleEnd)
						{
							list.Add(new TextEdit
							{
								Start = paragraphSnapshot.VisibleEnd,
								End = paragraphSnapshot2.VisibleStart,
								Replacement = string.Empty
							});
						}
						flag = true;
						i++;
						continue;
					}
				}
				Match match = FirstInlineRegex.Match(visibleText);
				if (!match.Success)
				{
					if (SingleRegex.IsMatch(visibleText))
					{
						flag = false;
					}
					else if (flag && NextRegex.IsMatch(visibleText))
					{
						AddMinimalTextEdit(list, paragraphSnapshot, NormalizeItem(visibleText));
					}
					else
					{
						flag = false;
					}
				}
				else
				{
					string corrected = match.Groups["prefix"].Value + "1." + match.Groups["body"].Value.TrimStart(Array.Empty<char>());
					AddMinimalTextEdit(list, paragraphSnapshot, corrected);
					flag = true;
				}
			}
			else
			{
				flag = false;
			}
		}
		return list;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string NormalizeItem(string text)
	{
		Match match = ItemRegex.Match(text ?? string.Empty);
		if (!match.Success)
		{
			return text;
		}
		return match.Groups["num"].Value + "." + match.Groups["body"].Value.TrimStart(Array.Empty<char>());
	}

	private static void AddMinimalTextEdit(List<TextEdit> edits, ParagraphSnapshot paragraph, string corrected)
	{
		string text = paragraph.VisibleText ?? string.Empty;
		corrected = corrected ?? string.Empty;
		if (!string.Equals(text, corrected, StringComparison.Ordinal))
		{
			int i = 0;
			for (int num = Math.Min(text.Length, corrected.Length); i < num && text[i] == corrected[i]; i++)
			{
			}
			int j = 0;
			int num2 = text.Length - i;
			for (int num3 = corrected.Length - i; j < num2 && j < num3 && text[text.Length - 1 - j] == corrected[corrected.Length - 1 - j]; j++)
			{
			}
			edits.Add(new TextEdit
			{
				Start = paragraph.VisibleStart + i,
				End = paragraph.VisibleEnd - j,
				Replacement = corrected.Substring(i, corrected.Length - i - j)
			});
		}
	}

	private static bool IsTrimCharacter(char value)
	{
		if (value != '\r' && value != '\n' && value != '\a' && value != ' ' && value != '\t' && value != '\u00a0')
		{
			return value == '\u3000';
		}
		return true;
	}
}
