using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Models.Signatures;
using DocumentRepository.Services.Formatting;
using DocumentRepository.Services.Formatting.Signatures;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Interop;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository;

public static class SignatureFormatter
{
	private sealed class SignaturePageFacts
	{
		internal float ContentWidthPt;

		internal bool GridEnabled;

		internal int CharsPerLine;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void InsertSpacingBeforeSignatureBlocks(Document doc, FormatConfig cfg, IList<SignatureBlock> blocks)
	{
		if (doc != null)
		{
			if (cfg != null)
			{
				if (blocks == null || blocks.Count == 0)
				{
					return;
				}
				List<SignatureBlock> list = new List<SignatureBlock>(blocks);
				list.Sort((SignatureBlock left, SignatureBlock right) => right.SignatureRangeStart.CompareTo(left.SignatureRangeStart));
				int blankLinesBefore = GetBlankLinesBefore(cfg);
				{
					foreach (SignatureBlock item in list)
					{
						if (item != null && item.IsValid)
						{
							EnsureBlankLinesBeforeBlock(doc, item, blankLinesBefore);
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
	private static void EnsureBlankLinesBeforeBlock(Document doc, SignatureBlock block, int targetBlankLines)
	{
		Paragraphs paragraphs = null;
		try
		{
			paragraphs = doc.Paragraphs;
			int num = block.SignatureParagraphIndex;
			if (num < 1 || num > paragraphs.Count)
			{
				throw new InvalidOperationException("无法根据落款分析结果定位署名段落。");
			}
			int num2 = 0;
			for (int num3 = num - 1; num3 >= 1; num3--)
			{
				Paragraph paragraph = null;
				Microsoft.Office.Interop.Word.Range range = null;
				try
				{
					paragraph = paragraphs[num3];
					range = paragraph.Range;
					if (!IsBlankParagraphText(range.Text))
					{
						break;
					}
					num2++;
					continue;
				}
				finally
				{
					if (range != null)
					{
						ComObjectRelease.ReleaseOwned(range, "EnsureBlankLinesBeforeBlock", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\SignatureFormatter.cs", 72);
					}
					if (paragraph != null)
					{
						ComObjectRelease.ReleaseOwned(paragraph, "EnsureBlankLinesBeforeBlock", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\SignatureFormatter.cs", 73);
					}
				}
			}
			while (num2 > targetBlankLines)
			{
				int index = num - num2;
				Paragraph paragraph2 = null;
				Microsoft.Office.Interop.Word.Range range2 = null;
				try
				{
					paragraph2 = paragraphs[index];
					range2 = paragraph2.Range;
					Microsoft.Office.Interop.Word.Range range3 = range2;
					object Unit = Type.Missing;
					object Count = Type.Missing;
					range3.Delete(ref Unit, ref Count);
				}
				finally
				{
					if (range2 != null)
					{
						ComObjectRelease.ReleaseOwned(range2, "EnsureBlankLinesBeforeBlock", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\SignatureFormatter.cs", 90);
					}
					if (paragraph2 != null)
					{
						ComObjectRelease.ReleaseOwned(paragraph2, "EnsureBlankLinesBeforeBlock", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\SignatureFormatter.cs", 91);
					}
				}
				num--;
				num2--;
			}
			if (num2 < targetBlankLines)
			{
				int num4 = targetBlankLines - num2;
				Paragraph paragraph3 = null;
				Microsoft.Office.Interop.Word.Range range4 = null;
				try
				{
					paragraph3 = paragraphs[num];
					range4 = paragraph3.Range;
					Microsoft.Office.Interop.Word.Range range5 = range4;
					object Count = WdCollapseDirection.wdCollapseStart;
					range5.Collapse(ref Count);
					for (int i = 0; i < num4; i++)
					{
						range4.InsertParagraphBefore();
					}
				}
				finally
				{
					if (range4 != null)
					{
						ComObjectRelease.ReleaseOwned(range4, "EnsureBlankLinesBeforeBlock", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\SignatureFormatter.cs", 112);
					}
					if (paragraph3 != null)
					{
						ComObjectRelease.ReleaseOwned(paragraph3, "EnsureBlankLinesBeforeBlock", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\SignatureFormatter.cs", 113);
					}
				}
				num += num4;
			}
			for (int j = num - targetBlankLines; j < num; j++)
			{
				if (j < 1)
				{
					continue;
				}
				Paragraph paragraph4 = null;
				try
				{
					paragraph4 = paragraphs[j];
					DocumentStyleManager.ApplyStyle(paragraph4, ElementType.Body);
				}
				finally
				{
					if (paragraph4 != null)
					{
						ComObjectRelease.ReleaseOwned(paragraph4, "EnsureBlankLinesBeforeBlock", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\SignatureFormatter.cs", 129);
					}
				}
			}
			LogService.Info("落款前空行已调整为" + targetBlankLines + "行");
		}
		finally
		{
			if (paragraphs != null)
			{
				ComObjectRelease.ReleaseOwned(paragraphs, "EnsureBlankLinesBeforeBlock", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\SignatureFormatter.cs", 137);
			}
		}
	}

	private static bool IsBlankParagraphText(string text)
	{
		return string.IsNullOrWhiteSpace((text ?? string.Empty).Trim(new char[] { '\r', '\n', '\u0007', ' ', '\u00A0', '\t', '\v', '\f' }));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void FormatSignatureRange(Document doc, Application app, FormatConfig cfg, IList<SignatureBlock> blocks, DocumentHostKind hostKind)
	{
		if (doc != null)
		{
			if (app == null)
			{
				throw new ArgumentNullException("app");
			}
			if (cfg != null)
			{
				if (blocks == null || blocks.Count == 0)
				{
					return;
				}
				bool repaginated = false;
				string orCreate = DocumentLifecycleRegistry.GetOrCreate(doc);
				{
					foreach (SignatureBlock block in blocks)
					{
						if (block != null && block.IsValid)
						{
							FormatSingleBlock(doc, app, cfg, block, hostKind, ref repaginated, orCreate);
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
	private static void FormatSingleBlock(Document doc, Application app, FormatConfig cfg, SignatureBlock block, DocumentHostKind hostKind, ref bool repaginated, string lifecycleId)
	{
		List<Paragraph> list = new List<Paragraph>();
		Paragraph paragraph = null;
		try
		{
			List<string> list2 = new List<string>();
			if (block.SignatureLines != null && block.SignatureLines.Count > 0)
			{
				foreach (SignatureLine signatureLine in block.SignatureLines)
				{
					if (signatureLine != null)
					{
						list.Add(GetParagraphByRangeStart(doc, signatureLine.RangeStart));
						list2.Add(signatureLine.Text ?? string.Empty);
					}
				}
			}
			else
			{
				list.Add(GetParagraphByRangeStart(doc, block.SignatureRangeStart));
				list2.Add(block.SignatureText ?? string.Empty);
			}
			paragraph = GetParagraphByRangeStart(doc, block.DateRangeStart);
			string text = block.DateText ?? string.Empty;
			for (int i = 0; i < list.Count; i++)
			{
				DocumentStyleManager.ApplyStyle(list[i], ElementType.Signature);
			}
			DocumentStyleManager.ApplyStyle(paragraph, ElementType.SignatureDate);
			SignaturePageFacts signaturePageFacts = TryReadPageFacts(cfg, paragraph);
			string hostVersion = ReadHostVersion(app);
			string text2 = ((cfg.Body != null) ? cfg.Body.FontName : null);
			string asciiFontName = EnglishNumberFontScopePolicy.ResolveStyleFont(cfg, ElementType.Signature, text2);
			float fontSizePt = ((cfg.Body != null) ? FontSizeHelper.ToPoints(cfg.Body.FontSize) : 16f);
			bool flag = signaturePageFacts != null;
			List<SignatureLayoutLineInput> list3 = new List<SignatureLayoutLineInput>(list.Count);
			SignatureLayoutLineInput signatureLayoutLineInput = null;
			if (flag)
			{
				for (int j = 0; j < list.Count; j++)
				{
					SignatureLayoutLineInput signatureLayoutLineInput2 = ResolveLineWidth(doc, app, list[j], list2[j], hostKind, hostVersion, text2, asciiFontName, fontSizePt, signaturePageFacts, ref repaginated, lifecycleId);
					if (signatureLayoutLineInput2 != null)
					{
						list3.Add(signatureLayoutLineInput2);
						continue;
					}
					flag = false;
					break;
				}
				if (flag)
				{
					signatureLayoutLineInput = ResolveLineWidth(doc, app, paragraph, text, hostKind, hostVersion, text2, asciiFontName, fontSizePt, signaturePageFacts, ref repaginated, lifecycleId);
					if (signatureLayoutLineInput == null)
					{
						flag = false;
					}
				}
			}
			SignatureLayoutPlan signatureLayoutPlan = null;
			if (flag)
			{
				signatureLayoutPlan = SignatureLayoutPlanner.Plan(new SignatureLayoutRequest
				{
					Host = hostKind,
					GridEnabled = signaturePageFacts.GridEnabled,
					CharsPerLine = signaturePageFacts.CharsPerLine,
					ContentWidthPt = signaturePageFacts.ContentWidthPt,
					FontSizePt = fontSizePt,
					WithSeal = IsWithSeal(cfg),
					SignatureLines = list3,
					Date = signatureLayoutLineInput
				});
				if (signatureLayoutPlan.Degradation != SignaturePlanDegradation.None)
				{
					LogService.Warn("落款定位按物理约束降级：host=" + hostKind.ToString() + ", sizePt=" + fontSizePt.ToString("0.0") + ", grid=" + signaturePageFacts.GridEnabled + ", degradation=" + signatureLayoutPlan.Degradation.ToString() + ", note=" + signatureLayoutPlan.DegradeNote);
				}
			}
			else
			{
				LogService.Warn("落款定位缺少可靠宽度，已保留安全格式：host=" + hostKind);
			}
			for (int k = 0; k < list.Count; k++)
			{
				float rightIndentPt = signatureLayoutPlan?.SignatureLines[k].RightIndentPt ?? 0f;
				ApplyPlannedParagraphFormat(list[k], cfg, rightIndentPt, ElementType.Signature);
			}
			float rightIndentPt2 = signatureLayoutPlan?.Date.RightIndentPt ?? 0f;
			ApplyPlannedParagraphFormat(paragraph, cfg, rightIndentPt2, ElementType.SignatureDate);
		}
		finally
		{
			for (int num = list.Count - 1; num >= 0; num--)
			{
				Paragraph paragraph2 = list[num];
				if (paragraph2 != null)
				{
					ComObjectRelease.ReleaseOwned(paragraph2, "FormatSingleBlock", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\SignatureFormatter.cs", 299);
				}
			}
			if (paragraph != null)
			{
				ComObjectRelease.ReleaseOwned(paragraph, "FormatSingleBlock", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\SignatureFormatter.cs", 301);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static SignatureLayoutLineInput ResolveLineWidth(Document doc, Application app, Paragraph para, string text, DocumentHostKind hostKind, string hostVersion, string cjkFontName, string asciiFontName, float fontSizePt, SignaturePageFacts facts, ref bool repaginated, string lifecycleId)
	{
		SignatureTextMorphology morphology = SignatureWidthEstimator.Classify(text);
		if (TryMeasureWithCache(doc, app, para, text, hostKind, hostVersion, cjkFontName, asciiFontName, fontSizePt, facts, ref repaginated, lifecycleId, out var widthPt, out var source))
		{
			LogService.Info((source == SignatureWidthSource.MeasurementCache) ? "落款行宽度使用当前文档测量缓存" : "落款行宽度使用当前宿主一次性实测");
			return new SignatureLayoutLineInput
			{
				WidthPt = widthPt,
				Morphology = morphology,
				Source = source
			};
		}
		return null;
	}

	private static bool TryMeasureWithCache(Document doc, Application app, Paragraph para, string text, DocumentHostKind hostKind, string hostVersion, string cjkFontName, string asciiFontName, float fontSizePt, SignaturePageFacts facts, ref bool repaginated, string lifecycleId, out float widthPt, out SignatureWidthSource source)
	{
		if (SignatureWidthMeasureCache.TryGet(lifecycleId, hostKind, hostVersion, cjkFontName, asciiFontName, fontSizePt, facts.GridEnabled, facts.CharsPerLine, facts.ContentWidthPt, text, out widthPt))
		{
			source = SignatureWidthSource.MeasurementCache;
			return true;
		}
		if (!repaginated)
		{
			doc.Repaginate();
			repaginated = true;
		}
		if (!TryMeasureLineWidth(app, para, facts.ContentWidthPt, out widthPt))
		{
			source = SignatureWidthSource.OneTimeMeasurement;
			return false;
		}
		SignatureWidthMeasureCache.Store(lifecycleId, hostKind, hostVersion, cjkFontName, asciiFontName, fontSizePt, facts.GridEnabled, facts.CharsPerLine, facts.ContentWidthPt, text, widthPt);
		source = SignatureWidthSource.OneTimeMeasurement;
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool TryMeasureLineWidth(Application app, Paragraph para, float contentWidthPt, out float widthPt)
	{
		widthPt = 0f;
		Microsoft.Office.Interop.Word.Range range = null;
		Microsoft.Office.Interop.Word.Range range2 = null;
		ParagraphFormat paragraphFormat = null;
		try
		{
			range = para.Range;
			range2 = range.Duplicate;
			if (range2.End > range2.Start)
			{
				range2.End--;
			}
			paragraphFormat = range2.ParagraphFormat;
			paragraphFormat.Alignment = WdParagraphAlignment.wdAlignParagraphLeft;
			paragraphFormat.LeftIndent = 0f;
			paragraphFormat.RightIndent = 0f;
			paragraphFormat.FirstLineIndent = 0f;
			paragraphFormat.CharacterUnitLeftIndent = 0f;
			paragraphFormat.CharacterUnitRightIndent = 0f;
			paragraphFormat.CharacterUnitFirstLineIndent = 0f;
			paragraphFormat.AutoAdjustRightIndent = 0;
			if (range2.ComputeStatistics(WdStatistic.wdStatisticLines) <= 1)
			{
				Microsoft.Office.Interop.Word.Range range3 = null;
				Microsoft.Office.Interop.Word.Range range4 = null;
				Selection selection = null;
				try
				{
					range3 = range2.Duplicate;
					range4 = range2.Duplicate;
					selection = app.Selection;
					Microsoft.Office.Interop.Word.Range range5 = range3;
					object Direction = WdCollapseDirection.wdCollapseStart;
					range5.Collapse(ref Direction);
					range3.Select();
					float num = Convert.ToSingle((dynamic)selection.get_Information(WdInformation.wdHorizontalPositionRelativeToPage));
					Microsoft.Office.Interop.Word.Range range6 = range4;
					Direction = WdCollapseDirection.wdCollapseEnd;
					range6.Collapse(ref Direction);
					range4.Select();
					float num2 = (float)Convert.ToSingle((dynamic)selection.get_Information(WdInformation.wdHorizontalPositionRelativeToPage)) - num;
					if (!float.IsNaN(num2) && !float.IsInfinity(num2) && num2 > 0f)
					{
						if (!(num2 <= contentWidthPt + 1f))
						{
							return false;
						}
						widthPt = num2;
						return true;
					}
					return false;
				}
				finally
				{
					if (selection != null)
					{
						ComObjectRelease.ReleaseOwned(selection, "TryMeasureLineWidth", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\SignatureFormatter.cs", 451);
					}
					if (range4 != null)
					{
						ComObjectRelease.ReleaseOwned(range4, "TryMeasureLineWidth", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\SignatureFormatter.cs", 452);
					}
					if (range3 != null)
					{
						ComObjectRelease.ReleaseOwned(range3, "TryMeasureLineWidth", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\SignatureFormatter.cs", 453);
					}
				}
			}
			return false;
		}
		catch (Exception ex)
		{
			LogService.Warn("落款行宽一次性实测异常，已放弃实测", ex);
			return false;
		}
		finally
		{
			if (paragraphFormat != null)
			{
				ComObjectRelease.ReleaseOwned(paragraphFormat, "TryMeasureLineWidth", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\SignatureFormatter.cs", 463);
			}
			if (range2 != null)
			{
				ComObjectRelease.ReleaseOwned(range2, "TryMeasureLineWidth", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\SignatureFormatter.cs", 464);
			}
			if (range != null)
			{
				ComObjectRelease.ReleaseOwned(range, "TryMeasureLineWidth", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\SignatureFormatter.cs", 465);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static SignaturePageFacts TryReadPageFacts(FormatConfig cfg, Paragraph anchorPara)
	{
		Microsoft.Office.Interop.Word.Range range = null;
		Sections sections = null;
		Section section = null;
		PageSetup pageSetup = null;
		try
		{
			range = anchorPara.Range;
			sections = range.Sections;
			section = sections[1];
			pageSetup = section.PageSetup;
			float num = pageSetup.PageWidth - pageSetup.LeftMargin - pageSetup.RightMargin;
			if (!float.IsNaN(num) && !float.IsInfinity(num) && !(num <= 0f))
			{
				bool flag = cfg.EnableDocumentGrid && pageSetup.LayoutMode == WdLayoutMode.wdLayoutModeGrid;
				int num2 = 0;
				if (flag)
				{
					num2 = TryReadGridInt(pageSetup, "CharsLine");
					if (num2 <= 0)
					{
						num2 = (cfg.DocumentGridOptions ?? new DocumentGridOptions()).CharsPerLine;
					}
				}
				return new SignaturePageFacts
				{
					ContentWidthPt = num,
					GridEnabled = flag,
					CharsPerLine = num2
				};
			}
			return null;
		}
		catch (Exception ex)
		{
			LogService.Warn("落款页面事实读取失败，落款按安全格式保留", ex);
			return null;
		}
		finally
		{
			if (pageSetup != null)
			{
				ComObjectRelease.ReleaseOwned(pageSetup, "TryReadPageFacts", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\SignatureFormatter.cs", 517);
			}
			if (section != null)
			{
				ComObjectRelease.ReleaseOwned(section, "TryReadPageFacts", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\SignatureFormatter.cs", 518);
			}
			if (sections != null)
			{
				ComObjectRelease.ReleaseOwned(sections, "TryReadPageFacts", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\SignatureFormatter.cs", 519);
			}
			if (range != null)
			{
				ComObjectRelease.ReleaseOwned(range, "TryReadPageFacts", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\SignatureFormatter.cs", 520);
			}
		}
	}

	private static int TryReadGridInt(PageSetup ps, string propertyName)
	{
		try
		{
			return Convert.ToInt32(ps.GetType().InvokeMember(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.GetProperty, null, ps, null));
		}
		catch (Exception)
		{
			return 0;
		}
	}

	private static string ReadHostVersion(Application app)
	{
		try
		{
			return app.Version;
		}
		catch (Exception)
		{
			return string.Empty;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static Paragraph GetParagraphByRangeStart(Document doc, int rangeStart)
	{
		if (doc == null)
		{
			throw new ArgumentNullException("doc");
		}
		WordDocumentBoundary.EnsureDocumentPosition(doc, rangeStart, "落款段落起点");
		Microsoft.Office.Interop.Word.Range range = null;
		Paragraphs paragraphs = null;
		try
		{
			object Start = rangeStart;
			object End = rangeStart;
			range = doc.Range(ref Start, ref End);
			paragraphs = range.Paragraphs;
			if (paragraphs == null || paragraphs.Count < 1)
			{
				throw new InvalidOperationException("无法根据落款分析结果定位段落。");
			}
			return paragraphs[1];
		}
		finally
		{
			if (paragraphs != null)
			{
				ComObjectRelease.ReleaseOwned(paragraphs, "GetParagraphByRangeStart", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\SignatureFormatter.cs", 575);
			}
			if (range != null)
			{
				ComObjectRelease.ReleaseOwned(range, "GetParagraphByRangeStart", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\SignatureFormatter.cs", 576);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ApplyPlannedParagraphFormat(Paragraph para, FormatConfig cfg, float rightIndentPt, ElementType type)
	{
		if (cfg != null)
		{
			TextStyle bodyCfg = cfg.Body ?? throw new FormatException("正文参数缺失。");
			if (para != null)
			{
				Microsoft.Office.Interop.Word.Range range = null;
				ParagraphFormat paragraphFormat = null;
				try
				{
					range = para.Range;
					paragraphFormat = range.ParagraphFormat;
					paragraphFormat.CharacterUnitLeftIndent = 0f;
					paragraphFormat.CharacterUnitRightIndent = 0f;
					paragraphFormat.CharacterUnitFirstLineIndent = 0f;
					paragraphFormat.LeftIndent = 0f;
					paragraphFormat.FirstLineIndent = 0f;
					paragraphFormat.RightIndent = 0f;
					paragraphFormat.AutoAdjustRightIndent = 0;
					paragraphFormat.Alignment = WdParagraphAlignment.wdAlignParagraphRight;
					paragraphFormat.LineSpacingRule = WdLineSpacing.wdLineSpaceExactly;
					paragraphFormat.LineSpacing = ResolveBodyLineSpacing(cfg, bodyCfg);
					if (cfg.EnableDocumentGrid)
					{
						paragraphFormat.DisableLineHeightGrid = 0;
					}
					paragraphFormat.SpaceBefore = 0f;
					paragraphFormat.SpaceAfter = 0f;
					paragraphFormat.SpaceBeforeAuto = 0;
					paragraphFormat.SpaceAfterAuto = 0;
					paragraphFormat.LineUnitBefore = 0f;
					paragraphFormat.LineUnitAfter = 0f;
					paragraphFormat.WidowControl = 0;
					paragraphFormat.RightIndent = rightIndentPt;
					return;
				}
				finally
				{
					if (paragraphFormat != null)
					{
						ComObjectRelease.ReleaseOwned(paragraphFormat, "ApplyPlannedParagraphFormat", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\SignatureFormatter.cs", 628);
					}
					if (range != null)
					{
						ComObjectRelease.ReleaseOwned(range, "ApplyPlannedParagraphFormat", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\SignatureFormatter.cs", 629);
					}
				}
			}
			throw new ArgumentNullException("para");
		}
		throw new ArgumentNullException("cfg");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool IsWithSeal(FormatConfig cfg)
	{
		if (cfg != null)
		{
			if (cfg.SignatureOptions != null)
			{
				return cfg.SignatureOptions.WithSeal;
			}
			return cfg.EnableSignatureWithSeal;
		}
		throw new ArgumentNullException("cfg");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int GetBlankLinesBefore(FormatConfig cfg)
	{
		if (cfg != null)
		{
			int num = ((cfg.SignatureOptions != null) ? cfg.SignatureOptions.BlankLinesBefore : ((!cfg.EnableSignatureWithSeal) ? 1 : 2));
			if (num < 0 || num > 10)
			{
				throw new FormatException("落款与正文之间的空行数必须在0到10之间。");
			}
			return num;
		}
		throw new ArgumentNullException("cfg");
	}

	private static float ResolveBodyLineSpacing(FormatConfig cfg, TextStyle bodyCfg)
	{
		if (!cfg.EnableDocumentGrid)
		{
			return LineSpacingConverter.ToPoints(bodyCfg.LineSpacing);
		}
		DocumentGridOptions documentGridOptions = cfg.DocumentGridOptions ?? new DocumentGridOptions();
		return DocumentGridMetrics.CalculateVerticalPitchFromCentimeters(29.7f, cfg.TopMargin, cfg.BottomMargin, documentGridOptions.LinesPerPage);
	}
}
