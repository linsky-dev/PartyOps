using DocumentRepository.Models.CompilationFormatting;

namespace DocumentRepository.Services.CompilationFormatting;

public static class CompilationTocRangeClassifier
{
	public static CompilationTocRangeRelation Classify(int regionStart, int regionEnd, int selectionStart, int selectionEnd)
	{
		if (regionEnd <= regionStart)
		{
			return CompilationTocRangeRelation.Outside;
		}
		if (selectionStart == selectionEnd)
		{
			if (selectionStart >= regionStart && selectionStart <= regionEnd)
			{
				return CompilationTocRangeRelation.Inside;
			}
			return CompilationTocRangeRelation.Outside;
		}
		bool num = selectionStart >= regionStart && selectionStart < regionEnd;
		bool flag = selectionEnd > regionStart && selectionEnd <= regionEnd;
		if (!(num && flag))
		{
			if (selectionStart >= regionEnd || selectionEnd <= regionStart)
			{
				return CompilationTocRangeRelation.Outside;
			}
			return CompilationTocRangeRelation.Overlapping;
		}
		return CompilationTocRangeRelation.Inside;
	}
}
