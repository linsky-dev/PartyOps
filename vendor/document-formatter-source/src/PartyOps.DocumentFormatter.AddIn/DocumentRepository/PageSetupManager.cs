using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using DocumentRepository.Models;
using DocumentRepository.Services.Analysis;
using DocumentRepository.Services.Formatting;
using DocumentRepository.Services.Formatting.PageNumbers;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.Performance;
using DocumentRepository.Services.Safety;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository;

public static class PageSetupManager
{
	private static readonly ISet<ElementType> PageNumberOnlyElementTypes = new HashSet<ElementType>();

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool SetPageSetup(Document doc, Application app, FormatConfig cfg, DocumentAnalysisResult analysis, DocumentHostKind hostKind)
	{
		if (doc == null)
		{
			throw new ArgumentNullException("doc");
		}
		if (app != null)
		{
			if (cfg == null)
			{
				throw new ArgumentNullException("cfg");
			}
			if (analysis != null)
			{
				WdMeasurementUnits? wdMeasurementUnits = null;
				bool? flag = null;
				if (cfg.EnableDocumentGrid)
				{
					Options options = null;
					try
					{
						options = app.Options;
						wdMeasurementUnits = options.MeasurementUnit;
						flag = options.UseCharacterUnit;
						options.MeasurementUnit = WdMeasurementUnits.wdCentimeters;
						options.UseCharacterUnit = true;
					}
					finally
					{
						if (options != null)
						{
							ComObjectRelease.ReleaseOwned(options, "SetPageSetup", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 54);
						}
					}
					Style style = null;
					try
					{
						Styles styles = doc.Styles;
						object Index = "正文";
						style = styles.get_Item(ref Index);
						if (style != null)
						{
							TextStyle textStyle = cfg.Body ?? throw new FormatException("正文参数缺失。");
							string text = RequireText(textStyle.FontName, "正文字体");
							string asciiFontName = EnglishNumberFontScopePolicy.ResolveStyleFont(cfg, ElementType.Body, text);
							float num = FontSizeHelper.ToPoints(textStyle.FontSize);
							Font font = style.Font;
							DocumentFontSlotService.Apply(font, text, asciiFontName);
							font.Size = num;
							font.SizeBi = num;
							font.Bold = (textStyle.Bold ? (-1) : 0);
							font.Italic = 0;
							ParagraphFormat paragraphFormat = style.ParagraphFormat;
							paragraphFormat.DisableLineHeightGrid = 0;
							paragraphFormat.WidowControl = 0;
							paragraphFormat.AutoAdjustRightIndent = -1;
							ComObjectRelease.ReleaseOwned(paragraphFormat, "SetPageSetup", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 86);
							ComObjectRelease.ReleaseOwned(font, "SetPageSetup", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 87);
						}
					}
					finally
					{
						if (style != null)
						{
							ComObjectRelease.ReleaseOwned(style, "SetPageSetup", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 92);
						}
					}
				}
				bool result = false;
				PageSetup pageSetup = doc.PageSetup;
				try
				{
					DocumentGridOptions gridOptions = GetGridOptions(cfg);
					pageSetup.TopMargin = app.CentimetersToPoints(cfg.TopMargin);
					pageSetup.BottomMargin = app.CentimetersToPoints(cfg.BottomMargin);
					pageSetup.LeftMargin = app.CentimetersToPoints(cfg.LeftMargin);
					pageSetup.RightMargin = app.CentimetersToPoints(cfg.RightMargin);
					pageSetup.PaperSize = WdPaperSize.wdPaperA4;
					pageSetup.HeaderDistance = app.CentimetersToPoints(cfg.HeaderDistance);
					pageSetup.FooterDistance = app.CentimetersToPoints(cfg.FooterDistance);
					if (cfg.EnableDocumentGrid)
					{
						pageSetup.LayoutMode = WdLayoutMode.wdLayoutModeGrid;
						string text2 = null;
						Exception compatibilityFailure = null;
						if (TrySetNativeGridProperty(pageSetup, "CharsLine", gridOptions.CharsPerLine, hostKind, out compatibilityFailure))
						{
							if (!TrySetNativeGridProperty(pageSetup, "LinesPage", gridOptions.LinesPerPage, hostKind, out compatibilityFailure))
							{
								text2 = "LinesPage";
							}
						}
						else
						{
							text2 = "CharsLine";
						}
						if (text2 != null)
						{
							pageSetup.LayoutMode = WdLayoutMode.wdLayoutModeDefault;
							result = true;
							LogService.Warn("DOCUMENT-GRID compatibility-fallback host=" + hostKind.ToString() + ", property=" + text2 + ", hresult=0x80004005, target=" + gridOptions.LinesPerPage + "x" + gridOptions.CharsPerLine + ", fallback=page-margins-and-exact-line-spacing", compatibilityFailure);
						}
						else
						{
							float num2 = DocumentGridMetrics.CalculateVerticalPitchPoints(pageSetup.PageHeight, pageSetup.TopMargin, pageSetup.BottomMargin, gridOptions.LinesPerPage);
							LogService.Info("文档网格已应用：" + gridOptions.LinesPerPage + "行×" + gridOptions.CharsPerLine + "字，预计网格行距" + num2.ToString("0.00") + "磅");
						}
					}
					else
					{
						pageSetup.LayoutMode = WdLayoutMode.wdLayoutModeDefault;
					}
				}
				finally
				{
					if (pageSetup != null)
					{
						ComObjectRelease.ReleaseOwned(pageSetup, "SetPageSetup", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 152);
					}
				}
				if (wdMeasurementUnits.HasValue || flag.HasValue)
				{
					Options options2 = null;
					try
					{
						options2 = app.Options;
						if (wdMeasurementUnits.HasValue)
						{
							options2.MeasurementUnit = wdMeasurementUnits.Value;
						}
						if (flag.HasValue)
						{
							options2.UseCharacterUnit = flag.Value;
						}
					}
					finally
					{
						if (options2 != null)
						{
							ComObjectRelease.ReleaseOwned(options2, "SetPageSetup", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 169);
						}
					}
				}
				return result;
			}
			throw new ArgumentNullException("analysis");
		}
		throw new ArgumentNullException("app");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool SetPageSetupIfNeeded(Document doc, Application app, FormatConfig cfg, DocumentAnalysisResult analysis, DocumentHostKind hostKind)
	{
		if (doc != null)
		{
			if (app == null)
			{
				throw new ArgumentNullException("app");
			}
			if (cfg != null)
			{
				if (analysis != null)
				{
					if (!IsPageSetupMatched(doc, app, cfg, hostKind))
					{
						return SetPageSetup(doc, app, cfg, analysis, hostKind);
					}
					LogService.Info("页面设置已符合当前模板，跳过重复设置");
					return false;
				}
				throw new ArgumentNullException("analysis");
			}
			throw new ArgumentNullException("cfg");
		}
		throw new ArgumentNullException("doc");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool IsPageSetupMatched(Document doc, Application app, FormatConfig cfg, DocumentHostKind hostKind)
	{
		if (doc == null)
		{
			throw new ArgumentNullException("doc");
		}
		if (app == null)
		{
			throw new ArgumentNullException("app");
		}
		if (cfg == null)
		{
			throw new ArgumentNullException("cfg");
		}
		PageSetup pageSetup = null;
		try
		{
			pageSetup = doc.PageSetup;
			if (!Near(pageSetup.TopMargin, app.CentimetersToPoints(cfg.TopMargin), 0.8f))
			{
				return false;
			}
			if (Near(pageSetup.BottomMargin, app.CentimetersToPoints(cfg.BottomMargin), 0.8f))
			{
				if (Near(pageSetup.LeftMargin, app.CentimetersToPoints(cfg.LeftMargin), 0.8f))
				{
					if (!Near(pageSetup.RightMargin, app.CentimetersToPoints(cfg.RightMargin), 0.8f))
					{
						return false;
					}
					if (Near(pageSetup.HeaderDistance, app.CentimetersToPoints(cfg.HeaderDistance), 0.8f))
					{
						if (Near(pageSetup.FooterDistance, app.CentimetersToPoints(cfg.FooterDistance), 0.8f))
						{
							if (pageSetup.PaperSize != WdPaperSize.wdPaperA4)
							{
								return false;
							}
							if (!cfg.EnableDocumentGrid)
							{
								if (pageSetup.LayoutMode == WdLayoutMode.wdLayoutModeGrid)
								{
									return false;
								}
							}
							else
							{
								DocumentGridOptions gridOptions = GetGridOptions(cfg);
								if (pageSetup.LayoutMode != WdLayoutMode.wdLayoutModeGrid)
								{
									return false;
								}
								if (!MatchesGridValue(pageSetup, "CharsLine", gridOptions.CharsPerLine, hostKind))
								{
									return false;
								}
								if (!MatchesGridValue(pageSetup, "LinesPage", gridOptions.LinesPerPage, hostKind))
								{
									return false;
								}
							}
							return true;
						}
						return false;
					}
					return false;
				}
				return false;
			}
			return false;
		}
		finally
		{
			if (pageSetup != null)
			{
				ComObjectRelease.ReleaseOwned(pageSetup, "IsPageSetupMatched", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 241);
			}
		}
	}

	private static bool Near(float actual, float expected, float tolerance)
	{
		return Math.Abs(actual - expected) <= tolerance;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool MatchesGridValue(PageSetup ps, string propertyName, int expected, DocumentHostKind hostKind)
	{
		if (ps == null)
		{
			throw new ArgumentNullException("ps");
		}
		try
		{
			return Convert.ToInt32(ps.GetType().InvokeMember(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.GetProperty, null, ps, null)) == expected;
		}
		catch (TargetInvocationException ex) when (DocumentGridCompatibilityPolicy.IsRecoverableFailure(hostKind, ex.InnerException))
		{
			string context = "DOCUMENT-GRID compatibility-read-failed host=" + hostKind.ToString() + ", property=" + propertyName + ", hresult=0x80004005; page setup will be reapplied with compatibility fallback.";
			_ = ex.InnerException;
			if (ex == null)
			{
			}
			LogService.Warn(context, ex);
			ExecutionWarningCollector.Report("document-grid-compatibility", "page-setup", "warn.documentgrid.compat");
			return false;
		}
		catch (COMException ex2) when (DocumentGridCompatibilityPolicy.IsRecoverableFailure(hostKind, ex2))
		{
			LogService.Warn("DOCUMENT-GRID compatibility-read-failed host=" + hostKind.ToString() + ", property=" + propertyName + ", hresult=0x80004005; page setup will be reapplied with compatibility fallback.", ex2);
			ExecutionWarningCollector.Report("document-grid-compatibility", "page-setup", "warn.documentgrid.compat");
			return false;
		}
	}

	private static bool TrySetNativeGridProperty(PageSetup pageSetup, string propertyName, int value, DocumentHostKind hostKind, out Exception compatibilityFailure)
	{
		compatibilityFailure = null;
		try
		{
			pageSetup.GetType().InvokeMember(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.SetProperty, null, pageSetup, new object[1] { value });
			return true;
		}
		catch (TargetInvocationException ex) when (DocumentGridCompatibilityPolicy.IsRecoverableFailure(hostKind, ex.InnerException))
		{
			_ = ex.InnerException;
			if (ex == null)
			{
			}
			compatibilityFailure = ex;
			return false;
		}
		catch (COMException ex2) when (DocumentGridCompatibilityPolicy.IsRecoverableFailure(hostKind, ex2))
		{
			compatibilityFailure = ex2;
			return false;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static DocumentGridOptions GetGridOptions(FormatConfig cfg)
	{
		if (cfg == null)
		{
			throw new ArgumentNullException("cfg");
		}
		DocumentGridOptions documentGridOptions = cfg.DocumentGridOptions ?? new DocumentGridOptions();
		if (documentGridOptions.LinesPerPage < 1 || documentGridOptions.LinesPerPage > 50)
		{
			throw new FormatException("每页行数必须在1到50之间。");
		}
		if (documentGridOptions.CharsPerLine < 1 || documentGridOptions.CharsPerLine > 50)
		{
			throw new FormatException("每行字数必须在1到50之间。");
		}
		return documentGridOptions;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void SetPageNumbers(Document doc, Application app, FormatConfig cfg, FirstFormatDiagnosticsSession diagnostics = null)
	{
		if (doc == null)
		{
			throw new ArgumentNullException("doc");
		}
		if (app == null)
		{
			throw new ArgumentNullException("app");
		}
		if (cfg == null)
		{
			throw new ArgumentNullException("cfg");
		}
		string text = PageNumberModes.Normalize(cfg.PageNumberMode, cfg.EnablePageNumbers);
		if (PageNumberModes.IsEnabled(text, cfg.EnablePageNumbers))
		{
			RequireText(cfg.PageNumberFontName, "页码字体");
		}
		try
		{
			SetPageNumbersCore(doc, app, cfg, text, diagnostics);
		}
		catch (COMException exception)
		{
			ReportPageNumberWarning("page-number-host-operation", "宿主未能完成全部页码设置。", exception);
		}
		catch (InvalidOperationException exception2)
		{
			ReportPageNumberWarning("page-number-operation", "未能完成全部页码设置。", exception2);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void SetPageNumbersCore(Document doc, Application app, FormatConfig cfg, string pageNumberMode, FirstFormatDiagnosticsSession diagnostics)
	{
		diagnostics?.Mark("page-number-normalize", doc.Sections.Count, reliable: true, "reset page numbering baseline");
		NormalizeDocumentPageNumbering(doc);
		diagnostics?.Mark("page-number-fact-read", 1, reliable: true, "capture page number facts");
		PageNumberFacts pageNumberFacts = PageNumberFacts.Capture(doc, app, cfg);
		if (!pageNumberFacts.IsReliable)
		{
			LogService.Warn("PAGE-NUMBER-FACTS unreliable: " + pageNumberFacts.UnreliableReason);
		}
		if (!pageNumberFacts.IsReliable || !PageNumberFactsMatcher.IsMatched(pageNumberFacts, cfg, app))
		{
			if (PageNumberModes.IsEnabled(pageNumberMode, cfg.EnablePageNumbers))
			{
				bool flag = cfg.PageAlign == PageAlignType.OddEvenDifferent;
				bool flag2 = PageNumberModes.IsFirstPageHidden(pageNumberMode);
				string text = cfg.PageLeftWing ?? "";
				if (text.Length > 0 && !text.EndsWith(" "))
				{
					text += " ";
				}
				string text2 = cfg.PageRightWing ?? "";
				if (text2.Length > 0 && !text2.StartsWith(" "))
				{
					text2 = " " + text2;
				}
				int count = doc.Sections.Count;
				bool flag3 = pageNumberFacts.IsReliable && SettingsMatchFacts(pageNumberFacts, flag, flag2);
				if (pageNumberFacts.IsReliable && HasAnyPageNumberContent(pageNumberFacts))
				{
					DeletePageNumbers(doc);
					diagnostics?.Mark("page-number-first-cleanup", count, reliable: true, "delete existing page numbers");
				}
				else
				{
					diagnostics?.Mark("page-number-first-cleanup", count, reliable: true, "skipped, no existing page numbers");
				}
				for (int i = 1; i <= count; i++)
				{
					Section section = null;
					try
					{
						section = doc.Sections[i];
						PageSetup pageSetup = null;
						try
						{
							pageSetup = section.PageSetup;
							pageSetup.OddAndEvenPagesHeaderFooter = (flag ? (-1) : 0);
							pageSetup.DifferentFirstPageHeaderFooter = ((flag2 && i == 1) ? (-1) : 0);
						}
						finally
						{
							if (pageSetup != null)
							{
								ComObjectRelease.ReleaseOwned(pageSetup, "SetPageNumbersCore", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 428);
							}
						}
					}
					finally
					{
						if (section != null)
						{
							ComObjectRelease.ReleaseOwned(section, "SetPageNumbersCore", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 433);
						}
					}
				}
				diagnostics?.Mark("page-number-section-setup", count, reliable: true, "odd/even and first-page settings applied");
				if (flag3)
				{
					diagnostics?.Mark("page-number-second-cleanup", count, reliable: true, "skipped, settings unchanged");
				}
				else
				{
					DeletePageNumbers(doc);
					diagnostics?.Mark("page-number-second-cleanup", count, reliable: true, "cleanup after section setup change");
				}
				PageSetup pageSetup2 = null;
				try
				{
					pageSetup2 = doc.PageSetup;
					pageSetup2.FooterDistance = app.CentimetersToPoints(cfg.FooterDistance);
				}
				finally
				{
					if (pageSetup2 != null)
					{
						ComObjectRelease.ReleaseOwned(pageSetup2, "SetPageNumbersCore", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 460);
					}
				}
				DocumentStyleManager.EnsureStylesScoped(doc, cfg, StyleRefreshMode.IfChanged, PageNumberOnlyElementTypes, includePageNumber: true, skipLegacyMigration: true, diagnostics);
				diagnostics?.Mark("page-number-style-ensure", 1, reliable: true, "ensure page number style exists");
				SetPageNumbersInFooters(doc, cfg, flag, text, text2);
				diagnostics?.Mark("page-number-footer-write", count, reliable: true, "write page numbers into footers");
				if (flag2)
				{
					ClearFirstPageFooter(doc);
				}
				Microsoft.Office.Interop.Word.Range range = null;
				int num;
				try
				{
					range = doc.Content;
					num = range.ComputeStatistics(WdStatistic.wdStatisticPages);
				}
				finally
				{
					if (range != null)
					{
						ComObjectRelease.ReleaseOwned(range, "SetPageNumbersCore", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 495);
					}
				}
				if (num <= 1)
				{
					DeletePageNumbers(doc);
					diagnostics?.Mark("page-number-page-count", num, reliable: true, "single page, removed page numbers");
					return;
				}
				diagnostics?.Mark("page-number-page-count", num, reliable: true, "page count computed");
				NormalizePageNumberFooters(doc, cfg, flag);
				diagnostics?.Mark("page-number-footer-normalize", count, reliable: true, "normalize footer paragraph formats");
				PageNumberFacts pageNumberFacts2 = PageNumberFacts.Capture(doc, app, cfg);
				PageNumberFormatVerifier.Verify(pageNumberFacts2, cfg);
				diagnostics?.Mark("page-number-final-verify", count, pageNumberFacts2.IsReliable, "final page number verification");
			}
			else
			{
				DeletePageNumbers(doc);
				diagnostics?.Mark("page-number-deleted", 1, reliable: true, "page numbers disabled");
			}
		}
		else
		{
			diagnostics?.Mark("page-number-matched", 1, reliable: true, "existing page numbers match config");
			LogService.Info("页码已经完整符合当前参数，跳过重复生成");
		}
	}

	private static bool SettingsMatchFacts(PageNumberFacts facts, bool oddEven, bool hideFirstPage)
	{
		if (facts == null || !facts.IsReliable)
		{
			return false;
		}
		for (int i = 0; i < facts.Sections.Count; i++)
		{
			PageNumberFacts.SectionFacts sectionFacts = facts.Sections[i];
			int num = i + 1;
			if (sectionFacts.OddAndEvenPagesHeaderFooter == oddEven)
			{
				if (sectionFacts.DifferentFirstPageHeaderFooter != (hideFirstPage && num == 1))
				{
					return false;
				}
				continue;
			}
			return false;
		}
		return true;
	}

	private static bool HasAnyPageNumberContent(PageNumberFacts facts)
	{
		if (facts != null && facts.IsReliable)
		{
			if (facts.TotalPageFields > 0)
			{
				return true;
			}
			foreach (PageNumberFacts.SectionFacts section in facts.Sections)
			{
				foreach (PageNumberFacts.FooterFacts value in section.Footers.Values)
				{
					if (value.GeneratedParagraphCount > 0)
					{
						return true;
					}
				}
				if (section.FirstPageFooter != null && section.FirstPageFooter.GeneratedParagraphCount > 0)
				{
					return true;
				}
			}
			return false;
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void NormalizeDocumentPageNumbering(Document doc)
	{
		if (doc == null)
		{
			throw new ArgumentNullException("doc");
		}
		Sections value = null;
		try
		{
			value = doc.Sections;
			if (value.Count < 1)
			{
				ReportPageNumberWarning("page-number-reset-empty-sections", "文档没有可用于重置页码的有效分节。");
				return;
			}
			for (int i = 1; i <= value.Count; i++)
			{
				Section value2 = null;
				HeaderFooter value3 = null;
				PageNumbers value4 = null;
				try
				{
					value2 = value[i];
					value3 = value2.Footers[WdHeaderFooterIndex.wdHeaderFooterPrimary];
					value4 = value3.PageNumbers;
					if (i != 1)
					{
						value4.RestartNumberingAtSection = false;
						if (value4.RestartNumberingAtSection)
						{
							ReportPageNumberWarning("page-number-reset-continuation", "后续分节未能保持连续编号。");
						}
						continue;
					}
					value4.RestartNumberingAtSection = true;
					value4.StartingNumber = 1;
					if (!value4.RestartNumberingAtSection || value4.StartingNumber != 1)
					{
						ReportPageNumberWarning("page-number-reset-first", "首节未能从第 1 页重新编号。");
					}
				}
				catch (COMException exception)
				{
					ReportPageNumberWarning("page-number-reset-section", "某个分节未能重置页码。", exception);
				}
				finally
				{
					ComObjectRelease.Release(ref value4, "PageSetupManager.NormalizePageNumbers.PageNumbers");
					ComObjectRelease.Release(ref value3, "PageSetupManager.NormalizePageNumbers.Footer");
					ComObjectRelease.Release(ref value2, "PageSetupManager.NormalizePageNumbers.Section");
				}
			}
		}
		finally
		{
			ComObjectRelease.Release(ref value, "PageSetupManager.NormalizePageNumbers.Sections");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void NormalizePageNumberFooters(Document doc, FormatConfig cfg, bool oddEven)
	{
		if (doc == null)
		{
			throw new ArgumentNullException("doc");
		}
		if (cfg == null)
		{
			throw new ArgumentNullException("cfg");
		}
		int count = doc.Sections.Count;
		for (int i = 1; i <= count; i++)
		{
			Section section = null;
			try
			{
				section = doc.Sections[i];
				if (!oddEven)
				{
					float leftChars = 0f;
					float rightChars = 0f;
					PageAlignType pageAlign = cfg.PageAlign;
					WdParagraphAlignment align;
					if (pageAlign != PageAlignType.Left)
					{
						if (pageAlign != PageAlignType.Right)
						{
							align = WdParagraphAlignment.wdAlignParagraphCenter;
						}
						else
						{
							align = WdParagraphAlignment.wdAlignParagraphRight;
							rightChars = 1f;
						}
					}
					else
					{
						align = WdParagraphAlignment.wdAlignParagraphLeft;
						leftChars = 1f;
					}
					NormalizeFooterParagraph(section, WdHeaderFooterIndex.wdHeaderFooterPrimary, align, leftChars, rightChars);
				}
				else
				{
					NormalizeFooterParagraph(section, WdHeaderFooterIndex.wdHeaderFooterPrimary, WdParagraphAlignment.wdAlignParagraphRight, 0f, 1f);
					NormalizeFooterParagraph(section, WdHeaderFooterIndex.wdHeaderFooterEvenPages, WdParagraphAlignment.wdAlignParagraphLeft, 1f, 0f);
				}
				NormalizeFooterParagraph(section, WdHeaderFooterIndex.wdHeaderFooterFirstPage, WdParagraphAlignment.wdAlignParagraphCenter, 0f, 0f);
			}
			finally
			{
				if (section != null)
				{
					ComObjectRelease.ReleaseOwned(section, "NormalizePageNumberFooters", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 650);
				}
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void NormalizeFooterParagraph(Section section, WdHeaderFooterIndex index, WdParagraphAlignment align, float leftChars, float rightChars)
	{
		if (section == null)
		{
			throw new ArgumentNullException("section");
		}
		HeaderFooter headerFooter = null;
		Microsoft.Office.Interop.Word.Range range = null;
		ParagraphFormat paragraphFormat = null;
		try
		{
			headerFooter = section.Footers[index];
			headerFooter.LinkToPrevious = false;
			range = headerFooter.Range;
			Fields fields = null;
			try
			{
				fields = range.Fields;
				for (int i = 1; i <= fields.Count; i++)
				{
					Field field = null;
					Microsoft.Office.Interop.Word.Range range2 = null;
					Paragraphs paragraphs = null;
					Paragraph paragraph = null;
					Microsoft.Office.Interop.Word.Range range3 = null;
					try
					{
						field = fields[i];
						if (field.Type == WdFieldType.wdFieldPage)
						{
							range2 = field.Result;
							paragraphs = range2.Paragraphs;
							if (paragraphs.Count >= 1)
							{
								paragraph = paragraphs[1];
								range3 = paragraph.Range;
								DocumentStyleManager.ApplyPageNumberStyle(range3);
								paragraphFormat = range3.ParagraphFormat;
								paragraphFormat.Alignment = align;
								paragraphFormat.LeftIndent = 0f;
								paragraphFormat.RightIndent = 0f;
								paragraphFormat.FirstLineIndent = 0f;
								paragraphFormat.CharacterUnitLeftIndent = leftChars;
								paragraphFormat.CharacterUnitRightIndent = rightChars;
								paragraphFormat.CharacterUnitFirstLineIndent = 0f;
								paragraphFormat.SpaceBefore = 0f;
								paragraphFormat.SpaceAfter = 0f;
								paragraphFormat.LineSpacingRule = WdLineSpacing.wdLineSpaceSingle;
								ComObjectRelease.ReleaseOwned(paragraphFormat, "NormalizeFooterParagraph", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 700);
								paragraphFormat = null;
							}
						}
					}
					finally
					{
						if (paragraphFormat != null)
						{
							ComObjectRelease.ReleaseOwned(paragraphFormat, "NormalizeFooterParagraph", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 705);
						}
						if (range3 != null)
						{
							ComObjectRelease.ReleaseOwned(range3, "NormalizeFooterParagraph", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 706);
						}
						if (paragraph != null)
						{
							ComObjectRelease.ReleaseOwned(paragraph, "NormalizeFooterParagraph", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 707);
						}
						if (paragraphs != null)
						{
							ComObjectRelease.ReleaseOwned(paragraphs, "NormalizeFooterParagraph", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 708);
						}
						if (range2 != null)
						{
							ComObjectRelease.ReleaseOwned(range2, "NormalizeFooterParagraph", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 709);
						}
						if (field != null)
						{
							ComObjectRelease.ReleaseOwned(field, "NormalizeFooterParagraph", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 710);
						}
					}
				}
			}
			finally
			{
				if (fields != null)
				{
					ComObjectRelease.ReleaseOwned(fields, "NormalizeFooterParagraph", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 716);
				}
			}
		}
		finally
		{
			if (paragraphFormat != null)
			{
				ComObjectRelease.ReleaseOwned(paragraphFormat, "NormalizeFooterParagraph", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 721);
			}
			if (range != null)
			{
				ComObjectRelease.ReleaseOwned(range, "NormalizeFooterParagraph", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 722);
			}
			if (headerFooter != null)
			{
				ComObjectRelease.ReleaseOwned(headerFooter, "NormalizeFooterParagraph", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 723);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void DeletePageNumbersInSection(Section section)
	{
		if (section != null)
		{
			DeleteFooterRange(section, WdHeaderFooterIndex.wdHeaderFooterPrimary);
			DeleteFooterRange(section, WdHeaderFooterIndex.wdHeaderFooterEvenPages);
			DeleteFooterRange(section, WdHeaderFooterIndex.wdHeaderFooterFirstPage);
			return;
		}
		throw new ArgumentNullException("section");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void DeleteFooterRange(Section section, WdHeaderFooterIndex index)
	{
		if (section == null)
		{
			throw new ArgumentNullException("section");
		}
		HeaderFooter headerFooter = null;
		Microsoft.Office.Interop.Word.Range range = null;
		try
		{
			headerFooter = section.Footers[index];
			range = headerFooter.Range;
			DeletePageNumbersFromRange(range);
		}
		finally
		{
			if (range != null)
			{
				ComObjectRelease.ReleaseOwned(range, "DeleteFooterRange", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 756);
			}
			if (headerFooter != null)
			{
				ComObjectRelease.ReleaseOwned(headerFooter, "DeleteFooterRange", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 757);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void DeletePageNumbersFromRange(Microsoft.Office.Interop.Word.Range range)
	{
		if (range == null)
		{
			throw new ArgumentNullException("range");
		}
		while (true)
		{
			Fields fields = null;
			Field field = null;
			try
			{
				fields = range.Fields;
				for (int num = fields.Count; num >= 1; num--)
				{
					Field field2 = fields[num];
					if (IsPageNumberField(field2))
					{
						field = field2;
						break;
					}
					ComObjectRelease.ReleaseOwned(field2, "DeletePageNumbersFromRange", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 782);
				}
				if (field != null)
				{
					DeletePageNumberField(field, range);
					continue;
				}
			}
			finally
			{
				if (field != null)
				{
					ComObjectRelease.ReleaseOwned(field, "DeletePageNumbersFromRange", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 789);
				}
				if (fields != null)
				{
					ComObjectRelease.ReleaseOwned(fields, "DeletePageNumbersFromRange", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 790);
				}
			}
			break;
		}
		RemoveGeneratedPageNumberParagraphs(range);
	}

	private static bool IsPageNumberField(Field field)
	{
		if (field == null)
		{
			return false;
		}
		if (field.Type != WdFieldType.wdFieldPage && field.Type != WdFieldType.wdFieldNumPages)
		{
			return field.Type == WdFieldType.wdFieldSectionPages;
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void DeletePageNumberField(Field field, Microsoft.Office.Interop.Word.Range storyRange)
	{
		if (field != null)
		{
			if (storyRange == null)
			{
				throw new ArgumentNullException("storyRange");
			}
			Microsoft.Office.Interop.Word.Range range = null;
			Paragraphs paragraphs = null;
			Paragraph paragraph = null;
			Microsoft.Office.Interop.Word.Range range2 = null;
			Paragraphs paragraphs2 = null;
			try
			{
				range = field.Result;
				paragraphs = range.Paragraphs;
				if (paragraphs.Count > 0)
				{
					paragraph = paragraphs[1];
					range2 = paragraph.Range;
					if (IsPageNumberOnlyParagraph(range2.Text))
					{
						paragraphs2 = storyRange.Paragraphs;
						if (paragraphs2.Count > 1)
						{
							Microsoft.Office.Interop.Word.Range range3 = range2;
							object Unit = Type.Missing;
							object Count = Type.Missing;
							range3.Delete(ref Unit, ref Count);
						}
						else
						{
							storyRange.Text = string.Empty;
						}
						return;
					}
				}
				field.Delete();
				return;
			}
			finally
			{
				if (paragraphs2 != null)
				{
					ComObjectRelease.ReleaseOwned(paragraphs2, "DeletePageNumberField", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 838);
				}
				if (range2 != null)
				{
					ComObjectRelease.ReleaseOwned(range2, "DeletePageNumberField", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 839);
				}
				if (paragraph != null)
				{
					ComObjectRelease.ReleaseOwned(paragraph, "DeletePageNumberField", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 840);
				}
				if (paragraphs != null)
				{
					ComObjectRelease.ReleaseOwned(paragraphs, "DeletePageNumberField", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 841);
				}
				if (range != null)
				{
					ComObjectRelease.ReleaseOwned(range, "DeletePageNumberField", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 842);
				}
			}
		}
		throw new ArgumentNullException("field");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void RemoveGeneratedPageNumberParagraphs(Microsoft.Office.Interop.Word.Range storyRange)
	{
		for (int i = 0; i < 64; i++)
		{
			Paragraphs paragraphs = null;
			Paragraph paragraph = null;
			Microsoft.Office.Interop.Word.Range range = null;
			try
			{
				paragraphs = storyRange.Paragraphs;
				int num = paragraphs.Count;
				while (num >= 1)
				{
					Paragraph paragraph2 = paragraphs[num];
					if (!IsGeneratedPageNumberParagraph(paragraph2))
					{
						ComObjectRelease.ReleaseOwned(paragraph2, "RemoveGeneratedPageNumberParagraphs", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 865);
						num--;
						continue;
					}
					paragraph = paragraph2;
					break;
				}
				if (paragraph == null)
				{
					return;
				}
				if (paragraphs.Count <= 1)
				{
					storyRange.Text = string.Empty;
					return;
				}
				range = paragraph.Range;
				Microsoft.Office.Interop.Word.Range range2 = range;
				object Unit = Type.Missing;
				object Count = Type.Missing;
				range2.Delete(ref Unit, ref Count);
			}
			finally
			{
				if (range != null)
				{
					ComObjectRelease.ReleaseOwned(range, "RemoveGeneratedPageNumberParagraphs", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 880);
				}
				if (paragraph != null)
				{
					ComObjectRelease.ReleaseOwned(paragraph, "RemoveGeneratedPageNumberParagraphs", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 881);
				}
				if (paragraphs != null)
				{
					ComObjectRelease.ReleaseOwned(paragraphs, "RemoveGeneratedPageNumberParagraphs", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 882);
				}
			}
		}
		ReportPageNumberWarning("page-number-cleanup-limit", "页脚中仍有较多历史页码段落。");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool IsGeneratedPageNumberParagraph(Paragraph paragraph)
	{
		if (paragraph != null)
		{
			if (!string.Equals(DocumentStyleManager.GetParagraphStructuralStyleName(paragraph), "OfficialDoc.PageNumber", StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
			Microsoft.Office.Interop.Word.Range range = null;
			Fields fields = null;
			try
			{
				range = paragraph.Range;
				fields = range.Fields;
				for (int i = 1; i <= fields.Count; i++)
				{
					Field field = null;
					try
					{
						field = fields[i];
						if (IsPageNumberField(field))
						{
							return true;
						}
					}
					finally
					{
						if (field != null)
						{
							ComObjectRelease.ReleaseOwned(field, "IsGeneratedPageNumberParagraph", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 917);
						}
					}
				}
				return IsPageNumberOnlyParagraph(range.Text);
			}
			finally
			{
				if (fields != null)
				{
					ComObjectRelease.ReleaseOwned(fields, "IsGeneratedPageNumberParagraph", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 924);
				}
				if (range != null)
				{
					ComObjectRelease.ReleaseOwned(range, "IsGeneratedPageNumberParagraph", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 925);
				}
			}
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool IsPageNumberOnlyParagraph(string text)
	{
		string text2 = (text ?? "").Replace("\r", "").Replace("\a", "").Trim();
		if (text2.Length == 0)
		{
			return true;
		}
		return string.IsNullOrWhiteSpace(Regex.Replace(text2, "[\\s\\d０-９\\-—－–―_·•\\(\\)（）\\[\\]【】第页頁共/\\\\]+", ""));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ClearFirstPageFooter(Document doc)
	{
		if (doc == null)
		{
			throw new ArgumentNullException("doc");
		}
		Section section = null;
		try
		{
			if (doc.Sections.Count >= 1)
			{
				section = doc.Sections[1];
				DeleteFooterRange(section, WdHeaderFooterIndex.wdHeaderFooterFirstPage);
				NormalizeFooterParagraph(section, WdHeaderFooterIndex.wdHeaderFooterFirstPage, WdParagraphAlignment.wdAlignParagraphCenter, 0f, 0f);
			}
		}
		finally
		{
			if (section != null)
			{
				ComObjectRelease.ReleaseOwned(section, "ClearFirstPageFooter", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 959);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void DeletePageNumbers(Document doc)
	{
		if (doc == null)
		{
			throw new ArgumentNullException("doc");
		}
		int count = doc.Sections.Count;
		for (int i = 1; i <= count; i++)
		{
			Section section = null;
			try
			{
				section = doc.Sections[i];
				DeleteFooterRange(section, WdHeaderFooterIndex.wdHeaderFooterPrimary);
				DeleteFooterRange(section, WdHeaderFooterIndex.wdHeaderFooterEvenPages);
				DeleteFooterRange(section, WdHeaderFooterIndex.wdHeaderFooterFirstPage);
			}
			finally
			{
				if (section != null)
				{
					ComObjectRelease.ReleaseOwned(section, "DeletePageNumbers", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 983);
				}
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void SetPageNumbersInFooters(Document doc, FormatConfig cfg, bool oddEven, string lWing, string rWing)
	{
		if (doc != null)
		{
			if (cfg == null)
			{
				throw new ArgumentNullException("cfg");
			}
			int count = doc.Sections.Count;
			for (int i = 1; i <= count; i++)
			{
				Section section = null;
				try
				{
					section = doc.Sections[i];
					if (!oddEven)
					{
						float leftChars = 0f;
						float rightChars = 0f;
						PageAlignType pageAlign = cfg.PageAlign;
						WdParagraphAlignment alignment;
						if (pageAlign != PageAlignType.Left)
						{
							if (pageAlign != PageAlignType.Right)
							{
								alignment = WdParagraphAlignment.wdAlignParagraphCenter;
							}
							else
							{
								alignment = WdParagraphAlignment.wdAlignParagraphRight;
								rightChars = 1f;
							}
						}
						else
						{
							alignment = WdParagraphAlignment.wdAlignParagraphLeft;
							leftChars = 1f;
						}
						WriteFooterPageNumber(section, WdHeaderFooterIndex.wdHeaderFooterPrimary, alignment, leftChars, rightChars, lWing, rWing);
					}
					else
					{
						WriteFooterPageNumber(section, WdHeaderFooterIndex.wdHeaderFooterPrimary, WdParagraphAlignment.wdAlignParagraphRight, 0f, 1f, lWing, rWing);
						WriteFooterPageNumber(section, WdHeaderFooterIndex.wdHeaderFooterEvenPages, WdParagraphAlignment.wdAlignParagraphLeft, 1f, 0f, lWing, rWing);
					}
				}
				catch (COMException exception)
				{
					ReportPageNumberWarning("page-number-footer-host", "宿主未能完成某个分节的页码设置。", exception);
				}
				catch (InvalidOperationException exception2)
				{
					ReportPageNumberWarning("page-number-footer", "未能完成某个分节的页码设置。", exception2);
				}
				finally
				{
					if (section != null)
					{
						ComObjectRelease.ReleaseOwned(section, "SetPageNumbersInFooters", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 1045);
					}
				}
			}
			return;
		}
		throw new ArgumentNullException("doc");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void WriteFooterPageNumber(Section section, WdHeaderFooterIndex index, WdParagraphAlignment alignment, float leftChars, float rightChars, string lWing, string rWing)
	{
		HeaderFooter headerFooter = null;
		Microsoft.Office.Interop.Word.Range range = null;
		Microsoft.Office.Interop.Word.Range range2 = null;
		Paragraphs paragraphs = null;
		Paragraph paragraph = null;
		Microsoft.Office.Interop.Word.Range range3 = null;
		ParagraphFormat paragraphFormat = null;
		Fields fields = null;
		Field field = null;
		try
		{
			headerFooter = section.Footers[index];
			headerFooter.LinkToPrevious = false;
			range = headerFooter.Range;
			DeletePageNumbersFromRange(range);
			ComObjectRelease.ReleaseOwned(range, "WriteFooterPageNumber", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 1069);
			range = headerFooter.Range;
			if (!string.IsNullOrWhiteSpace((range.Text ?? string.Empty).Replace("\r", "")))
			{
				range2 = range.Duplicate;
				range2.SetRange(Math.Max(range2.Start, range2.End - 1), Math.Max(range2.Start, range2.End - 1));
				range2.InsertBefore("\r");
				ComObjectRelease.ReleaseOwned(range2, "WriteFooterPageNumber", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 1077);
				range2 = null;
				ComObjectRelease.ReleaseOwned(range, "WriteFooterPageNumber", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 1079);
				range = headerFooter.Range;
			}
			paragraphs = range.Paragraphs;
			if (paragraphs.Count >= 1)
			{
				paragraph = paragraphs[paragraphs.Count];
				range3 = paragraph.Range;
				DocumentStyleManager.ApplyPageNumberStyle(range3);
				paragraphFormat = range3.ParagraphFormat;
				paragraphFormat.Alignment = alignment;
				paragraphFormat.LeftIndent = 0f;
				paragraphFormat.RightIndent = 0f;
				paragraphFormat.FirstLineIndent = 0f;
				paragraphFormat.CharacterUnitFirstLineIndent = 0f;
				paragraphFormat.CharacterUnitLeftIndent = leftChars;
				paragraphFormat.CharacterUnitRightIndent = rightChars;
				paragraphFormat.LineUnitBefore = 0f;
				paragraphFormat.LineUnitAfter = 0f;
				paragraphFormat.SpaceBeforeAuto = 0;
				paragraphFormat.SpaceAfterAuto = 0;
				paragraphFormat.SpaceBefore = 0f;
				paragraphFormat.SpaceAfter = 0f;
				paragraphFormat.LineSpacingRule = WdLineSpacing.wdLineSpaceSingle;
				range2 = range3.Duplicate;
				range2.SetRange(range2.Start, Math.Max(range2.Start, range2.End - 1));
				Microsoft.Office.Interop.Word.Range range4 = range2;
				object Direction = WdCollapseDirection.wdCollapseEnd;
				range4.Collapse(ref Direction);
				range2.Text = lWing ?? string.Empty;
				Microsoft.Office.Interop.Word.Range range5 = range2;
				Direction = WdCollapseDirection.wdCollapseEnd;
				range5.Collapse(ref Direction);
				int start = range2.Start;
				range2.Text = rWing ?? string.Empty;
				range2.SetRange(start, start);
				fields = range.Fields;
				Fields fields2 = fields;
				Microsoft.Office.Interop.Word.Range range6 = range2;
				Direction = WdFieldType.wdFieldPage;
				object Text = "";
				object PreserveFormatting = false;
				field = fields2.Add(range6, ref Direction, ref Text, ref PreserveFormatting);
				field.Update();
				DocumentStyleManager.ApplyPageNumberStyle(range3);
			}
			else
			{
				ReportPageNumberWarning("page-number-footer-missing-paragraph", "页脚缺少可写入页码的段落。");
			}
		}
		finally
		{
			if (field != null)
			{
				ComObjectRelease.ReleaseOwned(field, "WriteFooterPageNumber", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 1128);
			}
			if (fields != null)
			{
				ComObjectRelease.ReleaseOwned(fields, "WriteFooterPageNumber", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 1129);
			}
			if (paragraphFormat != null)
			{
				ComObjectRelease.ReleaseOwned(paragraphFormat, "WriteFooterPageNumber", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 1130);
			}
			if (range3 != null)
			{
				ComObjectRelease.ReleaseOwned(range3, "WriteFooterPageNumber", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 1131);
			}
			if (paragraph != null)
			{
				ComObjectRelease.ReleaseOwned(paragraph, "WriteFooterPageNumber", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 1132);
			}
			if (paragraphs != null)
			{
				ComObjectRelease.ReleaseOwned(paragraphs, "WriteFooterPageNumber", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 1133);
			}
			if (range2 != null)
			{
				ComObjectRelease.ReleaseOwned(range2, "WriteFooterPageNumber", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 1134);
			}
			if (range != null)
			{
				ComObjectRelease.ReleaseOwned(range, "WriteFooterPageNumber", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 1135);
			}
			if (headerFooter != null)
			{
				ComObjectRelease.ReleaseOwned(headerFooter, "WriteFooterPageNumber", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\PageSetupManager.cs", 1136);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ReportPageNumberWarning(string code, string detail, Exception exception = null)
	{
		if (exception == null)
		{
			LogService.Warn("PAGE-NUMBER-QUALITY " + code + ": " + detail);
		}
		else
		{
			LogService.Warn("PAGE-NUMBER-QUALITY " + code + ": " + detail, exception);
		}
		ExecutionWarningCollector.Report(code, "page-number", "warn.format.page-number");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string RequireText(string value, string fieldName)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw new FormatException(fieldName + "为空。");
		}
		return value.Trim();
	}
}
