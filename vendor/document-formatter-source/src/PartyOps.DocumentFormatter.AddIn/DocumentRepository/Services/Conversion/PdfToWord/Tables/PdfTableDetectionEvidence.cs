using System.Runtime.CompilerServices;

namespace DocumentRepository.Services.Conversion.PdfToWord.Tables;

public sealed class PdfTableDetectionEvidence
{
	public PdfTableEvidenceKind Kind { get; }

	public string Name { get; }

	public string Detail { get; }

	public PdfTableDetectionEvidence()
	{
		Name = string.Empty;
		Detail = string.Empty;
	}

	public PdfTableDetectionEvidence(PdfTableEvidenceKind kind, string name, string detail)
	{
		Kind = kind;
		Name = name ?? string.Empty;
		Detail = detail ?? string.Empty;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public override string ToString()
	{
		return Kind.ToString() + ":" + Name + ((Detail.Length > 0) ? (" (" + Detail + ")") : string.Empty);
	}
}
