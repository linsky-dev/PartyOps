using System.Collections.Generic;

namespace DocumentRepository.Services.CompilationFormatting;

public sealed class CompilationTocOwnedRegion
{
	public int Start { get; set; }

	public int End { get; set; }

	public IList<string> BookmarkNames { get; set; }

	public CompilationTocOwnedRegion()
	{
		BookmarkNames = new List<string>();
	}
}
