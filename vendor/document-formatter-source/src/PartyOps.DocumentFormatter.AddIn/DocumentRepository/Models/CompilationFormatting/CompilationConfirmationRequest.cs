using System.Collections.Generic;

namespace DocumentRepository.Models.CompilationFormatting;

public class CompilationConfirmationRequest
{
	public bool IsFullDocument { get; set; }

	public List<CompilationArticleInfo> Articles { get; set; }

	public List<int> SelectedArticleIndexes { get; set; }

	public string FrontMatterModeText { get; set; }

	public bool GenerateToc { get; set; }

	public string PageNumberModeText { get; set; }

	public bool StartEachArticleOnNewPage { get; set; }

	public bool OfferExpandToFullArticles { get; set; }

	public CompilationConfirmationRequest()
	{
		Articles = new List<CompilationArticleInfo>();
		SelectedArticleIndexes = new List<int>();
	}
}
