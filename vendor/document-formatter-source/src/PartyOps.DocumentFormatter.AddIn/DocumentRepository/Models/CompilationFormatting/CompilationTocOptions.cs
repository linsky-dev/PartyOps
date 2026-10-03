using System;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Models.CompilationFormatting;

[Serializable]
public class CompilationTocOptions
{
	public string TitleText { get; set; }

	public string TitleFontName { get; set; }

	public float TitleFontSize { get; set; }

	public bool TitleBold { get; set; }

	public CompilationTocTitleAlignment TitleAlignment { get; set; }

	public float TitleSpaceBefore { get; set; }

	public float TitleSpaceAfter { get; set; }

	public string EntryFontNameFarEast { get; set; }

	public string EntryFontNameAscii { get; set; }

	public float EntryFontSize { get; set; }

	public float EntryLineSpacingPoints { get; set; }

	public CompilationTocLeaderStyle LeaderStyle { get; set; }

	public bool HyperlinkEntries { get; set; }

	public bool PageBreakAfterToc { get; set; }

	public bool IncludeTocPageInContinuousNumbering { get; set; }

	public CompilationExistingTocPolicy ExistingTocPolicy { get; set; }

	[MethodImpl(MethodImplOptions.NoInlining)]
	public CompilationTocOptions()
	{
		TitleText = "目录";
		TitleFontName = "黑体";
		TitleFontSize = 16f;
		TitleBold = true;
		TitleAlignment = CompilationTocTitleAlignment.Center;
		TitleSpaceBefore = 0f;
		TitleSpaceAfter = 12f;
		EntryFontNameFarEast = "仿宋_GB2312";
		EntryFontNameAscii = "Times New Roman";
		EntryFontSize = 14f;
		EntryLineSpacingPoints = 28f;
		LeaderStyle = CompilationTocLeaderStyle.Dots;
		HyperlinkEntries = true;
		PageBreakAfterToc = true;
		IncludeTocPageInContinuousNumbering = false;
		ExistingTocPolicy = CompilationExistingTocPolicy.Keep;
	}

	public CompilationTocOptions Clone()
	{
		return (CompilationTocOptions)MemberwiseClone();
	}
}
