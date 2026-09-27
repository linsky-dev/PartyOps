using System.Collections.Generic;

namespace DocumentRepository.Services.CompilationFormatting;

public sealed class CompilationTocOwnershipSnapshot
{
	public IList<CompilationTocFieldFact> PluginFields { get; set; }

	public IList<CompilationTocOwnedRegion> OwnedRegions { get; set; }

	public CompilationTocOwnershipSnapshot()
	{
		PluginFields = new List<CompilationTocFieldFact>();
		OwnedRegions = new List<CompilationTocOwnedRegion>();
	}
}
