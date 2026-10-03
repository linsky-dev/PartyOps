using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DocumentRepository.Models;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.Performance;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Formatting;

internal static class TextAppearanceStyleService
{
	internal sealed class TextStyleExpectation
	{
		public bool StyleMissing;

		public string NameFarEast;

		public string NameAscii;

		public float Size;

		public int Bold;

		public int Italic;

		public WdColor Color;

		public WdColorIndex ColorIndex;
	}

	internal sealed class TextAppearanceApplyContext
	{
		private readonly Dictionary<ElementType, TextStyleExpectation> _expectations;

		[MethodImpl(MethodImplOptions.NoInlining)]
		public TextAppearanceApplyContext(Document document, IEnumerable<ElementType> types)
		{
			if (document != null)
			{
				_expectations = new Dictionary<ElementType, TextStyleExpectation>();
				if (types == null)
				{
					return;
				}
				{
					foreach (ElementType type in types)
					{
						if (IsSupported(type) && !_expectations.ContainsKey(type))
						{
							_expectations[type] = CaptureExpectation(document, type);
						}
					}
					return;
				}
			}
			throw new ArgumentNullException("document");
		}

		public bool TryGetColor(ElementType type, out WdColor color)
		{
			if (_expectations.TryGetValue(type, out var value) && !value.StyleMissing)
			{
				color = value.Color;
				return true;
			}
			color = WdColor.wdColorBlack;
			return false;
		}
	}

	private const string CharacterStylePrefix = "OfficialDoc.Text.";

	private const string LegacyCharacterStylePrefix = "SX$TextAppearance$";

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void EnsureStyles(Document document, IEnumerable<ElementType> requiredTypes, FirstFormatDiagnosticsSession diagnostics = null)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		HashSet<ElementType> hashSet = new HashSet<ElementType>();
		if (requiredTypes == null)
		{
			foreach (ElementType value2 in Enum.GetValues(typeof(ElementType)))
			{
				if (IsSupported(value2))
				{
					hashSet.Add(value2);
				}
			}
		}
		else
		{
			foreach (ElementType requiredType in requiredTypes)
			{
				if (IsSupported(requiredType))
				{
					hashSet.Add(requiredType);
				}
			}
		}
		Styles value = null;
		try
		{
			value = document.Styles;
			long startTimestamp = FirstFormatDiagnosticsSession.Timestamp();
			MigrateLegacyStyles(value, hashSet);
			diagnostics?.Accumulate("style-ensure.charstyle-migration", startTimestamp, hashSet.Count);
			int num = 0;
			foreach (ElementType item in hashSet)
			{
				if (EnsureStyle(value, item, diagnostics))
				{
					num++;
				}
			}
			LogService.Info("文字外观样式检查完成：checked=" + hashSet.Count + ", modified=" + num + ", skipped=" + (hashSet.Count - num));
		}
		finally
		{
			ComObjectRelease.Release(ref value, "TextAppearanceStyleService.EnsureStyles.Styles");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void MigrateLegacyStyles(Styles styles, IEnumerable<ElementType> types)
	{
		foreach (ElementType type in types)
		{
			string text = "SX$TextAppearance$" + type;
			string characterStyleName = GetCharacterStyleName(type);
			Style value = null;
			Style value2 = null;
			try
			{
				value = FindStyle(styles, text);
				if (value != null)
				{
					value2 = FindStyle(styles, characterStyleName);
					string text2 = ((value2 == null) ? characterStyleName : FindAvailableLegacyStyleName(styles, characterStyleName + ".Legacy"));
					try
					{
						value.NameLocal = text2;
						HideFromStyleGalleryIfNeeded(value, text2);
						LogService.Info("旧版文字外观样式已迁移为英文名称：" + text + " -> " + text2);
					}
					catch (COMException ex)
					{
						HideFromStyleGalleryIfNeeded(value, text);
						LogService.Warn("旧版文字外观样式英文改名失败，已继续隐藏：" + text, ex);
					}
				}
			}
			finally
			{
				ComObjectRelease.Release(ref value2, "TextAppearanceStyleService.Migrate.CurrentStyle");
				ComObjectRelease.Release(ref value, "TextAppearanceStyleService.Migrate.LegacyStyle");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string FindAvailableLegacyStyleName(Styles styles, string baseName)
	{
		for (int i = 0; i < 100; i++)
		{
			string text = ((i == 0) ? baseName : (baseName + "." + (i + 1)));
			Style value = null;
			try
			{
				value = FindStyle(styles, text);
				if (value == null)
				{
					return text;
				}
			}
			finally
			{
				ComObjectRelease.Release(ref value, "TextAppearanceStyleService.FindLegacyStyle.Existing");
			}
		}
		throw new InvalidOperationException("无法为旧版文字外观样式生成可用的英文名称：" + baseName);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ApplyParagraphText(Paragraph paragraph, ElementType type, FirstFormatDiagnosticsSession diagnostics = null, TextAppearanceApplyContext applyContext = null)
	{
		if (paragraph == null)
		{
			throw new ArgumentNullException("paragraph");
		}
		if (!IsSupported(type))
		{
			return;
		}
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			value = paragraph.Range;
			ApplyTextRange(value, type, diagnostics, applyContext);
		}
		finally
		{
			ComObjectRelease.Release(ref value, "TextAppearanceStyleService.ApplyParagraphText.Range");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ApplyParagraphText(Microsoft.Office.Interop.Word.Range range, ElementType type, FirstFormatDiagnosticsSession diagnostics = null, TextAppearanceApplyContext applyContext = null)
	{
		if (range == null)
		{
			throw new ArgumentNullException("range");
		}
		if (IsSupported(type))
		{
			ApplyTextRange(range, type, diagnostics, applyContext);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ApplyParagraphRun(Document document, Paragraphs paragraphs, int start, int end, ElementType type, FirstFormatDiagnosticsSession diagnostics = null, TextAppearanceApplyContext applyContext = null)
	{
		if (document != null)
		{
			if (paragraphs != null)
			{
				if (!IsSupported(type) || start > end)
				{
					return;
				}
				Paragraph value = null;
				Paragraph value2 = null;
				Microsoft.Office.Interop.Word.Range value3 = null;
				Microsoft.Office.Interop.Word.Range value4 = null;
				Microsoft.Office.Interop.Word.Range value5 = null;
				try
				{
					value = paragraphs[start + 1];
					value2 = paragraphs[end + 1];
					value3 = value.Range;
					value4 = value2.Range;
					long startTimestamp = FirstFormatDiagnosticsSession.Timestamp();
					object Start = value3.Start;
					object End = value4.End;
					value5 = document.Range(ref Start, ref End);
					diagnostics?.Accumulate("apply.range-create", startTimestamp);
					ApplyTextRange(value5, type, diagnostics, applyContext);
					return;
				}
				finally
				{
					ComObjectRelease.Release(ref value5, "TextAppearanceStyleService.ApplyParagraphRun.CombinedRange");
					ComObjectRelease.Release(ref value4, "TextAppearanceStyleService.ApplyParagraphRun.LastRange");
					ComObjectRelease.Release(ref value3, "TextAppearanceStyleService.ApplyParagraphRun.FirstRange");
					ComObjectRelease.Release(ref value2, "TextAppearanceStyleService.ApplyParagraphRun.LastParagraph");
					ComObjectRelease.Release(ref value, "TextAppearanceStyleService.ApplyParagraphRun.FirstParagraph");
				}
			}
			throw new ArgumentNullException("paragraphs");
		}
		throw new ArgumentNullException("document");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ApplyParagraphRun(Document document, int start, int end, ElementType type, FirstFormatDiagnosticsSession diagnostics = null, TextAppearanceApplyContext applyContext = null)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		Paragraphs value = null;
		try
		{
			value = document.Paragraphs;
			ApplyParagraphRun(document, value, start, end, type, diagnostics, applyContext);
		}
		finally
		{
			ComObjectRelease.Release(ref value, "TextAppearanceStyleService.ApplyParagraphRun.Paragraphs");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool ApplyTextRange(Microsoft.Office.Interop.Word.Range sourceRange, ElementType type, FirstFormatDiagnosticsSession diagnostics = null, TextAppearanceApplyContext applyContext = null)
	{
		if (sourceRange == null)
		{
			throw new ArgumentNullException("sourceRange");
		}
		if (!IsSupported(type))
		{
			return false;
		}
		Microsoft.Office.Interop.Word.Range value = null;
		Font value2 = null;
		Style value3 = null;
		Font value4 = null;
		try
		{
			long startTimestamp = FirstFormatDiagnosticsSession.Timestamp();
			long startTimestamp2 = FirstFormatDiagnosticsSession.Timestamp();
			value = sourceRange.Duplicate;
			TrimTerminalParagraphMarks(value);
			if (value.End <= value.Start)
			{
				diagnostics?.Accumulate("apply.charstyle-apply", startTimestamp);
				return false;
			}
			diagnostics?.Accumulate("apply.charstyle-range-prep", startTimestamp2);
			string characterStyleName = GetCharacterStyleName(type);
			startTimestamp2 = FirstFormatDiagnosticsSession.Timestamp();
			if (RangeHasStyle(value, characterStyleName))
			{
				diagnostics?.Accumulate("apply.charstyle-rangehasstyle", startTimestamp2);
				diagnostics?.Accumulate("apply.charstyle-apply", startTimestamp);
				return false;
			}
			diagnostics?.Accumulate("apply.charstyle-rangehasstyle", startTimestamp2);
			object prop = characterStyleName;
			startTimestamp2 = FirstFormatDiagnosticsSession.Timestamp();
			value.set_Style(ref prop);
			diagnostics?.Accumulate("apply.charstyle-set", startTimestamp2);
			startTimestamp2 = FirstFormatDiagnosticsSession.Timestamp();
			if (applyContext == null || !applyContext.TryGetColor(type, out var color))
			{
				value3 = value.Document.Styles.get_Item(ref prop);
				value4 = value3.Font;
				color = value4.Color;
			}
			diagnostics?.Accumulate("apply.charstyle-styles-lookup", startTimestamp2);
			startTimestamp2 = FirstFormatDiagnosticsSession.Timestamp();
			value2 = value.Font;
			diagnostics?.Accumulate("apply.charstyle-font-lookup", startTimestamp2);
			startTimestamp2 = FirstFormatDiagnosticsSession.Timestamp();
			value2.ColorIndex = WdColorIndex.wdAuto;
			value2.Color = color;
			diagnostics?.Accumulate("apply.charstyle-color-sync", startTimestamp2);
			diagnostics?.Accumulate("apply.charstyle-apply", startTimestamp);
			return true;
		}
		finally
		{
			if (value4 != null)
			{
				ComObjectRelease.Release(ref value4, "TextAppearanceStyleService.ApplyTextRange.StyleFont");
			}
			if (value3 != null)
			{
				ComObjectRelease.Release(ref value3, "TextAppearanceStyleService.ApplyTextRange.TargetStyle");
			}
			if (value2 != null)
			{
				ComObjectRelease.Release(ref value2, "TextAppearanceStyleService.ApplyTextRange.Font");
			}
			ComObjectRelease.Release(ref value, "TextAppearanceStyleService.ApplyTextRange.Range");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static TextStyleExpectation CaptureExpectation(Document document, ElementType type)
	{
		if (document != null)
		{
			TextStyleExpectation textStyleExpectation = new TextStyleExpectation();
			if (IsSupported(type))
			{
				Styles value = null;
				Style value2 = null;
				Font value3 = null;
				try
				{
					value = document.Styles;
					value2 = FindStyle(value, GetCharacterStyleName(type));
					if (value2 == null)
					{
						textStyleExpectation.StyleMissing = true;
						return textStyleExpectation;
					}
					value3 = value2.Font;
					textStyleExpectation.NameFarEast = value3.NameFarEast;
					textStyleExpectation.NameAscii = value3.NameAscii;
					textStyleExpectation.Size = value3.Size;
					textStyleExpectation.Bold = value3.Bold;
					textStyleExpectation.Italic = value3.Italic;
					textStyleExpectation.Color = value3.Color;
					textStyleExpectation.ColorIndex = value3.ColorIndex;
					return textStyleExpectation;
				}
				finally
				{
					ComObjectRelease.Release(ref value3, "TextAppearanceStyleService.Expectation.Font");
					ComObjectRelease.Release(ref value2, "TextAppearanceStyleService.Expectation.Style");
					ComObjectRelease.Release(ref value, "TextAppearanceStyleService.Expectation.Styles");
				}
			}
			textStyleExpectation.StyleMissing = true;
			return textStyleExpectation;
		}
		throw new ArgumentNullException("document");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static bool IsParagraphTextCompliant(Microsoft.Office.Interop.Word.Range range, ElementType type, TextStyleExpectation expectation)
	{
		if (range == null)
		{
			throw new ArgumentNullException("range");
		}
		if (expectation == null)
		{
			throw new ArgumentNullException("expectation");
		}
		if (!IsSupported(type))
		{
			return false;
		}
		Microsoft.Office.Interop.Word.Range value = null;
		Font value2 = null;
		try
		{
			value = range.Duplicate;
			TrimTerminalParagraphMarks(value);
			if (value.End > value.Start)
			{
				string characterStyleName = GetCharacterStyleName(type);
				if (RangeHasStyle(value, characterStyleName))
				{
					if (expectation.StyleMissing)
					{
						return false;
					}
					value2 = value.Font;
					return FontsMatchExpectation(value2, expectation);
				}
				return false;
			}
			return true;
		}
		finally
		{
			ComObjectRelease.Release(ref value2, "TextAppearanceStyleService.Compliance.ActualFont");
			ComObjectRelease.Release(ref value, "TextAppearanceStyleService.Compliance.Range");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool EnsureStyle(Styles styles, ElementType type, FirstFormatDiagnosticsSession diagnostics = null)
	{
		string styleName = DocumentStyleManager.GetStyleName(type);
		string characterStyleName = GetCharacterStyleName(type);
		Style value = null;
		Style value2 = null;
		Font value3 = null;
		Font value4 = null;
		bool result = false;
		try
		{
			value = FindStyle(styles, styleName);
			if (value != null)
			{
				value2 = FindStyle(styles, characterStyleName);
				if (value2 == null)
				{
					long startTimestamp = FirstFormatDiagnosticsSession.Timestamp();
					object Type = WdStyleType.wdStyleTypeCharacter;
					value2 = styles.Add(characterStyleName, ref Type);
					diagnostics?.Accumulate("style-ensure.charstyle-create", startTimestamp);
					result = true;
				}
				long startTimestamp2 = FirstFormatDiagnosticsSession.Timestamp();
				value3 = value.Font;
				value4 = value2.Font;
				if (CopyFontIfNeeded(value3, value4))
				{
					result = true;
				}
				if (HideFromStyleGalleryIfNeeded(value2, characterStyleName))
				{
					result = true;
				}
				diagnostics?.Accumulate("style-ensure.charstyle-sync", startTimestamp2);
				return result;
			}
			throw new InvalidOperationException("未找到段落样式，无法生成文字外观样式：" + styleName);
		}
		finally
		{
			ComObjectRelease.Release(ref value4, "TextAppearanceStyleService.EnsureStyle.TargetFont");
			ComObjectRelease.Release(ref value3, "TextAppearanceStyleService.EnsureStyle.SourceFont");
			ComObjectRelease.Release(ref value2, "TextAppearanceStyleService.EnsureStyle.TargetStyle");
			ComObjectRelease.Release(ref value, "TextAppearanceStyleService.EnsureStyle.SourceStyle");
		}
	}

	private static Style FindStyle(Styles styles, string name)
	{
		if (styles == null || string.IsNullOrWhiteSpace(name))
		{
			return null;
		}
		object Index = name;
		try
		{
			return styles.get_Item(ref Index);
		}
		catch (COMException)
		{
			return null;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool CopyFontIfNeeded(Font source, Font target)
	{
		if (source == null)
		{
			throw new ArgumentNullException("source");
		}
		if (target != null)
		{
			bool result = false;
			if (!DocumentFontSlotService.SameFontName(target.NameFarEast, source.NameFarEast))
			{
				target.NameFarEast = source.NameFarEast;
				result = true;
			}
			if (!DocumentFontSlotService.SameFontName(target.NameAscii, source.NameAscii))
			{
				target.NameAscii = source.NameAscii;
				result = true;
			}
			if (Math.Abs(target.Size - source.Size) > 0.1f)
			{
				target.Size = source.Size;
				result = true;
			}
			if (target.Bold != source.Bold)
			{
				target.Bold = source.Bold;
				result = true;
			}
			if (target.Italic != source.Italic)
			{
				target.Italic = source.Italic;
				result = true;
			}
			if (target.Color != source.Color)
			{
				target.Color = source.Color;
				result = true;
			}
			if (target.ColorIndex != source.ColorIndex)
			{
				target.ColorIndex = source.ColorIndex;
				result = true;
			}
			return result;
		}
		throw new ArgumentNullException("target");
	}

	private static bool FontsMatchExpectation(Font actual, TextStyleExpectation expected)
	{
		if (actual != null && expected != null)
		{
			if (DocumentFontSlotService.SameFontName(actual.NameFarEast, expected.NameFarEast))
			{
				if (DocumentFontSlotService.SameFontName(actual.NameAscii, expected.NameAscii))
				{
					if (Math.Abs(actual.Size - expected.Size) <= 0.1f)
					{
						if (actual.Bold != expected.Bold)
						{
							return false;
						}
						if (actual.Italic != expected.Italic)
						{
							return false;
						}
						if (actual.Color == expected.Color)
						{
							return true;
						}
						return ColorIndexMatchesExpectedColor(actual.ColorIndex, expected);
					}
					return false;
				}
				return false;
			}
			return false;
		}
		return false;
	}

	private static bool ColorIndexMatchesExpectedColor(WdColorIndex actual, TextStyleExpectation expected)
	{
		if (actual != expected.ColorIndex || expected.Color != WdColor.wdColorAutomatic)
		{
			if (TryMapColorIndex(actual, out var color))
			{
				return color == expected.Color;
			}
			return false;
		}
		return true;
	}

	private static bool TryMapColorIndex(WdColorIndex index, out WdColor color)
	{
		switch (index)
		{
		case WdColorIndex.wdGreen:
			color = WdColor.wdColorGreen;
			return true;
		case WdColorIndex.wdTurquoise:
			color = WdColor.wdColorTurquoise;
			return true;
		case WdColorIndex.wdBrightGreen:
			color = WdColor.wdColorBrightGreen;
			return true;
		case WdColorIndex.wdDarkBlue:
			color = WdColor.wdColorDarkBlue;
			return true;
		case WdColorIndex.wdTeal:
			color = WdColor.wdColorTeal;
			return true;
		case WdColorIndex.wdByAuthor:
			color = WdColor.wdColorAutomatic;
			return true;
		case WdColorIndex.wdGray25:
			color = WdColor.wdColorGray25;
			return true;
		case WdColorIndex.wdBlue:
			color = WdColor.wdColorBlue;
			return true;
		case WdColorIndex.wdYellow:
			color = WdColor.wdColorYellow;
			return true;
		case WdColorIndex.wdPink:
			color = WdColor.wdColorPink;
			return true;
		case WdColorIndex.wdViolet:
			color = WdColor.wdColorViolet;
			return true;
		case WdColorIndex.wdWhite:
			color = WdColor.wdColorWhite;
			return true;
		case WdColorIndex.wdRed:
			color = WdColor.wdColorRed;
			return true;
		case WdColorIndex.wdAuto:
			color = WdColor.wdColorAutomatic;
			return true;
		case WdColorIndex.wdGray50:
			color = WdColor.wdColorGray50;
			return true;
		case WdColorIndex.wdBlack:
			color = WdColor.wdColorBlack;
			return true;
		case WdColorIndex.wdDarkRed:
			color = WdColor.wdColorDarkRed;
			return true;
		case WdColorIndex.wdDarkYellow:
			color = WdColor.wdColorDarkYellow;
			return true;
		default:
			color = WdColor.wdColorBlack;
			return false;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool HideFromStyleGalleryIfNeeded(Style style, string styleName)
	{
		if (style == null)
		{
			return false;
		}
		bool result = false;
		try
		{
			if (style.QuickStyle)
			{
				style.QuickStyle = false;
				result = true;
			}
			if (style.UnhideWhenUsed)
			{
				style.UnhideWhenUsed = false;
				result = true;
			}
			if (style.Visibility)
			{
				style.Visibility = false;
				result = true;
			}
			if (style.Priority != 99)
			{
				style.Priority = 99;
				result = true;
			}
		}
		catch (COMException ex)
		{
			LogService.Warn("隐藏文字外观样式失败，样式仍可正常使用：" + styleName, ex);
		}
		return result;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool RangeHasStyle(Microsoft.Office.Interop.Word.Range range, string expectedStyleName)
	{
		object obj = null;
		try
		{
			obj = range.get_Style();
			return string.Equals((((obj is Style style) ? style.NameLocal : Convert.ToString(obj, CultureInfo.InvariantCulture)) ?? string.Empty).Trim(), (expectedStyleName ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
		}
		catch (COMException)
		{
			return false;
		}
		finally
		{
			ComObjectRelease.Release(obj, "TextAppearanceStyleService.RangeHasStyle.Style");
		}
	}

	private static void TrimTerminalParagraphMarks(Microsoft.Office.Interop.Word.Range range)
	{
		if (range == null)
		{
			return;
		}
		string text = range.Text ?? string.Empty;
		int num = 0;
		for (int num2 = text.Length - 1; num2 >= 0; num2--)
		{
			char c = text[num2];
			if (c != '\r' && c != '\n' && c != '\a' && c != '\v' && c != '\f')
			{
				break;
			}
			num++;
		}
		if (num > 0)
		{
			range.End = Math.Max(range.Start, range.End - num);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string GetCharacterStyleName(ElementType type)
	{
		return "OfficialDoc.Text." + type;
	}

	private static bool IsSupported(ElementType type)
	{
		if ((uint)(type - 1) <= 14u)
		{
			return true;
		}
		return false;
	}
}
