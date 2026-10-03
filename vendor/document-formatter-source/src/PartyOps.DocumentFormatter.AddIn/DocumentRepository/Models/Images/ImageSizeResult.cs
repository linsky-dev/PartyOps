namespace DocumentRepository.Models.Images;

public sealed class ImageSizeResult
{
	public float WidthPoints { get; set; }

	public float HeightPoints { get; set; }

	public bool ShouldResize { get; set; }

	public bool UseOriginalScalePercent { get; set; }
}
