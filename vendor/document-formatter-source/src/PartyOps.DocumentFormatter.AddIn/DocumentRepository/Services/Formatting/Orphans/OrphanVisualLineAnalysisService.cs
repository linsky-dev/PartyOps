using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Formatting.Orphans;

public static class OrphanVisualLineAnalysisService
{
	public static OrphanVisualLineAnalysisSession CreateSession(Document document)
	{
		return new OrphanVisualLineAnalysisSession(document);
	}
}
