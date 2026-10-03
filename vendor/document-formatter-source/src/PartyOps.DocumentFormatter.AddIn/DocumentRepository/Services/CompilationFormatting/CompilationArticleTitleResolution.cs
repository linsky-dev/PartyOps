namespace DocumentRepository.Services.CompilationFormatting;

public sealed class CompilationArticleTitleResolution
{
	public string Title { get; set; }

	public int TitleStartParagraphIndex { get; set; }

	public int TitleEndParagraphIndex { get; set; }

	public int TitleParagraphCount { get; set; }

	public string ErrorMessage { get; set; }

	public bool HasTitle
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(Title))
			{
				return TitleStartParagraphIndex >= 0;
			}
			return false;
		}
	}

	public CompilationArticleTitleResolution()
	{
		Title = string.Empty;
		TitleStartParagraphIndex = -1;
		TitleEndParagraphIndex = -1;
	}
}
