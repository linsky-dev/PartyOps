namespace DocumentRepository.Models.Images;

public sealed class ImageFormattingTarget
{
	public int SourceOrdinal { get; set; }

	public ImageObjectKind SourceKind { get; set; }

	public int TypeCode { get; set; }

	public int AnchorStart { get; set; }

	public int AnchorEnd { get; set; }

	public string Name { get; set; }

	public float SourceWidthPoints { get; set; }

	public float SourceHeightPoints { get; set; }

	public bool IsInTable { get; set; }

	public bool IsStandaloneParagraph { get; set; }

	public bool SourceIsInline
	{
		get
		{
			if (SourceKind != ImageObjectKind.InlinePicture)
			{
				return SourceKind == ImageObjectKind.InlineLinkedPicture;
			}
			return true;
		}
	}
}
