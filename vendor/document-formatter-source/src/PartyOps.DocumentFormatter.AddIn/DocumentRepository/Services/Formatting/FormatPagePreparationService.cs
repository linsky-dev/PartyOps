using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Services.Cleanup;
using DocumentRepository.Services.Performance;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Formatting;

public static class FormatPagePreparationService
{
	public static void ClearHeadersFootersIfNeeded(Document doc, FormatConfig cfg)
	{
		if (cfg != null && cfg.ClearHeadersFooters)
		{
			PageCleanupService.ClearHeadersFootersContent(doc);
		}
	}

	public static void NormalizeEmptyHeaderBorders(Document doc)
	{
		PageCleanupService.NormalizeEmptyHeaderBorders(doc);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ApplyPageSetup(Document doc, Application app, FormatContext fctx)
	{
		if (fctx == null)
		{
			throw new ArgumentNullException("fctx");
		}
		fctx.DocumentGridCompatibilityFallbackApplied = PageSetupManager.SetPageSetupIfNeeded(doc, app, fctx.Config, fctx.Analysis, fctx.HostKind);
	}

	public static void ClearBaseStyleResidue(Document doc)
	{
		FormattingCleanupService.ClearNormalStyleBorder(doc);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ApplyPageNumbers(Document doc, Application app, FormatContext fctx, FirstFormatDiagnosticsSession diagnostics = null)
	{
		if (fctx == null)
		{
			throw new ArgumentNullException("fctx");
		}
		if (fctx.Config != null)
		{
			PageSetupManager.SetPageNumbers(doc, app, fctx.Config, diagnostics);
			return;
		}
		throw new InvalidOperationException("Format context is missing config.");
	}
}
