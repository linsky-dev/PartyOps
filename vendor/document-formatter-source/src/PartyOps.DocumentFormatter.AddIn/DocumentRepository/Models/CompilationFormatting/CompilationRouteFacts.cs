namespace DocumentRepository.Models.CompilationFormatting;

public class CompilationRouteFacts
{
	public bool CompilationEnabled { get; set; }

	public bool CursorInsideCompilationToc { get; set; }

	public bool SelectionSpansTocAndBody { get; set; }

	public bool HasMeaningfulSelection { get; set; }

	public bool IsWholeDocumentSelection { get; set; }

	public int ArticleCount { get; set; }

	public bool HasArticleStructure { get; set; }

	public bool UsesVisibleMarkers { get; set; }

	public bool HasFrontMatter { get; set; }

	public int FirstSelectedArticleIndex { get; set; }

	public int LastSelectedArticleIndex { get; set; }

	public bool SelectionTruncatedAtStart { get; set; }

	public bool SelectionTruncatedAtEnd { get; set; }

	public bool SelectionInsideSingleArticle { get; set; }

	public CompilationRouteFacts()
	{
		FirstSelectedArticleIndex = -1;
		LastSelectedArticleIndex = -1;
	}
}
