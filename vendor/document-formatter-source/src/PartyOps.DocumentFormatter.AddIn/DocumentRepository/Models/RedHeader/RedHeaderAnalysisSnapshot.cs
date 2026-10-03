using DocumentRepository.Models.Snapshots;

namespace DocumentRepository.Models.RedHeader;

public sealed class RedHeaderAnalysisSnapshot
{
	public DocumentSnapshot DocumentSnapshot { get; set; }

	public int InitialPageCount { get; set; }

	public float FirstPageWidth { get; set; }

	public float FirstPageHeight { get; set; }

	public float FirstLeftMargin { get; set; }

	public float FirstRightMargin { get; set; }

	public float LastPageHeight { get; set; }

	public float LastBottomMargin { get; set; }

	public int SourceBodyLength { get; set; }

	public string SourceBodyHash { get; set; }

	public RedHeaderAnalysisSnapshot DeepCopy()
	{
		return new RedHeaderAnalysisSnapshot
		{
			DocumentSnapshot = ((DocumentSnapshot == null) ? null : DocumentSnapshot.DeepCopy()),
			InitialPageCount = InitialPageCount,
			FirstPageWidth = FirstPageWidth,
			FirstPageHeight = FirstPageHeight,
			FirstLeftMargin = FirstLeftMargin,
			FirstRightMargin = FirstRightMargin,
			LastPageHeight = LastPageHeight,
			LastBottomMargin = LastBottomMargin,
			SourceBodyLength = SourceBodyLength,
			SourceBodyHash = SourceBodyHash
		};
	}
}
