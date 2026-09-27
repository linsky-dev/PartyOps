using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace DocumentRepository.Services.Detection.Tables;

public static class TableIdentityComparer
{
	public static string NormalizeContentForIdentity(string value)
	{
		return NormalizeContentForIdentity(value, value?.Length ?? 0, null);
	}

	public static string NormalizeContentForIdentity(string value, int rangeLength, IEnumerable<int> inlineObjectRangeOffsets)
	{
		if (string.IsNullOrEmpty(value))
		{
			return string.Empty;
		}
		bool[] array = new bool[value.Length];
		if (inlineObjectRangeOffsets != null)
		{
			foreach (int inlineObjectRangeOffset in inlineObjectRangeOffsets)
			{
				if (TryMapRangeOffsetToTextOffset(value, rangeLength, inlineObjectRangeOffset, out var textOffset) && textOffset >= 0 && textOffset < value.Length && value[textOffset] != '\r' && value[textOffset] != '\a')
				{
					array[textOffset] = true;
				}
			}
		}
		StringBuilder stringBuilder = new StringBuilder(value.Length);
		for (int i = 0; i < value.Length; i++)
		{
			if (!array[i])
			{
				char c = value[i];
				if (c != '\u0001' && c != '\b' && c != '￼')
				{
					stringBuilder.Append(c);
				}
			}
		}
		return stringBuilder.ToString();
	}

	private static bool TryMapRangeOffsetToTextOffset(string value, int rangeLength, int rangeOffset, out int textOffset)
	{
		textOffset = -1;
		if (rangeOffset < 0 || rangeLength < 0 || rangeOffset >= rangeLength)
		{
			return false;
		}
		int num = 0;
		for (int i = 1; i < value.Length; i++)
		{
			if (value[i] == '\a' && value[i - 1] == '\r')
			{
				num++;
			}
		}
		bool flag = rangeLength == value.Length - num;
		if (flag || rangeLength == value.Length)
		{
			int num2 = rangeOffset;
			if (flag)
			{
				for (int j = 1; j < value.Length; j++)
				{
					if (value[j] == '\a' && value[j - 1] == '\r')
					{
						if (j > num2)
						{
							break;
						}
						num2++;
					}
				}
			}
			if (num2 < 0 || num2 >= value.Length)
			{
				return false;
			}
			textOffset = num2;
			return true;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string BuildStructureFingerprint(TableElementInfo item)
	{
		if (item == null)
		{
			return string.Empty;
		}
		return "story=" + item.StoryTypeCode + "|rows=" + ReliableValue(item.RowCountReliable, item.RowCount) + "|columns=" + ReliableValue(item.ColumnCountReliable, item.ColumnCount) + "|cells=" + ReliableValue(item.CellCountReliable, item.CellCount) + "|nested=" + ReliableValue(item.NestedTableCountReliable, item.NestedTableCount);
	}

	public static bool HasSameStableIdentity(TableElementInfo expected, TableElementInfo actual)
	{
		if (expected == null || actual == null)
		{
			return false;
		}
		if (expected.ContentIdentityReliable && actual.ContentIdentityReliable && !string.IsNullOrWhiteSpace(expected.ContentHash) && !string.IsNullOrWhiteSpace(actual.ContentHash) && string.Equals(expected.ContentHash, actual.ContentHash, StringComparison.Ordinal) && expected.ContentLength == actual.ContentLength && expected.StoryTypeCode == actual.StoryTypeCode)
		{
			if (OptionalCountMatches(expected.RowCountReliable, expected.RowCount, actual.RowCountReliable, actual.RowCount) && OptionalCountMatches(expected.ColumnCountReliable, expected.ColumnCount, actual.ColumnCountReliable, actual.ColumnCount) && OptionalCountMatches(expected.CellCountReliable, expected.CellCount, actual.CellCountReliable, actual.CellCount))
			{
				return OptionalCountMatches(expected.NestedTableCountReliable, expected.NestedTableCount, actual.NestedTableCountReliable, actual.NestedTableCount);
			}
			return false;
		}
		return false;
	}

	public static bool OptionalCountMatches(bool expectedReliable, int expected, bool actualReliable, int actual)
	{
		if (expectedReliable && actualReliable)
		{
			return expected == actual;
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string ReliableValue(bool reliable, int value)
	{
		if (!reliable)
		{
			return "?";
		}
		return value.ToString();
	}
}
