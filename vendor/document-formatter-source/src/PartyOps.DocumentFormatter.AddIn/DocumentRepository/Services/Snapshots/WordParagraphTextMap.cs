using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Services.Snapshots;

internal static class WordParagraphTextMap
{
	internal sealed class Entry
	{
		public int Index { get; set; }

		public int RangeStart { get; set; }

		public int RangeEnd { get; set; }

		public string Text { get; set; }
	}

	internal sealed class Result
	{
		public bool IsReliable { get; set; }

		public string FailureReason { get; set; }

		public List<Entry> Paragraphs { get; } = new List<Entry>();
	}

	internal sealed class TextSplitResult
	{
		public bool IsReliable { get; set; }

		public string FailureReason { get; set; }

		public List<string> Paragraphs { get; } = new List<string>();
	}

	private sealed class TextSpan
	{
		public int Start { get; set; }

		public int End { get; set; }

		public bool EndsAtTableCell { get; set; }
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static Result Build(string scopeText, int scopeStart, int scopeEnd, int expectedParagraphCount)
	{
		Result result = new Result();
		string text = scopeText ?? string.Empty;
		if (scopeStart >= 0 && scopeEnd >= scopeStart)
		{
			string failureReason;
			List<TextSpan> list = SelectReliableSpans(text, expectedParagraphCount, out failureReason);
			if (list == null)
			{
				return Fail(result, failureReason);
			}
			int num = scopeStart;
			foreach (TextSpan item in list)
			{
				int num2 = text.IndexOf('\a', item.Start, item.End - item.Start);
				if (num2 < 0 || (item.EndsAtTableCell && num2 == item.End - 1))
				{
					int num3 = item.End - item.Start;
					int num4 = num3 - (item.EndsAtTableCell ? 1 : 0);
					int num5 = num + num4;
					if (num4 <= 0 || num5 > scopeEnd)
					{
						return Fail(result, "range-out-of-scope " + num + "-" + num5 + ", scope=" + scopeStart + "-" + scopeEnd);
					}
					result.Paragraphs.Add(new Entry
					{
						Index = result.Paragraphs.Count + 1,
						RangeStart = num,
						RangeEnd = num5,
						Text = text.Substring(item.Start, num3)
					});
					num = num5;
					continue;
				}
				return Fail(result, "unexpected-table-cell-marker at text-offset=" + num2);
			}
			if (num != scopeEnd)
			{
				return Fail(result, "scope-end-mismatch mapped=" + num + ", word=" + scopeEnd);
			}
			result.IsReliable = true;
			return result;
		}
		return Fail(result, "invalid-scope " + scopeStart + "-" + scopeEnd);
	}

	internal static TextSplitResult SplitParagraphTexts(string scopeText, int expectedParagraphCount)
	{
		TextSplitResult textSplitResult = new TextSplitResult();
		string text = scopeText ?? string.Empty;
		string failureReason;
		List<TextSpan> list = SelectReliableSpans(text, expectedParagraphCount, out failureReason);
		if (list == null)
		{
			textSplitResult.FailureReason = failureReason;
			return textSplitResult;
		}
		foreach (TextSpan item in list)
		{
			textSplitResult.Paragraphs.Add(text.Substring(item.Start, item.End - item.Start));
		}
		textSplitResult.IsReliable = true;
		return textSplitResult;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static List<TextSpan> SelectReliableSpans(string full, int expectedParagraphCount, out string failureReason)
	{
		failureReason = null;
		if (expectedParagraphCount >= 0)
		{
			List<TextSpan> list = SplitIntoSpans(full, splitBareFormFeed: false);
			if (list.Count != expectedParagraphCount)
			{
				List<TextSpan> list2 = SplitIntoSpans(full, splitBareFormFeed: true);
				if (list2.Count == expectedParagraphCount)
				{
					return list2;
				}
				failureReason = "paragraph-count-mismatch ordinary=" + list.Count + ", section-aware=" + list2.Count + ", word=" + expectedParagraphCount;
				return null;
			}
			return list;
		}
		failureReason = "invalid-expected-paragraph-count " + expectedParagraphCount;
		return null;
	}

	private static List<TextSpan> SplitIntoSpans(string full, bool splitBareFormFeed)
	{
		List<TextSpan> list = new List<TextSpan>();
		int num = 0;
		while (num < full.Length)
		{
			int num2 = full.IndexOf('\r', num);
			int num3 = (splitBareFormFeed ? full.IndexOf('\f', num) : (-1));
			bool flag = false;
			int num4;
			if (num3 >= 0 && (num2 < 0 || num3 < num2) && (num3 + 1 >= full.Length || full[num3 + 1] != '\r'))
			{
				num4 = num3 + 1;
				flag = true;
			}
			else
			{
				num4 = ((num2 < 0) ? full.Length : (num2 + 1));
			}
			bool flag2 = !flag && num4 < full.Length && full[num4] == '\a';
			if (flag2)
			{
				num4++;
			}
			list.Add(new TextSpan
			{
				Start = num,
				End = num4,
				EndsAtTableCell = flag2
			});
			num = num4;
		}
		return list;
	}

	private static Result Fail(Result result, string reason)
	{
		result.IsReliable = false;
		result.FailureReason = reason;
		result.Paragraphs.Clear();
		return result;
	}
}
