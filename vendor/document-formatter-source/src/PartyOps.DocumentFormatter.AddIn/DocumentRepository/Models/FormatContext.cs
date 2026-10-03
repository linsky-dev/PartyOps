using System.Collections.Generic;
using DocumentRepository.Services.Analysis;
using DocumentRepository.Services.Hosting;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Models;

public class FormatContext
{
	public Application Application { get; set; }

	public Document Document { get; set; }

	public Range SelectionRange { get; set; }

	public DocumentAnalysisResult Analysis { get; set; }

	public bool HasTables { get; set; }

	public bool HasImages { get; set; }

	public bool HasAttachments { get; set; }

	public bool HasHyperlinks { get; set; }

	public bool HasEnglishNumbers { get; set; }

	public bool HasKeywordCandidates { get; set; }

	public bool HasSemicolonCandidates { get; set; }

	public bool HasTabs { get; set; }

	public bool HasOrphanCandidates { get; set; }

	public int ParagraphCount { get; set; }

	public DocumentElementList Elements { get; set; }

	public DocumentHostKind HostKind { get; set; }

	public bool DocumentGridCompatibilityFallbackApplied { get; set; }

	public bool IsSelectionMode { get; set; }

	public bool PreserveSectionBreaksInCleanup { get; set; }

	public List<SignatureBlock> SignatureBlocks { get; set; } = new List<SignatureBlock>();

	public int SignatureIndex { get; set; } = -1;

	public int DateIndex { get; set; } = -1;

	public List<int> AttachmentMarkerIndices { get; set; } = new List<int>();

	public FormatConfig Config { get; set; }
}
