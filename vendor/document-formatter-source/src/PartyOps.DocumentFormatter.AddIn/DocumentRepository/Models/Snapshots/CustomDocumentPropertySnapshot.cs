namespace DocumentRepository.Models.Snapshots;

public sealed class CustomDocumentPropertySnapshot
{
	public string Name { get; set; }

	public int Type { get; set; }

	public object Value { get; set; }

	public bool LinkToContent { get; set; }

	public string LinkSource { get; set; }

	public CustomDocumentPropertySnapshot DeepCopy()
	{
		return new CustomDocumentPropertySnapshot
		{
			Name = Name,
			Type = Type,
			Value = Value,
			LinkToContent = LinkToContent,
			LinkSource = LinkSource
		};
	}
}
