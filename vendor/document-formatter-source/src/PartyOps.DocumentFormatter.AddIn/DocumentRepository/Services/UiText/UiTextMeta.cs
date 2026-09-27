namespace DocumentRepository.Services.UiText;

public sealed class UiTextMeta
{
	public string Key { get; private set; }

	public string Text { get; private set; }

	public bool TooltipEnabled { get; private set; }

	public string Tooltip { get; private set; }

	public UiTextMeta(string key, string text, string tooltip = "", bool tooltipEnabled = true)
	{
		Key = key ?? "";
		Text = text ?? "";
		Tooltip = tooltip ?? "";
		TooltipEnabled = tooltipEnabled;
	}
}
