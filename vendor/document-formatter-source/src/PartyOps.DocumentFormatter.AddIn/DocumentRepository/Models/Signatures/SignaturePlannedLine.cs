using DocumentRepository.Services.Formatting.Signatures;

namespace DocumentRepository.Models.Signatures;

public sealed class SignaturePlannedLine
{
	public float RightIndentPt;

	public float WidthPt;

	public SignatureWidthSource Source;

	public SignatureTextMorphology Morphology;
}
