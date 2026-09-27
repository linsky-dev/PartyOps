using System.Collections.Generic;

namespace DocumentRepository.Services.Conversion.PdfToWord.Tables;

internal static class PdfTableEvidenceList
{
	public static bool HasCounter(IList<PdfTableDetectionEvidence> evidence)
	{
		if (evidence == null)
		{
			return false;
		}
		foreach (PdfTableDetectionEvidence item in evidence)
		{
			if (item.Kind != PdfTableEvidenceKind.Counter)
			{
				continue;
			}
			return true;
		}
		return false;
	}
}
