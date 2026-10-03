namespace DocumentRepository.Models.Images;

public enum ImageObjectKind
{
	InlinePicture,
	InlineLinkedPicture,
	FloatingPicture,
	FloatingLinkedPicture,
	UnsupportedInlineObject,
	UnsupportedFloatingObject
}
