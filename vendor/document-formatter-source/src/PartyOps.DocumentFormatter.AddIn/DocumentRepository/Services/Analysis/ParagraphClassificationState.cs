namespace DocumentRepository.Services.Analysis;

public sealed class ParagraphClassificationState
{
	public bool FirstNonEmptySeen { get; set; }

	public bool OpeningTitleRegionOpen { get; set; }

	public bool OpeningTitleSeen { get; set; }

	public bool OpeningPreviousWasTitle { get; set; }

	public bool InAttachmentList { get; set; }

	public bool InAttachmentBody { get; set; }

	public int AttachmentTitleScanLeft { get; set; }

	public int AttachmentMainTitleParaCount { get; set; }

	public bool AttachmentMainTitleFinished { get; set; }

	public bool AttachmentLastWasMainTitle { get; set; }

	public bool AttachmentSubTitleChecked { get; set; }

	public ParagraphClassificationState()
	{
		OpeningTitleRegionOpen = true;
	}

	public void BeginAttachmentBody()
	{
		InAttachmentList = false;
		InAttachmentBody = true;
		AttachmentTitleScanLeft = 4;
		AttachmentMainTitleParaCount = 0;
		AttachmentMainTitleFinished = false;
		AttachmentLastWasMainTitle = false;
		AttachmentSubTitleChecked = false;
	}

	public void EndAttachmentBodyTitleScan()
	{
		AttachmentTitleScanLeft = 0;
		AttachmentMainTitleFinished = true;
		AttachmentLastWasMainTitle = false;
		AttachmentSubTitleChecked = true;
	}
}
