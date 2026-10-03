using System;

namespace DocumentRepository.Models.CompilationFormatting;

[Serializable]
public class CompilationSeparatorRecord
{
	public int OrderIndex { get; set; }

	public string Kind { get; set; }

	public string AnchorBookmark { get; set; }
}
