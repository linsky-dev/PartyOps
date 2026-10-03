using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Services.Analysis;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Interop;

public static class WordRangeInspector
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool IsInTable(Microsoft.Office.Interop.Word.Range range)
	{
		if (range == null)
		{
			return false;
		}
		Tables value = null;
		try
		{
			value = range.Tables;
			return value != null && value.Count > 0;
		}
		catch
		{
			return false;
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "WordRangeInspector.tables");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool HasInlineShape(Microsoft.Office.Interop.Word.Range range)
	{
		if (range == null)
		{
			return false;
		}
		InlineShapes value = null;
		try
		{
			value = range.InlineShapes;
			return value != null && value.Count > 0;
		}
		catch
		{
			return false;
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "WordRangeInspector.shapes");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool HasShape(Microsoft.Office.Interop.Word.Range range)
	{
		if (range == null)
		{
			return false;
		}
		ShapeRange value = null;
		try
		{
			value = range.ShapeRange;
			return value != null && value.Count > 0;
		}
		catch
		{
			return false;
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "WordRangeInspector.shapes");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool HasInlineOrAnchoredShape(Microsoft.Office.Interop.Word.Range range)
	{
		if (range == null)
		{
			return false;
		}
		InlineShapes value = null;
		ShapeRange value2 = null;
		try
		{
			try
			{
				value = range.InlineShapes;
				if (value != null && value.Count > 0)
				{
					return true;
				}
			}
			catch (Exception ex)
			{
				LogService.Warn("WordRangeInspector.inline-shape-indeterminate", ex);
				return true;
			}
			finally
			{
				ComObjectRelease.Release(ref value, "WordRangeInspector.inlineShapes");
			}
			value2 = range.ShapeRange;
			return value2 != null && value2.Count > 0;
		}
		catch (Exception ex2)
		{
			LogService.Warn("WordRangeInspector.floating-shape-indeterminate", ex2);
			return true;
		}
		finally
		{
			ComObjectRelease.Release(ref value2, "WordRangeInspector.floatingShapes");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool HasInlineOrAnchoredShape(Paragraph para)
	{
		if (para == null)
		{
			return false;
		}
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			value = para.Range;
			return HasInlineOrAnchoredShape(value);
		}
		catch (Exception ex)
		{
			LogService.Warn("WordRangeInspector.paragraph-shape-indeterminate", ex);
			return true;
		}
		finally
		{
			ComObjectRelease.Release(ref value, "WordRangeInspector.paragraphRange");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool IsInTable(Paragraph para)
	{
		if (para == null)
		{
			return false;
		}
		Microsoft.Office.Interop.Word.Range value = null;
		Tables value2 = null;
		try
		{
			value = para.Range;
			value2 = value.Tables;
			return value2 != null && value2.Count > 0;
		}
		catch
		{
			return false;
		}
		finally
		{
			if (value2 != null)
			{
				ComObjectRelease.Release(ref value2, "WordRangeInspector.tables");
			}
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "WordRangeInspector.rng");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool HasInlineShape(Paragraph para)
	{
		if (para == null)
		{
			return false;
		}
		Microsoft.Office.Interop.Word.Range value = null;
		InlineShapes value2 = null;
		try
		{
			value = para.Range;
			value2 = value.InlineShapes;
			return value2 != null && value2.Count > 0;
		}
		catch
		{
			return false;
		}
		finally
		{
			if (value2 != null)
			{
				ComObjectRelease.Release(ref value2, "WordRangeInspector.shapes");
			}
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "WordRangeInspector.rng");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool HasShape(Paragraph para)
	{
		if (para == null)
		{
			return false;
		}
		Microsoft.Office.Interop.Word.Range value = null;
		ShapeRange value2 = null;
		try
		{
			value = para.Range;
			value2 = value.ShapeRange;
			return value2 != null && value2.Count > 0;
		}
		catch
		{
			return false;
		}
		finally
		{
			if (value2 != null)
			{
				ComObjectRelease.Release(ref value2, "WordRangeInspector.shapes");
			}
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "WordRangeInspector.rng");
			}
		}
	}

	public static bool ShouldSkipForSafeClean(Paragraph para, DocumentAnalysisResult analysis)
	{
		if (para == null)
		{
			return true;
		}
		if (analysis == null || !analysis.HasTables || !IsInTable(para))
		{
			if (analysis != null && analysis.HasImages && HasInlineOrAnchoredShape(para))
			{
				return true;
			}
			return false;
		}
		return true;
	}
}
