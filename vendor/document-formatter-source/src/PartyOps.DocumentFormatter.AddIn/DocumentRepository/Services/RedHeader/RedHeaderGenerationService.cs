using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DocumentRepository.Models.RedHeader;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Interop;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.Safety;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.RedHeader;

public static class RedHeaderGenerationService
{
	private const float TablePositionCenter = -999995f;

	private const float TablePositionBottom = -999997f;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Apply(DocumentWriteLease writeLease, Document document, RedHeaderLayoutPlan plan)
	{
		if (writeLease == null)
		{
			throw new ArgumentNullException("writeLease", "套红写入必须持有当前会话签发的写入凭证。");
		}
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		if (plan != null)
		{
			plan.EnsureSealed();
			writeLease.AssertActive(DocumentLifecycleRegistry.GetOrCreate(document), plan.PlanId, plan.SourceForExecution.DocumentSnapshot.SnapshotId);
			InsertGeneratedHeader(writeLease, document, plan);
			AddImprintTable(document, plan);
			try
			{
				object Start = 0;
				object End = 0;
				document.Range(ref Start, ref End).Select();
				return;
			}
			catch (Exception ex)
			{
				LogService.Warn("RedHeaderGenerationService.SelectDocumentStart", ex);
				return;
			}
		}
		throw new ArgumentNullException("plan");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void InsertGeneratedHeader(DocumentWriteLease writeLease, Document document, RedHeaderLayoutPlan plan)
	{
		int start = document.Content.Start;
		string generatedHeaderText = plan.GeneratedHeaderText;
		Microsoft.Office.Interop.Word.Range value = null;
		Microsoft.Office.Interop.Word.Range value2 = null;
		try
		{
			object Start = start;
			object End = start;
			value = document.Range(ref Start, ref End);
			value.Text = generatedHeaderText;
			writeLease.ConfirmWriteOccurred();
			RedHeaderGeneratedObjectIdentityService.RegisterSourceBody(document, plan, value.End);
			End = start;
			Start = start + generatedHeaderText.Length;
			value2 = document.Range(ref End, ref Start);
			FormatGeneratedHeader(value2, plan, document);
			Paragraph value3 = value2.Paragraphs[plan.RedLineParagraphIndex];
			try
			{
				DrawRedLine(document, value3, plan);
			}
			catch (Exception ex)
			{
				LogService.Error("RedHeaderGenerationService.DrawRedLine", ex);
				throw;
			}
			finally
			{
				if (value3 != null)
				{
					ComObjectRelease.Release(ref value3, "RedHeaderGenerationService.anchor");
				}
			}
		}
		finally
		{
			if (value2 != null)
			{
				ComObjectRelease.Release(ref value2, "RedHeaderGenerationService.inserted");
			}
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "RedHeaderGenerationService.insertion");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void FormatGeneratedHeader(Microsoft.Office.Interop.Word.Range range, RedHeaderLayoutPlan plan, Document document)
	{
		RedHeaderTemplate templateForExecution = plan.TemplateForExecution;
		int count = range.Paragraphs.Count;
		for (int i = 1; i <= count; i++)
		{
			Paragraph value = null;
			try
			{
				value = range.Paragraphs[i];
				Microsoft.Office.Interop.Word.Range range2 = value.Range;
				if (i <= plan.TopMarkLines.Count)
				{
					ApplyTopMarkParagraph(range2, templateForExecution.TopMarks);
				}
				else if (i == plan.HeaderParagraphIndex)
				{
					bool flag = HeaderUsesDistributedAlignment(templateForExecution);
					SetParagraph(range2, flag ? WdParagraphAlignment.wdAlignParagraphDistribute : ToParagraphAlignment(templateForExecution.HeaderAlignment), templateForExecution.HeaderLineSpacing, templateForExecution.HeaderSpaceAfter, templateForExecution.HeaderSpaceBefore);
					SetFont(range2, templateForExecution.HeaderFont, templateForExecution.HeaderSize, templateForExecution.HeaderBold, templateForExecution.HeaderColor);
					if (flag)
					{
						try
						{
							range2.Font.Spacing = 0f;
						}
						catch
						{
						}
						ApplyHeaderDistributedBounds(range2, plan);
					}
					else
					{
						ApplyVisibleHeaderSpacing(document, range2, templateForExecution.HeaderCharacterSpacing);
						try
						{
							range2.ParagraphFormat.CharacterUnitLeftIndent = templateForExecution.HeaderIndentChars;
						}
						catch
						{
						}
					}
					ApplyHeaderCharacterScale(document, range2, plan);
				}
				else if (plan.HasDocumentNumber && i == plan.DocumentNumberParagraphIndex)
				{
					SetParagraph(range2, WdParagraphAlignment.wdAlignParagraphCenter, templateForExecution.DocumentNumberLineSpacing, templateForExecution.DocumentNumberSpaceAfter, templateForExecution.DocumentNumberSpaceBefore);
					SetFont(range2, templateForExecution.DocumentNumberFont, templateForExecution.DocumentNumberSize, bold: false, 0);
					ApplyTimesNewRomanToArabicNumbers(document, range2, plan.DocumentNumberText, 0, templateForExecution.UseTimesNewRomanForNumbers, "documentNumber");
					ApplySignerNameFont(document, range2, plan.ConfigForExecution);
				}
				else if (i != plan.RedLineParagraphIndex)
				{
					SetParagraph(range2, WdParagraphAlignment.wdAlignParagraphLeft, templateForExecution.TitleGapLineSpacing, 0f, 0f);
					SetFont(range2, RequireTemplateFont(templateForExecution.DocumentNumberFont, "DocumentNumberFont"), templateForExecution.DocumentNumberSize, bold: false, 0);
				}
				else
				{
					SetParagraph(range2, WdParagraphAlignment.wdAlignParagraphCenter, 18f, templateForExecution.RedLineSpaceAfter, templateForExecution.RedLineSpaceBefore);
					SetFont(range2, RequireTemplateFont(templateForExecution.HeaderFont, "HeaderFont"), 1f, bold: false, templateForExecution.RedLineColor);
				}
			}
			finally
			{
				if (value != null)
				{
					ComObjectRelease.Release(ref value, "RedHeaderGenerationService.paragraph");
				}
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void SetParagraph(Microsoft.Office.Interop.Word.Range range, WdParagraphAlignment alignment, float lineSpacing, float spaceAfter, float spaceBefore)
	{
		if (range == null)
		{
			return;
		}
		try
		{
			ParagraphFormat paragraphFormat = range.ParagraphFormat;
			ResetParagraphFormat(paragraphFormat);
			paragraphFormat.Alignment = alignment;
			paragraphFormat.LineSpacingRule = WdLineSpacing.wdLineSpaceExactly;
			paragraphFormat.LineSpacing = lineSpacing;
			paragraphFormat.SpaceBefore = spaceBefore;
			paragraphFormat.SpaceAfter = spaceAfter;
		}
		catch (Exception ex)
		{
			LogService.Error("RedHeaderGenerationService.ApplySignerNameFont", ex);
			throw;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ApplyTopMarkParagraph(Microsoft.Office.Interop.Word.Range range, RedHeaderTopMarkOptions options)
	{
		if (range == null)
		{
			return;
		}
		if (options == null)
		{
			throw new InvalidOperationException("版头附加标识参数不能为空。");
		}
		SetParagraph(range, WdParagraphAlignment.wdAlignParagraphLeft, options.LineSpacing, 0f, 0f);
		SetFont(range, options.FontName, options.FontSize, bold: false, options.Color);
		try
		{
			range.ParagraphFormat.CharacterUnitLeftIndent = options.LeftIndentChars;
		}
		catch (Exception ex)
		{
			LogService.Error("RedHeaderGenerationService.ApplyTopMarkParagraph", ex);
			throw new InvalidOperationException("无法设置版头附加标识的左侧位置。", ex);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ApplyHeaderCharacterScale(Document document, Microsoft.Office.Interop.Word.Range paragraphRange, RedHeaderLayoutPlan plan)
	{
		if (document != null && paragraphRange != null && plan != null)
		{
			RedHeaderTemplate templateForExecution = plan.TemplateForExecution;
			int num = Convert.ToInt32(Math.Round(templateForExecution.HeaderCharacterScalePercent));
			int num2 = Convert.ToInt32(Math.Round(templateForExecution.HeaderMinimumScalePercent));
			if (!(templateForExecution.HeaderFitMode != "autoSingleLine"))
			{
				ApplyVisibleHeaderScaling(document, paragraphRange, num);
				if (CountRenderedHeaderLines(document, paragraphRange) <= 1)
				{
					return;
				}
				int num3 = num2;
				int num4 = num - 1;
				int num5 = -1;
				while (num3 <= num4)
				{
					int num6 = num3 + (num4 - num3) / 2;
					ApplyVisibleHeaderScaling(document, paragraphRange, num6);
					if (CountRenderedHeaderLines(document, paragraphRange) <= 1)
					{
						num5 = num6;
						num3 = num6 + 1;
					}
					else
					{
						num4 = num6 - 1;
					}
				}
				if (num5 >= 0)
				{
					ApplyVisibleHeaderScaling(document, paragraphRange, num5);
					return;
				}
				ApplyVisibleHeaderScaling(document, paragraphRange, num2);
				ReportQualityWarning("redheader-header-still-wrapped", "header");
			}
			else
			{
				ApplyVisibleHeaderScaling(document, paragraphRange, num);
			}
			return;
		}
		throw new ArgumentNullException("发文机关字符缩放缺少必要对象。");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ApplyVisibleHeaderScaling(Document document, Microsoft.Office.Interop.Word.Range paragraphRange, int scalePercent)
	{
		int num = VisibleTextLength(paragraphRange);
		if (num <= 0)
		{
			throw RedHeaderOperationException.Create(RedHeaderFailureReasonCode.HeaderLayoutFailed, RedHeaderFailureStage.Generate);
		}
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			object Start = paragraphRange.Start;
			object End = paragraphRange.Start + num;
			value = document.Range(ref Start, ref End);
			value.Font.Scaling = scalePercent;
		}
		catch (Exception ex)
		{
			LogService.Error("RedHeaderGenerationService.ApplyVisibleHeaderScaling", ex);
			throw RedHeaderOperationException.Create(RedHeaderFailureReasonCode.HeaderLayoutFailed, RedHeaderFailureStage.Generate, ex);
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "RedHeaderGenerationService.visible");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int CountRenderedHeaderLines(Document document, Microsoft.Office.Interop.Word.Range paragraphRange)
	{
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			int num = VisibleTextLength(paragraphRange);
			if (num <= 0)
			{
				throw new InvalidOperationException("发文机关可见文字为空。");
			}
			document.Repaginate();
			object Start = paragraphRange.Start;
			object End = paragraphRange.Start + num;
			value = document.Range(ref Start, ref End);
			int num2 = value.ComputeStatistics(WdStatistic.wdStatisticLines);
			if (num2 <= 0)
			{
				throw new InvalidOperationException("Word/WPS返回的发文机关行数无效。");
			}
			return num2;
		}
		catch (Exception ex)
		{
			LogService.Error("RedHeaderGenerationService.CountRenderedHeaderLines", ex);
			throw RedHeaderOperationException.Create(RedHeaderFailureReasonCode.HeaderLayoutFailed, RedHeaderFailureStage.Generate, ex);
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "RedHeaderGenerationService.visible");
			}
		}
	}

	private static void ResetParagraphFormat(ParagraphFormat format)
	{
		if (format == null)
		{
			return;
		}
		try
		{
			format.LeftIndent = 0f;
		}
		catch
		{
		}
		try
		{
			format.RightIndent = 0f;
		}
		catch
		{
		}
		try
		{
			format.FirstLineIndent = 0f;
		}
		catch
		{
		}
		try
		{
			format.CharacterUnitFirstLineIndent = 0f;
		}
		catch
		{
		}
		try
		{
			format.CharacterUnitLeftIndent = 0f;
		}
		catch
		{
		}
		try
		{
			format.CharacterUnitRightIndent = 0f;
		}
		catch
		{
		}
		try
		{
			format.SpaceBefore = 0f;
		}
		catch
		{
		}
		try
		{
			format.SpaceAfter = 0f;
		}
		catch
		{
		}
		try
		{
			format.LineUnitBefore = 0f;
		}
		catch
		{
		}
		try
		{
			format.LineUnitAfter = 0f;
		}
		catch
		{
		}
	}

	private static void SetFont(Microsoft.Office.Interop.Word.Range range, string name, float size, bool bold, int color)
	{
		if (range == null)
		{
			return;
		}
		try
		{
			range.Font.Name = name;
			range.Font.NameFarEast = name;
			range.Font.Size = size;
			range.Font.Bold = (bold ? (-1) : 0);
			range.Font.Color = (WdColor)color;
		}
		catch
		{
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string RequireTemplateFont(string fontName, string parameterName)
	{
		if (string.IsNullOrWhiteSpace(fontName))
		{
			throw new InvalidOperationException("Missing red header template font parameter: " + parameterName);
		}
		return fontName;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ApplySignerNameFont(Document document, Microsoft.Office.Interop.Word.Range range, FormatConfig config)
	{
		if (document == null || range == null)
		{
			return;
		}
		try
		{
			string text = (range.Text ?? "").TrimEnd('\r', '\a');
			Match match = Regex.Match(text, "签发人\\s*[:：]\\s*");
			if (!match.Success)
			{
				return;
			}
			int i;
			for (i = match.Index + match.Length; i < text.Length && char.IsWhiteSpace(text[i]); i++)
			{
			}
			int num = text.Length;
			while (num > i && char.IsWhiteSpace(text[num - 1]))
			{
				num--;
			}
			if (num <= i)
			{
				return;
			}
			if (config != null && config.Level2 != null && !string.IsNullOrWhiteSpace(config.Level2.FontName))
			{
				string fontName = config.Level2.FontName;
				Microsoft.Office.Interop.Word.Range value = null;
				try
				{
					object Start = range.Start + i;
					object End = range.Start + num;
					value = document.Range(ref Start, ref End);
					value.Font.Name = fontName;
					value.Font.NameFarEast = fontName;
					return;
				}
				finally
				{
					if (value != null)
					{
						ComObjectRelease.Release(ref value, "RedHeaderGenerationService.signerRange");
					}
				}
			}
			throw new InvalidOperationException("签发人姓名字体需要引用一键排版二级标题字体，但当前二级标题字体为空。");
		}
		catch
		{
		}
	}

	private static void ApplyHeaderDistributedBounds(Microsoft.Office.Interop.Word.Range range, RedHeaderLayoutPlan plan)
	{
		try
		{
			RedHeaderTemplate templateForExecution = plan.TemplateForExecution;
			float num = Math.Max(0f, templateForExecution.HeaderIndentChars) * templateForExecution.HeaderSize;
			ParagraphFormat paragraphFormat = range.ParagraphFormat;
			paragraphFormat.LeftIndent = plan.HeaderSideIndent + num;
			paragraphFormat.RightIndent = plan.HeaderSideIndent;
			paragraphFormat.CharacterUnitLeftIndent = 0f;
			paragraphFormat.CharacterUnitRightIndent = 0f;
			paragraphFormat.AutoAdjustRightIndent = 0;
		}
		catch
		{
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ApplyVisibleHeaderSpacing(Document document, Microsoft.Office.Interop.Word.Range range, float spacing)
	{
		try
		{
			range.Font.Spacing = 0f;
		}
		catch
		{
		}
		if (spacing == 0f || document == null || range == null)
		{
			return;
		}
		int num = VisibleTextLength(range);
		if (num <= 1)
		{
			return;
		}
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			object Start = range.Start;
			object End = range.Start + num - 1;
			value = document.Range(ref Start, ref End);
			value.Font.Spacing = spacing;
		}
		catch
		{
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "RedHeaderGenerationService.visible");
			}
		}
	}

	private static int VisibleTextLength(Microsoft.Office.Interop.Word.Range range)
	{
		string text = "";
		try
		{
			text = range.Text ?? "";
		}
		catch
		{
			return 0;
		}
		while (text.Length > 0 && (text[text.Length - 1] == '\r' || text[text.Length - 1] == '\a'))
		{
			text = text.Substring(0, text.Length - 1);
		}
		return text.Length;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void DrawRedLine(Document document, Paragraph anchorParagraph, RedHeaderLayoutPlan plan)
	{
		float redLineAnchorX = plan.RedLineAnchorX1;
		float redLineAnchorX2 = plan.RedLineAnchorX2;
		float redLineAnchorTop = plan.RedLineAnchorTop;
		float redLineWeight = plan.RedLineWeight;
		string normalizedRedLineStyle = plan.NormalizedRedLineStyle;
		int redLineColor = plan.TemplateForExecution.RedLineColor;
		int num = 0;
		switch (normalizedRedLineStyle)
		{
		case "lowerThickUpperThin":
			AddLineShape(document, anchorParagraph.Range, redLineAnchorX, redLineAnchorTop - 1.8f, redLineAnchorX2, redLineAnchorTop - 1.8f, 0.75f, redLineColor, ShapeName(plan, ++num));
			AddLineShape(document, anchorParagraph.Range, redLineAnchorX, redLineAnchorTop + 1.8f, redLineAnchorX2, redLineAnchorTop + 1.8f, redLineWeight, redLineColor, ShapeName(plan, ++num));
			break;
		case "star":
		{
			float num2 = (redLineAnchorX + redLineAnchorX2) / 2f;
			AddLineShape(document, anchorParagraph.Range, redLineAnchorX, redLineAnchorTop, num2 - 10f, redLineAnchorTop, redLineWeight, redLineColor, ShapeName(plan, ++num));
			AddLineShape(document, anchorParagraph.Range, num2 + 10f, redLineAnchorTop, redLineAnchorX2, redLineAnchorTop, redLineWeight, redLineColor, ShapeName(plan, ++num));
			AddCenterStar(document, anchorParagraph.Range, num2, redLineAnchorTop, redLineColor, ShapeName(plan, ++num));
			break;
		}
		case "singleBottom":
			AddLineShape(document, anchorParagraph.Range, redLineAnchorX, redLineAnchorTop + 2f, redLineAnchorX2, redLineAnchorTop + 2f, redLineWeight, redLineColor, ShapeName(plan, ++num));
			break;
		case "singleTop":
			AddLineShape(document, anchorParagraph.Range, redLineAnchorX, redLineAnchorTop - 2f, redLineAnchorX2, redLineAnchorTop - 2f, redLineWeight, redLineColor, ShapeName(plan, ++num));
			break;
		case "upperThickLowerThin":
			AddLineShape(document, anchorParagraph.Range, redLineAnchorX, redLineAnchorTop - 1.8f, redLineAnchorX2, redLineAnchorTop - 1.8f, redLineWeight, redLineColor, ShapeName(plan, ++num));
			AddLineShape(document, anchorParagraph.Range, redLineAnchorX, redLineAnchorTop + 1.8f, redLineAnchorX2, redLineAnchorTop + 1.8f, 0.75f, redLineColor, ShapeName(plan, ++num));
			break;
		default:
			AddLineShape(document, anchorParagraph.Range, redLineAnchorX, redLineAnchorTop, redLineAnchorX2, redLineAnchorTop, redLineWeight, redLineColor, ShapeName(plan, ++num));
			break;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void AddLineShape(Document document, Microsoft.Office.Interop.Word.Range anchorRange, float x1, float y1, float x2, float y2, float weight, int color, string shapeName)
	{
		Shape value = null;
		try
		{
			object Anchor = anchorRange;
			value = document.Shapes.AddLine(0f, 0f, x2 - x1, y2 - y1, ref Anchor);
			if (value != null)
			{
				value.Name = shapeName;
				NormalizeLineShape(value);
				PinShapeToAnchorParagraph(value, x1, y1);
				try
				{
					value.Line.Weight = weight;
				}
				catch
				{
				}
				try
				{
					value.Line.ForeColor.RGB = color;
				}
				catch
				{
				}
				try
				{
					value.WrapFormat.Type = WdWrapType.wdWrapNone;
					return;
				}
				catch
				{
					return;
				}
			}
			throw new InvalidOperationException("红线对象创建后为空。");
		}
		catch (Exception ex)
		{
			LogService.Error("RedHeaderGenerationService.AddLineShape.WithAnchor", ex);
			throw RedHeaderOperationException.Create(RedHeaderFailureReasonCode.RedLineCreationFailed, RedHeaderFailureStage.Generate, ex);
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "RedHeaderGenerationService.shape");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void AddCenterStar(Document document, Microsoft.Office.Interop.Word.Range anchorRange, float x, float y, int color, string shapeName)
	{
		Shape value = null;
		try
		{
			object Anchor = anchorRange;
			value = document.Shapes.AddShape(92, 0f, 0f, 11f, 11f, ref Anchor);
			if (value != null)
			{
				value.Name = shapeName;
				NormalizeLineShape(value);
				PinShapeToAnchorParagraph(value, x - 5.5f, y - 5.5f);
				try
				{
					value.Fill.ForeColor.RGB = color;
				}
				catch
				{
				}
				try
				{
					value.Line.ForeColor.RGB = color;
				}
				catch
				{
				}
				try
				{
					value.Line.Weight = 0.5f;
				}
				catch
				{
				}
				try
				{
					value.WrapFormat.Type = WdWrapType.wdWrapNone;
					return;
				}
				catch
				{
					return;
				}
			}
			throw new InvalidOperationException("红线星标对象创建后为空。");
		}
		catch (Exception ex)
		{
			LogService.Error("RedHeaderGenerationService.AddCenterStar.WithAnchor", ex);
			throw RedHeaderOperationException.Create(RedHeaderFailureReasonCode.RedLineCreationFailed, RedHeaderFailureStage.Generate, ex);
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "RedHeaderGenerationService.star");
			}
		}
	}

	private static void NormalizeLineShape(Shape shape)
	{
		if (shape == null)
		{
			return;
		}
		try
		{
			shape.Shadow.Visible = OfficeInteropCompatibility.MsoFalse;
		}
		catch
		{
		}
		try
		{
			shape.ThreeD.Visible = OfficeInteropCompatibility.MsoFalse;
		}
		catch
		{
		}
		try
		{
			shape.Line.Visible = OfficeInteropCompatibility.MsoTrue;
		}
		catch
		{
		}
		try
		{
			shape.Line.DashStyle = OfficeInteropCompatibility.MsoLineSolid;
		}
		catch
		{
		}
		try
		{
			shape.Line.Transparency = 0f;
		}
		catch
		{
		}
		try
		{
			shape.Line.BeginArrowheadStyle = OfficeInteropCompatibility.MsoArrowheadNone;
		}
		catch
		{
		}
		try
		{
			shape.Line.EndArrowheadStyle = OfficeInteropCompatibility.MsoArrowheadNone;
		}
		catch
		{
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void PinShapeToAnchorParagraph(Shape shape, float left, float top)
	{
		if (shape == null)
		{
			throw new ArgumentNullException("shape");
		}
		try
		{
			shape.RelativeHorizontalPosition = WdRelativeHorizontalPosition.wdRelativeHorizontalPositionColumn;
			shape.RelativeVerticalPosition = WdRelativeVerticalPosition.wdRelativeVerticalPositionParagraph;
			shape.Left = left;
			shape.Top = top;
			try
			{
				shape.LockAnchor = -1;
			}
			catch
			{
			}
		}
		catch (Exception ex)
		{
			LogService.Error("RedHeaderGenerationService.PinShapeToAnchorParagraph", ex);
			throw RedHeaderOperationException.Create(RedHeaderFailureReasonCode.RedLineCreationFailed, RedHeaderFailureStage.Generate, ex);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void AddImprintTable(Document document, RedHeaderLayoutPlan plan)
	{
		if (!plan.ImprintEnabled)
		{
			return;
		}
		EnsureImprintTailParagraph(document);
		PrepareImprintPage(document, plan);
		Table value = null;
		try
		{
			value = CreateImprintTable(document, plan);
			RedHeaderGeneratedObjectIdentityService.RegisterImprintTable(document, value, plan);
			CompressBlankParagraphAfterTable(document, value);
			StabilizeImprintLayout(document, value, plan);
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "RedHeaderGenerationService.table");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void EnsureImprintTailParagraph(Document document)
	{
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			value = document.Content;
			value.InsertAfter("\r");
		}
		finally
		{
			ComObjectRelease.Release(ref value, "RedHeaderGenerationService.ImprintTailParagraph");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static Table CreateImprintTable(Document document, RedHeaderLayoutPlan plan)
	{
		Microsoft.Office.Interop.Word.Range value = null;
		Table value2 = null;
		bool flag = false;
		try
		{
			object Start = document.Content.End - 1;
			object End = document.Content.End - 1;
			value = document.Range(ref Start, ref End);
			Tables tables = document.Tables;
			Microsoft.Office.Interop.Word.Range range = value;
			int imprintRowCount = plan.ImprintRowCount;
			End = Type.Missing;
			Start = Type.Missing;
			value2 = tables.Add(range, imprintRowCount, 1, ref End, ref Start);
			SetImprintTableBorders(value2);
			KeepImprintTableTogether(value2, plan.ImprintRowCount);
			SetImprintTableMargins(value2, plan.ImprintCellPaddingPoints);
			SetImprintTableRowHeights(value2, plan.ImprintRowHeight);
			if (plan.HasImprintSend)
			{
				value2.Cell(1, 1).Range.Text = plan.ImprintSendText;
			}
			value2.Cell(plan.ImprintOfficeRow, 1).Range.Text = plan.ImprintOfficeLine;
			try
			{
				value2.Rows.Alignment = WdRowAlignment.wdAlignRowCenter;
			}
			catch
			{
			}
			try
			{
				value2.PreferredWidthType = WdPreferredWidthType.wdPreferredWidthPercent;
				value2.PreferredWidth = 100f;
			}
			catch
			{
			}
			if (plan.HasImprintSend)
			{
				ApplyImprintSendParagraph(value2.Cell(1, 1).Range, plan.ImprintFontSize);
			}
			ApplyImprintOfficeParagraph(value2.Cell(plan.ImprintOfficeRow, 1).Range, plan.UsableWidth, plan.ImprintFontSize);
			RedHeaderTemplate templateForExecution = plan.TemplateForExecution;
			SetFont(value2.Range, templateForExecution.ImprintFont, plan.ImprintFontSize, bold: false, 0);
			ApplyTimesNewRomanToArabicNumbers(document, value2.Cell(plan.ImprintOfficeRow, 1).Range, plan.ResolvedImprintDate ?? string.Empty, (templateForExecution.ImprintOffice ?? string.Empty).Length + 1, templateForExecution.UseTimesNewRomanForNumbers, "imprintDate");
			try
			{
				value2.Range.ParagraphFormat.SpaceBefore = 0f;
				value2.Range.ParagraphFormat.SpaceAfter = 0f;
				value2.Range.ParagraphFormat.LineSpacingRule = WdLineSpacing.wdLineSpaceSingle;
				value2.Range.ParagraphFormat.KeepTogether = -1;
			}
			catch
			{
			}
			PositionImprintTableAtBottom(value2);
			flag = true;
			return value2;
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "RedHeaderGenerationService.insertRange");
			}
			if (!flag && value2 != null)
			{
				ComObjectRelease.Release(ref value2, "RedHeaderGenerationService.table");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ApplyTimesNewRomanToArabicNumbers(Document document, Microsoft.Office.Interop.Word.Range containerRange, string text, int textOffset, bool enabled, string diagnosticName)
	{
		if (!enabled || document == null || containerRange == null || string.IsNullOrEmpty(text))
		{
			return;
		}
		if (textOffset < 0)
		{
			throw new ArgumentOutOfRangeException("textOffset");
		}
		IReadOnlyList<RedHeaderDigitRun> readOnlyList = RedHeaderNumberFontPolicy.FindArabicDigitRuns(text);
		if (readOnlyList.Count == 0)
		{
			return;
		}
		for (int i = 0; i < readOnlyList.Count; i++)
		{
			RedHeaderDigitRun redHeaderDigitRun = readOnlyList[i];
			Microsoft.Office.Interop.Word.Range value = null;
			Font value2 = null;
			try
			{
				int num = containerRange.Start + textOffset + redHeaderDigitRun.Start;
				object Start = num;
				object End = num + redHeaderDigitRun.Length;
				value = document.Range(ref Start, ref End);
				value2 = value.Font;
				value2.Name = "Times New Roman";
				try
				{
					value2.NameAscii = "Times New Roman";
				}
				catch
				{
				}
				try
				{
					value2.NameOther = "Times New Roman";
				}
				catch
				{
				}
			}
			finally
			{
				if (value2 != null)
				{
					ComObjectRelease.Release(ref value2, "RedHeaderGenerationService." + diagnosticName + "DigitFont");
				}
				if (value != null)
				{
					ComObjectRelease.Release(ref value, "RedHeaderGenerationService." + diagnosticName + "DigitRange");
				}
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool PositionImprintTableAtBottom(Table table)
	{
		if (table == null)
		{
			throw new ArgumentNullException("table");
		}
		try
		{
			table.Rows.WrapAroundText = -1;
			table.Rows.RelativeHorizontalPosition = WdRelativeHorizontalPosition.wdRelativeHorizontalPositionColumn;
			table.Rows.HorizontalPosition = -999995f;
			table.Rows.RelativeVerticalPosition = WdRelativeVerticalPosition.wdRelativeVerticalPositionMargin;
			table.Rows.VerticalPosition = -999997f;
			try
			{
				table.Rows.AllowOverlap = 0;
			}
			catch
			{
			}
			return true;
		}
		catch (Exception ex)
		{
			LogService.Warn("RedHeaderGenerationService.PositionImprintTableAtBottom", ex);
			ReportQualityWarning("redheader-imprint-bottom-position", "imprint");
			return false;
		}
	}

	private static void SetImprintTableBorders(Table table)
	{
		try
		{
			table.Borders.Enable = 1;
		}
		catch
		{
		}
		try
		{
			table.Borders[WdBorderType.wdBorderLeft].LineStyle = WdLineStyle.wdLineStyleNone;
		}
		catch
		{
		}
		try
		{
			table.Borders[WdBorderType.wdBorderRight].LineStyle = WdLineStyle.wdLineStyleNone;
		}
		catch
		{
		}
		try
		{
			table.Borders[WdBorderType.wdBorderVertical].LineStyle = WdLineStyle.wdLineStyleNone;
		}
		catch
		{
		}
		SetImprintBorder(table, WdBorderType.wdBorderTop, WdLineWidth.wdLineWidth100pt);
		SetImprintBorder(table, WdBorderType.wdBorderBottom, WdLineWidth.wdLineWidth100pt);
		SetImprintBorder(table, WdBorderType.wdBorderHorizontal, WdLineWidth.wdLineWidth075pt);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void SetImprintBorder(Table table, WdBorderType borderType, WdLineWidth lineWidth)
	{
		Border value = null;
		try
		{
			value = table.Borders[borderType];
			value.LineStyle = WdLineStyle.wdLineStyleSingle;
			value.LineWidth = lineWidth;
			value.Color = WdColor.wdColorBlack;
		}
		catch (Exception ex)
		{
			LogService.Warn("RedHeaderGenerationService.SetImprintBorder", ex);
			ReportQualityWarning("redheader-imprint-border-write-skipped", "imprint");
		}
		finally
		{
			ComObjectRelease.Release(ref value, "RedHeaderGenerationService.ImprintBorder");
		}
	}

	private static void SetImprintTableMargins(Table table, float verticalPadding)
	{
		try
		{
			table.TopPadding = verticalPadding;
		}
		catch
		{
		}
		try
		{
			table.BottomPadding = verticalPadding;
		}
		catch
		{
		}
		try
		{
			table.LeftPadding = 0f;
		}
		catch
		{
		}
		try
		{
			table.RightPadding = 0f;
		}
		catch
		{
		}
	}

	private static void SetImprintTableRowHeights(Table table, float rowHeight)
	{
		try
		{
			table.Rows.HeightRule = WdRowHeightRule.wdRowHeightAtLeast;
		}
		catch
		{
		}
		try
		{
			table.Rows.Height = rowHeight;
		}
		catch
		{
		}
	}

	private static void KeepImprintTableTogether(Table table, int rowCount)
	{
		try
		{
			table.Rows.AllowBreakAcrossPages = 0;
		}
		catch
		{
		}
		for (int i = 1; i <= rowCount; i++)
		{
			try
			{
				table.Rows[i].AllowBreakAcrossPages = 0;
			}
			catch
			{
			}
			try
			{
				table.Rows[i].Range.ParagraphFormat.KeepTogether = -1;
			}
			catch
			{
			}
			if (i < rowCount)
			{
				try
				{
					table.Rows[i].Range.ParagraphFormat.KeepWithNext = -1;
				}
				catch
				{
				}
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ApplyImprintSendParagraph(Microsoft.Office.Interop.Word.Range cellRange, float fontSize)
	{
		try
		{
			ParagraphFormat paragraphFormat = cellRange.ParagraphFormat;
			SetImprintParagraphBase(paragraphFormat);
			paragraphFormat.Alignment = WdParagraphAlignment.wdAlignParagraphJustify;
			paragraphFormat.CharacterUnitLeftIndent = 1f;
			try
			{
				paragraphFormat.LeftIndent = fontSize * 4.1f;
			}
			catch
			{
			}
			try
			{
				paragraphFormat.FirstLineIndent = (0f - fontSize) * 3f;
			}
			catch
			{
			}
		}
		catch (Exception ex)
		{
			LogService.Warn("RedHeaderGenerationService.CompressBlankParagraphAfterLastTable", ex);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ApplyImprintOfficeParagraph(Microsoft.Office.Interop.Word.Range cellRange, float width, float fontSize)
	{
		try
		{
			ParagraphFormat paragraphFormat = cellRange.ParagraphFormat;
			SetImprintParagraphBase(paragraphFormat);
			paragraphFormat.Alignment = WdParagraphAlignment.wdAlignParagraphDistribute;
			paragraphFormat.CharacterUnitLeftIndent = 1f;
			paragraphFormat.CharacterUnitRightIndent = 1f;
			paragraphFormat.CharacterUnitFirstLineIndent = 0f;
			try
			{
				paragraphFormat.FirstLineIndent = 0f;
			}
			catch
			{
			}
			try
			{
				paragraphFormat.TabStops.ClearAll();
			}
			catch
			{
			}
			try
			{
				TabStops tabStops = paragraphFormat.TabStops;
				float position = width - fontSize * 0.6f;
				object Alignment = WdTabAlignment.wdAlignTabRight;
				object Leader = Type.Missing;
				tabStops.Add(position, ref Alignment, ref Leader);
			}
			catch
			{
			}
		}
		catch (Exception ex)
		{
			LogService.Warn("RedHeaderGenerationService.GetWidthLayout", ex);
		}
	}

	private static void SetImprintParagraphBase(ParagraphFormat format)
	{
		try
		{
			format.SpaceBefore = 0f;
		}
		catch
		{
		}
		try
		{
			format.SpaceAfter = 0f;
		}
		catch
		{
		}
		try
		{
			format.LineSpacingRule = WdLineSpacing.wdLineSpaceSingle;
		}
		catch
		{
		}
		try
		{
			format.LeftIndent = 0f;
		}
		catch
		{
		}
		try
		{
			format.RightIndent = 0f;
		}
		catch
		{
		}
		try
		{
			format.FirstLineIndent = 0f;
		}
		catch
		{
		}
		try
		{
			format.CharacterUnitFirstLineIndent = 0f;
		}
		catch
		{
		}
		try
		{
			format.CharacterUnitRightIndent = 0f;
		}
		catch
		{
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void PrepareImprintPage(Document document, RedHeaderLayoutPlan plan)
	{
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			object Start = document.Content.End - 1;
			object End = document.Content.End - 1;
			value = document.Range(ref Start, ref End);
			int num;
			float currentY;
			try
			{
				num = WordPageOrdinalService.ReadPhysicalPageOrdinal(value, "版记分页位置");
				currentY = Convert.ToSingle((dynamic)value.get_Information(WdInformation.wdVerticalPositionRelativeToPage));
			}
			catch (Exception ex)
			{
				LogService.Error("RedHeaderGenerationService.PrepareImprintPage.ReadPosition", ex);
				throw RedHeaderOperationException.Create(RedHeaderFailureReasonCode.ImprintPaginationFailed, RedHeaderFailureStage.Generate, ex);
			}
			if (RedHeaderImprintPagePlanner.ResolveAdvancePages(num, currentY, plan) <= 0)
			{
				return;
			}
			int num2 = num;
			int num3 = 0;
			bool flag = false;
			for (int i = 0; i < 2; i++)
			{
				int start = value.Start;
				Microsoft.Office.Interop.Word.Range range = value;
				End = WdBreakType.wdPageBreak;
				range.InsertBreak(ref End);
				num3++;
				RedHeaderGeneratedObjectIdentityService.RegisterGeneratedBreak(document, start, plan, num3);
				ComObjectRelease.Release(ref value, "RedHeaderGenerationService.endRange");
				try
				{
					document.Repaginate();
				}
				catch
				{
				}
				End = document.Content.End - 1;
				Start = document.Content.End - 1;
				value = document.Range(ref End, ref Start);
				num = WordPageOrdinalService.ReadPhysicalPageOrdinal(value, "版记分页后位置");
				currentY = Convert.ToSingle((dynamic)value.get_Information(WdInformation.wdVerticalPositionRelativeToPage));
				bool flag2 = currentY <= plan.ImprintTargetY;
				bool flag3 = !plan.TemplateForExecution.ImprintOnEvenPage || num % 2 == 0;
				flag = num > num2 && flag2 && flag3;
				LogService.Info("套红自适应分页：初始页=" + num2 + "，当前页=" + num + "，实际分页次数=" + num3 + "，空间满足=" + flag2 + "，奇偶满足=" + flag3);
				if (flag)
				{
					break;
				}
			}
			if (!flag)
			{
				ReportQualityWarning("redheader-imprint-page-target-unmet", "imprint");
			}
		}
		catch (Exception ex2)
		{
			LogService.Warn("RedHeaderGenerationService.PrepareImprintPage", ex2);
			ReportQualityWarning("redheader-imprint-page-indeterminate", "imprint");
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "RedHeaderGenerationService.endRange");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void CompressBlankParagraphAfterTable(Document document, Table table)
	{
		Microsoft.Office.Interop.Word.Range value = null;
		Microsoft.Office.Interop.Word.Range value2 = null;
		Microsoft.Office.Interop.Word.Range value3 = null;
		try
		{
			if (document != null && table != null)
			{
				value = table.Range;
				value2 = document.Content;
				int end = value.End;
				int end2 = value2.End;
				if (end < end2)
				{
					object Start = end;
					object End = end2;
					value3 = document.Range(ref Start, ref End);
					value3.Font.Size = 1f;
					value3.Font.Hidden = 1;
					value3.ParagraphFormat.LineSpacingRule = WdLineSpacing.wdLineSpaceExactly;
					value3.ParagraphFormat.LineSpacing = 1f;
					value3.ParagraphFormat.SpaceBefore = 0f;
					value3.ParagraphFormat.SpaceAfter = 0f;
					value3.ParagraphFormat.FirstLineIndent = 0f;
					value3.ParagraphFormat.CharacterUnitFirstLineIndent = 0f;
				}
			}
		}
		catch
		{
		}
		finally
		{
			if (value3 != null)
			{
				ComObjectRelease.Release(ref value3, "RedHeaderGenerationService.tail");
			}
			ComObjectRelease.Release(ref value2, "RedHeaderGenerationService.CompressDocumentRange");
			ComObjectRelease.Release(ref value, "RedHeaderGenerationService.CompressTableRange");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void StabilizeImprintLayout(Document document, Table table, RedHeaderLayoutPlan plan)
	{
		bool flag = false;
		bool flag2 = false;
		for (int i = 0; i < 2; i++)
		{
			Microsoft.Office.Interop.Word.Range value = null;
			Microsoft.Office.Interop.Word.Range value2 = null;
			Microsoft.Office.Interop.Word.Range value3 = null;
			try
			{
				PositionImprintTableAtBottom(table);
				CompressBlankParagraphAfterTable(document, table);
				try
				{
					document.Repaginate();
				}
				catch
				{
				}
				value = table.Range;
				object Start = value.Start;
				object End = value.Start;
				value2 = document.Range(ref Start, ref End);
				int num = WordPageOrdinalService.ReadPhysicalPageOrdinal(value2, "版记稳定定位");
				End = Type.Missing;
				int num2 = document.ComputeStatistics(WdStatistic.wdStatisticPages, ref End);
				flag = num == num2;
				flag2 = !plan.TemplateForExecution.ImprintOnEvenPage || num % 2 == 0;
				if (flag && flag2)
				{
					return;
				}
				if (!flag2)
				{
					int start = value.Start;
					End = start;
					Start = start;
					value3 = document.Range(ref End, ref Start);
					Microsoft.Office.Interop.Word.Range range = value3;
					Start = WdBreakType.wdPageBreak;
					range.InsertBreak(ref Start);
					RedHeaderGeneratedObjectIdentityService.RegisterGeneratedBreak(document, start, plan, 10 + i);
					RedHeaderGeneratedObjectIdentityService.RegisterImprintTable(document, table, plan);
				}
			}
			catch (Exception ex)
			{
				LogService.Warn("RedHeaderGenerationService.StabilizeImprintLayout, round=" + (i + 1), ex);
				break;
			}
			finally
			{
				ComObjectRelease.Release(ref value3, "RedHeaderGenerationService.StabilizeBreak");
				ComObjectRelease.Release(ref value2, "RedHeaderGenerationService.StabilizeAnchor");
				ComObjectRelease.Release(ref value, "RedHeaderGenerationService.StabilizeTableRange");
			}
		}
		if (!flag)
		{
			ReportQualityWarning("redheader-imprint-not-final-page", "imprint");
		}
		if (!flag2)
		{
			ReportQualityWarning("redheader-imprint-even-page-unmet", "imprint");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static bool RewriteImprintCellForCorrection(Document document, Table table, RedHeaderLayoutPlan plan, string stage)
	{
		Cell value = null;
		Microsoft.Office.Interop.Word.Range value2 = null;
		try
		{
			bool num = string.Equals(stage, "office", StringComparison.Ordinal);
			int row = ((!num) ? 1 : plan.ImprintOfficeRow);
			string text = (num ? plan.ImprintOfficeLine : plan.ImprintSendText);
			value = table.Cell(row, 1);
			value2 = value.Range;
			value2.Text = text ?? string.Empty;
			if (num)
			{
				ApplyImprintOfficeParagraph(value2, plan.UsableWidth, plan.ImprintFontSize);
			}
			else
			{
				ApplyImprintSendParagraph(value2, plan.ImprintFontSize);
			}
			RedHeaderTemplate templateForExecution = plan.TemplateForExecution;
			SetFont(value2, templateForExecution.ImprintFont, plan.ImprintFontSize, bold: false, 0);
			if (num)
			{
				ApplyTimesNewRomanToArabicNumbers(document, value2, plan.ResolvedImprintDate ?? string.Empty, (templateForExecution.ImprintOffice ?? string.Empty).Length + 1, templateForExecution.UseTimesNewRomanForNumbers, "imprintDateCorrection");
			}
			RedHeaderGeneratedObjectIdentityService.RegisterImprintTable(document, table, plan);
			return true;
		}
		catch (Exception ex)
		{
			LogService.Warn("RedHeaderGenerationService.RewriteImprintCellForCorrection, stage=" + (string.Equals(stage, "office", StringComparison.Ordinal) ? "office" : "send"), ex);
			return false;
		}
		finally
		{
			ComObjectRelease.Release(ref value2, "RedHeaderGenerationService.CorrectionRange");
			ComObjectRelease.Release(ref value, "RedHeaderGenerationService.CorrectionCell");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static bool TryCorrectRedLinePlacement(Document document, RedHeaderLayoutPlan plan)
	{
		Shapes value = null;
		Shape value2 = null;
		try
		{
			value = document.Shapes;
			int num = 1;
			while (true)
			{
				if (num > plan.ExpectedAddedShapes)
				{
					return true;
				}
				value2 = FindShapeByName(value, ShapeName(plan, num));
				if (value2 != null)
				{
					ApplyRedLineGeometry(value2, plan, num);
					ComObjectRelease.Release(ref value2, "RedHeaderGenerationService.CorrectedShape");
					num++;
					continue;
				}
				break;
			}
			return false;
		}
		catch (Exception ex)
		{
			LogService.Warn("RedHeaderGenerationService.TryCorrectRedLinePlacement", ex);
			return false;
		}
		finally
		{
			ComObjectRelease.Release(ref value2, "RedHeaderGenerationService.CorrectedShape.Finally");
			ComObjectRelease.Release(ref value, "RedHeaderGenerationService.CorrectedShapes");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ApplyRedLineGeometry(Shape shape, RedHeaderLayoutPlan plan, int ordinal)
	{
		if (shape == null)
		{
			throw new ArgumentNullException("shape");
		}
		float redLineAnchorX = plan.RedLineAnchorX1;
		float redLineAnchorX2 = plan.RedLineAnchorX2;
		float redLineAnchorTop = plan.RedLineAnchorTop;
		float num = redLineAnchorX;
		float top = redLineAnchorTop;
		float width = redLineAnchorX2 - redLineAnchorX;
		bool flag = false;
		switch (plan.NormalizedRedLineStyle)
		{
		case "singleBottom":
			top = redLineAnchorTop + 2f;
			break;
		case "singleTop":
			top = redLineAnchorTop - 2f;
			break;
		case "upperThickLowerThin":
		case "lowerThickUpperThin":
			top = redLineAnchorTop + ((ordinal == 1) ? (-1.8f) : 1.8f);
			break;
		case "star":
		{
			float num2 = (redLineAnchorX + redLineAnchorX2) / 2f;
			if (ordinal != 1)
			{
				if (ordinal == 2)
				{
					num = num2 + 10f;
					width = redLineAnchorX2 - num;
					break;
				}
				num = num2 - 5.5f;
				top = redLineAnchorTop - 5.5f;
				width = 11f;
				flag = true;
			}
			else
			{
				width = num2 - 10f - redLineAnchorX;
			}
			break;
		}
		}
		shape.RelativeHorizontalPosition = WdRelativeHorizontalPosition.wdRelativeHorizontalPositionColumn;
		shape.RelativeVerticalPosition = WdRelativeVerticalPosition.wdRelativeVerticalPositionParagraph;
		shape.Left = num;
		shape.Top = top;
		shape.Width = width;
		if (flag)
		{
			shape.Height = 11f;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string ShapeName(RedHeaderLayoutPlan plan, int ordinal)
	{
		return plan.RedLineShapeNamePrefix + "_" + ordinal;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static Shape FindShapeByName(Shapes shapes, string name)
	{
		if (shapes == null)
		{
			return null;
		}
		for (int i = 1; i <= shapes.Count; i++)
		{
			Shape value = null;
			try
			{
				object Index = i;
				value = shapes.get_Item(ref Index);
				if (!string.Equals(value.Name, name, StringComparison.Ordinal))
				{
					continue;
				}
				Shape result = value;
				value = null;
				return result;
			}
			finally
			{
				ComObjectRelease.Release(ref value, "RedHeaderGenerationService.ShapeCandidate");
			}
		}
		return null;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ReportQualityWarning(string code, string stage)
	{
		LogService.Warn("REDHEADER-QUALITY warning, code=" + code + ", stage=" + stage);
		ExecutionWarningCollector.Report(code, "redheader", "warn.redheader.quality");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool HeaderUsesDistributedAlignment(RedHeaderTemplate template)
	{
		string text = template.HeaderAlignment ?? "";
		if (!(text == "分散对齐"))
		{
			return text == "两端对齐";
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WdParagraphAlignment ToParagraphAlignment(string value)
	{
		return value switch
		{
			"两端对齐" => WdParagraphAlignment.wdAlignParagraphJustify, 
			"分散对齐" => WdParagraphAlignment.wdAlignParagraphDistribute, 
			"右对齐" => WdParagraphAlignment.wdAlignParagraphRight, 
			"左对齐" => WdParagraphAlignment.wdAlignParagraphLeft, 
			_ => WdParagraphAlignment.wdAlignParagraphCenter, 
		};
	}
}
