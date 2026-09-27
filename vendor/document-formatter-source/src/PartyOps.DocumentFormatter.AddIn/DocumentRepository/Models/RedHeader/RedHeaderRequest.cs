namespace DocumentRepository.Models.RedHeader;

public class RedHeaderRequest
{
	public OperationContext Context { get; set; }

	public RedHeaderTemplate Template { get; set; }

	public FormatConfig Config { get; set; }
}
