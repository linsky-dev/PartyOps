using System;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using DocumentRepository.Models.RedHeader;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.RedHeader;

internal static class RedHeaderGeneratedObjectIdentityService
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static void RegisterSourceBody(Document document, RedHeaderLayoutPlan plan, int sourceStart)
	{
		if (document == null || plan == null)
		{
			return;
		}
		Bookmarks value = null;
		Bookmark value2 = null;
		Microsoft.Office.Interop.Word.Range value3 = null;
		Microsoft.Office.Interop.Word.Range value4 = null;
		try
		{
			value3 = document.Content;
			int num = Math.Min(value3.End, sourceStart + 1);
			object Start = sourceStart;
			object End = num;
			value4 = document.Range(ref Start, ref End);
			value = document.Bookmarks;
			if (value.Exists(plan.SourceBodyBookmarkName))
			{
				Bookmarks bookmarks = value;
				End = plan.SourceBodyBookmarkName;
				value2 = bookmarks.get_Item(ref End);
				value2.Delete();
				ComObjectRelease.Release(ref value2, "RedHeaderIdentity.PreviousSourceBookmark");
			}
			object Range = value4;
			value2 = value.Add(plan.SourceBodyBookmarkName, ref Range);
		}
		finally
		{
			ComObjectRelease.Release(ref value2, "RedHeaderIdentity.SourceBookmark");
			ComObjectRelease.Release(ref value, "RedHeaderIdentity.SourceBookmarks");
			ComObjectRelease.Release(ref value4, "RedHeaderIdentity.SourceStartRange");
			ComObjectRelease.Release(ref value3, "RedHeaderIdentity.SourceDocumentRange");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static Microsoft.Office.Interop.Word.Range ResolveSourceBody(Document document, RedHeaderLayoutPlan plan)
	{
		Bookmarks value = null;
		Bookmark value2 = null;
		Microsoft.Office.Interop.Word.Range value3 = null;
		try
		{
			value = document.Bookmarks;
			if (value != null && value.Exists(plan.SourceBodyBookmarkName))
			{
				Bookmarks bookmarks = value;
				object Index = plan.SourceBodyBookmarkName;
				value2 = bookmarks.get_Item(ref Index);
				value3 = value2.Range;
				Microsoft.Office.Interop.Word.Range result = value3;
				value3 = null;
				return result;
			}
			return null;
		}
		finally
		{
			ComObjectRelease.Release(ref value3, "RedHeaderIdentity.ResolvedSourceRange");
			ComObjectRelease.Release(ref value2, "RedHeaderIdentity.ResolvedSourceBookmark");
			ComObjectRelease.Release(ref value, "RedHeaderIdentity.ResolveSourceBookmarks");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static void RegisterImprintTable(Document document, Table table, RedHeaderLayoutPlan plan)
	{
		if (document == null || table == null || plan == null)
		{
			return;
		}
		Bookmarks value = null;
		Bookmark value2 = null;
		Microsoft.Office.Interop.Word.Range value3 = null;
		try
		{
			value = document.Bookmarks;
			value3 = table.Range;
			if (value.Exists(plan.ImprintBookmarkName))
			{
				Bookmarks bookmarks = value;
				object Index = plan.ImprintBookmarkName;
				value2 = bookmarks.get_Item(ref Index);
				value2.Delete();
				ComObjectRelease.Release(ref value2, "RedHeaderIdentity.PreviousImprintBookmark");
			}
			object Range = value3;
			value2 = value.Add(plan.ImprintBookmarkName, ref Range);
		}
		finally
		{
			ComObjectRelease.Release(ref value2, "RedHeaderIdentity.ImprintBookmark");
			ComObjectRelease.Release(ref value3, "RedHeaderIdentity.ImprintRange");
			ComObjectRelease.Release(ref value, "RedHeaderIdentity.Bookmarks");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static Table ResolveImprintTable(Document document, RedHeaderLayoutPlan plan)
	{
		if (document == null || plan == null)
		{
			return null;
		}
		Table table = ResolveByBookmark(document, plan.ImprintBookmarkName);
		if (table == null)
		{
			Tables value = null;
			Table value2 = null;
			Table value3 = null;
			int num = 0;
			try
			{
				value = document.Tables;
				int num2 = value?.Count ?? 0;
				for (int i = 1; i <= num2; i++)
				{
					try
					{
						value2 = value[i];
						if (MatchesPlannedCells(value2, plan))
						{
							num++;
							if (num == 1)
							{
								value3 = value2;
								value2 = null;
							}
						}
					}
					catch (Exception ex)
					{
						LogService.Warn("REDHEADER-IDENTITY imprint-candidate-indeterminate, tableOrdinal=" + i, ex);
					}
					finally
					{
						ComObjectRelease.Release(ref value2, "RedHeaderIdentity.Candidate");
					}
				}
				if (num != 1)
				{
					ComObjectRelease.Release(ref value3, "RedHeaderIdentity.AmbiguousMatch");
					LogService.Warn("REDHEADER-IDENTITY imprint-unresolved, identityCandidates=" + num);
					return null;
				}
				RegisterImprintTable(document, value3, plan);
				return value3;
			}
			finally
			{
				ComObjectRelease.Release(ref value2, "RedHeaderIdentity.Candidate.Finally");
				ComObjectRelease.Release(ref value, "RedHeaderIdentity.Tables");
			}
		}
		return table;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static void RegisterGeneratedBreak(Document document, int position, RedHeaderLayoutPlan plan, int ordinal)
	{
		Bookmarks value = null;
		Bookmark value2 = null;
		Microsoft.Office.Interop.Word.Range value3 = null;
		try
		{
			int end = document.Content.End;
			int num = Math.Max(0, Math.Min(position, end - 1));
			object Start = num;
			object End = Math.Min(end, num + 1);
			value3 = document.Range(ref Start, ref End);
			value = document.Bookmarks;
			object Range = value3;
			value2 = value.Add(plan.GeneratedBreakBookmarkPrefix + "_" + ordinal, ref Range);
		}
		finally
		{
			ComObjectRelease.Release(ref value2, "RedHeaderIdentity.BreakBookmark");
			ComObjectRelease.Release(ref value, "RedHeaderIdentity.BreakBookmarks");
			ComObjectRelease.Release(ref value3, "RedHeaderIdentity.BreakRange");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static string ReadNormalizedCell(Table table, int row, int column)
	{
		Cell value = null;
		Microsoft.Office.Interop.Word.Range value2 = null;
		try
		{
			value = table.Cell(row, column);
			value2 = value.Range;
			return NormalizeCellText(value2?.Text);
		}
		finally
		{
			ComObjectRelease.Release(ref value2, "RedHeaderIdentity.CellRange");
			ComObjectRelease.Release(ref value, "RedHeaderIdentity.Cell");
		}
	}

	internal static string NormalizeCellText(string value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return string.Empty;
		}
		StringBuilder stringBuilder = new StringBuilder(value.Length);
		bool flag = false;
		foreach (char c in value)
		{
			if (c == '\a' || c == '\r' || c == '\n' || c == '\t' || char.IsWhiteSpace(c))
			{
				if (stringBuilder.Length > 0)
				{
					flag = true;
				}
				continue;
			}
			if (flag && stringBuilder.Length > 0)
			{
				stringBuilder.Append(' ');
			}
			flag = false;
			stringBuilder.Append(c);
		}
		return stringBuilder.ToString().Trim();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static void LogCellDifference(string stage, string expected, string actual, int rereadCount, int rewriteCount)
	{
		expected = expected ?? string.Empty;
		actual = actual ?? string.Empty;
		int index = FirstDifference(expected, actual);
		LogService.Warn("REDHEADER-QUALITY imprint-cell-difference, stage=" + SafeStage(stage) + ", expectedLength=" + expected.Length + ", actualLength=" + actual.Length + ", expectedHash=" + Hash(expected) + ", actualHash=" + Hash(actual) + ", firstDifference=" + index + ", expectedCategory=" + CategoryAt(expected, index) + ", actualCategory=" + CategoryAt(actual, index) + ", rereadCount=" + rereadCount + ", rewriteCount=" + rewriteCount);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static Table ResolveByBookmark(Document document, string bookmarkName)
	{
		Bookmarks value = null;
		Bookmark value2 = null;
		Microsoft.Office.Interop.Word.Range value3 = null;
		Tables value4 = null;
		Table value5 = null;
		try
		{
			value = document.Bookmarks;
			if (value == null || !value.Exists(bookmarkName))
			{
				return null;
			}
			Bookmarks bookmarks = value;
			object Index = bookmarkName;
			value2 = bookmarks.get_Item(ref Index);
			value3 = value2.Range;
			value4 = value3.Tables;
			if (value4 != null && value4.Count == 1)
			{
				value5 = value4[1];
				Table result = value5;
				value5 = null;
				return result;
			}
			return null;
		}
		finally
		{
			ComObjectRelease.Release(ref value5, "RedHeaderIdentity.BookmarkTable");
			ComObjectRelease.Release(ref value4, "RedHeaderIdentity.BookmarkTables");
			ComObjectRelease.Release(ref value3, "RedHeaderIdentity.BookmarkRange");
			ComObjectRelease.Release(ref value2, "RedHeaderIdentity.Bookmark");
			ComObjectRelease.Release(ref value, "RedHeaderIdentity.ResolveBookmarks");
		}
	}

	private static bool MatchesPlannedCells(Table table, RedHeaderLayoutPlan plan)
	{
		string a = NormalizeCellText(plan.ImprintOfficeLine);
		string b = ReadNormalizedCell(table, plan.ImprintOfficeRow, 1);
		if (!string.Equals(a, b, StringComparison.Ordinal))
		{
			return false;
		}
		if (!plan.HasImprintSend)
		{
			return true;
		}
		string a2 = NormalizeCellText(plan.ImprintSendText);
		string b2 = ReadNormalizedCell(table, 1, 1);
		return string.Equals(a2, b2, StringComparison.Ordinal);
	}

	private static int FirstDifference(string expected, string actual)
	{
		int num = Math.Min(expected.Length, actual.Length);
		int num2 = 0;
		while (true)
		{
			if (num2 < num)
			{
				if (expected[num2] != actual[num2])
				{
					break;
				}
				num2++;
				continue;
			}
			if (expected.Length != actual.Length)
			{
				return num;
			}
			return -1;
		}
		return num2;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string CategoryAt(string value, int index)
	{
		if (index < 0 || index >= value.Length)
		{
			return "end";
		}
		return char.GetUnicodeCategory(value[index]).ToString() + "-U+" + ((int)value[index]).ToString("X4");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string Hash(string value)
	{
		using SHA256 sHA = SHA256.Create();
		byte[] array = sHA.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
		StringBuilder stringBuilder = new StringBuilder(array.Length * 2);
		byte[] array2 = array;
		foreach (byte b in array2)
		{
			stringBuilder.Append(b.ToString("x2"));
		}
		return stringBuilder.ToString();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string SafeStage(string stage)
	{
		if (!string.Equals(stage, "office", StringComparison.Ordinal))
		{
			return "send";
		}
		return "office";
	}
}
