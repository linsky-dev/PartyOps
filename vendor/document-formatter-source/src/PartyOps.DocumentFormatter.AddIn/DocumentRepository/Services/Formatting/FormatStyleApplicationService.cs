using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Models.Tasks;
using DocumentRepository.Services.Performance;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Formatting;

public static class FormatStyleApplicationService
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void FormatDocument(Document doc, FormatContext fctx, ITaskProgressReporter progress, FirstFormatDiagnosticsSession diagnostics = null)
	{
		if (doc != null)
		{
			if (fctx != null)
			{
				StyleBasedFormatEngine.FormatDocument(doc, fctx.Config, progress, fctx.Elements, diagnostics);
				return;
			}
			throw new ArgumentNullException("fctx");
		}
		throw new ArgumentNullException("doc");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void FormatSelection(Document doc, Microsoft.Office.Interop.Word.Range selectionRange, FormatContext fctx)
	{
		if (doc == null)
		{
			throw new ArgumentNullException("doc");
		}
		if (selectionRange != null)
		{
			if (fctx != null)
			{
				StyleBasedFormatEngine.FormatSelection(doc, selectionRange, fctx.Config, null, fctx.Elements);
				return;
			}
			throw new ArgumentNullException("fctx");
		}
		throw new ArgumentNullException("selectionRange");
	}
}
