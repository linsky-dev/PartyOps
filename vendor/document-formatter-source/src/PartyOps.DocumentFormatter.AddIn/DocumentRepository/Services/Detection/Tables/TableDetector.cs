using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Detection.Tables;

public static class TableDetector
{
	public static bool HasTables(Document doc)
	{
		return AnalyzeDocument(doc).HasTables;
	}

	public static bool HasTables(Microsoft.Office.Interop.Word.Range range)
	{
		return AnalyzeRange(range).HasTables;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static TableAnalysisResult AnalyzeDocument(Document doc)
	{
		TableAnalysisResult tableAnalysisResult = new TableAnalysisResult();
		if (doc == null)
		{
			return tableAnalysisResult;
		}
		Microsoft.Office.Interop.Word.Tables value = null;
		try
		{
			value = doc.Tables;
			int num = (tableAnalysisResult.TableCount = value?.Count ?? 0);
			for (int i = 1; i <= num; i++)
			{
				Table value2 = null;
				try
				{
					value2 = value[i];
					tableAnalysisResult.Tables.Add(ReadTableFacts(value2, i));
				}
				catch (Exception innerException)
				{
					throw new InvalidOperationException("读取文档第 " + i + " 个表格事实失败，表格总数=" + num + "。", innerException);
				}
				finally
				{
					if (value2 != null)
					{
						ComObjectRelease.Release(ref value2, "TableDetector.table");
					}
				}
			}
			return tableAnalysisResult;
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "TableDetector.tables");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static TableAnalysisResult AnalyzeRange(Microsoft.Office.Interop.Word.Range range)
	{
		TableAnalysisResult tableAnalysisResult = new TableAnalysisResult();
		if (range == null)
		{
			return tableAnalysisResult;
		}
		Microsoft.Office.Interop.Word.Tables value = null;
		try
		{
			value = range.Tables;
			int num = (tableAnalysisResult.TableCount = value?.Count ?? 0);
			for (int i = 1; i <= num; i++)
			{
				Table value2 = null;
				try
				{
					value2 = value[i];
					tableAnalysisResult.Tables.Add(ReadTableFacts(value2, i));
				}
				catch (Exception innerException)
				{
					throw new InvalidOperationException("读取选区第 " + i + " 个表格事实失败，表格总数=" + num + "。", innerException);
				}
				finally
				{
					if (value2 != null)
					{
						ComObjectRelease.Release(ref value2, "TableDetector.table");
					}
				}
			}
			return tableAnalysisResult;
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "TableDetector.tables");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static TableElementInfo ReadTableFacts(Table table, int index)
	{
		if (table != null)
		{
			TableElementInfo tableElementInfo = new TableElementInfo
			{
				AnalysisOrdinal = index
			};
			ReadRangeFacts(table, tableElementInfo, index);
			tableElementInfo.RowCount = ReadOptionalCount(() => ReadRowCount(table), "TableDetector.RowCount", index, out var reliable);
			tableElementInfo.RowCountReliable = reliable;
			tableElementInfo.ColumnCount = ReadOptionalCount(() => ReadColumnCount(table), "TableDetector.ColumnCount", index, out var reliable2);
			tableElementInfo.ColumnCountReliable = reliable2;
			tableElementInfo.HasHeaderCandidate = tableElementInfo.RowCount > 0;
			tableElementInfo.StructureFingerprint = TableIdentityComparer.BuildStructureFingerprint(tableElementInfo);
			return tableElementInfo;
		}
		throw new ArgumentNullException("table");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ReadOptionalCount(Func<int> reader, string action, int tableOrdinal, out bool reliable)
	{
		for (int i = 0; i < 2; i++)
		{
			try
			{
				int result = reader();
				reliable = true;
				return result;
			}
			catch (Exception ex)
			{
				LogService.Warn(action + ", analysisOrdinal=" + tableOrdinal + ", attempt=" + (i + 1), ex);
			}
		}
		reliable = false;
		return 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ReadRangeFacts(Table table, TableElementInfo info, int index)
	{
		for (int i = 0; i < 2; i++)
		{
			Microsoft.Office.Interop.Word.Range range = null;
			try
			{
				range = table.Range;
				int start = range.Start;
				int end = range.End;
				int storyType = (int)range.StoryType;
				string text = TableIdentityComparer.NormalizeContentForIdentity(range.Text, end - start, ReadInlineObjectOffsets(range, start));
				bool reliable;
				int cellCount = ReadOptionalCount(() => ReadCellCount(range), "TableDetector.CellCount", index, out reliable);
				bool reliable2;
				int nestedTableCount = ReadOptionalCount(() => ReadNestedTableCount(range), "TableDetector.NestedTableCount", index, out reliable2);
				info.RangeStart = start;
				info.RangeEnd = end;
				info.StoryTypeCode = storyType;
				info.ContentLength = text.Length;
				info.ContentHash = Hash(text);
				info.ContentIdentityReliable = true;
				info.CellCount = cellCount;
				info.CellCountReliable = reliable;
				info.NestedTableCount = nestedTableCount;
				info.NestedTableCountReliable = reliable2;
				return;
			}
			catch (Exception ex)
			{
				LogService.Warn("TableDetector.RangeFacts, analysisOrdinal=" + index + ", attempt=" + (i + 1), ex);
			}
			finally
			{
				ComObjectRelease.Release(ref range, "TableDetector.range");
			}
		}
		info.ContentIdentityReliable = false;
		info.ContentHash = null;
		info.ContentLength = 0;
		info.CellCountReliable = false;
		info.NestedTableCountReliable = false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ReadRowCount(Table table)
	{
		Rows value = null;
		try
		{
			value = table.Rows;
			return value?.Count ?? 0;
		}
		finally
		{
			ComObjectRelease.Release(ref value, "TableDetector.rows");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ReadColumnCount(Table table)
	{
		Columns value = null;
		try
		{
			value = table.Columns;
			return value?.Count ?? 0;
		}
		finally
		{
			ComObjectRelease.Release(ref value, "TableDetector.columns");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ReadCellCount(Microsoft.Office.Interop.Word.Range range)
	{
		Cells value = null;
		try
		{
			value = range.Cells;
			return value?.Count ?? 0;
		}
		finally
		{
			ComObjectRelease.Release(ref value, "TableDetector.cells");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ReadNestedTableCount(Microsoft.Office.Interop.Word.Range range)
	{
		Microsoft.Office.Interop.Word.Tables value = null;
		try
		{
			value = range.Tables;
			return Math.Max(0, (value?.Count ?? 0) - 1);
		}
		finally
		{
			ComObjectRelease.Release(ref value, "TableDetector.nestedTables");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static IEnumerable<int> ReadInlineObjectOffsets(Microsoft.Office.Interop.Word.Range range, int tableStart)
	{
		List<int> list = new List<int>();
		InlineShapes value = null;
		try
		{
			value = range.InlineShapes;
			int num = value?.Count ?? 0;
			for (int i = 1; i <= num; i++)
			{
				InlineShape value2 = null;
				Microsoft.Office.Interop.Word.Range value3 = null;
				try
				{
					value2 = value[i];
					value3 = value2.Range;
					if (value3.End > value3.Start)
					{
						list.Add(value3.Start - tableStart);
					}
				}
				finally
				{
					ComObjectRelease.Release(ref value3, "TableDetector.inlineRange");
					ComObjectRelease.Release(ref value2, "TableDetector.inlineShape");
				}
			}
		}
		catch (Exception ex)
		{
			LogService.Warn("TableDetector.InlineObjectOffsets", ex);
			list.Clear();
		}
		finally
		{
			ComObjectRelease.Release(ref value, "TableDetector.inlineShapes");
		}
		return list;
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
}
