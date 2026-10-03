using System;
using System.Runtime.CompilerServices;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Formatting.PageNumbers;

internal static class PageNumberFactsMatcher
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool IsMatched(PageNumberFacts facts, FormatConfig config, Application application)
	{
		if (facts == null || !facts.IsReliable || application == null || config == null)
		{
			return false;
		}
		string mode = PageNumberModes.Normalize(config.PageNumberMode, config.EnablePageNumbers);
		if (PageNumberModes.IsEnabled(mode, config.EnablePageNumbers))
		{
			bool flag = config.PageAlign == PageAlignType.OddEvenDifferent;
			bool flag2 = PageNumberModes.IsFirstPageHidden(mode);
			string leftWing = NormalizeWing(config.PageLeftWing);
			string rightWing = NormalizeWing(config.PageRightWing);
			PageNumberFontSlots expectedFonts = PageNumberFontSlotPolicy.Resolve(RequireText(config.Body?.FontName, "正文中文字体"), RequireText(config.PageNumberFontName, "页码字体"));
			float expectedSize = FontSizeHelper.ToPoints(RequireText(config.PageFontSize, "页码字号"));
			int expectedBold = (config.PageNumberBold ? (-1) : 0);
			float num = application.CentimetersToPoints(config.FooterDistance);
			int num2 = 0;
			int num3 = ((!flag) ? 1 : 2);
			int num4 = 0;
			while (true)
			{
				if (num4 < facts.Sections.Count)
				{
					PageNumberFacts.SectionFacts sectionFacts = facts.Sections[num4];
					int num5 = num4 + 1;
					if (sectionFacts.OddAndEvenPagesHeaderFooter != flag)
					{
						break;
					}
					if (sectionFacts.DifferentFirstPageHeaderFooter == (flag2 && num5 == 1))
					{
						if (Math.Abs(sectionFacts.FooterDistance - num) <= 0.8f)
						{
							if (flag)
							{
								if (!FooterMatches(sectionFacts.Footers[WdHeaderFooterIndex.wdHeaderFooterPrimary], WdParagraphAlignment.wdAlignParagraphRight, 0f, 1f, leftWing, rightWing, expectedFonts, expectedSize, expectedBold))
								{
									return false;
								}
								if (!FooterMatches(sectionFacts.Footers[WdHeaderFooterIndex.wdHeaderFooterEvenPages], WdParagraphAlignment.wdAlignParagraphLeft, 1f, 0f, leftWing, rightWing, expectedFonts, expectedSize, expectedBold))
								{
									return false;
								}
							}
							else
							{
								float leftChars = 0f;
								float rightChars = 0f;
								ResolveAlignment(config.PageAlign, out var alignment, out leftChars, out rightChars);
								if (!FooterMatches(sectionFacts.Footers[WdHeaderFooterIndex.wdHeaderFooterPrimary], alignment, leftChars, rightChars, leftWing, rightWing, expectedFonts, expectedSize, expectedBold))
								{
									return false;
								}
							}
							if (!flag2 || num5 != 1 || (sectionFacts.FirstPageFooter != null && sectionFacts.FirstPageFooter.PageFieldCount == 0))
							{
								num2 += num3;
								num4++;
								continue;
							}
							return false;
						}
						return false;
					}
					return false;
				}
				if (facts.TotalPageFields != num2)
				{
					return false;
				}
				return true;
			}
			return false;
		}
		return facts.TotalPageFields == 0;
	}

	private static bool FooterMatches(PageNumberFacts.FooterFacts footer, WdParagraphAlignment alignment, float leftChars, float rightChars, string leftWing, string rightWing, PageNumberFontSlots expectedFonts, float expectedSize, int expectedBold)
	{
		if (footer != null)
		{
			if (footer.PageFieldCount == 1)
			{
				if (footer.GeneratedParagraphCount != 1)
				{
					return false;
				}
				if (footer.Alignment != alignment)
				{
					return false;
				}
				if (!(Math.Abs(footer.CharacterUnitLeftIndent - leftChars) <= 0.1f))
				{
					return false;
				}
				if (Math.Abs(footer.CharacterUnitRightIndent - rightChars) > 0.1f)
				{
					return false;
				}
				string value = leftWing + (footer.ResultText ?? string.Empty) + rightWing;
				if (!string.Equals(footer.VisibleText ?? string.Empty, NormalizeVisibleText(value), StringComparison.Ordinal))
				{
					return false;
				}
				if (SameText(footer.FontNameFarEast, expectedFonts.EastAsianFontName))
				{
					if (!SameText(footer.FontNameAscii, expectedFonts.AsciiFontName))
					{
						return false;
					}
					if (Math.Abs(footer.FontSize - expectedSize) > 0.1f)
					{
						return false;
					}
					if (footer.FontBold != expectedBold)
					{
						return false;
					}
					return true;
				}
				return false;
			}
			return false;
		}
		return false;
	}

	private static void ResolveAlignment(PageAlignType pageAlign, out WdParagraphAlignment alignment, out float leftChars, out float rightChars)
	{
		leftChars = 0f;
		rightChars = 0f;
		switch (pageAlign)
		{
		case PageAlignType.Right:
			alignment = WdParagraphAlignment.wdAlignParagraphRight;
			rightChars = 1f;
			break;
		case PageAlignType.Left:
			alignment = WdParagraphAlignment.wdAlignParagraphLeft;
			leftChars = 1f;
			break;
		default:
			alignment = WdParagraphAlignment.wdAlignParagraphCenter;
			break;
		}
	}

	private static string NormalizeWing(string value)
	{
		return (value ?? string.Empty).Trim();
	}

	private static string NormalizeVisibleText(string value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return string.Empty;
		}
		char[] array = new char[value.Length];
		int length = 0;
		foreach (char c in value)
		{
			if (c != '\r' && c != '\a' && !char.IsWhiteSpace(c))
			{
				array[length++] = c;
			}
		}
		return new string(array, 0, length);
	}

	private static bool SameText(string actual, string expected)
	{
		return string.Equals((actual ?? string.Empty).Trim(), expected, StringComparison.OrdinalIgnoreCase);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string RequireText(string value, string fieldName)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw new InvalidOperationException(fieldName + "为空。");
		}
		return value.Trim();
	}
}
