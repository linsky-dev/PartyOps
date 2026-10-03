using System;
using System.Collections.Generic;

namespace DocumentRepository.Models.CompilationFormatting;

[Serializable]
public class CompilationArticleList
{
	public int FrontMatterEndParagraphIndex { get; set; }

	public List<CompilationArticleInfo> Articles { get; set; }

	public string ErrorMessage { get; set; }

	public CompilationArticleList()
	{
		Articles = new List<CompilationArticleInfo>();
	}
}
