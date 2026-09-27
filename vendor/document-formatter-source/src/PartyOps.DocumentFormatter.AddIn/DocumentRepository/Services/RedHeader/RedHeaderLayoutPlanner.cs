using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using DocumentRepository.Models.RedHeader;

namespace DocumentRepository.Services.RedHeader;

public static class RedHeaderLayoutPlanner
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static RedHeaderLayoutPlan Create(RedHeaderTemplate template, FormatConfig config, RedHeaderAnalysisSnapshot source, DateTime taskDate)
	{
		if (template != null)
		{
			if (config == null)
			{
				throw new ArgumentNullException("config");
			}
			if (source == null || source.DocumentSnapshot == null)
			{
				throw new ArgumentNullException("source");
			}
			RedHeaderLayoutPlan redHeaderLayoutPlan = new RedHeaderLayoutPlan
			{
				PlanId = Guid.NewGuid().ToString("N"),
				RuleCatalogId = "redheader.templates",
				RuleSchemaVersion = 1,
				Source = source,
				Template = template,
				Config = config,
				TaskDate = taskDate
			};
			RedHeaderTemplate templateForPlanning = redHeaderLayoutPlan.TemplateForPlanning;
			RedHeaderAnalysisSnapshot sourceForPlanning = redHeaderLayoutPlan.SourceForPlanning;
			redHeaderLayoutPlan.RuleContentHash = RedHeaderTemplateService.ComputeRuleContentHash(templateForPlanning);
			string text = redHeaderLayoutPlan.PlanId.Substring(0, 24);
			redHeaderLayoutPlan.SourceBodyBookmarkName = "SXBody_" + text;
			redHeaderLayoutPlan.ImprintBookmarkName = "SXImprint_" + text;
			redHeaderLayoutPlan.GeneratedBreakBookmarkPrefix = "SXBreak_" + text;
			redHeaderLayoutPlan.RedLineShapeNamePrefix = "SXRedLine_" + text;
			redHeaderLayoutPlan.HeaderText = RequiredText(templateForPlanning.HeaderText, "HeaderText");
			redHeaderLayoutPlan.DocumentNumberText = templateForPlanning.DocumentNumberText ?? "";
			redHeaderLayoutPlan.HasDocumentNumber = redHeaderLayoutPlan.DocumentNumberText.Trim().Length > 0;
			redHeaderLayoutPlan.TitleGapLines = Clamp(templateForPlanning.TitleGapLines, 0, 5);
			redHeaderLayoutPlan.TopMarkLines = BuildTopMarkLines(templateForPlanning.TopMarks);
			BuildGeneratedHeaderText(redHeaderLayoutPlan);
			redHeaderLayoutPlan.ImprintEnabled = templateForPlanning.ImprintEnabled;
			redHeaderLayoutPlan.ImprintSendText = TrimText(templateForPlanning.ImprintSend);
			redHeaderLayoutPlan.HasImprintSend = redHeaderLayoutPlan.ImprintSendText.Length > 0;
			redHeaderLayoutPlan.ImprintRowCount = ((!redHeaderLayoutPlan.HasImprintSend) ? 1 : 2);
			redHeaderLayoutPlan.ImprintOfficeRow = ((!redHeaderLayoutPlan.HasImprintSend) ? 1 : 2);
			redHeaderLayoutPlan.ResolvedImprintDate = ((templateForPlanning.ImprintDateMode == RedHeaderImprintDateMode.AutoToday) ? taskDate.ToString("yyyy年M月d日印发") : TrimText(templateForPlanning.ImprintDate));
			redHeaderLayoutPlan.ImprintOfficeLine = templateForPlanning.ImprintOffice + "\t" + redHeaderLayoutPlan.ResolvedImprintDate;
			RedHeaderTemplateValidator.Validate(templateForPlanning);
			BuildGeometry(redHeaderLayoutPlan, templateForPlanning, sourceForPlanning);
			redHeaderLayoutPlan.Seal();
			return redHeaderLayoutPlan;
		}
		throw new ArgumentNullException("template");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void BuildGeometry(RedHeaderLayoutPlan plan, RedHeaderTemplate template, RedHeaderAnalysisSnapshot source)
	{
		float num = source.FirstPageWidth - source.FirstLeftMargin - source.FirstRightMargin;
		if (!(num > 0f))
		{
			throw new InvalidOperationException("套红计划计算到的版心宽度无效。");
		}
		float num2 = RequirePercent(template.HeaderLayoutWidthPercent, "HeaderLayoutWidthPercent");
		float num3 = num * num2 / 100f;
		plan.UsableWidth = num;
		plan.HeaderSideIndent = (num - num3) / 2f;
		float num4 = RequirePercent(template.RedLineWidthPercent, "RedLineWidthPercent");
		float num5 = num * num4 / 100f;
		plan.RedLineAnchorX1 = (num - num5) / 2f;
		plan.RedLineAnchorX2 = plan.RedLineAnchorX1 + num5;
		plan.RedLineAnchorTop = 6f;
		plan.NormalizedRedLineStyle = NormalizeRedLineStyle(template.RedLineStyle);
		plan.RedLineWeight = ResolveLineWeight(template, plan.NormalizedRedLineStyle);
		plan.ExpectedAddedShapes = ResolveShapeCount(plan.NormalizedRedLineStyle);
		plan.ImprintFontSize = FontSizeHelper.ToPoints(template.ImprintSize);
		plan.ImprintRowHeight = Clamp(Math.Max(18f, plan.ImprintFontSize + 4f), 18f, 32f);
		plan.ImprintCellPaddingPoints = Clamp(template.ImprintCellPaddingCm, 0f, 1f) * 28.346457f;
		float num6 = ResolveImprintReserve(plan);
		plan.ImprintTargetY = source.LastPageHeight - source.LastBottomMargin - num6 + Clamp(template.ImprintBottomOffset, -36f, 36f);
		if (plan.ImprintEnabled && plan.ImprintTargetY <= 0f)
		{
			throw new InvalidOperationException("套红计划计算到的版记纵坐标无效。");
		}
		plan.ExpectedAddedTables = (plan.ImprintEnabled ? 1 : 0);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static List<RedHeaderTopMarkLine> BuildTopMarkLines(RedHeaderTopMarkOptions options)
	{
		List<RedHeaderTopMarkLine> list = new List<RedHeaderTopMarkLine>();
		options = options ?? new RedHeaderTopMarkOptions();
		string text = RedHeaderTemplateValidator.NormalizeCopyNumber(options.CopyNumber);
		if (options.CopyNumberEnabled && text.Length > 0)
		{
			list.Add(new RedHeaderTopMarkLine("copyNumber", text));
		}
		if (!string.Equals(options.SecurityLevel, "无", StringComparison.Ordinal))
		{
			string text2 = options.SecurityLevel;
			string text3 = TrimText(options.ConfidentialityPeriod);
			if (text3.Length > 0)
			{
				text2 = text2 + "★" + text3;
			}
			list.Add(new RedHeaderTopMarkLine("security", text2));
		}
		if (!string.Equals(options.UrgencyLevel, "无", StringComparison.Ordinal))
		{
			list.Add(new RedHeaderTopMarkLine("urgency", options.UrgencyLevel));
		}
		return list;
	}

	private static void BuildGeneratedHeaderText(RedHeaderLayoutPlan plan)
	{
		StringBuilder stringBuilder = new StringBuilder();
		for (int i = 0; i < plan.TopMarkLines.Count; i++)
		{
			stringBuilder.Append(plan.TopMarkLines[i].Text).Append('\r');
		}
		stringBuilder.Append(plan.HeaderText).Append('\r');
		if (plan.HasDocumentNumber)
		{
			stringBuilder.Append(plan.DocumentNumberText).Append('\r');
		}
		plan.ExpectedPrefixText = stringBuilder.ToString();
		plan.HeaderParagraphIndex = plan.TopMarkLines.Count + 1;
		plan.DocumentNumberParagraphIndex = (plan.HasDocumentNumber ? (plan.HeaderParagraphIndex + 1) : 0);
		plan.RedLineParagraphIndex = plan.HeaderParagraphIndex + ((!plan.HasDocumentNumber) ? 1 : 2);
		StringBuilder stringBuilder2 = new StringBuilder(plan.ExpectedPrefixText);
		stringBuilder2.Append('\r');
		for (int j = 0; j < plan.TitleGapLines; j++)
		{
			stringBuilder2.Append('\r');
		}
		plan.GeneratedHeaderText = stringBuilder2.ToString();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string RequiredText(string value, string fieldName)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw new InvalidOperationException("Red header template field is required: " + fieldName);
		}
		return value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static float RequirePercent(float value, string fieldName)
	{
		if (value < 20f || !(value <= 200f))
		{
			throw new InvalidOperationException("Red header template percentage is outside 20-200: " + fieldName);
		}
		return value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string NormalizeRedLineStyle(string value)
	{
		switch (value)
		{
		case "double":
			return "upperThickLowerThin";
		case "star":
		case "upperThickLowerThin":
		case "lowerThickUpperThin":
		case "singleTop":
		case "singleBottom":
			return value;
		case "thick":
			return "lowerThickUpperThin";
		default:
			if (string.IsNullOrWhiteSpace(value) || value == "normal")
			{
				return "normal";
			}
			throw new InvalidOperationException("Unknown red line style: " + value);
		case "solid":
			return "normal";
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static float ResolveLineWeight(RedHeaderTemplate template, string style)
	{
		if (style == "upperThickLowerThin" || style == "lowerThickUpperThin")
		{
			return Math.Max(2f, template.RedLineThickness);
		}
		return Math.Max(0.75f, template.RedLineThickness);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ResolveShapeCount(string style)
	{
		switch (style)
		{
		case "star":
			return 3;
		case "upperThickLowerThin":
		case "lowerThickUpperThin":
			return 2;
		default:
			return 1;
		}
	}

	private static float ResolveImprintReserve(RedHeaderLayoutPlan plan)
	{
		int num = ((!plan.HasImprintSend) ? 1 : 2);
		float num2 = ((num == 1) ? 7f : 10f);
		float num3 = plan.ImprintCellPaddingPoints * 2f * (float)num;
		return Clamp(plan.ImprintRowHeight * (float)num + num3 + num2, (num == 1) ? 24f : 44f, (num == 1) ? 56f : 84f);
	}

	private static string TrimText(string value)
	{
		return (value ?? "").Trim();
	}

	private static int Clamp(int value, int min, int max)
	{
		return Math.Max(min, Math.Min(max, value));
	}

	private static float Clamp(float value, float min, float max)
	{
		return Math.Max(min, Math.Min(max, value));
	}
}
