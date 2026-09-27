namespace DocumentRepository.Services.Conversion.PdfToWord.Tables;

public sealed class TableAnalysisOptions
{
	public float LineSnapTolerance { get; set; }

	public float SegmentMergeGap { get; set; }

	public float MinHorizontalLineLength { get; set; }

	public float MinVerticalLineLength { get; set; }

	public int MinColumns { get; set; }

	public int MinRows { get; set; }

	public float VerticalCoverageForHigh { get; set; }

	public float HorizontalSpanForHigh { get; set; }

	public float DenseRowRatio { get; set; }

	public float RowPitchStabilityForHigh { get; set; }

	public float LineBaselineTolerance { get; set; }

	public float BlockGapThreshold { get; set; }

	public float ColumnClusterTolerance { get; set; }

	public float ColumnSupportRatio { get; set; }

	public int MinColumnSupportRows { get; set; }

	public float OpenTableColumnDriftTolerance { get; set; }

	public float BorderlessHighDenseRowRatio { get; set; }

	public float BorderlessHighRowPitchRatio { get; set; }

	public float HeaderFooterRatio { get; set; }

	public TableAnalysisOptions()
	{
		LineSnapTolerance = 2f;
		SegmentMergeGap = 2f;
		MinHorizontalLineLength = 30f;
		MinVerticalLineLength = 10f;
		MinColumns = 2;
		MinRows = 2;
		VerticalCoverageForHigh = 0.6f;
		HorizontalSpanForHigh = 0.8f;
		DenseRowRatio = 0.6f;
		RowPitchStabilityForHigh = 2f;
		LineBaselineTolerance = 3f;
		BlockGapThreshold = 12f;
		ColumnClusterTolerance = 5f;
		ColumnSupportRatio = 0.3f;
		MinColumnSupportRows = 3;
		OpenTableColumnDriftTolerance = 5f;
		BorderlessHighDenseRowRatio = 0.9f;
		BorderlessHighRowPitchRatio = 1.35f;
		HeaderFooterRatio = 0.08f;
	}
}
