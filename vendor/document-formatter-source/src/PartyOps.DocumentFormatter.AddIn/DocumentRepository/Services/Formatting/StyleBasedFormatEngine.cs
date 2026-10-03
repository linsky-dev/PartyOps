using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Models.Tasks;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.Performance;
using DocumentRepository.Services.Snapshots;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Formatting;

public static class StyleBasedFormatEngine
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void FormatDocument(Document doc, FormatConfig cfg, ITaskProgressReporter progress, DocumentElementList elements, FirstFormatDiagnosticsSession diagnostics = null)
	{
		if (doc == null)
		{
			throw new ArgumentNullException("doc");
		}
		if (cfg == null)
		{
			throw new ArgumentNullException("cfg");
		}
		Stopwatch sw = Stopwatch.StartNew();
		List<string> lines = new List<string>();
		string runId = "style-full-" + DateTime.Now.ToString("HHmmssfff");
		long previousMs = 0L;
		previousMs = LogPerf(lines, runId, "enter style formatting", sw, previousMs);
		FormatTextStyleDefinition styleDefinition = FormatStyleDefinitionBuilder.Build(cfg);
		Paragraphs value = null;
		int count;
		try
		{
			value = doc.Paragraphs;
			count = value.Count;
		}
		catch
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "StyleBasedFormatEngine.paragraphs");
			}
			throw;
		}
		try
		{
			if (elements == null || elements.Items == null)
			{
				throw new InvalidOperationException("样式化排版必须使用分析层产出的 DocumentElementList。");
			}
			if (elements.Items.Count != count)
			{
				throw new InvalidOperationException("样式化排版收到的 DocumentElementList 与当前段落数不一致。");
			}
			int valueOrDefault = (elements?.Items?.Count).GetValueOrDefault();
			string[] array = new string[valueOrDefault];
			ElementType[] array2 = new ElementType[valueOrDefault];
			int num = 0;
			int num2 = 0;
			int num3 = 0;
			int num4 = 0;
			int num5 = 0;
			int compliantCount = 0;
			int styleRefreshCount = 0;
			int fullRebuildCount = 0;
			int num6 = 0;
			for (int i = 0; i < valueOrDefault; i++)
			{
				DocumentElement documentElement = elements.Items[i];
				array[i] = documentElement?.Text ?? string.Empty;
				array2[i] = documentElement?.Type ?? ElementType.Unknown;
				if (documentElement == null || documentElement.IsEmpty || string.IsNullOrEmpty(array[i]))
				{
					num++;
				}
			}
			HashSet<ElementType> hashSet = new HashSet<ElementType>(array2);
			progress?.Report(TaskProgressInfo.Create("一键排版", 36, 100, "初始化排版样式", "样式准备"));
			bool flag = DocumentStyleManager.EnsureStyles(doc, cfg, StyleRefreshMode.IfChanged, hashSet, includePageNumber: true);
			TextAppearanceStyleService.EnsureStyles(doc, hashSet);
			diagnostics?.Mark("style-ensure", hashSet.Count, reliable: true, "styles rebuilt=" + flag);
			TextAppearanceStyleService.TextAppearanceApplyContext applyContext = new TextAppearanceStyleService.TextAppearanceApplyContext(doc, hashSet);
			previousMs = LogPerf(lines, runId, "ensure required styles rebuilt=" + flag + ", types=" + hashSet.Count, sw, previousMs);
			ParagraphTextSnapshot paragraphTextSnapshot = ParagraphTextSnapshot.Capture(doc);
			bool isReliable = paragraphTextSnapshot.IsReliable;
			if (!isReliable && !string.IsNullOrWhiteSpace(paragraphTextSnapshot.FailureReason))
			{
				LogService.Warn("StyleBasedFormatEngine text snapshot unavailable, fallback to per-paragraph path: " + paragraphTextSnapshot.FailureReason);
			}
			int[] rangeStarts = null;
			int[] rangeEnds = null;
			bool flag2 = !isReliable && TryCaptureParagraphOffsets(value, valueOrDefault, out rangeStarts, out rangeEnds);
			ParagraphComplianceState[] array3 = (isReliable ? BuildWriteOnlyStates(elements, array2, out compliantCount, out styleRefreshCount, out fullRebuildCount) : (flag2 ? BuildComplianceStates(doc, rangeStarts, rangeEnds, elements, array2, styleDefinition, out compliantCount, out styleRefreshCount, out fullRebuildCount) : BuildComplianceStates(value, elements, array2, styleDefinition, out compliantCount, out styleRefreshCount, out fullRebuildCount)));
			string text = (isReliable ? "snapshot" : (flag2 ? "offsets" : "legacy"));
			previousMs = LogPerf(lines, runId, "evaluate compliance path=" + text + ", compliant=" + compliantCount + ", styleRefresh=" + styleRefreshCount + ", fullRebuild=" + fullRebuildCount, sw, previousMs);
			diagnostics?.Mark("style-compliance", valueOrDefault, isReliable, "compliant=" + compliantCount + ", refresh=" + styleRefreshCount + ", rebuild=" + fullRebuildCount);
			previousMs = LogPerf(lines, runId, "prepare direct-format ranges=" + (isReliable ? ParagraphStyleApplyService.PrepareDirectFormattingRuns(doc, paragraphTextSnapshot, array3, diagnostics) : (flag2 ? ParagraphStyleApplyService.PrepareDirectFormattingRuns(doc, rangeStarts, rangeEnds, array3, diagnostics) : ParagraphStyleApplyService.PrepareDirectFormattingRuns(doc, value, array3, diagnostics))), sw, previousMs);
			previousMs = LogPerf(lines, runId, "load paragraph types total=" + valueOrDefault + ", empty=" + num, sw, previousMs);
			if (isReliable || flag2)
			{
				int[] array4;
				int[] array5;
				if (isReliable)
				{
					array4 = new int[valueOrDefault];
					array5 = new int[valueOrDefault];
					for (int j = 0; j < valueOrDefault; j++)
					{
						array4[j] = paragraphTextSnapshot.Paragraphs[j].RangeStart;
						array5[j] = paragraphTextSnapshot.Paragraphs[j].RangeEnd;
					}
				}
				else
				{
					array4 = rangeStarts;
					array5 = rangeEnds;
				}
				List<Tuple<int, int, ElementType, ParagraphComplianceState>> list = new List<Tuple<int, int, ElementType, ParagraphComplianceState>>();
				int k = 0;
				while (k < valueOrDefault)
				{
					if (array2[k] == ElementType.Unknown || array2[k] == ElementType.DocumentNumber || array2[k] == ElementType.Table || array2[k] == ElementType.TableHeader || array2[k] == ElementType.Image)
					{
						k++;
						continue;
					}
					int num7 = k;
					ElementType elementType;
					for (elementType = array2[k]; k + 1 < valueOrDefault && array2[k + 1] == elementType; k++)
					{
					}
					int num8 = k;
					if (elementType != ElementType.Body || num8 - num7 + 1 < 2)
					{
						for (int l = num7; l <= num8; l++)
						{
							list.Add(Tuple.Create(l, l, elementType, array3[l]));
						}
					}
					else
					{
						int num9 = num7;
						while (num9 <= num8)
						{
							ParagraphComplianceState paragraphComplianceState = array3[num9];
							int m;
							for (m = num9; m + 1 <= num8 && array3[m + 1] == paragraphComplianceState; m++)
							{
							}
							list.Add(Tuple.Create(num9, m, elementType, paragraphComplianceState));
							num9 = m + 1;
						}
					}
					k++;
				}
				int progressInterval = GetProgressInterval(list.Count);
				for (int num10 = list.Count - 1; num10 >= 0; num10--)
				{
					int item = list[num10].Item1;
					int item2 = list[num10].Item2;
					ElementType item3 = list[num10].Item3;
					ParagraphComplianceState item4 = list[num10].Item4;
					if (item4 == ParagraphComplianceState.Compliant)
					{
						num2 += item2 - item + 1;
					}
					else if (item3 == ElementType.Body && item2 > item)
					{
						Microsoft.Office.Interop.Word.Range value2 = null;
						try
						{
							long startTimestamp = FirstFormatDiagnosticsSession.Timestamp();
							object Start = array4[item];
							object End = array5[item2];
							value2 = doc.Range(ref Start, ref End);
							diagnostics?.Accumulate("apply.range-create", startTimestamp);
							DocumentStyleManager.ApplyPreparedStyle(value2, item3, diagnostics);
							if (item4 == ParagraphComplianceState.NeedsFullRebuild)
							{
								TextAppearanceStyleService.ApplyParagraphText(value2, item3, diagnostics, applyContext);
							}
							num3++;
							num4 += item2 - item + 1;
						}
						finally
						{
							if (value2 != null)
							{
								ComObjectRelease.Release(ref value2, "StyleBasedFormatEngine.merged");
							}
						}
					}
					else if (item4 == ParagraphComplianceState.NeedsStyleRefresh)
					{
						Microsoft.Office.Interop.Word.Range value3 = null;
						try
						{
							long startTimestamp2 = FirstFormatDiagnosticsSession.Timestamp();
							object End = array4[item];
							object Start = array5[item2];
							value3 = doc.Range(ref End, ref Start);
							diagnostics?.Accumulate("apply.range-create", startTimestamp2);
							DocumentStyleManager.ApplyPreparedStyle(value3, item3, diagnostics);
							num5++;
						}
						finally
						{
							if (value3 != null)
							{
								ComObjectRelease.Release(ref value3, "StyleBasedFormatEngine.pr");
							}
						}
					}
					else
					{
						Microsoft.Office.Interop.Word.Range value4 = null;
						try
						{
							long startTimestamp3 = FirstFormatDiagnosticsSession.Timestamp();
							object Start = array4[item];
							object End = array5[item2];
							value4 = doc.Range(ref Start, ref End);
							diagnostics?.Accumulate("apply.range-create", startTimestamp3);
							array[item] = ParagraphStyleApplyService.ApplyParagraphToRange(value4, item3, array2, item, array[item], styleDefinition, diagnostics);
							TextAppearanceStyleService.ApplyParagraphText(value4, item3, diagnostics, applyContext);
							MixedHeadingBodyStyleService.ApplyCharacterOverrides(value4, item3, array[item], styleDefinition, preserveBodyBold: false, diagnostics);
							num5++;
							if (MixedHeadingBodyStyleService.IsMixedHeadingCandidate(item3))
							{
								num6++;
							}
						}
						finally
						{
							if (value4 != null)
							{
								ComObjectRelease.Release(ref value4, "StyleBasedFormatEngine.pr");
							}
						}
					}
					if (progress != null && (list.Count - num10) % progressInterval == 0)
					{
						if (progress.CancellationRequested)
						{
							throw new OperationCanceledException("用户已取消一键排版（应用段落样式）。");
						}
						long startTimestamp4 = FirstFormatDiagnosticsSession.Timestamp();
						int currentStep = ((valueOrDefault <= 0) ? 85 : (40 + Math.Min(45, (list.Count - num10) * 45 / Math.Max(1, list.Count))));
						progress.Report(TaskProgressInfo.Create("一键排版", currentStep, 100, "应用段落样式", "正文排版"));
						diagnostics?.Accumulate("apply.progress-report", startTimestamp4);
					}
				}
			}
			else
			{
				int progressInterval2 = GetProgressInterval(valueOrDefault);
				int n = 0;
				while (n < valueOrDefault)
				{
					if (array2[n] == ElementType.Unknown || array2[n] == ElementType.DocumentNumber || array2[n] == ElementType.Table || array2[n] == ElementType.TableHeader || array2[n] == ElementType.Image)
					{
						num2++;
						n++;
						continue;
					}
					int num11 = n;
					ElementType elementType2;
					for (elementType2 = array2[n]; n + 1 < valueOrDefault && array2[n + 1] == elementType2; n++)
					{
					}
					int num12 = n;
					if (num12 - num11 + 1 >= 2 && elementType2 == ElementType.Body)
					{
						int num13 = num11;
						while (num13 <= num12)
						{
							ParagraphComplianceState paragraphComplianceState2 = array3[num13];
							int num14;
							for (num14 = num13; num14 + 1 <= num12 && array3[num14 + 1] == paragraphComplianceState2; num14++)
							{
							}
							int num15 = num14 - num13 + 1;
							switch (paragraphComplianceState2)
							{
							case ParagraphComplianceState.NeedsStyleRefresh:
								ParagraphStyleApplyService.ApplyMergedStyle(doc, value, num13, num14, elementType2, diagnostics);
								num3++;
								num4 += num15;
								break;
							case ParagraphComplianceState.Compliant:
								num2 += num15;
								break;
							default:
								ParagraphStyleApplyService.ApplyMergedStyle(doc, value, num13, num14, elementType2, diagnostics);
								TextAppearanceStyleService.ApplyParagraphRun(doc, value, num13, num14, elementType2, diagnostics, applyContext);
								num3++;
								num4 += num15;
								break;
							}
							num13 = num14 + 1;
						}
					}
					else
					{
						for (int num16 = num11; num16 <= num12; num16++)
						{
							Paragraph value5 = null;
							try
							{
								value5 = value[num16 + 1];
								ParagraphComplianceState paragraphComplianceState3 = array3[num16];
								if (paragraphComplianceState3 != ParagraphComplianceState.Compliant)
								{
									if (paragraphComplianceState3 == ParagraphComplianceState.NeedsStyleRefresh)
									{
										ParagraphStyleApplyService.RefreshPreparedStyle(value5, elementType2, diagnostics);
										num5++;
										continue;
									}
									if (paragraphComplianceState3 == ParagraphComplianceState.NeedsFullRebuild)
									{
										array[num16] = ParagraphStyleApplyService.ApplyParagraph(value5, elementType2, array2, num16, array[num16], styleDefinition, diagnostics);
									}
									TextAppearanceStyleService.ApplyParagraphText(value5, elementType2, diagnostics, applyContext);
									MixedHeadingBodyStyleService.ApplyCharacterOverrides(value5, elementType2, array[num16], styleDefinition, preserveBodyBold: false, diagnostics);
									num5++;
									if (MixedHeadingBodyStyleService.IsMixedHeadingCandidate(elementType2))
									{
										num6++;
									}
								}
								else
								{
									num2++;
								}
							}
							finally
							{
								if (value5 != null)
								{
									ComObjectRelease.Release(ref value5, "StyleBasedFormatEngine.para");
								}
							}
						}
					}
					if (progress != null && n % progressInterval2 == 0)
					{
						if (progress.CancellationRequested)
						{
							throw new OperationCanceledException("用户已取消一键排版（应用段落样式）。");
						}
						long startTimestamp5 = FirstFormatDiagnosticsSession.Timestamp();
						int currentStep2 = ((valueOrDefault <= 0) ? 85 : (40 + Math.Min(45, (n + 1) * 45 / valueOrDefault)));
						progress.Report(TaskProgressInfo.Create("一键排版", currentStep2, 100, "应用段落样式", "正文排版"));
						diagnostics?.Accumulate("apply.progress-report", startTimestamp5);
					}
					n++;
				}
			}
			previousMs = LogPerf(lines, runId, "apply styles mergedRuns=" + num3 + ", mergedParas=" + num4 + ", individualParas=" + num5 + ", skipped=" + num2 + ", complianceCompliant=" + compliantCount + ", complianceStyleRefresh=" + styleRefreshCount + ", complianceFullRebuild=" + fullRebuildCount + ", charOverrideCandidates=" + num6, sw, previousMs);
			diagnostics?.Mark("style-apply", valueOrDefault, reliable: true, "mergedRuns=" + num3 + ", individual=" + num5 + ", skipped=" + num2);
			FlushPerf(lines);
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "StyleBasedFormatEngine.paragraphs");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void FormatSelection(Document doc, Microsoft.Office.Interop.Word.Range selectionRange, FormatConfig cfg, ITaskProgressReporter progress, DocumentElementList elements)
	{
		if (doc == null)
		{
			throw new ArgumentNullException("doc");
		}
		if (selectionRange == null)
		{
			throw new ArgumentNullException("selectionRange");
		}
		if (cfg == null)
		{
			throw new ArgumentNullException("cfg");
		}

		Stopwatch stopwatch = Stopwatch.StartNew();
		List<string> performanceLines = new List<string>();
		string runId = "style-selection-" + DateTime.Now.ToString("HHmmssfff");
		long previousMs = LogPerf(performanceLines, runId, "enter selection style formatting", stopwatch, 0L);
		FormatTextStyleDefinition styleDefinition = FormatStyleDefinitionBuilder.Build(cfg);
		Microsoft.Office.Interop.Word.Range selectionCopy = null;

		try
		{
			selectionCopy = selectionRange.Duplicate;
			int paragraphCount = selectionCopy.Paragraphs.Count;
			ElementType[] selectionTypes = BuildSelectionTypes(elements, paragraphCount);
			HashSet<ElementType> usedTypes = new HashSet<ElementType>(selectionTypes ?? new ElementType[0]);
			bool stylesRepaired = DocumentStyleManager.EnsureStyles(doc, cfg, StyleRefreshMode.IfChanged, usedTypes, includePageNumber: false);
			TextAppearanceStyleService.EnsureStyles(doc, usedTypes);
			previousMs = LogPerf(performanceLines, runId, "ensure selection styles repaired=" + stylesRepaired + ", types=" + usedTypes.Count, stopwatch, previousMs);

			int progressInterval = GetProgressInterval(paragraphCount);
			int formattedCount = 0;
			int compliantCount = 0;
			int styleRefreshedCount = 0;
			ParagraphComplianceContext complianceContext = new ParagraphComplianceContext();

			for (int paragraphIndex = 1; paragraphIndex <= paragraphCount; paragraphIndex++)
			{
				Paragraph paragraph = null;
				Microsoft.Office.Interop.Word.Range paragraphRange = null;
				bool processedParagraph = false;
				try
				{
					paragraph = selectionCopy.Paragraphs[paragraphIndex];
					paragraphRange = paragraph.Range;
					string paragraphText = (paragraphRange.Text ?? "").Trim(new char[] { '\r', '\n', '\u0007', '\f' });
					if (!string.IsNullOrEmpty(paragraphText))
					{
						DocumentElement element = FindElementForRange(elements, paragraphRange.Start, paragraphRange.End);
						if (element == null)
						{
							throw new InvalidOperationException("Selection paragraph does not have an analysis element.");
						}

						ElementType elementType = element.Type;
						if (selectionTypes != null && paragraphIndex - 1 < selectionTypes.Length)
						{
							elementType = selectionTypes[paragraphIndex - 1];
						}

						if (elementType != ElementType.Unknown && elementType != ElementType.DocumentNumber &&
							elementType != ElementType.Table && elementType != ElementType.TableHeader && elementType != ElementType.Image)
						{
							processedParagraph = true;
							ParagraphComplianceState complianceState = ParagraphFormatComplianceEvaluator.Evaluate(
								paragraphRange, doc, element, elementType, selectionTypes, paragraphIndex - 1, styleDefinition, complianceContext);

							switch (complianceState)
							{
							case ParagraphComplianceState.Compliant:
								compliantCount++;
								break;
							case ParagraphComplianceState.NeedsStyleRefresh:
								ParagraphStyleApplyService.RefreshPreparedStyle(paragraph, elementType);
								styleRefreshedCount++;
								formattedCount++;
								break;
							default:
								paragraphText = ParagraphStyleApplyService.ApplySelectionParagraph(paragraph, elementType, selectionTypes, paragraphIndex - 1, paragraphText, styleDefinition);
								TextAppearanceStyleService.ApplyParagraphText(paragraph, elementType);
								MixedHeadingBodyStyleService.ApplyCharacterOverrides(paragraph, elementType, paragraphText, styleDefinition);
								formattedCount++;
								break;
							}
						}
					}
				}
				finally
				{
					if (paragraphRange != null)
					{
						ComObjectRelease.Release(ref paragraphRange, "StyleBasedFormatEngine.rng");
					}
					if (paragraph != null)
					{
						ComObjectRelease.Release(ref paragraph, "StyleBasedFormatEngine.para");
					}
				}

				if (processedParagraph && progress != null && paragraphIndex % progressInterval == 0)
				{
					if (progress.CancellationRequested)
					{
						throw new OperationCanceledException("用户已取消选中快排（应用段落样式）。");
					}
					int currentStep = (paragraphCount <= 0) ? 85 : (10 + Math.Min(75, paragraphIndex * 75 / paragraphCount));
					progress.Report(TaskProgressInfo.Create("选中快排", currentStep, 100, "应用选区段落样式", "选区排版"));
				}
			}

			LogPerf(performanceLines, runId,
				"apply selection styles paragraphs=" + paragraphCount + ", formatted=" + formattedCount +
				", compliant=" + compliantCount + ", styleRefreshed=" + styleRefreshedCount,
				stopwatch, previousMs);
		}
		finally
		{
			if (selectionCopy != null)
			{
				ComObjectRelease.Release(ref selectionCopy, "StyleBasedFormatEngine.selRange");
			}
			FlushPerf(performanceLines);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ParagraphComplianceState[] BuildComplianceStates(Document doc, IList<int> paragraphRangeStarts, IList<int> paragraphRangeEnds, DocumentElementList elements, ElementType[] types, FormatTextStyleDefinition styleDefinition, out int compliantCount, out int styleRefreshCount, out int fullRebuildCount)
	{
		if (doc == null)
		{
			throw new ArgumentNullException("doc");
		}
		if (elements == null || elements.Items == null)
		{
			throw new ArgumentNullException("elements");
		}
		if (types == null)
		{
			throw new ArgumentNullException("types");
		}
		if (styleDefinition != null)
		{
			if (paragraphRangeStarts != null && paragraphRangeEnds != null && paragraphRangeStarts.Count >= types.Length && paragraphRangeEnds.Count >= types.Length)
			{
				ParagraphComplianceState[] array = new ParagraphComplianceState[types.Length];
				compliantCount = 0;
				styleRefreshCount = 0;
				fullRebuildCount = 0;
				ParagraphComplianceContext context = new ParagraphComplianceContext();
				for (int i = 0; i < types.Length; i++)
				{
					ElementType elementType = types[i];
					if (elementType == ElementType.Unknown || elementType == ElementType.DocumentNumber || elementType == ElementType.Table || elementType == ElementType.TableHeader || elementType == ElementType.Image)
					{
						array[i] = ParagraphComplianceState.NotApplicable;
						continue;
					}
					Microsoft.Office.Interop.Word.Range value = null;
					try
					{
						object Start = paragraphRangeStarts[i];
						object End = paragraphRangeEnds[i];
						value = doc.Range(ref Start, ref End);
						ParagraphComplianceState paragraphComplianceState = ParagraphFormatComplianceEvaluator.Evaluate(value, doc, elements.Items[i], elementType, types, i, styleDefinition, context);
						if (paragraphComplianceState == ParagraphComplianceState.NotApplicable)
						{
							paragraphComplianceState = ParagraphComplianceState.NeedsFullRebuild;
						}
						array[i] = paragraphComplianceState;
						switch (paragraphComplianceState)
						{
						case ParagraphComplianceState.NeedsFullRebuild:
							fullRebuildCount++;
							break;
						case ParagraphComplianceState.Compliant:
							compliantCount++;
							break;
						case ParagraphComplianceState.NeedsStyleRefresh:
							styleRefreshCount++;
							break;
						}
					}
					finally
					{
						if (value != null)
						{
							ComObjectRelease.Release(ref value, "StyleBasedFormatEngine.Compliance.EntryRange");
						}
					}
				}
				return array;
			}
			throw new ArgumentException("段落偏移表不可用或与段落数不一致。", "paragraphRangeStarts");
		}
		throw new ArgumentNullException("styleDefinition");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ParagraphComplianceState[] BuildWriteOnlyStates(DocumentElementList elements, ElementType[] types, out int compliantCount, out int styleRefreshCount, out int fullRebuildCount)
	{
		if (elements == null || elements.Items == null)
		{
			throw new ArgumentNullException("elements");
		}
		if (types != null)
		{
			ParagraphComplianceState[] array = new ParagraphComplianceState[types.Length];
			compliantCount = 0;
			styleRefreshCount = 0;
			fullRebuildCount = 0;
			for (int i = 0; i < types.Length; i++)
			{
				if (!IsWriteOnlyStyleType(types[i]) || i >= elements.Items.Count || elements.Items[i] == null || elements.Items[i].IsEmpty)
				{
					array[i] = ParagraphComplianceState.NotApplicable;
					continue;
				}
				array[i] = ParagraphComplianceState.NeedsFullRebuild;
				fullRebuildCount++;
			}
			return array;
		}
		throw new ArgumentNullException("types");
	}

	private static bool IsWriteOnlyStyleType(ElementType type)
	{
		if (type != ElementType.MainTitle && type != ElementType.SubTitle && type != ElementType.Level1Title && type != ElementType.Level2Title && type != ElementType.Level3Title && type != ElementType.Body)
		{
			return type == ElementType.Salutation;
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool TryCaptureParagraphOffsets(Paragraphs paragraphs, int expectedCount, out int[] rangeStarts, out int[] rangeEnds)
	{
		rangeStarts = null;
		rangeEnds = null;
		if (paragraphs == null || expectedCount < 0)
		{
			return false;
		}
		List<int> list = new List<int>(expectedCount);
		List<int> list2 = new List<int>(expectedCount);
		try
		{
			foreach (Paragraph paragraph in paragraphs)
			{
				Paragraph value = paragraph;
				Microsoft.Office.Interop.Word.Range value2 = null;
				try
				{
					value2 = value.Range;
					list.Add(value2.Start);
					list2.Add(value2.End);
				}
				finally
				{
					ComObjectRelease.Release(ref value2, "StyleBasedFormatEngine.Offsets.Range");
					ComObjectRelease.Release(ref value, "StyleBasedFormatEngine.Offsets.Paragraph");
				}
			}
		}
		catch (Exception ex)
		{
			LogService.Warn("StyleBasedFormatEngine paragraph offset enumeration failed, fallback to per-paragraph path", ex);
			return false;
		}
		if (list.Count != expectedCount)
		{
			LogService.Warn("StyleBasedFormatEngine paragraph offset count mismatch, fallback to per-paragraph path: enumerated=" + list.Count + ", expected=" + expectedCount);
			return false;
		}
		rangeStarts = list.ToArray();
		rangeEnds = list2.ToArray();
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ParagraphComplianceState[] BuildComplianceStates(Paragraphs paragraphs, DocumentElementList elements, ElementType[] types, FormatTextStyleDefinition styleDefinition, out int compliantCount, out int styleRefreshCount, out int fullRebuildCount)
	{
		if (paragraphs == null)
		{
			throw new ArgumentNullException("paragraphs");
		}
		if (elements != null && elements.Items != null)
		{
			if (types == null)
			{
				throw new ArgumentNullException("types");
			}
			if (styleDefinition == null)
			{
				throw new ArgumentNullException("styleDefinition");
			}
			ParagraphComplianceState[] array = new ParagraphComplianceState[types.Length];
			compliantCount = 0;
			styleRefreshCount = 0;
			fullRebuildCount = 0;
			ParagraphComplianceContext context = new ParagraphComplianceContext();
			for (int i = 0; i < types.Length; i++)
			{
				ElementType elementType = types[i];
				if (elementType == ElementType.Unknown || elementType == ElementType.DocumentNumber || elementType == ElementType.Table || elementType == ElementType.TableHeader || elementType == ElementType.Image)
				{
					array[i] = ParagraphComplianceState.NotApplicable;
					continue;
				}
				Paragraph value = null;
				Microsoft.Office.Interop.Word.Range value2 = null;
				Document value3 = null;
				try
				{
					value = paragraphs[i + 1];
					value2 = value.Range;
					value3 = value2.Document;
					ParagraphComplianceState paragraphComplianceState = ParagraphFormatComplianceEvaluator.Evaluate(value2, value3, elements.Items[i], elementType, types, i, styleDefinition, context);
					if (paragraphComplianceState == ParagraphComplianceState.NotApplicable)
					{
						paragraphComplianceState = ParagraphComplianceState.NeedsFullRebuild;
					}
					array[i] = paragraphComplianceState;
					switch (paragraphComplianceState)
					{
					case ParagraphComplianceState.NeedsFullRebuild:
						fullRebuildCount++;
						break;
					case ParagraphComplianceState.NeedsStyleRefresh:
						styleRefreshCount++;
						break;
					case ParagraphComplianceState.Compliant:
						compliantCount++;
						break;
					}
				}
				finally
				{
					if (value3 != null)
					{
						ComObjectRelease.Release(ref value3, "StyleBasedFormatEngine.Compliance.Document");
					}
					if (value2 != null)
					{
						ComObjectRelease.Release(ref value2, "StyleBasedFormatEngine.Compliance.Range");
					}
					ComObjectRelease.Release(ref value, "StyleBasedFormatEngine.Compliance.Paragraph");
				}
			}
			return array;
		}
		throw new ArgumentNullException("elements");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ElementType[] BuildSelectionTypes(DocumentElementList elements, int paragraphCount)
	{
		if (paragraphCount <= 0)
		{
			return new ElementType[0];
		}
		ElementType[] array = new ElementType[paragraphCount];
		for (int i = 0; i < array.Length; i++)
		{
			array[i] = ElementType.Unknown;
		}
		if (elements == null || elements.Items == null)
		{
			throw new InvalidOperationException("选区样式化排版必须使用分析层产出的 DocumentElementList。");
		}
		foreach (DocumentElement item in elements.Items)
		{
			if (item != null)
			{
				if (item.ScopeParagraphIndex <= 0 || item.ScopeParagraphIndex > paragraphCount)
				{
					throw new InvalidOperationException("选区元素缺少有效的作用域段落序号：scopeIndex=" + item.ScopeParagraphIndex + ", documentIndex=" + item.ParagraphIndex + ", paragraphCount=" + paragraphCount);
				}
				array[item.ScopeParagraphIndex - 1] = item.Type;
			}
		}
		return array;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static DocumentElement FindElementForRange(DocumentElementList elements, int start, int end)
	{
		if (elements == null || elements.Items == null)
		{
			throw new InvalidOperationException("选区段落范围匹配必须使用分析层产出的 DocumentElementList。");
		}
		foreach (DocumentElement item in elements.Items)
		{
			if (item != null && !item.IsEmpty)
			{
				if (item.RangeStart <= start && item.RangeEnd >= end)
				{
					return item;
				}
				if (Math.Abs(item.RangeStart - start) <= 1)
				{
					return item;
				}
			}
		}
		return null;
	}

	public static void RepairMixedHeadingBodyFonts(Document doc, FormatConfig cfg, DocumentElementList elements)
	{
		MixedHeadingBodyStyleService.RepairMixedHeadingBodyFonts(doc, cfg, elements);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static long LogPerf(List<string> lines, string runId, string step, Stopwatch sw, long previousMs)
	{
		long num = sw?.ElapsedMilliseconds ?? 0;
		long num2 = Math.Max(0L, num - previousMs);
		lines?.Add("[FORMAT-PERF-INNER] " + runId + " | " + step + " | step=" + num2 + "ms | total=" + num + "ms");
		return num;
	}

	private static void FlushPerf(List<string> lines)
	{
		if (lines != null && lines.Count != 0)
		{
			LogService.Info(string.Join(Environment.NewLine, lines));
			lines.Clear();
		}
	}

	private static int GetProgressInterval(int totalParagraphs)
	{
		if (totalParagraphs < 100)
		{
			return 20;
		}
		if (totalParagraphs < 500)
		{
			return 50;
		}
		return 100;
	}
}
