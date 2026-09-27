using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Services.Formatting.Tables;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Formatting;

public static class FormatSelectionElementService
{
	public static void FormatAttachments(Document doc, Application app, Microsoft.Office.Interop.Word.Range range, FormatContext fctx)
	{
		Validate(doc, app, range, fctx);
		if (fctx.Config.EnableAttachmentFormatting && fctx.HasAttachments)
		{
			AttachmentFormatter.FormatRange(doc, app, range, fctx.Config, fctx.Elements);
		}
	}

	public static void FormatSignatures(Document doc, Application app, Microsoft.Office.Interop.Word.Range range, FormatContext fctx)
	{
		Validate(doc, app, range, fctx);
		if (fctx.SignatureBlocks != null && fctx.SignatureBlocks.Count > 0)
		{
			SignatureFormatter.FormatSignatureRange(doc, app, fctx.Config, fctx.SignatureBlocks, fctx.HostKind);
		}
	}

	public static void FormatTables(Document doc, Application app, Microsoft.Office.Interop.Word.Range range, FormatContext fctx)
	{
		Validate(doc, app, range, fctx);
		if (fctx.Config.EnableTableFormatting && fctx.HasTables)
		{
			TableFormattingService.FormatRange(range, app, fctx.Config, fctx.Analysis, fctx.HostKind);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void Validate(Document doc, Application app, Microsoft.Office.Interop.Word.Range range, FormatContext fctx)
	{
		if (doc == null)
		{
			throw new ArgumentNullException("doc");
		}
		if (app != null)
		{
			if (range != null)
			{
				if (fctx == null)
				{
					throw new ArgumentNullException("fctx");
				}
				if (fctx.Config == null)
				{
					throw new InvalidOperationException("Format context is missing config.");
				}
				return;
			}
			throw new ArgumentNullException("range");
		}
		throw new ArgumentNullException("app");
	}
}
