using System;
using DocumentRepository.Models.CompilationFormatting;

namespace DocumentRepository;

[Serializable]
public class CompilationFormatOptions
{
	public const int CurrentOptionsVersion = 1;

	public int OptionsVersion { get; set; }

	public bool GenerateToc { get; set; }

	public bool StartEachArticleOnNewPage { get; set; }

	public CompilationFrontMatterMode FrontMatterMode { get; set; }

	public CompilationTocPosition TocPosition { get; set; }

	public CompilationPageNumberMode PageNumberMode { get; set; }

	public CompilationTocOptions TocOptions { get; set; }

	public CompilationFormatOptions()
	{
		OptionsVersion = 1;
		GenerateToc = true;
		StartEachArticleOnNewPage = true;
		FrontMatterMode = CompilationFrontMatterMode.Preserve;
		TocPosition = CompilationTocPosition.BeforeFirstArticle;
		PageNumberMode = CompilationPageNumberMode.Continuous;
		TocOptions = new CompilationTocOptions();
	}
}
