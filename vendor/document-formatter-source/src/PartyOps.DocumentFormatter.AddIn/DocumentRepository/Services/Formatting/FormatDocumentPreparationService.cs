using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Formatting;

public static class FormatDocumentPreparationService
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ConvertDocumentNumbersToText(Document doc)
	{
		if (doc == null)
		{
			throw new ArgumentNullException("doc");
		}
		try
		{
			ListFormat listFormat = doc.Content.ListFormat;
			LogService.Info("[FORMAT-PREP] document-list-conversion-start");
			object NumberType = Type.Missing;
			listFormat.ConvertNumbersToText(ref NumberType);
			LogService.Info("[FORMAT-PREP] document-list-conversion-complete");
		}
		finally
		{
			LogService.Info("[FORMAT-PREP] document-list-proxies-detached");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ConvertRangeNumbersToText(Microsoft.Office.Interop.Word.Range range)
	{
		if (range == null)
		{
			throw new ArgumentNullException("range");
		}
		try
		{
			ListFormat listFormat = range.ListFormat;
			LogService.Info("[FORMAT-PREP] selection-list-conversion-start");
			object NumberType = Type.Missing;
			listFormat.ConvertNumbersToText(ref NumberType);
			LogService.Info("[FORMAT-PREP] selection-list-conversion-complete");
		}
		finally
		{
			LogService.Info("[FORMAT-PREP] selection-list-proxy-detached");
		}
	}
}
