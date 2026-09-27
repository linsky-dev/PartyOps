using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Services.Detection;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.Performance;
using DocumentRepository.Services.Snapshots;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Formatting;

internal static class MixedHeadingBodyStyleService
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void RepairMixedHeadingBodyFonts(Document doc, FormatConfig cfg, DocumentElementList elements)
	{
		if (doc != null)
		{
			if (cfg != null)
			{
				if (elements == null || elements.Items == null)
				{
					throw new InvalidOperationException("Mixed heading repair requires DocumentElementList from analysis.");
				}
				FormatTextStyleDefinition styleDefinition = FormatStyleDefinitionBuilder.Build(cfg);
				ParagraphTextSnapshot paragraphTextSnapshot = ParagraphTextSnapshot.Capture(doc);
				bool isReliable = paragraphTextSnapshot.IsReliable;
				if (!isReliable && !string.IsNullOrWhiteSpace(paragraphTextSnapshot.FailureReason))
				{
					LogService.Warn("MixedHeadingBodyStyleService.RepairMixedHeadingBodyFonts text snapshot unavailable, fallback: " + paragraphTextSnapshot.FailureReason);
				}
				{
					foreach (DocumentElement item in elements.Items)
					{
						if (item == null || item.IsEmpty || !IsMixedHeadingCandidate(item.Type))
						{
							continue;
						}
						if (isReliable)
						{
							if (item.ParagraphIndex <= 0 || item.ParagraphIndex > paragraphTextSnapshot.Paragraphs.Count)
							{
								continue;
							}
							WordParagraphTextMap.Entry entry = paragraphTextSnapshot.Paragraphs[item.ParagraphIndex - 1];
							Microsoft.Office.Interop.Word.Range value = null;
							try
							{
								object Start = entry.RangeStart;
								object End = entry.RangeEnd;
								value = doc.Range(ref Start, ref End);
								string text = (entry.Text ?? item.Text ?? string.Empty).TrimEnd('\r', '\a');
								ApplyCharacterOverrides(value, item.Type, text, styleDefinition, preserveBodyBold: true);
							}
							finally
							{
								if (value != null)
								{
									ComObjectRelease.Release(ref value, "MixedHeadingBodyStyleService.range");
								}
							}
						}
						else
						{
							if (item.ParagraphIndex <= 0 || item.ParagraphIndex > doc.Paragraphs.Count)
							{
								continue;
							}
							Paragraph value2 = null;
							try
							{
								value2 = doc.Paragraphs[item.ParagraphIndex];
								string text2 = value2.Range.Text ?? item.Text ?? string.Empty;
								text2 = text2.TrimEnd('\r', '\a');
								ApplyCharacterOverrides(value2, item.Type, text2, styleDefinition, preserveBodyBold: true);
							}
							finally
							{
								if (value2 != null)
								{
									ComObjectRelease.Release(ref value2, "MixedHeadingBodyStyleService.para");
								}
							}
						}
					}
					return;
				}
			}
			throw new ArgumentNullException("cfg");
		}
		throw new ArgumentNullException("doc");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ApplyCharacterOverrides(Microsoft.Office.Interop.Word.Range paragraphRange, ElementType type, string text, FormatTextStyleDefinition styleDefinition, bool preserveBodyBold = false, FirstFormatDiagnosticsSession diagnostics = null)
	{
		if (!IsMixedHeadingCandidate(type))
		{
			return;
		}
		if (paragraphRange != null)
		{
			if (styleDefinition == null)
			{
				throw new ArgumentNullException("styleDefinition");
			}
			long startTimestamp = FirstFormatDiagnosticsSession.Timestamp();
			int num = MixedContentDetector.FindTitleBodyBoundary(text);
			if (num <= 0 || num >= text.Length - 1)
			{
				diagnostics?.Accumulate("apply.char-overrides", startTimestamp);
				return;
			}
			TextStyle textStyle = styleDefinition.GetTextStyle(type);
			TextStyle style = styleDefinition.Body ?? throw new InvalidOperationException("Body style definition is missing.");
			Microsoft.Office.Interop.Word.Range value = null;
			Microsoft.Office.Interop.Word.Range value2 = null;
			try
			{
				value = paragraphRange.Duplicate;
				value.Start = paragraphRange.Start;
				value.End = Math.Min(paragraphRange.Start + num + 1, paragraphRange.End - 1);
				ApplyFontToRange(value, textStyle, applyBold: true, styleDefinition.GetEnglishFontName(type, textStyle));
				value2 = paragraphRange.Duplicate;
				value2.Start = paragraphRange.Start + num + 1;
				value2.End = paragraphRange.End - 1;
				ApplyFontToRange(value2, style, !preserveBodyBold, styleDefinition.BodyEnglishFontName);
			}
			finally
			{
				if (value != null)
				{
					ComObjectRelease.Release(ref value, "MixedHeadingBodyStyleService.titleRng");
				}
				if (value2 != null)
				{
					ComObjectRelease.Release(ref value2, "MixedHeadingBodyStyleService.bodyRng");
				}
			}
			diagnostics?.Accumulate("apply.char-overrides", startTimestamp);
			return;
		}
		throw new ArgumentNullException("paragraphRange");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ApplyCharacterOverrides(Paragraph para, ElementType type, string text, FormatTextStyleDefinition styleDefinition, bool preserveBodyBold = false, FirstFormatDiagnosticsSession diagnostics = null)
	{
		if (!IsMixedHeadingCandidate(type))
		{
			return;
		}
		if (para != null)
		{
			if (styleDefinition == null)
			{
				throw new ArgumentNullException("styleDefinition");
			}
			long startTimestamp = FirstFormatDiagnosticsSession.Timestamp();
			int num = MixedContentDetector.FindTitleBodyBoundary(text);
			if (num <= 0 || num >= text.Length - 1)
			{
				diagnostics?.Accumulate("apply.char-overrides", startTimestamp);
				return;
			}
			TextStyle textStyle = styleDefinition.GetTextStyle(type);
			TextStyle style = styleDefinition.Body ?? throw new InvalidOperationException("Body style definition is missing.");
			Microsoft.Office.Interop.Word.Range value = null;
			Microsoft.Office.Interop.Word.Range value2 = null;
			Microsoft.Office.Interop.Word.Range value3 = null;
			try
			{
				value3 = para.Range;
				value = value3.Duplicate;
				value.Start = value3.Start;
				value.End = Math.Min(value3.Start + num + 1, value3.End - 1);
				ApplyFontToRange(value, textStyle, applyBold: true, styleDefinition.GetEnglishFontName(type, textStyle));
				value2 = value3.Duplicate;
				value2.Start = value3.Start + num + 1;
				value2.End = value3.End - 1;
				ApplyFontToRange(value2, style, !preserveBodyBold, styleDefinition.BodyEnglishFontName);
			}
			finally
			{
				if (value != null)
				{
					ComObjectRelease.Release(ref value, "MixedHeadingBodyStyleService.titleRng");
				}
				if (value2 != null)
				{
					ComObjectRelease.Release(ref value2, "MixedHeadingBodyStyleService.bodyRng");
				}
				if (value3 != null)
				{
					ComObjectRelease.Release(ref value3, "MixedHeadingBodyStyleService.paragraphRange");
				}
			}
			diagnostics?.Accumulate("apply.char-overrides", startTimestamp);
			return;
		}
		throw new ArgumentNullException("para");
	}

	public static bool IsMixedHeadingCandidate(ElementType type)
	{
		if (type != ElementType.Level1Title && type != ElementType.Level2Title && type != ElementType.Level3Title)
		{
			return type == ElementType.MainTitle;
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ApplyFontToRange(Microsoft.Office.Interop.Word.Range range, TextStyle style, bool applyBold = true, string englishFontName = null)
	{
		if (range == null)
		{
			throw new ArgumentNullException("range");
		}
		if (style == null)
		{
			throw new ArgumentNullException("style");
		}
		if (range.End <= range.Start)
		{
			return;
		}
		string eastAsianFontName = RequireText(style.FontName, "Mixed heading font name");
		string asciiFontName = RequireText(englishFontName, "Mixed heading english font name");
		float num = FontSizeHelper.ToPoints(style.FontSize);
		Font value = null;
		try
		{
			value = range.Font;
			DocumentFontSlotService.ApplyIfNeeded(value, eastAsianFontName, asciiFontName);
			if (!(Math.Abs(value.Size - num) <= 0.1f))
			{
				value.Size = num;
			}
			int num2 = (style.Bold ? (-1) : 0);
			if (applyBold && value.Bold != num2)
			{
				value.Bold = num2;
			}
			if (value.Italic != 0)
			{
				value.Italic = 0;
			}
			if (value.Color != WdColor.wdColorBlack)
			{
				value.Color = WdColor.wdColorBlack;
			}
			if (value.ColorIndex != WdColorIndex.wdBlack)
			{
				value.ColorIndex = WdColorIndex.wdBlack;
			}
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "MixedHeadingBodyStyleService.font");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string RequireText(string value, string fieldName)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw new FormatException(fieldName + " is empty.");
		}
		return value.Trim();
	}
}
