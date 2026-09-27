using System.IO;
using DocumentRepository.Models.Conversion;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Conversion;

public static class ConvertDocumentAnalyzer
{
	public static ConvertDocumentAnalysis Analyze(Document document)
	{
		if (document == null)
		{
			throw ConvertOperationException.Create(ConvertFailureReasonCode.NoActiveDocument, ConvertFailureStage.Analyze);
		}
		string documentPath = OutputPathService.GetDocumentPath(document);
		if (string.IsNullOrWhiteSpace(documentPath) || !File.Exists(documentPath))
		{
			throw ConvertOperationException.Create(ConvertFailureReasonCode.DocumentNeedsSave, ConvertFailureStage.Analyze);
		}
		return new ConvertDocumentAnalysis
		{
			SourcePath = documentPath,
			SourceExtension = Path.GetExtension(documentPath),
			PageCount = GetPageCount(document)
		};
	}

	private static int GetPageCount(Document document)
	{
		try
		{
			document.Repaginate();
		}
		catch
		{
		}
		object IncludeFootnotesAndEndnotes = false;
		try
		{
			return document.ComputeStatistics(WdStatistic.wdStatisticPages, ref IncludeFootnotesAndEndnotes);
		}
		catch
		{
			return 0;
		}
	}
}
