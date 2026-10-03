namespace DocumentRepository.Services.Formatting.Signatures;

public enum SignatureStaticRejection
{
	None,
	UnknownHost,
	UncalibratedCjkFont,
	UncalibratedAsciiFont,
	UncalibratedFontSize,
	UncalibratedCharacters,
	RequiresHostMeasurement
}
