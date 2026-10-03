namespace DocumentRepository.Services.Conversion.PdfToWord;

public sealed class LayoutReconstructionOptions
{
	public float LineBaselineTolerance { get; set; } = 3f;

	public float BlockGapThreshold { get; set; } = 12f;

	public float ColumnClusterTolerance { get; set; } = 5f;

	public float ColumnSupportRatio { get; set; } = 0.3f;

	public int MinColumnSupportRows { get; set; } = 3;

	public int MinTableRows { get; set; } = 2;

	public float ParagraphGapFactor { get; set; } = 1.7f;

	public float IndentThreshold { get; set; } = 10f;

	public float PageNumberBottomRatio { get; set; } = 0.25f;

	public float DecorationEdgePoints { get; set; } = 72f;

	public float DecorationEdgeRatio { get; set; } = 0.12f;

	public int DecorationMinFrequencyPages { get; set; } = 3;

	public float DecorationFrequencyRatio { get; set; } = 0.3f;

	public float DecorationXStabilityTolerance { get; set; } = 6f;

	public float DecorationFontSizeTolerance { get; set; } = 1f;

	public float DecorationYStdDevRatio { get; set; } = 0.05f;

	public float ColumnGutterMinWidth { get; set; } = 12f;

	public float ColumnCenterMinRatio { get; set; } = 0.4f;

	public float ColumnCenterMaxRatio { get; set; } = 0.6f;

	public int ColumnMinLines { get; set; } = 4;

	public float ColumnProseDensityRatio { get; set; } = 0.35f;
}
