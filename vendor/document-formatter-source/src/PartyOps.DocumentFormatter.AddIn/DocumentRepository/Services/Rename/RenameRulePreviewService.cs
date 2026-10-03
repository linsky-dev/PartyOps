using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Rename;

public static class RenameRulePreviewService
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static RenameRulePreviewInfo CreateFromApplication(Application application)
	{
		try
		{
			Document document = application?.ActiveDocument;
			if (document != null)
			{
				RenameInfo renameInfo = RenameAnalysisService.ExtractRenameInfo(document);
				return new RenameRulePreviewInfo
				{
					DocumentNumber = renameInfo.DocumentNumber,
					MainTitle = renameInfo.MainTitle,
					Subtitle = renameInfo.Subtitle
				};
			}
			return new RenameRulePreviewInfo();
		}
		catch (Exception ex)
		{
			LogService.Warn("RenameRulePreviewService.CreateFromApplication", ex);
			return new RenameRulePreviewInfo();
		}
	}
}
