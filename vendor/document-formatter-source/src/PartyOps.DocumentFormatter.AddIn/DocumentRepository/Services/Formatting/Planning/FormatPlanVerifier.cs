using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using DocumentRepository.Models;
using DocumentRepository.Models.Formatting;
using DocumentRepository.Models.FormattingPlan;
using DocumentRepository.Models.Images;
using DocumentRepository.Models.Mutations;
using DocumentRepository.Models.Snapshots;
using DocumentRepository.Services.Detection.Images;
using DocumentRepository.Services.Detection.Tables;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.Mutations;
using DocumentRepository.Services.Performance;
using DocumentRepository.Services.Safety;
using DocumentRepository.Services.Snapshots;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Formatting.Planning;

internal sealed class FormatPlanVerifier : IMutationPlanVerifier
{
	internal static Action BeforeVerificationForTesting;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void Verify(DocumentMutationPlan mutationPlan, MutationExecutionContext context)
	{
		FormatExecutionPlan formatExecutionPlan = (mutationPlan as FormatExecutionPlan) ?? throw new InvalidOperationException("排版验证器收到的不是排版计划。");
		BeforeVerificationForTesting?.Invoke();
		FirstFormatDiagnosticsSession firstFormatDiagnosticsSession = ResolveDiagnostics(context);
		RefreshExecutionAnchors(context);
		DocumentSnapshot documentSnapshot = DocumentSnapshotService.Capture(context.Document, context.ScopeRange, collectStyleNames: true);
		firstFormatDiagnosticsSession?.Mark("verify-snapshot", documentSnapshot.ParagraphCount, reliable: true, "post-execution snapshot captured");
		VerifyProtectedObjects(formatExecutionPlan, documentSnapshot.ProtectedObjects);
		firstFormatDiagnosticsSession?.Mark("verify-protected", 1, reliable: true, "protected object counts verified");
		VerifyTableObjects(formatExecutionPlan, context);
		firstFormatDiagnosticsSession?.Mark("verify-tables", 1, reliable: true, "table objects verified");
		VerifyImageObjects(formatExecutionPlan, context);
		firstFormatDiagnosticsSession?.Mark("verify-images", 1, reliable: true, "image objects verified");
		VerifyPlannedStyles(formatExecutionPlan, context.Document, documentSnapshot, firstFormatDiagnosticsSession);
		firstFormatDiagnosticsSession?.Mark("verify-styles", 1, reliable: true, "planned paragraph styles verified");
		if (formatExecutionPlan.ExecutionScope != FormatExecutionScope.CompilationArticle)
		{
			FormatIdempotencyStampService.MarkSuccessful(formatExecutionPlan, context.Document, documentSnapshot);
		}
		firstFormatDiagnosticsSession?.Mark("verify-stamp", 1, reliable: true, "idempotency stamp stored");
		if (formatExecutionPlan.ExecutionScope != FormatExecutionScope.FullDocument && formatExecutionPlan.OutsideScopeSnapshot != null)
		{
			DocumentSnapshotService.AssertOutsideScopeUnchanged(formatExecutionPlan.OutsideScopeSnapshot, context.Document, context.ScopeRange);
		}
	}

	void IMutationPlanVerifier.Verify(DocumentMutationPlan mutationPlan, MutationExecutionContext context)
	{
		this.Verify(mutationPlan, context);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static FirstFormatDiagnosticsSession ResolveDiagnostics(MutationExecutionContext context)
	{
		if (context != null && context.Items != null && context.Items.TryGetValue("format.diagnostics", out var value))
		{
			return value as FirstFormatDiagnosticsSession;
		}
		return null;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void VerifyProtectedObjects(FormatExecutionPlan plan, ProtectedObjectSnapshot actual)
	{
		ProtectedObjectSnapshot protectedObjects = (plan.OriginalSnapshot ?? plan.SourceSnapshot).ProtectedObjects;
		TableAnalysisResult tableAnalysisResult = ((plan.FormatContext == null || plan.FormatContext.Analysis == null) ? null : plan.FormatContext.Analysis.TableAnalysis);
		if (tableAnalysisResult == null || !tableAnalysisResult.HasTables)
		{
			AssertEqual(FormatFailureReasonCode.TableStructureChanged, "表格", protectedObjects.Tables, actual.Tables);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void VerifyTableObjects(FormatExecutionPlan plan, MutationExecutionContext context)
	{
		TableAnalysisResult tableAnalysisResult = ((plan.FormatContext == null || plan.FormatContext.Analysis == null) ? null : plan.FormatContext.Analysis.TableAnalysis);
		if (tableAnalysisResult == null || !tableAnalysisResult.HasTables)
		{
			return;
		}
		TableAnalysisResult tableAnalysisResult2 = CaptureTables(plan, context);
		if (tableAnalysisResult.TableCount != tableAnalysisResult2.TableCount || tableAnalysisResult.Tables.Count != tableAnalysisResult2.Tables.Count)
		{
			try
			{
				context.Document.Repaginate();
			}
			catch (Exception ex)
			{
				LogService.Warn("FORMAT-QUALITY table-count-repaginate-failed", ex);
			}
			tableAnalysisResult2 = CaptureTables(plan, context);
		}
		bool[] array = new bool[tableAnalysisResult2.Tables.Count];
		int[] array2 = new int[tableAnalysisResult.Tables.Count];
		for (int i = 0; i < array2.Length; i++)
		{
			array2[i] = -1;
		}
		for (int j = 0; j < tableAnalysisResult.Tables.Count; j++)
		{
			array2[j] = FindUniqueTableMatch(tableAnalysisResult.Tables[j], tableAnalysisResult2.Tables, array);
			if (array2[j] >= 0)
			{
				array[array2[j]] = true;
			}
		}
		int num = 0;
		while (true)
		{
			if (num >= tableAnalysisResult.Tables.Count)
			{
				int num2 = 0;
				while (true)
				{
					if (num2 < tableAnalysisResult2.Tables.Count)
					{
						if (!array[num2])
						{
							if (!IsActualNestedTableCoveredByMatchedParent(num2, tableAnalysisResult.Tables, tableAnalysisResult2.Tables, array2))
							{
								throw VerificationFailure(FormatFailureReasonCode.TableStructureChanged, "排版结果出现无法解释的新表格：序号=" + ((tableAnalysisResult2.Tables[num2] == null) ? (num2 + 1) : tableAnalysisResult2.Tables[num2].AnalysisOrdinal));
							}
							ReportTableEnumerationAnomaly((tableAnalysisResult2.Tables[num2] == null) ? (num2 + 1) : tableAnalysisResult2.Tables[num2].AnalysisOrdinal);
						}
						num2++;
						continue;
					}
					if (tableAnalysisResult.TableCount != tableAnalysisResult2.TableCount || tableAnalysisResult.Tables.Count != tableAnalysisResult2.Tables.Count)
					{
						LogService.Warn("FORMAT-QUALITY table-enumeration-count-drift, expected=" + tableAnalysisResult.TableCount + ", actual=" + tableAnalysisResult2.TableCount);
						ExecutionWarningCollector.Report("format-table-enumeration-count-drift", "table", "warn.format.table");
					}
					break;
				}
				break;
			}
			TableElementInfo tableElementInfo = tableAnalysisResult.Tables[num];
			int num3 = array2[num];
			if (num3 < 0)
			{
				LogUnmatchedTableIdentity(tableElementInfo, tableAnalysisResult2.Tables);
				if (!IsNestedTableCoveredByMatchedParent(num, tableAnalysisResult.Tables, tableAnalysisResult2.Tables, array2))
				{
					throw VerificationFailure(FormatFailureReasonCode.TableStructureChanged, "原有表格未能在排版结果中找到：序号=" + (tableElementInfo?.AnalysisOrdinal ?? (num + 1)));
				}
				ReportTableEnumerationAnomaly(tableElementInfo?.AnalysisOrdinal ?? (num + 1));
			}
			else
			{
				TableElementInfo tableElementInfo2 = tableAnalysisResult2.Tables[num3];
				if (tableElementInfo == null || tableElementInfo2 == null)
				{
					throw VerificationFailure(FormatFailureReasonCode.PlanInvariantViolation, "表格完整性快照缺少对象：" + (num + 1));
				}
				if (tableElementInfo.ContentIdentityReliable && tableElementInfo2.ContentIdentityReliable)
				{
					if (!string.Equals(tableElementInfo.ContentHash, tableElementInfo2.ContentHash, StringComparison.Ordinal) || tableElementInfo.ContentLength != tableElementInfo2.ContentLength)
					{
						throw VerificationFailure(FormatFailureReasonCode.TableContentChanged, "表格文字内容发生变化：序号=" + tableElementInfo.AnalysisOrdinal);
					}
					if (tableElementInfo.StoryTypeCode != tableElementInfo2.StoryTypeCode)
					{
						throw VerificationFailure(FormatFailureReasonCode.TableStructureChanged, "表格故事区发生变化：序号=" + tableElementInfo.AnalysisOrdinal);
					}
				}
				else
				{
					ReportTableFactIndeterminate("内容身份", tableElementInfo.AnalysisOrdinal);
				}
				VerifyOptionalTableCount("行数", tableElementInfo.RowCountReliable, tableElementInfo.RowCount, tableElementInfo2.RowCountReliable, tableElementInfo2.RowCount, tableElementInfo.AnalysisOrdinal);
				VerifyOptionalTableCount("列数", tableElementInfo.ColumnCountReliable, tableElementInfo.ColumnCount, tableElementInfo2.ColumnCountReliable, tableElementInfo2.ColumnCount, tableElementInfo.AnalysisOrdinal);
				VerifyOptionalTableCount("单元格数", tableElementInfo.CellCountReliable, tableElementInfo.CellCount, tableElementInfo2.CellCountReliable, tableElementInfo2.CellCount, tableElementInfo.AnalysisOrdinal);
				VerifyOptionalTableCount("嵌套表格数", tableElementInfo.NestedTableCountReliable, tableElementInfo.NestedTableCount, tableElementInfo2.NestedTableCountReliable, tableElementInfo2.NestedTableCount, tableElementInfo.AnalysisOrdinal);
			}
			num++;
		}
	}

	private static TableAnalysisResult CaptureTables(FormatExecutionPlan plan, MutationExecutionContext context)
	{
		if (plan.ExecutionScope == FormatExecutionScope.FullDocument)
		{
			return TableDetector.AnalyzeDocument(context.Document);
		}
		return TableDetector.AnalyzeRange(context.ScopeRange);
	}

	private static int FindUniqueTableMatch(TableElementInfo expected, IList<TableElementInfo> actual, bool[] used)
	{
		if (expected == null || actual == null)
		{
			return -1;
		}
		List<int> list = new List<int>();
		for (int i = 0; i < actual.Count; i++)
		{
			if (!used[i] && TableIdentityComparer.HasSameStableIdentity(expected, actual[i]))
			{
				list.Add(i);
			}
		}
		if (list.Count == 0)
		{
			return -1;
		}
		if (list.Count == 1)
		{
			return list[0];
		}
		List<int> list2 = list.Where((int index) => actual[index].RangeStart == expected.RangeStart && actual[index].RangeEnd == expected.RangeEnd).ToList();
		if (list2.Count != 1)
		{
			int result = -1;
			int num = int.MaxValue;
			bool flag = false;
			foreach (int item in list)
			{
				int num2 = Math.Abs(actual[item].RangeStart - expected.RangeStart);
				if (num2 >= num)
				{
					if (num2 == num)
					{
						flag = true;
					}
				}
				else
				{
					result = item;
					num = num2;
					flag = false;
				}
			}
			if (!flag)
			{
				return result;
			}
			return -1;
		}
		return list2[0];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void LogUnmatchedTableIdentity(TableElementInfo expected, IList<TableElementInfo> actual)
	{
		LogService.Warn("FORMAT-SAFETY table-identity-unmatched expected=" + DescribeTableIdentity(expected) + ", actualCount=" + (actual?.Count ?? 0));
		if (actual != null)
		{
			for (int i = 0; i < actual.Count; i++)
			{
				LogService.Warn("FORMAT-SAFETY table-identity-candidate index=" + (i + 1) + ", facts=" + DescribeTableIdentity(actual[i]));
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string DescribeTableIdentity(TableElementInfo value)
	{
		if (value == null)
		{
			return "null";
		}
		string text = (string.IsNullOrWhiteSpace(value.ContentHash) ? "none" : value.ContentHash.Substring(0, Math.Min(12, value.ContentHash.Length)));
		return "ordinal=" + value.AnalysisOrdinal + "|range=" + value.RangeStart + "-" + value.RangeEnd + "|story=" + value.StoryTypeCode + "|content=" + value.ContentIdentityReliable + ":" + value.ContentLength + ":" + text + "|rows=" + value.RowCountReliable + ":" + value.RowCount + "|columns=" + value.ColumnCountReliable + ":" + value.ColumnCount + "|cells=" + value.CellCountReliable + ":" + value.CellCount + "|nested=" + value.NestedTableCountReliable + ":" + value.NestedTableCount;
	}

	private static bool IsNestedTableCoveredByMatchedParent(int missingExpectedIndex, IList<TableElementInfo> expected, IList<TableElementInfo> actual, int[] matches)
	{
		TableElementInfo tableElementInfo = expected[missingExpectedIndex];
		if (tableElementInfo == null)
		{
			return false;
		}
		for (int i = 0; i < expected.Count; i++)
		{
			int num = matches[i];
			if (i != missingExpectedIndex && num >= 0)
			{
				TableElementInfo tableElementInfo2 = expected[i];
				TableElementInfo tableElementInfo3 = actual[num];
				if (ContainsRange(tableElementInfo2, tableElementInfo) && tableElementInfo2.NestedTableCountReliable && tableElementInfo3.NestedTableCountReliable && tableElementInfo2.NestedTableCount > 0 && tableElementInfo2.NestedTableCount == tableElementInfo3.NestedTableCount && TableIdentityComparer.HasSameStableIdentity(tableElementInfo2, tableElementInfo3))
				{
					return true;
				}
			}
		}
		return false;
	}

	private static bool IsActualNestedTableCoveredByMatchedParent(int extraActualIndex, IList<TableElementInfo> expected, IList<TableElementInfo> actual, int[] matches)
	{
		TableElementInfo tableElementInfo = actual[extraActualIndex];
		if (tableElementInfo == null)
		{
			return false;
		}
		for (int i = 0; i < expected.Count; i++)
		{
			int num = matches[i];
			if (num >= 0 && num != extraActualIndex)
			{
				TableElementInfo tableElementInfo2 = expected[i];
				TableElementInfo tableElementInfo3 = actual[num];
				if (ContainsRange(tableElementInfo3, tableElementInfo) && tableElementInfo2.NestedTableCountReliable && tableElementInfo3.NestedTableCountReliable && tableElementInfo2.NestedTableCount > 0 && tableElementInfo2.NestedTableCount == tableElementInfo3.NestedTableCount && TableIdentityComparer.HasSameStableIdentity(tableElementInfo2, tableElementInfo3))
				{
					return true;
				}
			}
		}
		return false;
	}

	private static bool ContainsRange(TableElementInfo parent, TableElementInfo child)
	{
		if (parent != null && child != null && parent.RangeStart <= child.RangeStart && parent.RangeEnd >= child.RangeEnd)
		{
			if (parent.RangeStart == child.RangeStart)
			{
				return parent.RangeEnd != child.RangeEnd;
			}
			return true;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ReportTableEnumerationAnomaly(int ordinal)
	{
		LogService.Warn("FORMAT-QUALITY table-enumeration-nested-drift, analysisOrdinal=" + ordinal);
		ExecutionWarningCollector.Report("format-table-enumeration-nested-drift", "table", "warn.format.table");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void VerifyOptionalTableCount(string factName, bool expectedReliable, int expected, bool actualReliable, int actual, int ordinal)
	{
		if (expectedReliable && actualReliable)
		{
			if (expected != actual)
			{
				throw VerificationFailure(FormatFailureReasonCode.TableStructureChanged, "表格" + factName + "发生变化：序号=" + ordinal + "，排版前=" + expected + "，排版后=" + actual);
			}
		}
		else
		{
			ReportTableFactIndeterminate(factName, ordinal);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ReportTableFactIndeterminate(string factName, int ordinal)
	{
		LogService.Warn("FORMAT-QUALITY table-structure-detail-indeterminate, fact=" + factName + ", analysisOrdinal=" + ordinal);
		ExecutionWarningCollector.Report("format-table-structure-detail-indeterminate", "table", "warn.format.table");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void VerifyImageObjects(FormatExecutionPlan plan, MutationExecutionContext context)
	{
		if (plan.ImagePlan != null)
		{
			ImageAnalysisSnapshot sourceSnapshot = plan.ImagePlan.SourceSnapshot;
			ImageAnalysisSnapshot imageAnalysisSnapshot = ((context.ScopeRange == null) ? ImageSnapshotService.Capture(context.Document) : ImageSnapshotService.Capture(context.ScopeRange));
			if (!sourceSnapshot.CountsReliable || !imageAnalysisSnapshot.CountsReliable)
			{
				throw VerificationFailure(FormatFailureReasonCode.DocumentStructureUnreadable, "真实图片数量暂时无法可靠读取，已停止提交排版结果。");
			}
			ImageFormattingExecutionResult imageExecutionResult = GetImageExecutionResult(context);
			int num = imageExecutionResult?.AppliedInlineDelta ?? plan.ImagePlan.ExpectedInlineDelta;
			int num2 = imageExecutionResult?.AppliedFloatingDelta ?? plan.ImagePlan.ExpectedFloatingDelta;
			int expected = GetObservedInlineCount(sourceSnapshot) + num;
			int expected2 = GetObservedFloatingCount(sourceSnapshot) + num2;
			AssertEqual(FormatFailureReasonCode.ImageObjectChanged, "全部内嵌对象", expected, GetObservedInlineCount(imageAnalysisSnapshot));
			AssertEqual(FormatFailureReasonCode.ImageObjectChanged, "全部浮动对象", expected2, GetObservedFloatingCount(imageAnalysisSnapshot));
			AssertEqual(FormatFailureReasonCode.ImageObjectChanged, "全部图片对象", GetObservedObjectCount(sourceSnapshot), GetObservedObjectCount(imageAnalysisSnapshot));
			if (!sourceSnapshot.DetailsReliable || !imageAnalysisSnapshot.DetailsReliable)
			{
				LogService.Warn("FORMAT-QUALITY image-detail-indeterminate");
				ExecutionWarningCollector.Report("format-image-detail-indeterminate", "image", "warn.format.image");
				return;
			}
			int expected3 = CountEligibleInline(sourceSnapshot) + num;
			int expected4 = CountEligibleFloating(sourceSnapshot) + num2;
			AssertEqual(FormatFailureReasonCode.ImageObjectChanged, "真实内嵌图片", expected3, CountEligibleInline(imageAnalysisSnapshot));
			AssertEqual(FormatFailureReasonCode.ImageObjectChanged, "真实浮动图片", expected4, CountEligibleFloating(imageAnalysisSnapshot));
			AssertEqual(FormatFailureReasonCode.ImageObjectChanged, "真实图片总数", sourceSnapshot.EligibleCount, imageAnalysisSnapshot.EligibleCount);
			VerifyFilteredEligibleObjects(plan.ImagePlan, imageAnalysisSnapshot);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ImageFormattingExecutionResult GetImageExecutionResult(MutationExecutionContext context)
	{
		if (context != null && context.Items.TryGetValue("format.image-execution-result", out var value))
		{
			return value as ImageFormattingExecutionResult;
		}
		return null;
	}

	private static int GetObservedInlineCount(ImageAnalysisSnapshot snapshot)
	{
		if (snapshot == null)
		{
			return 0;
		}
		if (snapshot.ObservedObjectCount > 0 || snapshot.Objects.Count == 0)
		{
			return snapshot.ObservedInlineObjectCount;
		}
		return CountEligibleInline(snapshot);
	}

	private static int GetObservedFloatingCount(ImageAnalysisSnapshot snapshot)
	{
		if (snapshot != null)
		{
			if (snapshot.ObservedObjectCount > 0 || snapshot.Objects.Count == 0)
			{
				return snapshot.ObservedFloatingObjectCount;
			}
			return snapshot.Objects.Count - CountEligibleInline(snapshot);
		}
		return 0;
	}

	private static int GetObservedObjectCount(ImageAnalysisSnapshot snapshot)
	{
		return GetObservedInlineCount(snapshot) + GetObservedFloatingCount(snapshot);
	}

	private static int CountEligibleInline(ImageAnalysisSnapshot snapshot)
	{
		int num = 0;
		foreach (ImageObjectSnapshot @object in snapshot.Objects)
		{
			if (@object != null && @object.IsEligible && (@object.Kind == ImageObjectKind.InlinePicture || @object.Kind == ImageObjectKind.InlineLinkedPicture))
			{
				num++;
			}
		}
		return num;
	}

	private static int CountEligibleFloating(ImageAnalysisSnapshot snapshot)
	{
		return snapshot.EligibleCount - CountEligibleInline(snapshot);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void VerifyFilteredEligibleObjects(ImageFormattingPlan plan, ImageAnalysisSnapshot actual)
	{
		if (plan.ProtectedEligibleObjects.Count == 0)
		{
			return;
		}
		Dictionary<string, int> dictionary = BuildEligibleSignatures(plan.ProtectedEligibleObjects);
		Dictionary<string, int> dictionary2 = BuildEligibleSignatures(actual.Objects);
		foreach (KeyValuePair<string, int> item in dictionary)
		{
			if (dictionary2.TryGetValue(item.Key, out var value) && value >= item.Value)
			{
				continue;
			}
			throw VerificationFailure(FormatFailureReasonCode.ImageObjectChanged, "被筛选排除的真实图片发生变化：" + item.Key);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static Dictionary<string, int> BuildEligibleSignatures(IEnumerable<ImageObjectSnapshot> objects)
	{
		Dictionary<string, int> dictionary = new Dictionary<string, int>(StringComparer.Ordinal);
		foreach (ImageObjectSnapshot @object in objects)
		{
			if (@object != null && @object.IsEligible)
			{
				bool flag = @object.Kind == ImageObjectKind.FloatingPicture || @object.Kind == ImageObjectKind.FloatingLinkedPicture;
				string key = @object.Kind.ToString() + "|" + @object.TypeCode + "|" + (flag ? (@object.Name ?? string.Empty) : string.Empty) + "|" + Math.Round(@object.WidthPoints, 1) + "|" + Math.Round(@object.HeightPoints, 1);
				dictionary.TryGetValue(key, out var value);
				dictionary[key] = value + 1;
			}
		}
		return dictionary;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void VerifyPlannedStyles(FormatExecutionPlan plan, Document document, DocumentSnapshot actual, FirstFormatDiagnosticsSession diagnostics)
	{
		if (plan.FormatContext != null && plan.FormatContext.Elements != null)
		{
			int num = 0;
			int num2 = 0;
			int num3 = 0;
			long startTimestamp = FirstFormatDiagnosticsSession.Timestamp();
			foreach (DocumentElement item in plan.FormatContext.Elements.Items)
			{
				if (item == null || item.IsEmpty || !RequiresStyleVerification(item.Type))
				{
					continue;
				}
				ParagraphSnapshot paragraphSnapshot = FindSnapshotParagraph(actual?.Paragraphs, item.RangeStart);
				if (paragraphSnapshot == null || string.IsNullOrWhiteSpace(paragraphSnapshot.StyleName))
				{
					long startTimestamp2 = FirstFormatDiagnosticsSession.Timestamp();
					num2++;
					Microsoft.Office.Interop.Word.Range value = null;
					Paragraphs value2 = null;
					Paragraph value3 = null;
					try
					{
						object Start = item.RangeStart;
						object End = item.RangeStart;
						value = document.Range(ref Start, ref End);
						value2 = value.Paragraphs;
						if (value2 == null || value2.Count == 0)
						{
							throw VerificationFailure(FormatFailureReasonCode.PlanInvariantViolation, "无法定位已规划段落：" + item.ParagraphIndex);
						}
						value3 = value2[1];
						if (!DocumentStyleManager.IsParagraphStyle(value3, item.Type))
						{
							num3++;
							string paragraphStructuralStyleName = DocumentStyleManager.GetParagraphStructuralStyleName(value3);
							LogService.Warn("FORMAT-QUALITY paragraph-style-mismatch" + item.ParagraphIndex + "，类型=" + item.Type.ToString() + "，计划结构样式=" + DocumentStyleManager.GetStyleName(item.Type) + "，实际结构样式=" + (string.IsNullOrWhiteSpace(paragraphStructuralStyleName) ? "<空>" : paragraphStructuralStyleName) + "，" + DescribeElementForLog(item));
							ExecutionWarningCollector.Report("format-paragraph-style-mismatch", "paragraph-style", "warn.format.paragraph");
						}
					}
					finally
					{
						ComObjectRelease.Release(ref value3, "FormatVerifier.Paragraph");
						ComObjectRelease.Release(ref value2, "FormatVerifier.Paragraphs");
						ComObjectRelease.Release(ref value, "FormatVerifier.Range");
					}
					diagnostics?.Accumulate("verify-styles.com-fallback", startTimestamp2);
				}
				else
				{
					num++;
					string styleName = DocumentStyleManager.GetStyleName(item.Type);
					if (!string.Equals(paragraphSnapshot.StyleName, styleName, StringComparison.OrdinalIgnoreCase))
					{
						num3++;
						LogService.Warn("FORMAT-QUALITY paragraph-style-mismatch" + item.ParagraphIndex + "，类型=" + item.Type.ToString() + "，计划结构样式=" + styleName + "，实际结构样式=" + paragraphSnapshot.StyleName + "，" + DescribeElementForLog(item));
						ExecutionWarningCollector.Report("format-paragraph-style-mismatch", "paragraph-style", "warn.format.paragraph");
					}
				}
			}
			diagnostics?.Accumulate("verify-styles.snapshot-compare", startTimestamp, num);
			diagnostics?.Accumulate("verify-styles.snapshot-null-fallback", FirstFormatDiagnosticsSession.Timestamp(), num2);
			diagnostics?.Accumulate("verify-styles.style-mismatch-warn", FirstFormatDiagnosticsSession.Timestamp(), num3);
			return;
		}
		throw VerificationFailure(FormatFailureReasonCode.PlanInvariantViolation, "排版验证缺少一次性识别结果。");
	}

	private static ParagraphSnapshot FindSnapshotParagraph(IList<ParagraphSnapshot> paragraphs, int position)
	{
		if (paragraphs == null || paragraphs.Count == 0)
		{
			return null;
		}
		int num = 0;
		int num2 = paragraphs.Count - 1;
		int num3 = -1;
		while (true)
		{
			if (num > num2)
			{
				if (num3 < 0)
				{
					return null;
				}
				break;
			}
			int num4 = num + (num2 - num) / 2;
			ParagraphSnapshot paragraphSnapshot = paragraphs[num4];
			if (paragraphSnapshot != null)
			{
				if (paragraphSnapshot.RangeStart <= position)
				{
					num3 = num4;
					num = num4 + 1;
				}
				else
				{
					num2 = num4 - 1;
				}
				continue;
			}
			return null;
		}
		ParagraphSnapshot paragraphSnapshot2 = paragraphs[num3];
		if (position >= paragraphSnapshot2.RangeEnd)
		{
			return null;
		}
		return paragraphSnapshot2;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void RefreshExecutionAnchors(MutationExecutionContext context)
	{
		if (context == null)
		{
			throw new ArgumentNullException("context");
		}
		if (!context.Items.TryGetValue("format.anchors", out var value) || !(value is FormatExecutionAnchorSet))
		{
			throw VerificationFailure(FormatFailureReasonCode.PlanInvariantViolation, "排版验证缺少执行锚点，无法验证最终段落位置。");
		}
		((FormatExecutionAnchorSet)value).RefreshPositions();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string DescribeElementForLog(DocumentElement element)
	{
		string text = element?.Text;
		int num = ((!string.IsNullOrEmpty(text)) ? text.Length : 0);
		int num2 = element?.RangeStart ?? (-1);
		return "文本长度=" + num + "，文本指纹=" + ShortTextFingerprint(text) + "，段落位置=" + num2;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string ShortTextFingerprint(string text)
	{
		using SHA256 sHA = SHA256.Create();
		byte[] array = sHA.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty));
		StringBuilder stringBuilder = new StringBuilder(12);
		for (int i = 0; i < 6; i++)
		{
			stringBuilder.Append(array[i].ToString("x2"));
		}
		return stringBuilder.ToString();
	}

	internal static bool RequiresStyleVerification(ElementType type)
	{
		if ((uint)(type - 1) <= 8u)
		{
			return true;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void AssertEqual(FormatFailureReasonCode reasonCode, string objectName, int expected, int actual)
	{
		if (expected != actual)
		{
			throw VerificationFailure(reasonCode, objectName + "数量发生变化：排版前=" + expected + "，排版后=" + actual);
		}
	}

	private static FormatOperationException VerificationFailure(FormatFailureReasonCode reasonCode, string diagnosticMessage)
	{
		return FormatOperationException.Create(reasonCode, FormatFailureStage.Verify, DocumentSafetyDisposition.PendingRecovery, new InvalidOperationException(diagnosticMessage));
	}
}
