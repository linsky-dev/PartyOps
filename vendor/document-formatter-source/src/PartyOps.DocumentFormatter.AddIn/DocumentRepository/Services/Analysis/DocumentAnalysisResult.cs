using DocumentRepository.Models;
using DocumentRepository.Services.Detection.Tables;

namespace DocumentRepository.Services.Analysis;

public class DocumentAnalysisResult
{
	public bool HasTables { get; set; }

	public TableAnalysisResult TableAnalysis { get; set; }

	public bool HasImages { get; set; }

	public bool HasAttachments { get; set; }

	public bool HasHyperlinks { get; set; }

	public bool HasEnglishNumbers { get; set; }

	public bool HasTabs { get; set; }

	public bool HasOrphanCandidates { get; set; }

	public int ParagraphCount { get; set; }

	public DocumentElementList Elements { get; set; }

	public bool HasKeywordCandidates { get; set; }

	public bool HasSemicolonCandidates { get; set; }
}
