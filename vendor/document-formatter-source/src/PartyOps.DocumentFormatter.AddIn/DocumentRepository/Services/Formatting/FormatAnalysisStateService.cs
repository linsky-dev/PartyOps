using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Services.Analysis;

namespace DocumentRepository.Services.Formatting;

public static class FormatAnalysisStateService
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Apply(FormatContext fctx, DocumentAnalysisResult analysis)
	{
		if (fctx == null)
		{
			throw new ArgumentNullException("fctx");
		}
		if (analysis != null)
		{
			fctx.HasTables = analysis.HasTables;
			fctx.HasImages = analysis.HasImages;
			fctx.HasAttachments = analysis.HasAttachments;
			fctx.HasHyperlinks = analysis.HasHyperlinks;
			fctx.HasEnglishNumbers = analysis.HasEnglishNumbers;
			fctx.HasKeywordCandidates = analysis.HasKeywordCandidates;
			fctx.HasSemicolonCandidates = analysis.HasSemicolonCandidates;
			fctx.HasTabs = analysis.HasTabs;
			fctx.HasOrphanCandidates = analysis.HasOrphanCandidates;
			fctx.ParagraphCount = analysis.ParagraphCount;
			fctx.Elements = analysis.Elements;
			fctx.Analysis = analysis;
			return;
		}
		throw new ArgumentNullException("analysis");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string BuildStructuralSummary(FormatContext fctx)
	{
		if (fctx == null)
		{
			return "文档结构分析";
		}
		return "文档结构分析 paragraphs=" + fctx.ParagraphCount + ", tables=" + fctx.HasTables + ", images=" + fctx.HasImages + ", attachments=" + fctx.HasAttachments + ", hyperlinks=" + fctx.HasHyperlinks + ", englishNumbers=" + fctx.HasEnglishNumbers + ", semicolonCandidates=" + fctx.HasSemicolonCandidates + ", tabs=" + fctx.HasTabs + ", orphanCandidates=" + fctx.HasOrphanCandidates;
	}
}
