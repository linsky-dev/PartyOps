using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Cleanup;

public static class PageCleanupService
{
	private static readonly WdHeaderFooterIndex[] HeaderIndexes = new WdHeaderFooterIndex[3]
	{
		WdHeaderFooterIndex.wdHeaderFooterPrimary,
		WdHeaderFooterIndex.wdHeaderFooterEvenPages,
		WdHeaderFooterIndex.wdHeaderFooterFirstPage
	};

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ClearHeadersFootersContent(Document document)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		int count = document.Sections.Count;
		for (int i = 1; i <= count; i++)
		{
			Section value = null;
			try
			{
				value = document.Sections[i];
				ClearHeaderFooterRange(value.Headers[WdHeaderFooterIndex.wdHeaderFooterPrimary]);
				ClearHeaderFooterRange(value.Headers[WdHeaderFooterIndex.wdHeaderFooterEvenPages]);
				ClearHeaderFooterRange(value.Headers[WdHeaderFooterIndex.wdHeaderFooterFirstPage]);
				ClearHeaderFooterRange(value.Footers[WdHeaderFooterIndex.wdHeaderFooterPrimary]);
				ClearHeaderFooterRange(value.Footers[WdHeaderFooterIndex.wdHeaderFooterEvenPages]);
				ClearHeaderFooterRange(value.Footers[WdHeaderFooterIndex.wdHeaderFooterFirstPage]);
			}
			catch (Exception ex)
			{
				LogService.Warn("PageCleanupService.ClearHeadersFootersContent.Section", ex);
			}
			finally
			{
				if (value != null)
				{
					ComObjectRelease.Release(ref value, "PageCleanupService.section");
				}
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void NormalizeEmptyHeaderBorders(Document document)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		int count = document.Sections.Count;
		for (int i = 1; i <= count; i++)
		{
			Section value = null;
			try
			{
				value = document.Sections[i];
				WdHeaderFooterIndex[] headerIndexes = HeaderIndexes;
				foreach (WdHeaderFooterIndex index in headerIndexes)
				{
					NormalizeEmptyHeaderBorder(value.Headers[index]);
				}
			}
			catch (Exception ex)
			{
				LogService.Warn("PageCleanupService.NormalizeEmptyHeaderBorders.Section", ex);
			}
			finally
			{
				if (value != null)
				{
					ComObjectRelease.Release(ref value, "PageCleanupService.NormalizeEmptyHeaderBorders.section");
				}
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void NormalizeEmptyHeaderBorder(HeaderFooter headerFooter)
	{
		Microsoft.Office.Interop.Word.Range value = null;
		Fields value2 = null;
		InlineShapes value3 = null;
		Tables value4 = null;
		ContentControls value5 = null;
		Shapes value6 = null;
		Borders value7 = null;
		try
		{
			if (headerFooter == null || !headerFooter.Exists)
			{
				return;
			}
			value = headerFooter.Range;
			if ((value.Text ?? string.Empty).Trim(new char[] { '\r', '\n', '\u0007', '\v', '\f', ' ', '\t', '\u00A0', '\u3000' }).Length > 0)
			{
				return;
			}
			value2 = value.Fields;
			if (value2 != null && value2.Count > 0)
			{
				return;
			}
			value3 = value.InlineShapes;
			if (value3 != null && value3.Count > 0)
			{
				return;
			}
			value4 = value.Tables;
			if (value4 != null && value4.Count > 0)
			{
				return;
			}
			value5 = value.ContentControls;
			if (value5 != null && value5.Count > 0)
			{
				return;
			}
			value6 = headerFooter.Shapes;
			if (value6 == null || value6.Count <= 0)
			{
				value7 = value.Borders;
				if (value7.Enable != 0)
				{
					value7.Enable = 0;
				}
			}
		}
		catch (Exception ex)
		{
			LogService.Warn("PageCleanupService.NormalizeEmptyHeaderBorder", ex);
		}
		finally
		{
			if (value7 != null)
			{
				ComObjectRelease.Release(ref value7, "PageCleanupService.NormalizeEmptyHeaderBorder.borders");
			}
			if (value6 != null)
			{
				ComObjectRelease.Release(ref value6, "PageCleanupService.NormalizeEmptyHeaderBorder.shapes");
			}
			if (value5 != null)
			{
				ComObjectRelease.Release(ref value5, "PageCleanupService.NormalizeEmptyHeaderBorder.contentControls");
			}
			if (value4 != null)
			{
				ComObjectRelease.Release(ref value4, "PageCleanupService.NormalizeEmptyHeaderBorder.tables");
			}
			if (value3 != null)
			{
				ComObjectRelease.Release(ref value3, "PageCleanupService.NormalizeEmptyHeaderBorder.inlineShapes");
			}
			if (value2 != null)
			{
				ComObjectRelease.Release(ref value2, "PageCleanupService.NormalizeEmptyHeaderBorder.fields");
			}
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "PageCleanupService.NormalizeEmptyHeaderBorder.range");
			}
			if (headerFooter != null)
			{
				ComObjectRelease.Release(ref headerFooter, "PageCleanupService.NormalizeEmptyHeaderBorder.headerFooter");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ClearHeaderFooterRange(HeaderFooter headerFooter)
	{
		Microsoft.Office.Interop.Word.Range value = null;
		Microsoft.Office.Interop.Word.Range value2 = null;
		try
		{
			if (headerFooter != null && headerFooter.Exists)
			{
				value = headerFooter.Range;
				int num = Math.Max(value.Start, value.End - 1);
				if (num > value.Start)
				{
					value2 = value.Duplicate;
					value2.SetRange(value.Start, num);
					Microsoft.Office.Interop.Word.Range range = value2;
					object Unit = Type.Missing;
					object Count = Type.Missing;
					range.Delete(ref Unit, ref Count);
				}
			}
		}
		catch (Exception ex)
		{
			LogService.Warn("PageCleanupService.ClearHeaderFooterRange", ex);
		}
		finally
		{
			if (value2 != null)
			{
				ComObjectRelease.Release(ref value2, "PageCleanupService.contentRange");
			}
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "PageCleanupService.range");
			}
			if (headerFooter != null)
			{
				ComObjectRelease.Release(ref headerFooter, "PageCleanupService.headerFooter");
			}
		}
	}
}
