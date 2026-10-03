using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Formatting;

public static class FormatKeywordService
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void FixSemicolonsForDocument(Document doc, FormatContext fctx)
	{
		if (fctx == null)
		{
			throw new ArgumentNullException("fctx");
		}
		if (fctx.Config == null)
		{
			throw new InvalidOperationException("Format context is missing config.");
		}
		if (fctx.HasSemicolonCandidates && fctx.Config.EnableFixSemicolons)
		{
			KeywordBoldener.FixSemicolonsForXShi(doc, fctx.Elements);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ApplyKeywordBoldForDocument(Document doc, FormatContext fctx)
	{
		if (fctx != null)
		{
			if (fctx.Config == null)
			{
				throw new InvalidOperationException("Format context is missing config.");
			}
			if (fctx.HasKeywordCandidates && KeywordBoldener.HasEnabledRules(fctx.Config))
			{
				KeywordBoldener.ApplyXShiBold(doc, fctx.Config, fctx.Elements);
			}
			return;
		}
		throw new ArgumentNullException("fctx");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void RepairMixedHeadingBodyFonts(Document doc, FormatContext fctx)
	{
		if (fctx == null)
		{
			throw new ArgumentNullException("fctx");
		}
		if (fctx.Config != null)
		{
			if (fctx.HasKeywordCandidates)
			{
				StyleBasedFormatEngine.RepairMixedHeadingBodyFonts(doc, fctx.Config, fctx.Elements);
			}
			return;
		}
		throw new InvalidOperationException("Format context is missing config.");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void FixSemicolonsForRange(Microsoft.Office.Interop.Word.Range range, FormatContext fctx)
	{
		if (fctx != null)
		{
			if (fctx.Config == null)
			{
				throw new InvalidOperationException("Format context is missing config.");
			}
			if (fctx.HasSemicolonCandidates && fctx.Config.EnableFixSemicolons)
			{
				KeywordBoldener.FixSemicolonsForXShi(range, fctx.Elements);
			}
			return;
		}
		throw new ArgumentNullException("fctx");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ApplyKeywordBoldForRange(Microsoft.Office.Interop.Word.Range range, FormatContext fctx)
	{
		if (fctx != null)
		{
			if (fctx.Config == null)
			{
				throw new InvalidOperationException("Format context is missing config.");
			}
			if (fctx.HasKeywordCandidates && KeywordBoldener.HasEnabledRules(fctx.Config))
			{
				KeywordBoldener.ApplyXShiBold(range, fctx.Config, fctx.Elements);
			}
			return;
		}
		throw new ArgumentNullException("fctx");
	}
}
