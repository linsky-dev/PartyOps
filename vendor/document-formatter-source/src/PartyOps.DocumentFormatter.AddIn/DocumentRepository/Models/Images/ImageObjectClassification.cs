namespace DocumentRepository.Models.Images;

public sealed class ImageObjectClassification
{
	public ImageObjectKind Kind { get; set; }

	public bool IsEligible { get; set; }

	public string SkipReason { get; set; }
}
