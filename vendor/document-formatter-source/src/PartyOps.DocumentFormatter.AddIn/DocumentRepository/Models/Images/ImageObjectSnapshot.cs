namespace DocumentRepository.Models.Images;

public sealed class ImageObjectSnapshot
{
	public int Ordinal { get; set; }

	public ImageObjectKind Kind { get; set; }

	public bool IsEligible { get; set; }

	public string SkipReason { get; set; }

	public int TypeCode { get; set; }

	public int StoryTypeCode { get; set; }

	public int AnchorStart { get; set; }

	public int AnchorEnd { get; set; }

	public string Name { get; set; }

	public float WidthPoints { get; set; }

	public float HeightPoints { get; set; }

	public bool IsInTable { get; set; }

	public bool IsStandaloneParagraph { get; set; }
}
