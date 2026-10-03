namespace DocumentRepository;

public class RenameRulePreviewInfo
{
	public string DocumentNumber { get; set; }

	public string MainTitle { get; set; }

	public string Subtitle { get; set; }

	public RenameRulePreviewInfo()
	{
		DocumentNumber = "";
		MainTitle = "";
		Subtitle = "";
	}
}
