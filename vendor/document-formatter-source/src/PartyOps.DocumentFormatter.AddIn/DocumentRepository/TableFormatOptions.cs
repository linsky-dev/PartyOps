using System;
using System.Runtime.CompilerServices;

namespace DocumentRepository;

[Serializable]
public class TableFormatOptions
{
	public int OptionsVersion { get; set; }

	public int HeaderRows { get; set; }

	public bool FitWindow { get; set; }

	public bool UseBorders { get; set; }

	public bool ShadeHeader { get; set; }

	public bool RepeatHeaderRows { get; set; }

	public bool ClearCellIndents { get; set; }

	public string HeaderFontName { get; set; }

	public string HeaderFontSize { get; set; }

	public string BodyFontName { get; set; }

	public string BodyFontSize { get; set; }

	public string HeaderAlignment { get; set; }

	public string BodyAlignment { get; set; }

	public string TableAlignment { get; set; }

	public string TextWrapping { get; set; }

	public string RowHeightMode { get; set; }

	public string ColumnWidthMode { get; set; }

	public float RowHeight { get; set; }

	public float ColumnWidth { get; set; }

	public float CellTopPadding { get; set; }

	public float CellBottomPadding { get; set; }

	public float CellLeftPadding { get; set; }

	public float CellRightPadding { get; set; }

	[MethodImpl(MethodImplOptions.NoInlining)]
	public TableFormatOptions()
	{
		OptionsVersion = 1;
		HeaderRows = 1;
		FitWindow = true;
		UseBorders = true;
		ShadeHeader = true;
		RepeatHeaderRows = true;
		ClearCellIndents = true;
		HeaderFontName = "黑体";
		HeaderFontSize = "小四";
		BodyFontName = "仿宋_GB2312";
		BodyFontSize = "小四";
		HeaderAlignment = "居中";
		BodyAlignment = "两端对齐";
		TableAlignment = "Center";
		TextWrapping = "None";
		RowHeightMode = "AtLeast";
		ColumnWidthMode = "Window";
		RowHeight = 30f;
		ColumnWidth = 2f;
		CellTopPadding = 0f;
		CellBottomPadding = 0f;
		CellLeftPadding = 0f;
		CellRightPadding = 0f;
	}
}
