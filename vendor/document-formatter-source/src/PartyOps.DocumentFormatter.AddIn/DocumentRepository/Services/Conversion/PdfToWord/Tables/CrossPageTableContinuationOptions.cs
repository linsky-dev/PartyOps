namespace DocumentRepository.Services.Conversion.PdfToWord.Tables;

public sealed class CrossPageTableContinuationOptions
{
	public float PageEdgeTouchRatio { get; set; }

	public float PageWidthToleranceRatio { get; set; }

	public float PageWidthTolerancePoints { get; set; }

	public float HorizontalPositionToleranceRatio { get; set; }

	public float HorizontalPositionTolerancePoints { get; set; }

	public float ColumnBoundaryToleranceRatio { get; set; }

	public float HeaderFontSizeTolerance { get; set; }

	public CrossPageTableContinuationOptions()
	{
		PageEdgeTouchRatio = 0.1f;
		PageWidthToleranceRatio = 0.01f;
		PageWidthTolerancePoints = 2f;
		HorizontalPositionToleranceRatio = 0.01f;
		HorizontalPositionTolerancePoints = 3f;
		ColumnBoundaryToleranceRatio = 0.02f;
		HeaderFontSizeTolerance = 1f;
	}
}
