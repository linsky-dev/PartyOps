using DocumentRepository.Services.Formatting.Signatures;

namespace DocumentRepository.Models.Signatures;

public sealed class SignatureLayoutLineInput
{
	public SignatureTextMorphology Morphology;

	public float WidthPt;

	public SignatureWidthSource Source;
}
