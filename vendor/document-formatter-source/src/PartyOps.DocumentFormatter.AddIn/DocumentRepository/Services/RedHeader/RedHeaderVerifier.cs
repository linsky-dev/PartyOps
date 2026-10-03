using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Mutations;
using DocumentRepository.Models.RedHeader;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Interop;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.Safety;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.RedHeader;

public static class RedHeaderVerifier
{
	internal static Func<string, string, string> ImprintReadTransformForTesting;

	internal static Action<Document, RedHeaderLayoutPlan> BeforeGeneratedObjectVerificationForTesting;

	internal static Action<Document, RedHeaderLayoutPlan> BeforeFinalSourceIntegrityForTesting;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static VerificationReceipt Verify(string taskId, Document document, RedHeaderLayoutPlan plan, IEnumerable<VerificationFinding> warnings = null)
	{
		if (document != null)
		{
			if (plan != null)
			{
				plan.EnsureSealed();
				RedHeaderAnalysisSnapshot sourceForExecution = plan.SourceForExecution;
				if (sourceForExecution != null && sourceForExecution.DocumentSnapshot != null)
				{
					List<string> list = new List<string> { "source-body-integrity", "red-header-prefix", "header-text-layout", "object-counts", "red-line-placement" };
					VerifySourceBodyIntegrity(document, plan);
					TryQuality(delegate
					{
						VerifyPrefix(document, plan);
					}, "redheader-prefix-quality", "header");
					TryQuality(delegate
					{
						VerifyHeaderTextLayout(document, plan);
					}, "redheader-header-layout", "header");
					BeforeGeneratedObjectVerificationForTesting?.Invoke(document, plan);
					VerifyObjectCounts(document, plan);
					VerifyRedLinePlacement(document, plan);
					if (plan.ImprintEnabled)
					{
						VerifyImprint(document, plan);
						list.Add("imprint");
					}
					BeforeFinalSourceIntegrityForTesting?.Invoke(document, plan);
					VerifySourceBodyIntegrity(document, plan);
					return new VerificationReceipt(taskId, plan.PlanId, sourceForExecution.DocumentSnapshot.SnapshotId, list, MergeWarnings(warnings, ExecutionWarningCollector.Snapshot()), plan.RuleContentHash);
				}
				throw new InvalidOperationException("套红验证缺少源快照或执行计划。");
			}
			throw new InvalidOperationException("套红验证缺少源快照或执行计划。");
		}
		throw new ArgumentNullException("document");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void VerifySourceBodyIntegrity(Document document, RedHeaderLayoutPlan plan)
	{
		RedHeaderAnalysisSnapshot sourceForExecution = plan.SourceForExecution;
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			value = document.Content;
			string text = ((value == null) ? string.Empty : (value.Text ?? string.Empty));
			int num = sourceForExecution.DocumentSnapshot.ScopeStart + plan.GeneratedHeaderText.Length;
			int num2 = Math.Max(0, num - 4);
			int num3 = Math.Min(text.Length - sourceForExecution.SourceBodyLength, num + 16);
			bool flag = false;
			int num4 = -1;
			for (int i = num2; i <= num3; i++)
			{
				if (string.Equals(RedHeaderAnalysisService.Hash(text.Substring(i, sourceForExecution.SourceBodyLength)), sourceForExecution.SourceBodyHash, StringComparison.Ordinal))
				{
					flag = true;
					num4 = i;
					break;
				}
			}
			if (!flag)
			{
				LogService.Warn("REDHEADER-SAFETY source-body-difference, expectedLength=" + sourceForExecution.SourceBodyLength + ", generatedLength=" + plan.GeneratedHeaderText.Length + ", sourceStart=" + sourceForExecution.DocumentSnapshot.ScopeStart + ", sourceEnd=" + sourceForExecution.DocumentSnapshot.ScopeEnd + ", candidateLower=" + num2 + ", candidateUpper=" + num3);
				throw RedHeaderOperationException.Create(RedHeaderFailureReasonCode.OriginalTextChanged, RedHeaderFailureStage.Verify);
			}
			LogService.Info("REDHEADER-SAFETY source-body-verified, length=" + sourceForExecution.SourceBodyLength + ", relativeStart=" + (num4 - num));
		}
		finally
		{
			ComObjectRelease.Release(ref value, "RedHeaderVerifier.SourceContent");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void VerifyPrefix(Document document, RedHeaderLayoutPlan plan)
	{
		string expectedPrefixText = plan.ExpectedPrefixText;
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			int scopeStart = plan.SourceForExecution.DocumentSnapshot.ScopeStart;
			object Start = scopeStart;
			object End = scopeStart + expectedPrefixText.Length;
			value = document.Range(ref Start, ref End);
			if (!string.Equals(value.Text ?? string.Empty, expectedPrefixText, StringComparison.Ordinal))
			{
				throw new InvalidOperationException("红头文字或发文字号与执行计划不一致。");
			}
		}
		finally
		{
			ComObjectRelease.Release(ref value, "RedHeaderVerifier.Prefix");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void VerifyHeaderTextLayout(Document document, RedHeaderLayoutPlan plan)
	{
		Microsoft.Office.Interop.Word.Range value = null;
		Paragraph value2 = null;
		Microsoft.Office.Interop.Word.Range value3 = null;
		Microsoft.Office.Interop.Word.Range value4 = null;
		try
		{
			int scopeStart = plan.SourceForExecution.DocumentSnapshot.ScopeStart;
			object Start = scopeStart;
			object End = scopeStart + plan.GeneratedHeaderText.Length;
			value = document.Range(ref Start, ref End);
			value2 = value.Paragraphs[plan.HeaderParagraphIndex];
			value3 = value2.Range;
			int length = (value3.Text ?? string.Empty).TrimEnd('\r', '\a').Length;
			if (length > 0)
			{
				End = value3.Start;
				Start = value3.Start + length;
				value4 = document.Range(ref End, ref Start);
				int num = Convert.ToInt32(value4.Font.Scaling);
				RedHeaderTemplate templateForExecution = plan.TemplateForExecution;
				if (!(templateForExecution.HeaderFitMode == "autoSingleLine") || (num >= Convert.ToInt32(Math.Round(templateForExecution.HeaderMinimumScalePercent)) && num <= Convert.ToInt32(Math.Round(templateForExecution.HeaderCharacterScalePercent))))
				{
					if (templateForExecution.HeaderFitMode == "manual" || templateForExecution.HeaderFitMode == "allowWrap")
					{
						int num2 = Convert.ToInt32(Math.Round(templateForExecution.HeaderCharacterScalePercent));
						if (Math.Abs(num - num2) > 1)
						{
							throw new InvalidOperationException("发文机关字符缩放未按模板应用。");
						}
					}
					if (templateForExecution.HeaderFitMode == "autoSingleLine" && value4.ComputeStatistics(WdStatistic.wdStatisticLines) != 1)
					{
						throw new InvalidOperationException("发文机关未能保持单行。");
					}
					return;
				}
				throw new InvalidOperationException("发文机关字符缩放超出模板范围。");
			}
			throw new InvalidOperationException("发文机关可见文字为空。");
		}
		finally
		{
			ComObjectRelease.Release(ref value4, "RedHeaderVerifier.HeaderVisibleRange");
			ComObjectRelease.Release(ref value3, "RedHeaderVerifier.HeaderParagraphRange");
			ComObjectRelease.Release(ref value2, "RedHeaderVerifier.HeaderParagraph");
			ComObjectRelease.Release(ref value, "RedHeaderVerifier.GeneratedHeaderRange");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void VerifyObjectCounts(Document document, RedHeaderLayoutPlan plan)
	{
		int shapes = plan.SourceForExecution.DocumentSnapshot.ProtectedObjects.Shapes;
		int tables = plan.SourceForExecution.DocumentSnapshot.ProtectedObjects.Tables;
		Shapes value = null;
		Tables value2 = null;
		Table value3 = null;
		try
		{
			value = document.Shapes;
			value2 = document.Tables;
			int num = value?.Count ?? 0;
			int num2 = value2?.Count ?? 0;
			int num3 = CountGeneratedRedLineShapes(value, plan);
			value3 = RedHeaderGeneratedObjectIdentityService.ResolveImprintTable(document, plan);
			int num4 = ((value3 != null) ? 1 : 0);
			int num5 = num - num3;
			int num6 = num2 - num4;
			if (num5 < shapes)
			{
				throw RedHeaderOperationException.Create(RedHeaderFailureReasonCode.OriginalObjectReduced, RedHeaderFailureStage.Verify);
			}
			if (num6 >= tables)
			{
				if (num3 != plan.ExpectedAddedShapes || num5 != shapes)
				{
					ReportQualityWarning("redheader-redline-count", "redline");
				}
				if (num4 != plan.ExpectedAddedTables || num6 != tables)
				{
					ReportQualityWarning("redheader-imprint-count", "imprint");
				}
				return;
			}
			throw RedHeaderOperationException.Create(RedHeaderFailureReasonCode.OriginalObjectReduced, RedHeaderFailureStage.Verify);
		}
		finally
		{
			ComObjectRelease.Release(ref value3, "RedHeaderVerifier.ObjectCountImprint");
			ComObjectRelease.Release(ref value2, "RedHeaderVerifier.ObjectCountTables");
			ComObjectRelease.Release(ref value, "RedHeaderVerifier.ObjectCountShapes");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int CountGeneratedRedLineShapes(Shapes shapes, RedHeaderLayoutPlan plan)
	{
		if (shapes == null)
		{
			return 0;
		}
		int num = 0;
		for (int i = 1; i <= shapes.Count; i++)
		{
			Shape value = null;
			try
			{
				object Index = i;
				value = shapes.get_Item(ref Index);
				if ((value.Name ?? string.Empty).StartsWith(plan.RedLineShapeNamePrefix + "_", StringComparison.Ordinal))
				{
					num++;
				}
			}
			finally
			{
				ComObjectRelease.Release(ref value, "RedHeaderVerifier.CountShape");
			}
		}
		return num;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void VerifyImprint(Document document, RedHeaderLayoutPlan plan)
	{
		Table value = null;
		try
		{
			value = RedHeaderGeneratedObjectIdentityService.ResolveImprintTable(document, plan);
			if (value != null)
			{
				if (plan.HasImprintSend)
				{
					VerifyAndRepairImprintCell(document, value, plan, "send", 1, plan.ImprintSendText);
				}
				VerifyAndRepairImprintCell(document, value, plan, "office", plan.ImprintOfficeRow, plan.ImprintOfficeLine);
				VerifyImprintGeometry(document, value, plan);
			}
			else
			{
				ReportQualityWarning("redheader-imprint-unresolved", "imprint");
			}
		}
		catch (Exception ex)
		{
			LogService.Warn("RedHeaderVerifier.VerifyImprint", ex);
			ReportQualityWarning("redheader-imprint-quality", "imprint");
		}
		finally
		{
			ComObjectRelease.Release(ref value, "RedHeaderVerifier.ImprintTable");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void VerifyAndRepairImprintCell(Document document, Table table, RedHeaderLayoutPlan plan, string stage, int row, string expectedRaw)
	{
		string text = RedHeaderGeneratedObjectIdentityService.NormalizeCellText(expectedRaw);
		string text2 = TransformImprintReadForTesting(stage, RedHeaderGeneratedObjectIdentityService.ReadNormalizedCell(table, row, 1));
		if (string.Equals(text, text2, StringComparison.Ordinal))
		{
			return;
		}
		RedHeaderGeneratedObjectIdentityService.LogCellDifference(stage, text, text2, 0, 0);
		try
		{
			document.Repaginate();
		}
		catch
		{
		}
		text2 = TransformImprintReadForTesting(stage, RedHeaderGeneratedObjectIdentityService.ReadNormalizedCell(table, row, 1));
		if (string.Equals(text, text2, StringComparison.Ordinal))
		{
			return;
		}
		RedHeaderGeneratedObjectIdentityService.LogCellDifference(stage, text, text2, 1, 0);
		bool flag = RedHeaderGenerationService.RewriteImprintCellForCorrection(document, table, plan, stage);
		if (flag)
		{
			text2 = TransformImprintReadForTesting(stage, RedHeaderGeneratedObjectIdentityService.ReadNormalizedCell(table, row, 1));
			if (string.Equals(text, text2, StringComparison.Ordinal))
			{
				return;
			}
		}
		RedHeaderGeneratedObjectIdentityService.LogCellDifference(stage, text, text2, 1, flag ? 1 : 0);
		ReportQualityWarning("redheader-imprint-" + stage + "-mismatch", "imprint");
	}

	private static string TransformImprintReadForTesting(string stage, string actual)
	{
		Func<string, string, string> imprintReadTransformForTesting = ImprintReadTransformForTesting;
		if (imprintReadTransformForTesting != null)
		{
			return imprintReadTransformForTesting(stage, actual);
		}
		return actual;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void VerifyImprintGeometry(Document document, Table table, RedHeaderLayoutPlan plan)
	{
		try
		{
			if (table.Rows.WrapAroundText == 0 || table.Rows.RelativeHorizontalPosition != WdRelativeHorizontalPosition.wdRelativeHorizontalPositionColumn || table.Rows.RelativeVerticalPosition != WdRelativeVerticalPosition.wdRelativeVerticalPositionMargin || !(Math.Abs(Convert.ToSingle(table.Rows.HorizontalPosition) - -999995f) <= 0.5f) || !(Math.Abs(Convert.ToSingle(table.Rows.VerticalPosition) - -999997f) <= 0.5f))
			{
				ReportQualityWarning("redheader-imprint-bottom-position", "imprint");
			}
		}
		catch (Exception ex)
		{
			LogService.Warn("RedHeaderVerifier.ImprintGeometry", ex);
			ReportQualityWarning("redheader-imprint-geometry-indeterminate", "imprint");
		}
		Microsoft.Office.Interop.Word.Range value = null;
		Microsoft.Office.Interop.Word.Range value2 = null;
		try
		{
			value = table.Range;
			object Start = value.Start;
			object End = value.Start;
			value2 = document.Range(ref Start, ref End);
			End = Type.Missing;
			int num = document.ComputeStatistics(WdStatistic.wdStatisticPages, ref End);
			int num2 = WordPageOrdinalService.ReadPhysicalPageOrdinal(value2, "版记表格锚点");
			if (num2 != num)
			{
				ReportQualityWarning("redheader-imprint-not-final-page", "imprint");
			}
			if (plan.TemplateForExecution.ImprintOnEvenPage && num2 % 2 != 0)
			{
				ReportQualityWarning("redheader-imprint-even-page-unmet", "imprint");
			}
		}
		catch (Exception ex2)
		{
			LogService.Warn("RedHeaderVerifier.ImprintPage", ex2);
			ReportQualityWarning("redheader-imprint-page-indeterminate", "imprint");
		}
		finally
		{
			ComObjectRelease.Release(ref value2, "RedHeaderVerifier.ImprintAnchor");
			ComObjectRelease.Release(ref value, "RedHeaderVerifier.ImprintRange");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void VerifyRedLinePlacement(Document document, RedHeaderLayoutPlan plan)
	{
		if (TryVerifyRedLinePlacement(document, plan))
		{
			return;
		}
		if (RedHeaderGenerationService.TryCorrectRedLinePlacement(document, plan))
		{
			try
			{
				document.Repaginate();
			}
			catch
			{
			}
			if (TryVerifyRedLinePlacement(document, plan))
			{
				return;
			}
		}
		ReportQualityWarning("redheader-redline-placement", "redline");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool TryVerifyRedLinePlacement(Document document, RedHeaderLayoutPlan plan)
	{
		Shapes value = null;
		Shape value2 = null;
		Microsoft.Office.Interop.Word.Range value3 = null;
		try
		{
			value = document.Shapes;
			int num = 1;
			while (true)
			{
				if (num <= plan.ExpectedAddedShapes)
				{
					value2 = FindShapeByName(value, plan.RedLineShapeNamePrefix + "_" + num);
					if (value2 != null)
					{
						value3 = value2.Anchor;
						int num2 = WordPageOrdinalService.ReadPhysicalPageOrdinal(value3, "红线锚点");
						float num3 = Convert.ToSingle(value2.Top);
						if (num2 != 1)
						{
							return false;
						}
						VerifyShapeGeometry(value2, num3, plan);
						if (value2.RelativeHorizontalPosition != WdRelativeHorizontalPosition.wdRelativeHorizontalPositionColumn || value2.RelativeVerticalPosition != WdRelativeVerticalPosition.wdRelativeVerticalPositionParagraph)
						{
							break;
						}
						if (Math.Abs(num3 - plan.RedLineAnchorTop) <= 16f)
						{
							ComObjectRelease.Release(ref value3, "RedHeaderVerifier.RedLineAnchor");
							ComObjectRelease.Release(ref value2, "RedHeaderVerifier.RedLineShape");
							num++;
							continue;
						}
						return false;
					}
					return false;
				}
				return true;
			}
			return false;
		}
		catch (Exception ex)
		{
			LogService.Warn("RedHeaderVerifier.RedLinePlacement", ex);
			return false;
		}
		finally
		{
			ComObjectRelease.Release(ref value3, "RedHeaderVerifier.RedLineAnchor.Finally");
			ComObjectRelease.Release(ref value2, "RedHeaderVerifier.RedLineShape.Finally");
			ComObjectRelease.Release(ref value, "RedHeaderVerifier.RedLineShapes");
		}
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
				ComObjectRelease.Release(ref value, "RedHeaderVerifier.ShapeCandidate");
			}
		}
		return null;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void VerifyShapeGeometry(Shape shape, float top, RedHeaderLayoutPlan plan)
	{
		float num = Convert.ToSingle(shape.Left);
		float num2 = Math.Abs(Convert.ToSingle(shape.Width));
		float num3 = num + num2;
		if (!IsFinite(num) || !IsFinite(top) || !IsFinite(num2) || num2 < 0.5f)
		{
			throw new InvalidOperationException("红线形状坐标无效。");
		}
		if (top < plan.RedLineAnchorTop - 18f || top > plan.RedLineAnchorTop + 18f)
		{
			throw new InvalidOperationException("红线超出占位段纵向范围。");
		}
		if (num < plan.RedLineAnchorX1 - 36f || num3 > plan.RedLineAnchorX2 + 36f)
		{
			throw new InvalidOperationException("红线超出占位段横向范围。");
		}
	}

	private static bool IsFinite(float value)
	{
		if (!float.IsNaN(value))
		{
			return !float.IsInfinity(value);
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void TryQuality(Action verifier, string code, string stage)
	{
		try
		{
			verifier();
		}
		catch (Exception ex)
		{
			LogService.Warn("RedHeaderVerifier.Quality, code=" + code + ", stage=" + stage, ex);
			ReportQualityWarning(code, stage);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ReportQualityWarning(string code, string stage)
	{
		LogService.Warn("REDHEADER-QUALITY warning, code=" + code + ", stage=" + stage);
		ExecutionWarningCollector.Report(code, "redheader", "warn.redheader.quality");
	}

	private static IReadOnlyList<VerificationFinding> MergeWarnings(IEnumerable<VerificationFinding> initial, IEnumerable<VerificationFinding> current)
	{
		List<VerificationFinding> list = new List<VerificationFinding>();
		AddWarnings(list, initial);
		AddWarnings(list, current);
		return list.AsReadOnly();
	}

	private static void AddWarnings(ICollection<VerificationFinding> target, IEnumerable<VerificationFinding> source)
	{
		if (source == null)
		{
			return;
		}
		foreach (VerificationFinding item in source)
		{
			if (item == null)
			{
				continue;
			}
			bool flag = false;
			foreach (VerificationFinding item2 in target)
			{
				if (!string.Equals(item2.Code, item.Code, StringComparison.Ordinal))
				{
					continue;
				}
				flag = true;
				break;
			}
			if (!flag)
			{
				target.Add(item);
			}
		}
	}
}
