using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DocumentRepository.Services.Detection.Tables;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.Safety;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Formatting.Tables;

public static class TableStyleApplier
{
	private const int CannotAccessIndividualRowsHResult = -2146822297;

	internal static Action<int> BeforeApplyTableForTesting;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ApplyToDocument(Document doc, Application app, TableStyleDefinition style, TableAnalysisResult analysis, DocumentHostKind hostKind)
	{
		if (doc != null)
		{
			if (app != null)
			{
				if (style == null)
				{
					throw new ArgumentNullException("style");
				}
				if (analysis == null)
				{
					throw new InvalidOperationException("Table formatting requires TableAnalysisResult.");
				}
				if (analysis.HasTables)
				{
					ValidateAnalysis(analysis, "document");
					ReadDocumentBounds(doc, out var documentStart, out var documentEnd);
					for (int i = 0; i < analysis.Tables.Count; i++)
					{
						TryResolveAndApply(doc, app, style, documentStart, documentEnd, analysis.Tables[i], "document", hostKind);
					}
				}
				return;
			}
			throw new ArgumentNullException("app");
		}
		throw new ArgumentNullException("doc");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ApplyToRange(Microsoft.Office.Interop.Word.Range range, Application app, TableStyleDefinition style, TableAnalysisResult analysis, DocumentHostKind hostKind)
	{
		if (range != null)
		{
			if (app == null)
			{
				throw new ArgumentNullException("app");
			}
			if (style == null)
			{
				throw new ArgumentNullException("style");
			}
			if (analysis != null)
			{
				if (analysis.HasTables)
				{
					ValidateAnalysis(analysis, "selection");
					Document document = null;
					document = range.Document;
					if (document == null)
					{
						throw new InvalidOperationException("Selection table formatting requires a range bound to a document.");
					}
					ReadDocumentBounds(document, out var documentStart, out var documentEnd);
					for (int i = 0; i < analysis.Tables.Count; i++)
					{
						TryResolveAndApply(document, app, style, documentStart, documentEnd, analysis.Tables[i], "selection", hostKind);
					}
				}
				return;
			}
			throw new InvalidOperationException("Selection table formatting requires TableAnalysisResult.");
		}
		throw new ArgumentNullException("range");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool TryResolveAndApply(Document document, Application application, TableStyleDefinition style, int documentStart, int documentEnd, TableElementInfo tableInfo, string scope, DocumentHostKind hostKind)
	{
		if (ShouldSkipKnownComplexWpsTable(tableInfo, hostKind))
		{
			ReportWarning("format-table-complex-skipped", tableInfo, "wps-nested-table");
			return false;
		}
		for (int i = 0; i < 2; i++)
		{
			Table value = null;
			try
			{
				value = TableExecutionTargetResolver.Resolve(document, tableInfo, scope, documentEnd);
				if (value == null)
				{
					ReportWarning("format-table-target-unresolved", tableInfo, "target-unresolved");
					return false;
				}
				BeforeApplyTableForTesting?.Invoke(tableInfo.AnalysisOrdinal);
				ApplyTable(value, application, style, documentStart, tableInfo, hostKind);
				return true;
			}
			catch (Exception ex)
			{
				if (i != 0)
				{
					LogService.Warn("TABLE-FORMAT property-write-skipped, analysisOrdinal=" + tableInfo.AnalysisOrdinal, ex);
					ReportWarning("format-table-property-write-skipped", tableInfo, "property-write-failed");
					return false;
				}
				LogService.Warn("TABLE-FORMAT retry, analysisOrdinal=" + tableInfo.AnalysisOrdinal, ex);
			}
			finally
			{
				ComObjectRelease.Release(ref value, "TableStyleApplier.table");
			}
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ReportWarning(string code, TableElementInfo tableInfo, string reason)
	{
		LogService.Warn("TABLE-FORMAT warning, code=" + code + ", analysisOrdinal=" + (tableInfo?.AnalysisOrdinal ?? 0) + ", reason=" + reason);
		ExecutionWarningCollector.Report(code, "table", "warn.format.table");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ReadDocumentBounds(Document doc, out int documentStart, out int documentEnd)
	{
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			value = doc.Content;
			documentStart = value.Start;
			documentEnd = value.End;
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "TableStyleApplier.content");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ValidateAnalysis(TableAnalysisResult analysis, string scope)
	{
		if (analysis == null || analysis.Tables == null)
		{
			throw new InvalidOperationException("表格执行缺少分析结果：scope=" + scope + "。");
		}
		if (analysis.TableCount != analysis.Tables.Count)
		{
			throw new InvalidOperationException("表格分析结果不完整：scope=" + scope + "，reported=" + analysis.TableCount + "，captured=" + analysis.Tables.Count + "。");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ApplyTable(Table table, Application app, TableStyleDefinition style, int documentStart, TableElementInfo tableInfo, DocumentHostKind hostKind)
	{
		if (table != null)
		{
			if (app == null)
			{
				throw new ArgumentNullException("app");
			}
			if (style != null)
			{
				if (tableInfo != null)
				{
					Rows value = null;
					Microsoft.Office.Interop.Word.Range value2 = null;
					try
					{
						value = table.Rows;
						value2 = table.Range;
						int count = value.Count;
						bool flag = SupportsIndividualRowAccess(value, count, tableInfo);
						if (hostKind != DocumentHostKind.WpsWriter || flag)
						{
							NormalizeTableLayout(table, value, value2, style, documentStart, tableInfo, flag);
							if (style.UseBorders)
							{
								ApplySingleLineBorders(table);
							}
							ApplyTableWideFormat(value2, style);
							FormatRows(value, value2, count, flag, style, tableInfo);
						}
						else
						{
							ReportWarning("format-table-complex-skipped", tableInfo, "wps-individual-row-access-unavailable");
						}
						return;
					}
					finally
					{
						if (value2 != null)
						{
							ComObjectRelease.Release(ref value2, "TableStyleApplier.tableRange");
						}
						if (value != null)
						{
							ComObjectRelease.Release(ref value, "TableStyleApplier.rows");
						}
					}
				}
				throw new ArgumentNullException("tableInfo");
			}
			throw new ArgumentNullException("style");
		}
		throw new ArgumentNullException("table");
	}

	private static bool ShouldSkipKnownComplexWpsTable(TableElementInfo tableInfo, DocumentHostKind hostKind)
	{
		if (hostKind == DocumentHostKind.WpsWriter && tableInfo != null && tableInfo.NestedTableCountReliable)
		{
			return tableInfo.NestedTableCount > 0;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void NormalizeTableLayout(Table table, Rows rows, Microsoft.Office.Interop.Word.Range tableRange, TableStyleDefinition style, int documentStart, TableElementInfo tableInfo, bool supportsIndividualRows)
	{
		Columns value = null;
		try
		{
			value = table.Columns;
			table.PreferredWidthType = WdPreferredWidthType.wdPreferredWidthPercent;
			table.PreferredWidth = 100f;
			HostOptionalComProperty.TrySet(table, "AllowSpacingBetweenCells", false, "table-cell-spacing-enabled");
			HostOptionalComProperty.TrySet(table, "Spacing", 0f, "table-cell-spacing");
			HostOptionalComProperty.TrySet(rows, "WrapAroundText", (style.TextWrapping == TableTextWrapping.Around) ? (-1) : 0, "table-row-text-wrapping");
			switch (style.ColumnWidthMode)
			{
			case TableColumnWidthMode.Content:
				table.AllowAutoFit = true;
				table.AutoFitBehavior(WdAutoFitBehavior.wdAutoFitContent);
				break;
			case TableColumnWidthMode.Fixed:
				table.AllowAutoFit = false;
				table.AutoFitBehavior(WdAutoFitBehavior.wdAutoFitFixed);
				value.PreferredWidthType = WdPreferredWidthType.wdPreferredWidthPoints;
				value.PreferredWidth = style.ColumnWidthPoints;
				break;
			case TableColumnWidthMode.Auto:
				table.AllowAutoFit = true;
				value.PreferredWidthType = WdPreferredWidthType.wdPreferredWidthAuto;
				break;
			case TableColumnWidthMode.Window:
				table.AllowAutoFit = true;
				table.AutoFitBehavior(WdAutoFitBehavior.wdAutoFitWindow);
				table.AllowAutoFit = false;
				if (supportsIndividualRows)
				{
					value.DistributeWidth();
				}
				else
				{
					LogService.Warn("TABLE-FORMAT column-width-distribution-skipped, analysisOrdinal=" + tableInfo.AnalysisOrdinal + ", range=" + tableInfo.RangeStart + "-" + tableInfo.RangeEnd + ", reason=individual-row-access-unavailable");
				}
				break;
			default:
				{
					throw new InvalidOperationException("Unknown table column width mode: " + style.ColumnWidthMode);
				}
			}
			rows.Alignment = ToRowAlignment(style.TableAlignment);
				rows.AllowBreakAcrossPages = -1;
				ApplyRowsHeight(rows, style);
				ApplyCellPadding(tableRange, style);
			ClearPaginationLocks(tableRange, documentStart);
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "TableStyleApplier.columns");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ApplyRowsHeight(Rows rows, TableStyleDefinition style)
	{
		switch (style.RowHeightMode)
		{
		case TableRowHeightMode.Auto:
			rows.HeightRule = WdRowHeightRule.wdRowHeightAuto;
			break;
		default:
			throw new InvalidOperationException("Unknown table row height mode: " + style.RowHeightMode);
		case TableRowHeightMode.Exactly:
			rows.HeightRule = WdRowHeightRule.wdRowHeightExactly;
			if (!(style.RowHeightPoints <= 0f))
			{
				rows.Height = style.RowHeightPoints;
			}
			break;
		case TableRowHeightMode.AtLeast:
			rows.HeightRule = WdRowHeightRule.wdRowHeightAtLeast;
			if (style.RowHeightPoints > 0f)
			{
				rows.Height = style.RowHeightPoints;
			}
			break;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ApplyCellPadding(Microsoft.Office.Interop.Word.Range tableRange, TableStyleDefinition style)
	{
		Table value = null;
		try
		{
			value = tableRange.Tables[1];
			value.TopPadding = style.CellTopPaddingPoints;
			value.BottomPadding = style.CellBottomPaddingPoints;
			value.LeftPadding = style.CellLeftPaddingPoints;
			value.RightPadding = style.CellRightPaddingPoints;
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "TableStyleApplier.ApplyCellPadding.Table");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ApplyTableWideFormat(Microsoft.Office.Interop.Word.Range range, TableStyleDefinition style)
	{
		Font value = null;
		ParagraphFormat value2 = null;
		object value3 = null;
		try
		{
			value = range.Font;
			DocumentFontSlotService.Apply(value, style.BodyFontName, style.BodyEnglishFontName);
			value.Bold = 0;
			value.Size = style.BodyFontSizePoints;
			value.DisableCharacterSpaceGrid = true;
			value2 = range.ParagraphFormat;
			value2.Alignment = ToParagraphAlignment(style.BodyAlignment);
			value2.SpaceBefore = 0f;
			value2.SpaceAfter = 0f;
			value2.LineSpacingRule = WdLineSpacing.wdLineSpaceSingle;
			value2.DisableLineHeightGrid = -1;
			if (style.ClearCellIndents)
			{
				value2.LeftIndent = 0f;
				value2.RightIndent = 0f;
				value2.FirstLineIndent = 0f;
				value2.CharacterUnitLeftIndent = 0f;
				value2.CharacterUnitRightIndent = 0f;
				value2.CharacterUnitFirstLineIndent = 0f;
				value2.AutoAdjustRightIndent = 0;
			}
			value3 = range.Cells;
			HostOptionalComProperty.TrySet(value3, "VerticalAlignment", WdCellVerticalAlignment.wdCellAlignVerticalCenter, "table-cell-vertical-alignment");
		}
		finally
		{
			if (value3 != null && Marshal.IsComObject(value3))
			{
				ComObjectRelease.Release(ref value3, "TableStyleApplier.cells");
			}
			if (value2 != null)
			{
				ComObjectRelease.Release(ref value2, "TableStyleApplier.pf");
			}
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "TableStyleApplier.font");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ClearPaginationLocks(Microsoft.Office.Interop.Word.Range tableRange, int documentStart)
	{
		ParagraphFormat value = null;
		Microsoft.Office.Interop.Word.Range value2 = null;
		ParagraphFormat value3 = null;
		try
		{
			value = tableRange.ParagraphFormat;
			value.KeepWithNext = 0;
			value.KeepTogether = 0;
			value.PageBreakBefore = 0;
			if (tableRange.Start > documentStart)
			{
				object Unit = WdUnits.wdParagraph;
				object Count = 1;
				value2 = tableRange.Previous(ref Unit, ref Count);
				if (value2 != null)
				{
					value3 = value2.ParagraphFormat;
					value3.KeepWithNext = 0;
					value3.KeepTogether = 0;
					value3.PageBreakBefore = 0;
				}
			}
		}
		finally
		{
			if (value3 != null)
			{
				ComObjectRelease.Release(ref value3, "TableStyleApplier.previousPf");
			}
			if (value2 != null)
			{
				ComObjectRelease.Release(ref value2, "TableStyleApplier.previousRange");
			}
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "TableStyleApplier.tablePf");
			}
		}
	}

	private static void FormatRows(Rows rows, Microsoft.Office.Interop.Word.Range tableRange, int rowCount, bool supportsIndividualRows, TableStyleDefinition style, TableElementInfo tableInfo)
	{
		if (!supportsIndividualRows || !TryFormatRowsByIndex(rows, rowCount, style, tableInfo))
		{
			FormatMergedTableHeaderCells(tableRange, rowCount, style, tableInfo);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool SupportsIndividualRowAccess(Rows rows, int rowCount, TableElementInfo tableInfo)
	{
		if (rowCount == 0)
		{
			return true;
		}
		Row value = null;
		try
		{
			value = rows[1];
			return true;
		}
		catch (COMException ex) when (IsMergedRowAccessError(ex))
		{
			LogService.Warn("TABLE-FORMAT individual-row-access-unavailable, analysisOrdinal=" + tableInfo.AnalysisOrdinal + ", range=" + tableInfo.RangeStart + "-" + tableInfo.RangeEnd + ", fallback=header-cells", ex);
			return false;
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "TableStyleApplier.firstRow");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool TryFormatRowsByIndex(Rows rows, int rowCount, TableStyleDefinition style, TableElementInfo tableInfo)
	{
		try
		{
			int num = Math.Max(0, Math.Min(style.HeaderRows, rowCount));
			for (int i = 1; i <= rowCount; i++)
			{
				Row value = null;
				try
				{
					value = rows[i];
					bool flag = i <= num;
					value.HeadingFormat = ((flag && style.RepeatHeaderRows) ? (-1) : 0);
					if (flag)
					{
						FormatHeaderRow(value, style);
					}
				}
				finally
				{
					if (value != null)
					{
						ComObjectRelease.Release(ref value, "TableStyleApplier.row");
					}
				}
			}
			return true;
		}
		catch (COMException ex) when (IsMergedRowAccessError(ex))
		{
			LogService.Warn("TABLE-FORMAT individual-row-access-became-unavailable, analysisOrdinal=" + tableInfo.AnalysisOrdinal + ", range=" + tableInfo.RangeStart + "-" + tableInfo.RangeEnd + ", fallback=header-cells", ex);
			return false;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void FormatHeaderRow(Row row, TableStyleDefinition style)
	{
		Cells value = null;
		try
		{
			value = row.Cells;
			int count = value.Count;
			for (int i = 1; i <= count; i++)
			{
				Cell value2 = null;
				try
				{
					value2 = value[i];
					FormatHeaderCell(value2, style);
				}
				finally
				{
					if (value2 != null)
					{
						ComObjectRelease.Release(ref value2, "TableStyleApplier.cell");
					}
				}
			}
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "TableStyleApplier.cells");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void FormatMergedTableHeaderCells(Microsoft.Office.Interop.Word.Range tableRange, int rowCount, TableStyleDefinition style, TableElementInfo tableInfo)
	{
		int num = Math.Max(0, Math.Min(style.HeaderRows, rowCount));
		if (num == 0)
		{
			LogMergedTableRowOptionsPreserved(style, tableInfo);
			return;
		}
		Cells value = null;
		try
		{
			value = tableRange.Cells;
			int count = value.Count;
			for (int i = 1; i <= count; i++)
			{
				Cell value2 = null;
				try
				{
					value2 = value[i];
					if (TryReadCellRowIndex(value2, tableInfo, out var rowIndex))
					{
						if (rowIndex <= num)
						{
							FormatHeaderCell(value2, style);
						}
						continue;
					}
					return;
				}
				finally
				{
					if (value2 != null)
					{
						ComObjectRelease.Release(ref value2, "TableStyleApplier.mergedHeaderCell");
					}
				}
			}
			LogMergedTableRowOptionsPreserved(style, tableInfo);
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "TableStyleApplier.mergedHeaderCells");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool TryReadCellRowIndex(Cell cell, TableElementInfo tableInfo, out int rowIndex)
	{
		try
		{
			rowIndex = cell.RowIndex;
			return true;
		}
		catch (COMException ex) when (IsMergedRowAccessError(ex))
		{
			rowIndex = 0;
			LogService.Warn("TABLE-FORMAT header-cell-row-index-unavailable, analysisOrdinal=" + tableInfo.AnalysisOrdinal + ", range=" + tableInfo.RangeStart + "-" + tableInfo.RangeEnd + ", fallback=preserve-existing-header-format", ex);
			return false;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void LogMergedTableRowOptionsPreserved(TableStyleDefinition style, TableElementInfo tableInfo)
	{
		LogService.Warn("TABLE-FORMAT row-heading-format-preserved, analysisOrdinal=" + tableInfo.AnalysisOrdinal + ", range=" + tableInfo.RangeStart + "-" + tableInfo.RangeEnd + ", repeatHeaderRequested=" + style.RepeatHeaderRows + ", reason=individual-row-access-unavailable");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void FormatHeaderCell(Cell cell, TableStyleDefinition style)
	{
		Microsoft.Office.Interop.Word.Range value = null;
		Font value2 = null;
		ParagraphFormat value3 = null;
		Shading value4 = null;
		try
		{
			value = cell.Range;
			value2 = value.Font;
			DocumentFontSlotService.Apply(value2, style.HeaderFontName, style.HeaderEnglishFontName);
			value2.Bold = -1;
			value2.Size = style.HeaderFontSizePoints;
			value3 = value.ParagraphFormat;
			value3.Alignment = ToParagraphAlignment(style.HeaderAlignment);
			value3.SpaceBefore = 0f;
			value3.SpaceAfter = 0f;
			value3.LineSpacingRule = WdLineSpacing.wdLineSpaceSingle;
			cell.VerticalAlignment = WdCellVerticalAlignment.wdCellAlignVerticalCenter;
			if (style.ShadeHeader)
			{
				value4 = cell.Shading;
				value4.BackgroundPatternColor = (WdColor)15921906;
			}
		}
		finally
		{
			if (value4 != null)
			{
				ComObjectRelease.Release(ref value4, "TableStyleApplier.shading");
			}
			if (value3 != null)
			{
				ComObjectRelease.Release(ref value3, "TableStyleApplier.pf");
			}
			if (value2 != null)
			{
				ComObjectRelease.Release(ref value2, "TableStyleApplier.font");
			}
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "TableStyleApplier.range");
			}
		}
	}

	private static bool IsMergedRowAccessError(COMException ex)
	{
		if (ex != null)
		{
			return ex.ErrorCode == -2146822297;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ApplySingleLineBorders(Table table)
	{
		Borders value = null;
		try
		{
			value = table.Borders;
			value.Enable = 0;
			SetBorder(value, WdBorderType.wdBorderTop, WdLineStyle.wdLineStyleSingle);
			SetBorder(value, WdBorderType.wdBorderBottom, WdLineStyle.wdLineStyleSingle);
			SetBorder(value, WdBorderType.wdBorderLeft, WdLineStyle.wdLineStyleSingle);
			SetBorder(value, WdBorderType.wdBorderRight, WdLineStyle.wdLineStyleSingle);
			SetBorder(value, WdBorderType.wdBorderHorizontal, WdLineStyle.wdLineStyleSingle);
			SetBorder(value, WdBorderType.wdBorderVertical, WdLineStyle.wdLineStyleSingle);
			SetBorder(value, WdBorderType.wdBorderDiagonalDown, WdLineStyle.wdLineStyleNone);
			SetBorder(value, WdBorderType.wdBorderDiagonalUp, WdLineStyle.wdLineStyleNone);
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "TableStyleApplier.borders");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void SetBorder(Borders borders, WdBorderType type, WdLineStyle lineStyle)
	{
		Border value = null;
		try
		{
			value = borders[type];
			value.LineStyle = lineStyle;
			if (lineStyle != WdLineStyle.wdLineStyleNone)
			{
				value.LineWidth = WdLineWidth.wdLineWidth050pt;
				value.Color = WdColor.wdColorBlack;
			}
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "TableStyleApplier.border");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WdParagraphAlignment ToParagraphAlignment(TableParagraphAlignment value)
	{
		return value switch
		{
			TableParagraphAlignment.Justify => WdParagraphAlignment.wdAlignParagraphJustify, 
			TableParagraphAlignment.Center => WdParagraphAlignment.wdAlignParagraphCenter, 
			TableParagraphAlignment.Left => WdParagraphAlignment.wdAlignParagraphLeft, 
			TableParagraphAlignment.Distribute => WdParagraphAlignment.wdAlignParagraphDistribute, 
			TableParagraphAlignment.Right => WdParagraphAlignment.wdAlignParagraphRight, 
			_ => throw new InvalidOperationException("Unknown table paragraph alignment: " + value), 
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WdRowAlignment ToRowAlignment(TableHorizontalAlignment value)
	{
		return value switch
		{
			TableHorizontalAlignment.Right => WdRowAlignment.wdAlignRowRight, 
			TableHorizontalAlignment.Left => WdRowAlignment.wdAlignRowLeft, 
			TableHorizontalAlignment.Center => WdRowAlignment.wdAlignRowCenter, 
			_ => throw new InvalidOperationException("Unknown table alignment: " + value), 
		};
	}
}
