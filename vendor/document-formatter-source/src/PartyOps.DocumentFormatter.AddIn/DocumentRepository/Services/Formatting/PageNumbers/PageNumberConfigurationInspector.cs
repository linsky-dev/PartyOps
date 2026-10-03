using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Formatting.PageNumbers;

public static class PageNumberConfigurationInspector
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool IsMatched(Document document, Application application, FormatConfig config)
	{
		if (document != null)
		{
			if (application != null)
			{
				if (config == null)
				{
					throw new ArgumentNullException("config");
				}
				try
				{
					return PageNumberFactsMatcher.IsMatched(PageNumberFacts.Capture(document, application, config), config, application);
				}
				catch (Exception ex)
				{
					LogService.Info("Page number configuration requires rebuild: " + ex.Message);
					return false;
				}
			}
			throw new ArgumentNullException("application");
		}
		throw new ArgumentNullException("document");
	}
}
