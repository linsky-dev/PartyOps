using System;

namespace DocumentRepository.Models.CompilationFormatting;

[Serializable]
public class CompilationArticleInfo
{
	public int OrderIndex { get; set; }

	public string Title { get; set; }

	public string BeginBookmarkName { get; set; }

	public string EndBookmarkName { get; set; }

	public int TitleStartParagraphIndex { get; set; }

	public int TitleEndParagraphIndex { get; set; }

	public int TitleParagraphCount { get; set; }

	public int StartPosition { get; set; }

	public int EndPosition { get; set; }
}
