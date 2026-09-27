namespace DocumentRepository.Models.RedHeader;

public sealed class RedHeaderTopMarkLine
{
	public string Kind { get; private set; }

	public string Text { get; private set; }

	public RedHeaderTopMarkLine(string kind, string text)
	{
		Kind = kind;
		Text = text;
	}
}
