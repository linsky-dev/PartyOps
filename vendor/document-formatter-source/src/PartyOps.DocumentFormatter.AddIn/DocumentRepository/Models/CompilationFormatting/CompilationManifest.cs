using System;
using System.Collections.Generic;

namespace DocumentRepository.Models.CompilationFormatting;

[Serializable]
public class CompilationManifest
{
	public int SchemaVersion { get; set; }

	public string FormatTemplateName { get; set; }

	public string GeneratedAt { get; set; }

	public string TocOptionsSnapshotJson { get; set; }

	public string TocPositionSnapshot { get; set; }

	public bool HasFrontMatter { get; set; }

	public bool TocMayBeStale { get; set; }

	public List<CompilationArticleInfo> Articles { get; set; }

	public List<CompilationSeparatorRecord> Separators { get; set; }

	public CompilationManifest()
	{
		SchemaVersion = 2;
		Articles = new List<CompilationArticleInfo>();
		Separators = new List<CompilationSeparatorRecord>();
	}
}
