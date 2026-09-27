using System.Runtime.CompilerServices;

namespace DocumentRepository.Services.Conversion.PdfToWord.Tables;

public sealed class CrossPageTableConservationReport
{
	public bool Passed { get; }

	public int SourceCharacterCount { get; }

	public int OutputCharacterCount { get; }

	public int ProvenRepeatedHeaderCharacters { get; }

	public int MissingCount { get; }

	public int UnexpectedCount { get; }

	public CrossPageTableConservationReport(int sourceCharacterCount, int outputCharacterCount, int provenRepeatedHeaderCharacters, int missingCount, int unexpectedCount)
	{
		SourceCharacterCount = sourceCharacterCount;
		OutputCharacterCount = outputCharacterCount;
		ProvenRepeatedHeaderCharacters = provenRepeatedHeaderCharacters;
		MissingCount = missingCount;
		UnexpectedCount = unexpectedCount;
		Passed = missingCount == 0 && unexpectedCount == 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public override string ToString()
	{
		return $"conservation src={SourceCharacterCount} out={OutputCharacterCount} provenHeader={ProvenRepeatedHeaderCharacters} missing={MissingCount} unexpected={UnexpectedCount} passed={Passed}";
	}
}
