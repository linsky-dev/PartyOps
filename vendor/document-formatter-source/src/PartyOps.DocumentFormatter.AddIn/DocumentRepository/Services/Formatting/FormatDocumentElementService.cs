using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Services.Cleanup;
using DocumentRepository.Services.Formatting.Tables;
using DocumentRepository.Services.Hosting;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Formatting;

public static class FormatDocumentElementService
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void InsertSignatureSpacing(FormatContext fctx)
	{
		if (fctx == null)
		{
			throw new ArgumentNullException("fctx");
		}
		if (fctx.Config == null)
		{
			throw new InvalidOperationException("Format context is missing config.");
		}
		if (fctx.SignatureBlocks != null && fctx.SignatureBlocks.Count > 0)
		{
			SignatureFormatter.InsertSpacingBeforeSignatureBlocks(fctx.Document, fctx.Config, fctx.SignatureBlocks);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void FormatAnchoredSignatures(FormatContext fctx)
	{
		if (fctx == null)
		{
			throw new ArgumentNullException("fctx");
		}
		if (fctx.Config != null)
		{
			if (fctx.SignatureBlocks != null && fctx.SignatureBlocks.Count > 0)
			{
				SignatureFormatter.FormatSignatureRange(fctx.Document, fctx.Application, fctx.Config, fctx.SignatureBlocks, fctx.HostKind);
			}
			return;
		}
		throw new InvalidOperationException("Format context is missing config.");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void FormatAttachments(FormatContext fctx)
	{
		if (fctx != null)
		{
			if (fctx.Config != null)
			{
				if (fctx.Config.EnableAttachmentFormatting && fctx.HasAttachments)
				{
					AttachmentFormatter.FormatDocument(fctx.Document, fctx.Application, fctx.Config, fctx.Elements);
				}
				return;
			}
			throw new InvalidOperationException("Format context is missing config.");
		}
		throw new ArgumentNullException("fctx");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void FormatTables(FormatContext fctx)
	{
		if (fctx != null)
		{
			if (fctx.Config != null)
			{
				if (fctx.Config.EnableTableFormatting && fctx.HasTables)
				{
					TableFormattingService.FormatDocument(fctx.Document, fctx.Application, fctx.Config, fctx.Analysis, fctx.HostKind);
				}
				return;
			}
			throw new InvalidOperationException("Format context is missing config.");
		}
		throw new ArgumentNullException("fctx");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void FixOrphanChars(FormatContext fctx)
	{
		if (fctx != null)
		{
			if (fctx.Config == null)
			{
				throw new InvalidOperationException("Format context is missing config.");
			}
			if (fctx.Config.EnableOrphanCharFix && fctx.HasOrphanCandidates)
			{
				OrphanCharFormatter.FixDocument(fctx.Document, fctx.Elements);
			}
			return;
		}
		throw new ArgumentNullException("fctx");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ClearTabs(FormatContext fctx)
	{
		if (fctx != null)
		{
			if (!fctx.HasTabs)
			{
				return;
			}
			if (fctx.Document != null)
			{
				if (fctx.Elements == null || fctx.Elements.Items == null)
				{
					throw new InvalidOperationException("制表符清理必须使用分析层产出的 DocumentElementList。");
				}
				bool flag = false;
				for (int num = fctx.Elements.Items.Count - 1; num >= 0; num--)
				{
					DocumentElement documentElement = fctx.Elements.Items[num];
					if (documentElement != null && documentElement.HasTabs)
					{
						Microsoft.Office.Interop.Word.Range value = null;
						try
						{
							Document document = fctx.Document;
							object Start = documentElement.RangeStart;
							object End = documentElement.RangeEnd;
							value = document.Range(ref Start, ref End);
							TextCleanupService.FindReplace(value, "\t", "", useWildcards: false);
							flag = true;
						}
						finally
						{
							if (value != null)
							{
								ComObjectRelease.Release(ref value, "FormatDocumentElementService.range");
							}
						}
					}
				}
				if (flag)
				{
					return;
				}
				throw new InvalidOperationException("分析结果标记存在制表符，但未找到任何候选段落。");
			}
			throw new InvalidOperationException("Format context is missing document.");
		}
		throw new ArgumentNullException("fctx");
	}
}
