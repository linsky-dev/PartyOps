using System.Collections.Generic;

namespace DocumentRepository.Models.Images;

public sealed class ImageAnalysisSnapshot
{
	public List<ImageObjectSnapshot> Objects { get; } = new List<ImageObjectSnapshot>();

	public bool CountsReliable { get; set; } = true;

	public bool DetailsReliable { get; set; } = true;

	public string FailureReasonCode { get; set; }

	public int ObservedInlineObjectCount { get; set; }

	public int ObservedFloatingObjectCount { get; set; }

	public int ObservedObjectCount => ObservedInlineObjectCount + ObservedFloatingObjectCount;

	public int EligibleCount
	{
		get
		{
			int num = 0;
			foreach (ImageObjectSnapshot @object in Objects)
			{
				if (@object != null && @object.IsEligible)
				{
					num++;
				}
			}
			return num;
		}
	}

	public int SkippedCount => Objects.Count - EligibleCount;
}
