namespace DocumentRepository;

public class RenameRulePart
{
	public string Type { get; set; }

	public string Text { get; set; }

	public RenameRulePart()
	{
		Type = "";
		Text = "";
	}

	public RenameRulePart(string type, string text = "")
	{
		Type = type;
		Text = text ?? "";
	}

	public RenameRulePart Clone()
	{
		return new RenameRulePart(Type, Text);
	}
}
