using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;

namespace DocumentRepository.Services.Formatting.Tables;

public static class TableStyleBuilder
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static TableStyleDefinition FromConfig(FormatConfig config)
	{
		if (config == null)
		{
			throw new ArgumentNullException("config");
		}
		TableFormatOptions tableFormatOptions = config.TableOptions ?? throw new InvalidOperationException("Table options are missing.");
		return new TableStyleDefinition
		{
			HeaderRows = ValidateRange(tableFormatOptions.HeaderRows, 0, 5, "Header rows"),
			UseBorders = tableFormatOptions.UseBorders,
			ShadeHeader = tableFormatOptions.ShadeHeader,
			RepeatHeaderRows = tableFormatOptions.RepeatHeaderRows,
			ClearCellIndents = tableFormatOptions.ClearCellIndents,
			HeaderFontName = RequireText(tableFormatOptions.HeaderFontName, "Table header font name"),
			HeaderFontSizePoints = FontSizeHelper.ToPoints(RequireText(tableFormatOptions.HeaderFontSize, "Table header font size")),
			BodyFontName = RequireText(tableFormatOptions.BodyFontName, "Table body font name"),
			BodyFontSizePoints = FontSizeHelper.ToPoints(RequireText(tableFormatOptions.BodyFontSize, "Table body font size")),
			HeaderEnglishFontName = EnglishNumberFontScopePolicy.ResolveStyleFont(config, ElementType.TableHeader, tableFormatOptions.HeaderFontName),
			BodyEnglishFontName = EnglishNumberFontScopePolicy.ResolveStyleFont(config, ElementType.Table, tableFormatOptions.BodyFontName),
			HeaderAlignment = ToParagraphAlignment(tableFormatOptions.HeaderAlignment),
			BodyAlignment = ToParagraphAlignment(tableFormatOptions.BodyAlignment),
			TableAlignment = ToTableAlignment(tableFormatOptions.TableAlignment),
			TextWrapping = ToTextWrapping(tableFormatOptions.TextWrapping),
			RowHeightMode = ToRowHeightMode(tableFormatOptions.RowHeightMode),
			ColumnWidthMode = ToColumnWidthMode(tableFormatOptions.ColumnWidthMode),
			RowHeightPoints = ((tableFormatOptions.RowHeight > 0f) ? tableFormatOptions.RowHeight : 0f),
			ColumnWidthPoints = CentimetersToPoints(tableFormatOptions.ColumnWidth),
			CellTopPaddingPoints = CentimetersToPoints(tableFormatOptions.CellTopPadding),
			CellBottomPaddingPoints = CentimetersToPoints(tableFormatOptions.CellBottomPadding),
			CellLeftPaddingPoints = CentimetersToPoints(tableFormatOptions.CellLeftPadding),
			CellRightPaddingPoints = CentimetersToPoints(tableFormatOptions.CellRightPadding)
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string RequireText(string value, string fieldName)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw new FormatException(fieldName + " is empty.");
		}
		return value.Trim();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ValidateRange(int value, int min, int max, string fieldName)
	{
		if (value < min || value > max)
		{
			throw new FormatException(fieldName + " is out of range: " + value);
		}
		return value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static TableParagraphAlignment ToParagraphAlignment(string value)
	{
		return (value ?? string.Empty).Trim() switch
		{
			"分散对齐" => TableParagraphAlignment.Distribute, 
			"右对齐" => TableParagraphAlignment.Right, 
			"居中" => TableParagraphAlignment.Center, 
			"两端对齐" => TableParagraphAlignment.Justify, 
			"左对齐" => TableParagraphAlignment.Left, 
			_ => throw new FormatException("Invalid table paragraph alignment: " + value), 
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static TableHorizontalAlignment ToTableAlignment(string value)
	{
		return (value ?? string.Empty).Trim() switch
		{
			"Right" => TableHorizontalAlignment.Right, 
			"Left" => TableHorizontalAlignment.Left, 
			"Center" => TableHorizontalAlignment.Center, 
			_ => throw new FormatException("Invalid table alignment: " + value), 
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static TableTextWrapping ToTextWrapping(string value)
	{
		string text = (value ?? string.Empty).Trim();
		if (text == "Around")
		{
			return TableTextWrapping.Around;
		}
		if (text == "None")
		{
			return TableTextWrapping.None;
		}
		throw new FormatException("Invalid table text wrapping: " + value);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static TableRowHeightMode ToRowHeightMode(string value)
	{
		return (value ?? string.Empty).Trim() switch
		{
			"AtLeast" => TableRowHeightMode.AtLeast, 
			"Exactly" => TableRowHeightMode.Exactly, 
			"Auto" => TableRowHeightMode.Auto, 
			_ => throw new FormatException("Invalid table row height mode: " + value), 
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static TableColumnWidthMode ToColumnWidthMode(string value)
	{
		return (value ?? string.Empty).Trim() switch
		{
			"Auto" => TableColumnWidthMode.Auto, 
			"Fixed" => TableColumnWidthMode.Fixed, 
			"Content" => TableColumnWidthMode.Content, 
			"Window" => TableColumnWidthMode.Window, 
			_ => throw new FormatException("Invalid table column width mode: " + value), 
		};
	}

	private static float CentimetersToPoints(float centimeters)
	{
		if (!(centimeters <= 0f))
		{
			return centimeters * 28.346457f;
		}
		return 0f;
	}
}
