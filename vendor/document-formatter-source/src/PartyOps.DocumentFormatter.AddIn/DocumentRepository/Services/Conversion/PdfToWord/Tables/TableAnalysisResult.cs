using System.Collections.Generic;

namespace DocumentRepository.Services.Conversion.PdfToWord.Tables;

public sealed class TableAnalysisResult
{
	public IList<PdfTableCandidate> Candidates { get; set; }

	public IList<PdfTableLayoutPlan> ConfirmedTables { get; set; }

	public TableAnalysisResult()
	{
		Candidates = new List<PdfTableCandidate>();
		ConfirmedTables = new List<PdfTableLayoutPlan>();
	}
}
