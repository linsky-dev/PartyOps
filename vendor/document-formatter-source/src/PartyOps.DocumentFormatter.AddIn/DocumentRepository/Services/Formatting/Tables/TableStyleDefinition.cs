namespace DocumentRepository.Services.Formatting.Tables;

public class TableStyleDefinition
{
	public int HeaderRows { get; set; }

	public bool UseBorders { get; set; }

	public bool ShadeHeader { get; set; }

	public bool RepeatHeaderRows { get; set; }

	public bool ClearCellIndents { get; set; }

	public string HeaderFontName { get; set; }

	public float HeaderFontSizePoints { get; set; }

	public string BodyFontName { get; set; }

	public float BodyFontSizePoints { get; set; }

	public string HeaderEnglishFontName { get; set; }

	public string BodyEnglishFontName { get; set; }

	public TableParagraphAlignment HeaderAlignment { get; set; }

	public TableParagraphAlignment BodyAlignment { get; set; }

	public TableHorizontalAlignment TableAlignment { get; set; }

	public TableTextWrapping TextWrapping { get; set; }

	public TableRowHeightMode RowHeightMode { get; set; }

	public TableColumnWidthMode ColumnWidthMode { get; set; }

	public float RowHeightPoints { get; set; }

	public float ColumnWidthPoints { get; set; }

	public float CellTopPaddingPoints { get; set; }

	public float CellBottomPaddingPoints { get; set; }

	public float CellLeftPaddingPoints { get; set; }

	public float CellRightPaddingPoints { get; set; }
}
