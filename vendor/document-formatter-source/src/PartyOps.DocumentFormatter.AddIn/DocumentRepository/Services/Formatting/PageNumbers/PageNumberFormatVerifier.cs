using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.Safety;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Formatting.PageNumbers;

public static class PageNumberFormatVerifier
{
	private static readonly WdHeaderFooterIndex[] FooterIndexes = new WdHeaderFooterIndex[3]
	{
		WdHeaderFooterIndex.wdHeaderFooterPrimary,
		WdHeaderFooterIndex.wdHeaderFooterEvenPages,
		WdHeaderFooterIndex.wdHeaderFooterFirstPage
	};

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Verify(Document document, FormatConfig config)
	{
		if (document != null)
		{
			if (config == null)
			{
				throw new ArgumentNullException("config");
			}
			Verify(PageNumberFacts.Capture(document, null, config), config);
			return;
		}
		throw new ArgumentNullException("document");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Verify(PageNumberFacts facts, FormatConfig config)
	{
		if (facts != null)
		{
			if (config != null)
			{
				if (facts.IsReliable)
				{
					PageNumberFontSlots expectedFonts = PageNumberFontSlotPolicy.Resolve(RequireText((config.Body == null) ? null : config.Body.FontName, "正文中文字体"), RequireText(config.PageNumberFontName, "页码字体"));
					float expectedSize = FontSizeHelper.ToPoints(RequireText(config.PageFontSize, "页码字号"));
					int expectedBold = (config.PageNumberBold ? (-1) : 0);
					int num = 0;
					int count = facts.Sections.Count;
					bool flag = config.PageAlign == PageAlignType.OddEvenDifferent;
					string mode = PageNumberModes.Normalize(config.PageNumberMode, config.EnablePageNumbers);
					bool flag2 = PageNumberModes.IsFirstPageHidden(mode);
					for (int i = 0; i < count; i++)
					{
						PageNumberFacts.SectionFacts sectionFacts = facts.Sections[i];
						foreach (WdHeaderFooterIndex targetFooterIndex in facts.TargetFooterIndexes)
						{
							if (sectionFacts.Footers.TryGetValue(targetFooterIndex, out var value))
							{
								VerifyFooterFacts(value, expectedFonts, expectedSize, expectedBold);
								num += value.PageFieldCount;
							}
						}
						if (flag2 && i == 0 && sectionFacts.FirstPageFooter != null && sectionFacts.FirstPageFooter.PageFieldCount > 0)
						{
							ReportFormatWarning("page-number-first-page-field", "首页页脚不应存在 PAGE 域。");
						}
					}
					if (PageNumberModes.IsEnabled(mode, config.EnablePageNumbers))
					{
						if (num <= 0)
						{
							ReportFormatWarning("page-number-field-missing", "未找到 PAGE 页码域。");
						}
						int num2 = count * ((!flag) ? 1 : 2);
						if (num != num2)
						{
							ReportFormatWarning("page-number-field-count", "PAGE 页码域数量与计划不一致，实际为" + num + "，计划为" + num2 + "。");
						}
					}
					else if (num > 0)
					{
						ReportFormatWarning("page-number-disabled-but-present", "页码已禁用，但文档中仍存在 PAGE 域。");
					}
				}
				else
				{
					ReportFormatWarning("page-number-facts-unreliable", "页码最终验证事实不可靠：" + facts.UnreliableReason);
				}
				return;
			}
			throw new ArgumentNullException("config");
		}
		throw new ArgumentNullException("facts");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void VerifyFooterFacts(PageNumberFacts.FooterFacts footer, PageNumberFontSlots expectedFonts, float expectedSize, int expectedBold)
	{
		if (footer.PageFieldCount == 0)
		{
			return;
		}
		if (footer.PageFieldCount > 1)
		{
			ReportFormatWarning("page-number-paragraph-duplicate", "页脚中存在重复的公文页码段落。");
		}
		if (footer.GeneratedParagraphCount == 0)
		{
			ReportFormatWarning("page-number-field-orphan", "PAGE 域不在有效段落中。");
		}
		else if (footer.GeneratedParagraphCount <= 1)
		{
			if (footer.PageFieldCount == 1 && footer.GeneratedParagraphCount != 1)
			{
				ReportFormatWarning("page-number-field-location", "PAGE 域未位于唯一的公文页码段落中。");
			}
		}
		else
		{
			ReportFormatWarning("page-number-paragraph-duplicate", "页脚中存在重复的公文页码段落。");
		}
		VerifyFontName("东亚字体", footer.FontNameFarEast, expectedFonts.EastAsianFontName);
		VerifyFontName("ASCII 字体", footer.FontNameAscii, expectedFonts.AsciiFontName);
		if (Math.Abs(footer.FontSize - expectedSize) > 0.1f)
		{
			ReportFormatWarning("page-number-size", "页码字号未按参数生效。");
		}
		if (footer.FontBold != expectedBold)
		{
			ReportFormatWarning("page-number-bold", "页码加粗参数未生效。");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void VerifyFontName(string slot, string actual, string expected)
	{
		if (!string.Equals((actual ?? string.Empty).Trim(), expected, StringComparison.OrdinalIgnoreCase))
		{
			ReportFormatWarning("page-number-font", slot + "应为“" + expected + "”，实际为“" + actual + "”。");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ReportFormatWarning(string code, string detail)
	{
		LogService.Warn("PAGE-NUMBER-QUALITY " + code + ": " + detail);
		ExecutionWarningCollector.Report(code, "page-number", "warn.format.page-number");
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
