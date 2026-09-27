using System;
using System.Collections.Generic;

namespace DocumentRepository.Services.Conversion.PdfToWord;

public sealed class DecorationDetectionResult
{
	public IList<DecorationOccurrence> Occurrences { get; private set; }

	public int ConfirmedGroupCount { get; set; }

	public int RemovedCount
	{
		get
		{
			int num = 0;
			foreach (DecorationOccurrence occurrence in Occurrences)
			{
				if (occurrence.Removed)
				{
					num++;
				}
			}
			return num;
		}
	}

	public DecorationDetectionResult()
	{
		Occurrences = new List<DecorationOccurrence>();
	}

	public bool ShouldRemove(int pageIndex, float baseline, string normalizedText)
	{
		string text = DecorationRegionDetector.StripEdgePageNumbers(normalizedText);
		if (text.Length < 2)
		{
			return false;
		}
		foreach (DecorationOccurrence occurrence in Occurrences)
		{
			if (!occurrence.Removed || occurrence.PageIndex != pageIndex || !(Math.Abs(occurrence.Baseline - baseline) <= 3f) || !(occurrence.Fingerprint == text))
			{
				continue;
			}
			return true;
		}
		return false;
	}
}
