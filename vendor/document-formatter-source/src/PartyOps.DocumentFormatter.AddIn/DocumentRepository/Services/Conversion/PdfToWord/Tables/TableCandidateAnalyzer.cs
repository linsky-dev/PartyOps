using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace DocumentRepository.Services.Conversion.PdfToWord.Tables;

public static class TableCandidateAnalyzer
{
	private sealed class TextLineView
	{
		public float Baseline;

		public float StartX;

		public float EndX;

		public float FontSize;

		public string Text;

		public List<PdfTextElement> Elements;
	}

	private sealed class TextBlockView
	{
		public float StartX;

		public float EndX;

		public List<PdfTextElement> Elements;
	}

	private sealed class AxisBoundary
	{
		public float Position;

		public float AxisMin;

		public float AxisMax;

		public List<PdfLineFrame> Segments = new List<PdfLineFrame>();

		public List<float[]> MergedSpans = new List<float[]>();

		public float Span => AxisMax - AxisMin;

		public bool Covers(float lo, float hi, float snap)
		{
			foreach (float[] mergedSpan in MergedSpans)
			{
				if (!(mergedSpan[0] > lo + snap) && !(mergedSpan[1] < hi - snap))
				{
					return true;
				}
			}
			return false;
		}
	}

	private sealed class GroupVLine
	{
		public float X;

		public List<float[]> Spans = new List<float[]>();

		public float MinY
		{
			get
			{
				float num = float.MaxValue;
				foreach (float[] span in Spans)
				{
					if (!(span[0] >= num))
					{
						num = span[0];
					}
				}
				return num;
			}
		}

		public float MaxY
		{
			get
			{
				float num = float.MinValue;
				foreach (float[] span in Spans)
				{
					if (span[1] > num)
					{
						num = span[1];
					}
				}
				return num;
			}
		}

		public bool CoversBand(float bandBottom, float bandTop, float snap)
		{
			foreach (float[] span in Spans)
			{
				if (!(span[0] > bandBottom + snap) && span[1] >= bandTop - snap)
				{
					return true;
				}
			}
			return false;
		}
	}

	public static TableAnalysisResult Analyze(PdfPageContent page, TableAnalysisOptions options)
	{
		TableAnalysisResult tableAnalysisResult = new TableAnalysisResult();
		if (page == null)
		{
			return tableAnalysisResult;
		}
		if (options == null)
		{
			options = new TableAnalysisOptions();
		}
		List<TextLineView> textRows = BuildTextRows(page.Elements, options.LineBaselineTolerance);
		List<AxisBoundary> list = BuildAxisBoundaries(page.LineFrames, PdfLineFrameOrientation.Horizontal, options.LineSnapTolerance, options.SegmentMergeGap, options.MinHorizontalLineLength);
		List<AxisBoundary> list2 = BuildAxisBoundaries(page.LineFrames, PdfLineFrameOrientation.Vertical, options.LineSnapTolerance, options.SegmentMergeGap, options.MinVerticalLineLength);
		List<PdfTableRegion> list3 = new List<PdfTableRegion>();
		if (list2.Count >= options.MinColumns + 1 && list.Count >= options.MinRows + 1)
		{
			foreach (PdfTableCandidate item in AnalyzeWireframe(page, list, list2, textRows, options))
			{
				tableAnalysisResult.Candidates.Add(item);
				list3.Add(item.Region);
			}
		}
		else if (list.Count >= options.MinRows + 1 && list2.Count < options.MinColumns + 1)
		{
			tableAnalysisResult.Candidates.Add(BuildHorizontalOnlyCandidate(page, list, list2.Count, textRows, options));
			list3.Add(tableAnalysisResult.Candidates[tableAnalysisResult.Candidates.Count - 1].Region);
		}
		PdfTableCandidate pdfTableCandidate = AnalyzeBorderless(page, textRows, list3, options);
		if (pdfTableCandidate != null)
		{
			tableAnalysisResult.Candidates.Add(pdfTableCandidate);
		}
		foreach (PdfTableCandidate candidate in tableAnalysisResult.Candidates)
		{
			PdfTableLayoutPlan pdfTableLayoutPlan = PdfTableLayoutPlan.FromCandidate(candidate);
			if (pdfTableLayoutPlan != null)
			{
				tableAnalysisResult.ConfirmedTables.Add(pdfTableLayoutPlan);
			}
		}
		tableAnalysisResult.Candidates = tableAnalysisResult.Candidates.OrderByDescending((PdfTableCandidate c) => (c.Region == null) ? 0f : c.Region.TopY).ToList();
		tableAnalysisResult.ConfirmedTables = tableAnalysisResult.ConfirmedTables.OrderByDescending((PdfTableLayoutPlan p) => p.AnchorY).ToList();
		return tableAnalysisResult;
	}

	private static List<TextLineView> BuildTextRows(IList<PdfTextElement> elements, float baselineTolerance)
	{
		List<TextLineView> list = new List<TextLineView>();
		if (elements == null || elements.Count == 0)
		{
			return list;
		}
		List<PdfTextElement> list2 = (from e in elements
			orderby e.Baseline descending, e.X
			select e).ToList();
		List<PdfTextElement> list3 = new List<PdfTextElement>();
		float num = 0f;
		float num2 = 0f;
		foreach (PdfTextElement item in list2)
		{
			if (list3.Count != 0)
			{
				if (item.Baseline >= num - baselineTolerance && !(item.Baseline > num2 + baselineTolerance))
				{
					list3.Add(item);
					if (!(item.Baseline >= num))
					{
						num = item.Baseline;
					}
					if (!(item.Baseline <= num2))
					{
						num2 = item.Baseline;
					}
				}
				else
				{
					list.Add(FinalizeTextRow(list3));
					list3 = new List<PdfTextElement> { item };
					num = (num2 = item.Baseline);
				}
			}
			else
			{
				list3.Add(item);
				num = (num2 = item.Baseline);
			}
		}
		if (list3.Count > 0)
		{
			list.Add(FinalizeTextRow(list3));
		}
		return list;
	}

	private static TextLineView FinalizeTextRow(List<PdfTextElement> elements)
	{
		elements.Sort((PdfTextElement a, PdfTextElement b) => a.X.CompareTo(b.X));
		StringBuilder stringBuilder = new StringBuilder();
		foreach (PdfTextElement element in elements)
		{
			if (!string.IsNullOrEmpty(element.Text))
			{
				stringBuilder.Append(element.Text);
			}
		}
		return new TextLineView
		{
			Baseline = elements.Average((PdfTextElement e) => e.Baseline),
			StartX = elements.Min((PdfTextElement e) => e.X),
			EndX = elements.Max((PdfTextElement e) => e.X + e.Width),
			FontSize = elements.Max((PdfTextElement e) => e.FontSize),
			Text = stringBuilder.ToString(),
			Elements = elements
		};
	}

	private static List<AxisBoundary> BuildAxisBoundaries(IList<PdfLineFrame> frames, PdfLineFrameOrientation orientation, float snap, float mergeGap, float minLength)
	{
		List<AxisBoundary> list = new List<AxisBoundary>();
		if (frames == null || frames.Count == 0)
		{
			return list;
		}
		foreach (PdfLineFrame item in (from f in frames
			where f.Orientation == orientation
			orderby (orientation != PdfLineFrameOrientation.Horizontal) ? f.StartX : f.StartY
			select f).ToList())
		{
			float num = ((orientation == PdfLineFrameOrientation.Horizontal) ? item.StartY : item.StartX);
			AxisBoundary axisBoundary = null;
			foreach (AxisBoundary item2 in list)
			{
				if (Math.Abs(item2.Position - num) <= snap)
				{
					axisBoundary = item2;
					break;
				}
			}
			if (axisBoundary == null)
			{
				axisBoundary = new AxisBoundary
				{
					Position = num
				};
				list.Add(axisBoundary);
			}
			axisBoundary.Segments.Add(item);
		}
		foreach (AxisBoundary item3 in list)
		{
			item3.Segments.Sort((PdfLineFrame a, PdfLineFrame b) => ((orientation == PdfLineFrameOrientation.Horizontal) ? a.StartX : a.StartY).CompareTo((orientation == PdfLineFrameOrientation.Horizontal) ? b.StartX : b.StartY));
			float num2 = float.MaxValue;
			float num3 = float.MinValue;
			float num4 = float.MinValue;
			float num5 = float.MinValue;
			foreach (PdfLineFrame segment in item3.Segments)
			{
				float num6 = ((orientation == PdfLineFrameOrientation.Horizontal) ? segment.StartX : segment.StartY);
				float num7 = ((orientation == PdfLineFrameOrientation.Horizontal) ? segment.EndX : segment.EndY);
				if (num5 == float.MinValue)
				{
					num4 = num6;
					num5 = num7;
				}
				else if (!(num6 <= num5 + mergeGap))
				{
					item3.MergedSpans.Add(new float[2] { num4, num5 });
					num4 = num6;
					num5 = num7;
				}
				else
				{
					num5 = Math.Max(num5, num7);
				}
				num2 = Math.Min(num2, num6);
				num3 = Math.Max(num3, num7);
			}
			if (num5 != float.MinValue)
			{
				item3.MergedSpans.Add(new float[2] { num4, num5 });
			}
			item3.AxisMin = num2;
			item3.AxisMax = num3;
		}
		list = list.Where((AxisBoundary b) => b.Span >= minLength).ToList();
		if (orientation == PdfLineFrameOrientation.Horizontal)
		{
			list.Sort((AxisBoundary a, AxisBoundary b) => b.Position.CompareTo(a.Position));
		}
		else
		{
			list.Sort((AxisBoundary a, AxisBoundary b) => a.Position.CompareTo(b.Position));
		}
		return list;
	}

	private static List<PdfTableCandidate> AnalyzeWireframe(PdfPageContent page, List<AxisBoundary> hBounds, List<AxisBoundary> vBounds, List<TextLineView> textRows, TableAnalysisOptions options)
	{
		List<PdfTableCandidate> list = new List<PdfTableCandidate>();
		float lineSnapTolerance = options.LineSnapTolerance;
		List<GroupVLine> list2 = new List<GroupVLine>();
		foreach (AxisBoundary vBound in vBounds)
		{
			foreach (float[] mergedSpan in vBound.MergedSpans)
			{
				list2.Add(new GroupVLine
				{
					X = vBound.Position,
					Spans = { mergedSpan }
				});
			}
		}
		List<List<GroupVLine>> list3 = new List<List<GroupVLine>>();
		List<float> list4 = new List<float>();
		List<float> list5 = new List<float>();
		foreach (GroupVLine item in list2.OrderByDescending((GroupVLine r) => r.MaxY))
		{
			int num = -1;
			for (int num2 = 0; num2 < list3.Count; num2++)
			{
				if (item.MaxY >= list4[num2] - lineSnapTolerance && item.MinY <= list5[num2] + lineSnapTolerance)
				{
					num = num2;
					break;
				}
			}
			if (num >= 0)
			{
				GroupVLine groupVLine = null;
				foreach (GroupVLine item2 in list3[num])
				{
					if (!(Math.Abs(item2.X - item.X) > lineSnapTolerance))
					{
						groupVLine = item2;
						break;
					}
				}
				if (groupVLine != null)
				{
					groupVLine.Spans.Add(item.Spans[0]);
				}
				else
				{
					list3[num].Add(item);
				}
				list4[num] = Math.Min(list4[num], item.MinY);
				list5[num] = Math.Max(list5[num], item.MaxY);
			}
			else
			{
				list3.Add(new List<GroupVLine> { item });
				list4.Add(item.MinY);
				list5.Add(item.MaxY);
			}
		}
		for (int num3 = 0; num3 < list3.Count; num3++)
		{
			PdfTableCandidate pdfTableCandidate = BuildWireframeCandidate(page, list3[num3], hBounds, textRows, options);
			if (pdfTableCandidate != null)
			{
				list.Add(pdfTableCandidate);
			}
		}
		return list;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static PdfTableCandidate BuildWireframeCandidate(PdfPageContent page, List<GroupVLine> groupVLines, List<AxisBoundary> hBounds, List<TextLineView> textRows, TableAnalysisOptions options)
	{
		float lineSnapTolerance = options.LineSnapTolerance;
		List<AxisBoundary> list = new List<AxisBoundary>();
		foreach (AxisBoundary hBound in hBounds)
		{
			HashSet<float> hashSet = new HashSet<float>();
			foreach (GroupVLine groupVLine in groupVLines)
			{
				if (!(hBound.Position < groupVLine.MinY - lineSnapTolerance) && !(hBound.Position > groupVLine.MaxY + lineSnapTolerance) && !(hBound.AxisMin > groupVLine.X + lineSnapTolerance) && hBound.AxisMax >= groupVLine.X - lineSnapTolerance)
				{
					hashSet.Add(groupVLine.X);
				}
			}
			if (hashSet.Count >= 2)
			{
				list.Add(hBound);
			}
		}
		if (list.Count < options.MinRows + 1)
		{
			return null;
		}
		List<GroupVLine> list2 = new List<GroupVLine>();
		foreach (GroupVLine groupVLine2 in groupVLines)
		{
			int num = 0;
			foreach (AxisBoundary item in list)
			{
				if (!(item.Position < groupVLine2.MinY - lineSnapTolerance) && !(item.Position > groupVLine2.MaxY + lineSnapTolerance) && !(item.AxisMin > groupVLine2.X + lineSnapTolerance) && !(item.AxisMax < groupVLine2.X - lineSnapTolerance))
				{
					num++;
				}
			}
			if (num >= 2)
			{
				list2.Add(groupVLine2);
			}
		}
		if (list2.Count >= options.MinColumns + 1)
		{
			list2.Sort((GroupVLine a, GroupVLine b) => a.X.CompareTo(b.X));
			list.Sort((AxisBoundary a, AxisBoundary b) => b.Position.CompareTo(a.Position));
			PdfTableCandidate pdfTableCandidate = new PdfTableCandidate
			{
				HasWireframe = true
			};
			pdfTableCandidate.Region = new PdfTableRegion(page.PageIndex, list2[0].X, list2[list2.Count - 1].X, list[0].Position, list[list.Count - 1].Position);
			for (int num2 = 0; num2 < list2.Count - 1; num2++)
			{
				pdfTableCandidate.Columns.Add(new PdfTableColumn(num2, list2[num2].X, list2[num2 + 1].X));
			}
			int num3 = list.Count - 1;
			float num4 = 1f;
			foreach (GroupVLine item2 in list2)
			{
				int num5 = 0;
				for (int num6 = 0; num6 < num3; num6++)
				{
					if (item2.CoversBand(list[num6 + 1].Position, list[num6].Position, lineSnapTolerance))
					{
						num5++;
					}
				}
				float val = ((num3 > 0) ? ((float)num5 * 1f / (float)num3) : 0f);
				num4 = Math.Min(num4, val);
			}
			float width = pdfTableCandidate.Region.Width;
			float num7 = 1f;
			foreach (AxisBoundary item3 in list)
			{
				float num8 = 0f;
				if (width > 0f)
				{
					foreach (float[] mergedSpan in item3.MergedSpans)
					{
						float val2 = Math.Min(mergedSpan[1], pdfTableCandidate.Region.RightX) - Math.Max(mergedSpan[0], pdfTableCandidate.Region.LeftX);
						num8 = Math.Max(num8, Math.Max(0f, val2));
					}
				}
				if (width > 0f)
				{
					num7 = Math.Min(num7, Math.Max(0f, num8) / width);
				}
			}
			bool flag = HasMergeAwareHorizontalBoundaries(list, list2, lineSnapTolerance);
			bool flag2 = (pdfTableCandidate.ClosedGridFrame = num4 >= options.VerticalCoverageForHigh && (num7 >= options.HorizontalSpanForHigh || flag));
			pdfTableCandidate.Evidence.Add(new PdfTableDetectionEvidence((!flag2) ? PdfTableEvidenceKind.Auxiliary : PdfTableEvidenceKind.Strong, flag2 ? "ClosedGrid" : "PartialGrid", $"竖线{list2.Count}条(最低覆盖{num4:P0}) 横线{list.Count}条(最低横跨{num7:P0})"));
			BuildWireframeRows(pdfTableCandidate, list, list2, page.Elements, options);
			int num9 = InferVerticalMerges(pdfTableCandidate, list, list2, options);
			if (num9 > 0)
			{
				pdfTableCandidate.Evidence.Add(new PdfTableDetectionEvidence(PdfTableEvidenceKind.Strong, "VerticalMergeGridEvidence", $"{num9} 个纵向续接格由列边界对齐缺线+空续格共同证明"));
			}
			pdfTableCandidate.Evidence.Add(new PdfTableDetectionEvidence(PdfTableEvidenceKind.Strong, "GridShape", pdfTableCandidate.RowCount + "行x" + pdfTableCandidate.ColumnCount + "列"));
			float num10 = ComputeDensity(pdfTableCandidate.Rows);
			pdfTableCandidate.Evidence.Add(new PdfTableDetectionEvidence((!(num10 >= options.DenseRowRatio)) ? PdfTableEvidenceKind.Auxiliary : PdfTableEvidenceKind.Strong, "RowDensity", $"跨≥2格行占比 {num10:P0}"));
			float num11 = ComputeRowPitchRatio(list.Select((AxisBoundary h) => h.Position).ToList());
			if (num11 > 0f && !(num11 > options.RowPitchStabilityForHigh))
			{
				pdfTableCandidate.Evidence.Add(new PdfTableDetectionEvidence(PdfTableEvidenceKind.Auxiliary, "StableRowPitch", $"行高max/min={num11:F2}"));
			}
			if (HasColumnLeftAlignment(pdfTableCandidate))
			{
				pdfTableCandidate.Evidence.Add(new PdfTableDetectionEvidence(PdfTableEvidenceKind.Auxiliary, "ColumnTextAlignment", "列内文字左缘聚簇"));
			}
			EvaluateCounterEvidence(pdfTableCandidate, RowTexts(pdfTableCandidate), page, flag2, borderless: false, options);
			if (!pdfTableCandidate.HasCounterEvidence)
			{
				if (flag2 && num10 >= options.DenseRowRatio)
				{
					pdfTableCandidate.Confidence = PdfTableConfidence.High;
				}
				else
				{
					pdfTableCandidate.Confidence = PdfTableConfidence.Medium;
				}
			}
			else
			{
				pdfTableCandidate.Confidence = PdfTableConfidence.Low;
			}
			return pdfTableCandidate;
		}
		return null;
	}

	private static void BuildWireframeRows(PdfTableCandidate candidate, List<AxisBoundary> gridHLines, List<GroupVLine> gridVLines, IList<PdfTextElement> pageElements, TableAnalysisOptions options)
	{
		float lineSnapTolerance = options.LineSnapTolerance;
		int num = gridHLines.Count - 1;
		int num2 = gridVLines.Count - 1;
		for (int i = 0; i < num; i++)
		{
			float position = gridHLines[i].Position;
			float position2 = gridHLines[i + 1].Position;
			int[] array = new int[num2];
			int num3 = 0;
			for (int j = 0; j < num2; j++)
			{
				if (j == 0)
				{
					array[j] = 0;
					continue;
				}
				if (!gridVLines[j].CoversBand(position2, position, lineSnapTolerance))
				{
					array[j] = num3;
					continue;
				}
				num3 = j;
				array[j] = j;
			}
			List<PdfTextElement>[] array2 = new List<PdfTextElement>[num2];
			for (int k = 0; k < num2; k++)
			{
				array2[k] = new List<PdfTextElement>();
			}
			if (pageElements != null)
			{
				foreach (PdfTextElement pageElement in pageElements)
				{
					if (pageElement.Baseline <= position2 - 1f || pageElement.Baseline > position + 1f)
					{
						continue;
					}
					float centerX = pageElement.CenterX;
					if (centerX >= candidate.Region.LeftX - lineSnapTolerance * 2f && !(centerX > candidate.Region.RightX + lineSnapTolerance * 2f))
					{
						int l;
						for (l = 0; l < num2 - 1 && centerX >= gridVLines[l + 1].X; l++)
						{
						}
						array2[array[l]].Add(pageElement);
					}
					else
					{
						candidate.UnassignedElementCount++;
					}
				}
			}
			List<PdfTableCell> list = new List<PdfTableCell>();
			for (int m = 0; m < num2; m++)
			{
				bool flag = array[m] != m;
				int columnSpan = 1;
				if (!flag)
				{
					int num4 = 0;
					for (int n = m + 1; n < num2 && array[n] == m; n++)
					{
						num4++;
					}
					columnSpan = num4 + 1;
				}
				list.Add(new PdfTableCell(i, m, columnSpan, flag, GroupElementsIntoTextLines(array2[m], options.LineBaselineTolerance)));
			}
			candidate.Rows.Add(new PdfTableRow(i, position, position2, list));
		}
	}

	private static IEnumerable<IEnumerable<PdfTextElement>> GroupElementsIntoTextLines(List<PdfTextElement> elements, float baselineTolerance)
	{
		List<List<PdfTextElement>> list = new List<List<PdfTextElement>>();
		if (elements == null || elements.Count == 0)
		{
			return list;
		}
		foreach (PdfTextElement item in elements.OrderByDescending((PdfTextElement e) => e.Baseline))
		{
			int num = list.Count;
			for (int num2 = 0; num2 < list.Count; num2++)
			{
				float num3 = list[num2].Average((PdfTextElement e) => e.Baseline);
				if (Math.Abs(num3 - item.Baseline) > baselineTolerance)
				{
					if (num3 < item.Baseline - baselineTolerance && num == list.Count)
					{
						num = num2;
					}
					continue;
				}
				InsertByX(list[num2], item);
				num = -1;
				break;
			}
			if (num >= 0)
			{
				list.Insert(num, new List<PdfTextElement> { item });
			}
		}
		return list;
	}

	private static void InsertByX(IList<PdfTextElement> line, PdfTextElement element)
	{
		int index = line.Count;
		for (int i = 0; i < line.Count; i++)
		{
			if (element.X < line[i].X)
			{
				index = i;
				break;
			}
		}
		line.Insert(index, element);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static PdfTableCandidate AnalyzeBorderless(PdfPageContent page, List<TextLineView> textRows, List<PdfTableRegion> claimedRegions, TableAnalysisOptions options)
	{
		List<TextLineView> list = textRows.Where((TextLineView r) => !claimedRegions.Any((PdfTableRegion region) => region.Contains((r.StartX + r.EndX) / 2f, r.Baseline, options.LineSnapTolerance))).ToList();
		if (list.Count < options.MinRows)
		{
			return null;
		}
		list = ExcludeStandaloneBorderlessCaption(page, list, options, out var excluded);
		if (list.Count < options.MinRows)
		{
			return null;
		}
		List<float> list2 = new List<float>();
		List<int> lineOfOccurrence = new List<int>();
		for (int num = 0; num < list.Count; num++)
		{
			List<PdfTextElement> elements = list[num].Elements;
			for (int num2 = 1; num2 < elements.Count; num2++)
			{
				if (!(elements[num2].X - (elements[num2 - 1].X + elements[num2 - 1].Width) <= options.BlockGapThreshold))
				{
					list2.Add(elements[num2].X);
					lineOfOccurrence.Add(num);
				}
			}
		}
		if (list2.Count == 0)
		{
			return null;
		}
		List<List<float>> list3 = new List<List<float>>();
		List<HashSet<int>> clusterLines = new List<HashSet<int>>();
		foreach (var item in from p in list2.Select((float x, int i) => new
			{
				X = x,
				Line = lineOfOccurrence[i]
			})
			orderby p.X
			select p)
		{
			if (list3.Count == 0 || !(item.X - list3[list3.Count - 1].Average() <= options.ColumnClusterTolerance))
			{
				list3.Add(new List<float>());
				clusterLines.Add(new HashSet<int>());
			}
			list3[list3.Count - 1].Add(item.X);
			clusterLines[clusterLines.Count - 1].Add(item.Line);
		}
		int supportThreshold = Math.Max(options.MinColumnSupportRows, (int)Math.Ceiling((float)list.Count * options.ColumnSupportRatio));
		float margin = list.Min((TextLineView r) => r.StartX);
		List<float> list4 = (from c in list3.Select((List<float> c, int i) => new
			{
				Mean = c.Average(),
				Lines = clusterLines[i].Count
			})
			where c.Lines >= supportThreshold && c.Mean > margin + 10f
			select c.Mean into x
			orderby x
			select x).ToList();
		if (list4.Count == 0)
		{
			return null;
		}
		PdfTableCandidate candidate = new PdfTableCandidate
		{
			HasWireframe = false
		};
		candidate.Region = new PdfTableRegion(page.PageIndex, margin, list.Max((TextLineView r) => r.EndX), list.Max((TextLineView r) => r.Baseline + r.FontSize), list.Min((TextLineView r) => r.Baseline - r.FontSize * 0.3f));
		for (int num3 = 0; num3 <= list4.Count; num3++)
		{
			candidate.Columns.Add(new PdfTableColumn(num3, (num3 == 0) ? candidate.Region.LeftX : list4[num3 - 1], (num3 == list4.Count) ? candidate.Region.RightX : list4[num3]));
		}
		for (int num4 = 0; num4 < list.Count; num4++)
		{
			TextLineView textLineView = list[num4];
			List<PdfTextElement>[] array = new List<PdfTextElement>[list4.Count + 1];
			for (int num5 = 0; num5 <= list4.Count; num5++)
			{
				array[num5] = new List<PdfTextElement>();
			}
			foreach (PdfTextElement element in textLineView.Elements)
			{
				int num6;
				for (num6 = 0; num6 < list4.Count && !(element.CenterX < list4[num6]); num6++)
				{
				}
				array[num6].Add(element);
			}
			List<PdfTableCell> list5 = new List<PdfTableCell>();
			for (int num7 = 0; num7 <= list4.Count; num7++)
			{
				list5.Add(new PdfTableCell(num4, num7, 1, isMergedContinuation: false, GroupElementsIntoTextLines(array[num7], options.LineBaselineTolerance)));
			}
			candidate.Rows.Add(new PdfTableRow(num4, textLineView.Baseline + textLineView.FontSize, textLineView.Baseline - textLineView.FontSize * 0.3f, list5));
		}
		float num8 = ComputeDensity(candidate.Rows);
		float num9 = ComputeRowPitchRatio(list.Select((TextLineView r) => r.Baseline).ToList());
		candidate.Evidence.Add(new PdfTableDetectionEvidence(PdfTableEvidenceKind.Auxiliary, "GapColumnClusters", $"空白列边界{list4.Count}个（支持≥{supportThreshold}行）"));
		if (excluded)
		{
			candidate.Evidence.Add(new PdfTableDetectionEvidence(PdfTableEvidenceKind.Auxiliary, "StandaloneCaptionExcluded", "居中短行与后续稳定行簇存在明显垂直间隔，作为表题留在表格外"));
		}
		candidate.Evidence.Add(new PdfTableDetectionEvidence(PdfTableEvidenceKind.Auxiliary, "RowDensity", $"跨≥2格行占比 {num8:P0}"));
		bool flag = num9 > 0f && num9 <= options.RowPitchStabilityForHigh;
		if (flag)
		{
			candidate.Evidence.Add(new PdfTableDetectionEvidence(PdfTableEvidenceKind.Strong, "StableRowPitch", $"行距max/min={num9:F2}"));
		}
		if (HasColumnLeftAlignment(candidate))
		{
			candidate.Evidence.Add(new PdfTableDetectionEvidence(PdfTableEvidenceKind.Auxiliary, "ColumnTextAlignment", "列内文字左缘聚簇"));
		}
		if (list4.Count < 2)
		{
			candidate.Evidence.Add(new PdfTableDetectionEvidence(PdfTableEvidenceKind.Counter, "SingleColumnBoundary", "仅1个空白列边界，不足以确认表格"));
		}
		EvaluateCounterEvidence(candidate, RowTexts(candidate), page, closedGrid: false, borderless: true, options);
		candidate.Confidence = PdfTableConfidence.Low;
		bool flag2 = num9 > 0f && num9 <= options.BorderlessHighRowPitchRatio;
		bool flag3 = HasColumnLeftAlignment(candidate);
		bool flag4 = num8 >= options.BorderlessHighDenseRowRatio;
		bool flag5 = HasHeaderAndDataTypeEvidence(candidate);
		bool flag6 = candidate.Rows.All((PdfTableRow r) => r.NonEmptyCellCount >= Math.Max(2, candidate.ColumnCount - 1));
		if (!candidate.HasCounterEvidence && list4.Count >= 2 && candidate.RowCount >= 4 && flag4 && flag2 && flag3 && flag6 && flag5)
		{
			candidate.Evidence.Add(new PdfTableDetectionEvidence(PdfTableEvidenceKind.Strong, "BorderlessStableGrid", "稳定列簇+高稠密行+严格行距+表头/数据类型证据"));
			candidate.Confidence = PdfTableConfidence.High;
		}
		else if (!candidate.HasCounterEvidence && list4.Count >= 2 && num8 >= options.DenseRowRatio && flag)
		{
			candidate.Evidence.Add(new PdfTableDetectionEvidence(PdfTableEvidenceKind.Auxiliary, "BorderlessColumnStability", "空白列簇稳定但强证据不足，保留为普通文字"));
		}
		return candidate;
	}

	private static List<TextLineView> ExcludeStandaloneBorderlessCaption(PdfPageContent page, List<TextLineView> rows, TableAnalysisOptions options, out bool excluded)
	{
		excluded = false;
		if (page != null && rows != null && rows.Count >= options.MinRows + 1)
		{
			TextLineView textLineView = rows[0];
			TextLineView textLineView2 = rows[1];
			float num = textLineView.Baseline - textLineView2.Baseline;
			List<float> list = new List<float>();
			for (int i = 2; i < rows.Count; i++)
			{
				float num2 = rows[i - 1].Baseline - rows[i].Baseline;
				if (!(num2 <= 0f))
				{
					list.Add(num2);
				}
			}
			if (list.Count < 2)
			{
				return rows;
			}
			list.Sort();
			int num3 = list.Count / 2;
			float num4 = (((list.Count & 1) == 1) ? list[num3] : ((list[num3 - 1] + list[num3]) / 2f));
			bool num5 = Math.Abs((textLineView.StartX + textLineView.EndX) / 2f - page.Width / 2f) <= Math.Max(20f, page.Width * 0.05f);
			bool flag = textLineView.EndX - textLineView.StartX <= page.Width * 0.7f;
			bool flag2 = num >= Math.Max(num4 * 1.35f, Math.Max(textLineView.FontSize, 1f) * 1.8f);
			if (!num5 || !flag || !flag2)
			{
				return rows;
			}
			excluded = true;
			return rows.Skip(1).ToList();
		}
		return rows;
	}

	private static bool HasMergeAwareHorizontalBoundaries(IList<AxisBoundary> hLines, IList<GroupVLine> vLines, float snap)
	{
		if (hLines == null || vLines == null || hLines.Count < 3 || vLines.Count < 4)
		{
			return false;
		}
		float x = vLines[0].X;
		float x2 = vLines[vLines.Count - 1].X;
		if (!hLines[0].Covers(x, x2, snap) || !hLines[hLines.Count - 1].Covers(x, x2, snap))
		{
			return false;
		}
		bool result = false;
		for (int i = 1; i < hLines.Count - 1; i++)
		{
			int num = 0;
			int num2 = 0;
			for (int j = 0; j < vLines.Count - 1; j++)
			{
				int num3 = ClassifyHorizontalCoverage(hLines[i], vLines[j].X, vLines[j + 1].X, snap);
				if (num3 < 0)
				{
					return false;
				}
				if (num3 != 1)
				{
					num2++;
				}
				else
				{
					num++;
				}
			}
			if (num == 0)
			{
				return false;
			}
			if (num2 > 0)
			{
				result = true;
			}
		}
		return result;
	}

	private static int ClassifyHorizontalCoverage(AxisBoundary line, float left, float right, float snap)
	{
		if (line.Covers(left, right, snap))
		{
			return 1;
		}
		float val = left + snap;
		float val2 = right - snap;
		foreach (float[] mergedSpan in line.MergedSpans)
		{
			if (Math.Min(mergedSpan[1], val2) - Math.Max(mergedSpan[0], val) > snap)
			{
				return -1;
			}
		}
		return 0;
	}

	private static int InferVerticalMerges(PdfTableCandidate candidate, IList<AxisBoundary> hLines, IList<GroupVLine> vLines, TableAnalysisOptions options)
	{
		if (candidate != null && candidate.Rows.Count >= 2)
		{
			int count = candidate.Rows.Count;
			int columnCount = candidate.ColumnCount;
			PdfCellVerticalMerge[,] array = new PdfCellVerticalMerge[count, columnCount];
			int num = 0;
			for (int i = 1; i < count; i++)
			{
				AxisBoundary line = hLines[i];
				for (int j = 0; j < columnCount; j++)
				{
					PdfTableCell pdfTableCell = candidate.Rows[i - 1].Cells[j];
					PdfTableCell pdfTableCell2 = candidate.Rows[i].Cells[j];
					if (pdfTableCell.IsMergedContinuation || pdfTableCell2.IsMergedContinuation || pdfTableCell.ColumnSpan != 1 || pdfTableCell2.ColumnSpan != 1 || pdfTableCell2.HasText || ClassifyHorizontalCoverage(line, vLines[j].X, vLines[j + 1].X, options.LineSnapTolerance) != 0)
					{
						continue;
					}
					bool flag = array[i - 1, j] == PdfCellVerticalMerge.Restart || array[i - 1, j] == PdfCellVerticalMerge.Continue;
					if (pdfTableCell.HasText || flag)
					{
						if (!flag)
						{
							array[i - 1, j] = PdfCellVerticalMerge.Restart;
						}
						array[i, j] = PdfCellVerticalMerge.Continue;
						num++;
					}
				}
			}
			if (num == 0)
			{
				return 0;
			}
			List<PdfTableRow> list = new List<PdfTableRow>();
			for (int k = 0; k < count; k++)
			{
				List<PdfTableCell> list2 = new List<PdfTableCell>();
				foreach (PdfTableCell cell in candidate.Rows[k].Cells)
				{
					list2.Add(new PdfTableCell(cell.RowIndex, cell.ColumnIndex, cell.ColumnSpan, cell.IsMergedContinuation, cell.TextLines, array[k, cell.ColumnIndex]));
				}
				list.Add(new PdfTableRow(candidate.Rows[k].Index, candidate.Rows[k].TopY, candidate.Rows[k].BottomY, list2));
			}
			candidate.Rows.Clear();
			{
				foreach (PdfTableRow item in list)
				{
					candidate.Rows.Add(item);
				}
				return num;
			}
		}
		return 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static PdfTableCandidate BuildHorizontalOnlyCandidate(PdfPageContent page, List<AxisBoundary> hBounds, int vBoundCount, List<TextLineView> textRows, TableAnalysisOptions options)
	{
		PdfTableCandidate pdfTableCandidate = TryBuildThreeLineCandidate(page, hBounds, textRows, options);
		if (pdfTableCandidate != null)
		{
			return pdfTableCandidate;
		}
		return new PdfTableCandidate
		{
			HasWireframe = false,
			Region = new PdfTableRegion(topY: hBounds.Max((AxisBoundary h) => h.Position), bottomY: hBounds.Min((AxisBoundary h) => h.Position), pageIndex: page.PageIndex, leftX: hBounds.Min((AxisBoundary h) => h.AxisMin), rightX: hBounds.Max((AxisBoundary h) => h.AxisMax)),
			Evidence = 
			{
				new PdfTableDetectionEvidence(PdfTableEvidenceKind.Counter, (vBoundCount == 0) ? "OnlyHorizontalLines" : "NoInnerVerticalLines", $"页内有{hBounds.Count}条长横线但竖线边界仅{vBoundCount}条，按三线版头/分隔线处理，不判表格")
			},
			Confidence = PdfTableConfidence.Low
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static PdfTableCandidate TryBuildThreeLineCandidate(PdfPageContent page, List<AxisBoundary> hBounds, List<TextLineView> textRows, TableAnalysisOptions options)
	{
		if (hBounds != null && hBounds.Count == 3 && textRows != null)
		{
			List<AxisBoundary> list = hBounds.OrderByDescending((AxisBoundary h) => h.Position).ToList();
			float num = list.Max((AxisBoundary h) => h.AxisMin);
			float num2 = list.Min((AxisBoundary h) => h.AxisMax);
			if (num2 - num >= options.MinHorizontalLineLength)
			{
				if (Math.Max(list.Max((AxisBoundary h) => h.AxisMin) - list.Min((AxisBoundary h) => h.AxisMin), list.Max((AxisBoundary h) => h.AxisMax) - list.Min((AxisBoundary h) => h.AxisMax)) <= options.OpenTableColumnDriftTolerance)
				{
					float top = list[0].Position;
					float separator = list[1].Position;
					float bottom = list[2].Position;
					List<TextLineView> list2 = (from r in textRows
						where r.Baseline < top - 0.5f && r.Baseline > separator + 0.5f
						orderby r.Baseline descending
						select r).ToList();
					List<TextLineView> list3 = (from r in textRows
						where r.Baseline < separator - 0.5f && r.Baseline > bottom + 0.5f
						orderby r.Baseline descending
						select r).ToList();
					if (list2.Count == 1 && list3.Count >= 2)
					{
						List<TextLineView> list4 = new List<TextLineView> { list2[0] };
						list4.AddRange(list3);
						List<List<TextBlockView>> list5 = list4.Select((TextLineView r) => SplitTextRowIntoBlocks(r, options.BlockGapThreshold)).ToList();
						int columnCount = list5[0].Count;
						if (columnCount >= options.MinColumns && columnCount <= 8)
						{
							if (!list5.Any((List<TextBlockView> row) => row.Count != columnCount))
							{
								float[] array = new float[columnCount];
								int c;
								for (c = 0; c < columnCount; c++)
								{
									float num3 = list5.Min((List<TextBlockView> row) => row[c].StartX);
									if (!(list5.Max((List<TextBlockView> row) => row[c].StartX) - num3 <= options.OpenTableColumnDriftTolerance))
									{
										return null;
									}
									array[c] = list5.Average((List<TextBlockView> row) => row[c].StartX);
									if (c > 0 && array[c] - array[c - 1] <= options.BlockGapThreshold)
									{
										return null;
									}
								}
								List<float> list6 = new List<float>();
								int c2;
								for (c2 = 1; c2 < columnCount; c2++)
								{
									float num4 = list5.Max((List<TextBlockView> row) => row[c2 - 1].EndX);
									float num5 = list5.Min((List<TextBlockView> row) => row[c2].StartX);
									if (!(num5 - num4 >= options.BlockGapThreshold))
									{
										return null;
									}
									list6.Add((num4 + num5) / 2f);
								}
								PdfTableCandidate pdfTableCandidate = BuildOpenGridCandidate(page, list4, list5, list6, num, num2, top, bottom, options);
								if (pdfTableCandidate == null || !HasHeaderAndDataTypeEvidence(pdfTableCandidate))
								{
									return null;
								}
								EvaluateCounterEvidence(pdfTableCandidate, RowTexts(pdfTableCandidate), page, closedGrid: false, borderless: false, options);
								if (!pdfTableCandidate.HasCounterEvidence)
								{
									pdfTableCandidate.HasWireframe = true;
									pdfTableCandidate.ClosedGridFrame = false;
									pdfTableCandidate.Confidence = PdfTableConfidence.High;
									pdfTableCandidate.Evidence.Add(new PdfTableDetectionEvidence(PdfTableEvidenceKind.Strong, "ThreeLineTable", "三条同宽横线+独立表头带+稳定多列数据"));
									return pdfTableCandidate;
								}
								return null;
							}
							return null;
						}
						return null;
					}
					return null;
				}
				return null;
			}
			return null;
		}
		return null;
	}

	private static PdfTableCandidate BuildOpenGridCandidate(PdfPageContent page, IList<TextLineView> rows, IList<List<TextBlockView>> blocks, IList<float> boundaries, float left, float right, float top, float bottom, TableAnalysisOptions options)
	{
		int num = boundaries.Count + 1;
		PdfTableCandidate pdfTableCandidate = new PdfTableCandidate();
		pdfTableCandidate.Region = new PdfTableRegion(page.PageIndex, left, right, top, bottom);
		for (int i = 0; i < num; i++)
		{
			pdfTableCandidate.Columns.Add(new PdfTableColumn(i, (i == 0) ? left : boundaries[i - 1], (i == num - 1) ? right : boundaries[i]));
		}
		for (int j = 0; j < rows.Count; j++)
		{
			float topY = ((j == 0) ? top : ((rows[j - 1].Baseline + rows[j].Baseline) / 2f));
			float bottomY = ((j == rows.Count - 1) ? bottom : ((rows[j].Baseline + rows[j + 1].Baseline) / 2f));
			List<PdfTableCell> list = new List<PdfTableCell>();
			for (int k = 0; k < num; k++)
			{
				list.Add(new PdfTableCell(j, k, 1, isMergedContinuation: false, new IOrderedEnumerable<PdfTextElement>[1] { blocks[j][k].Elements.OrderBy((PdfTextElement e) => e.X) }));
			}
			pdfTableCandidate.Rows.Add(new PdfTableRow(j, topY, bottomY, list));
		}
		return pdfTableCandidate;
	}

	private static List<TextBlockView> SplitTextRowIntoBlocks(TextLineView row, float gapThreshold)
	{
		List<TextBlockView> list = new List<TextBlockView>();
		if (row == null || row.Elements == null)
		{
			return list;
		}
		foreach (PdfTextElement item in row.Elements.OrderBy((PdfTextElement e) => e.X))
		{
			if (list.Count == 0 || !(item.X - list[list.Count - 1].EndX <= gapThreshold))
			{
				list.Add(new TextBlockView
				{
					StartX = item.X,
					EndX = item.X + item.Width,
					Elements = new List<PdfTextElement> { item }
				});
			}
			else
			{
				TextBlockView textBlockView = list[list.Count - 1];
				textBlockView.EndX = Math.Max(textBlockView.EndX, item.X + item.Width);
				textBlockView.Elements.Add(item);
			}
		}
		return list;
	}

	private static bool HasHeaderAndDataTypeEvidence(PdfTableCandidate candidate)
	{
		if (candidate == null || candidate.Rows.Count < 3 || candidate.ColumnCount < 2)
		{
			return false;
		}
		PdfTableRow pdfTableRow = candidate.Rows[0];
		int num = pdfTableRow.Cells.Count((PdfTableCell c) => c.HasText);
		int num2 = pdfTableRow.Cells.Count((PdfTableCell c) => c.TextLines.SelectMany((IReadOnlyList<PdfTextElement> line) => line).Any((PdfTextElement e) => e?.Bold ?? false));
		bool flag = num >= 2 && num2 * 2 >= num;
		int num3 = 0;
		for (int num4 = 1; num4 < candidate.Rows.Count; num4++)
		{
			if (candidate.Rows[num4].Cells.Count((PdfTableCell c) => IsNumericData(c.PlainText)) >= Math.Min(2, candidate.ColumnCount - 1))
			{
				num3++;
			}
		}
		bool flag2 = num3 * 2 >= candidate.Rows.Count - 1;
		bool flag3 = pdfTableRow.Cells.Any((PdfTableCell c) => c.HasText && !IsNumericData(c.PlainText));
		if (!flag)
		{
			return flag2 && flag3;
		}
		return true;
	}

	private static bool IsNumericData(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		int num = 0;
		int num2 = 0;
		foreach (char c in text)
		{
			if (char.IsDigit(c))
			{
				num++;
			}
			else if ((c >= '一' && c <= '鿿') || char.IsLetter(c))
			{
				num2++;
			}
		}
		if (num != 0)
		{
			if (num < num2)
			{
				return text.IndexOf('%') >= 0;
			}
			return true;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void EvaluateCounterEvidence(PdfTableCandidate candidate, List<string> rowTexts, PdfPageContent page, bool closedGrid, bool borderless, TableAnalysisOptions options)
	{
		foreach (string rowText in rowTexts)
		{
			if (LooksLikeDirectoryEntry(rowText))
			{
				candidate.Evidence.Add(new PdfTableDetectionEvidence(PdfTableEvidenceKind.Counter, "DirectoryLeaderDots", "行尾点前导符+页码 " + PrivacySanitizer.LengthAndHash(rowText)));
				break;
			}
		}
		if (rowTexts.Count > 0)
		{
			string text = ((rowTexts[0] == null) ? string.Empty : rowTexts[0].TrimStart(Array.Empty<char>()));
			if (text.StartsWith("附件：", StringComparison.Ordinal) || text.StartsWith("附件:", StringComparison.Ordinal) || text.StartsWith("附：", StringComparison.Ordinal))
			{
				candidate.Evidence.Add(new PdfTableDetectionEvidence(PdfTableEvidenceKind.Counter, "AttachmentList", "附件清单行组"));
			}
		}
		if (rowTexts.Count <= 2)
		{
			foreach (string rowText2 in rowTexts)
			{
				if (!LooksLikeDateOnlyLine(rowText2))
				{
					continue;
				}
				candidate.Evidence.Add(new PdfTableDetectionEvidence(PdfTableEvidenceKind.Counter, "SignatureDate", "整行日期（署名/落款区） " + PrivacySanitizer.LengthAndHash(rowText2)));
				break;
			}
		}
		if (rowTexts.Count >= 3)
		{
			int num = 0;
			int num2 = 0;
			foreach (string rowText3 in rowTexts)
			{
				string text2 = rowText3;
				if (StartsWithListNumber(text2))
				{
					num++;
				}
				if (text2 == null)
				{
					text2 = string.Empty;
				}
				if (text2.Length >= 15)
				{
					num2++;
				}
			}
			if (num * 2 >= rowTexts.Count && num2 * 2 >= rowTexts.Count)
			{
				candidate.Evidence.Add(new PdfTableDetectionEvidence(PdfTableEvidenceKind.Counter, "NumberedListProse", $"{num}/{rowTexts.Count} 行以编号起头且行长≥15字"));
			}
		}
		if (borderless && candidate.Rows.Count >= 3 && candidate.ColumnCount >= 2)
		{
			int num3 = 0;
			foreach (PdfTableRow row in candidate.Rows)
			{
				for (int i = 1; i < row.Cells.Count; i++)
				{
					if ((row.Cells[i].PlainText ?? string.Empty).Length >= 25)
					{
						num3++;
						break;
					}
				}
			}
			if ((double)num3 * 1.0 / (double)candidate.Rows.Count >= 0.6)
			{
				candidate.Evidence.Add(new PdfTableDetectionEvidence(PdfTableEvidenceKind.Counter, "MultiColumnProse", $"{num3}/{candidate.Rows.Count} 行非首列是长文本（≥25字）"));
			}
		}
		if (!closedGrid && candidate.Region != null && page.Height > 0f)
		{
			float num4 = page.Height * options.HeaderFooterRatio;
			if (candidate.Region.BottomY >= page.Height - num4 || !(candidate.Region.TopY > num4))
			{
				candidate.Evidence.Add(new PdfTableDetectionEvidence(PdfTableEvidenceKind.Counter, "HeaderFooterZone", $"区域整体位于页顶/页底 {options.HeaderFooterRatio:P0} 带内"));
			}
		}
	}

	private static float ComputeDensity(IList<PdfTableRow> rows)
	{
		if (rows == null || rows.Count == 0)
		{
			return 0f;
		}
		int num = 0;
		foreach (PdfTableRow row in rows)
		{
			if (row.NonEmptyCellCount >= 2)
			{
				num++;
			}
		}
		return (float)num * 1f / (float)rows.Count;
	}

	private static float ComputeRowPitchRatio(IList<float> positionsDesc)
	{
		float num = float.MaxValue;
		float num2 = 0f;
		for (int i = 1; i < positionsDesc.Count; i++)
		{
			float num3 = positionsDesc[i - 1] - positionsDesc[i];
			if (num3 > 0.01f)
			{
				if (num3 < num)
				{
					num = num3;
				}
				if (!(num3 <= num2))
				{
					num2 = num3;
				}
			}
		}
		if (num == float.MaxValue || num2 <= 0f)
		{
			return 0f;
		}
		return num2 / num;
	}

	private static bool HasColumnLeftAlignment(PdfTableCandidate candidate)
	{
		for (int i = 1; i < candidate.ColumnCount; i++)
		{
			List<float> list = new List<float>();
			foreach (PdfTableRow row in candidate.Rows)
			{
				if (i >= row.Cells.Count)
				{
					continue;
				}
				PdfTableCell pdfTableCell = row.Cells[i];
				if (!pdfTableCell.IsMergedContinuation && pdfTableCell.TextLines.Count != 0)
				{
					IReadOnlyList<PdfTextElement> readOnlyList = pdfTableCell.TextLines[0];
					if (readOnlyList.Count > 0)
					{
						list.Add(readOnlyList[0].X);
					}
				}
			}
			if (list.Count >= 2 && list.Max() - list.Min() <= 5f)
			{
				return true;
			}
		}
		return false;
	}

	private static List<string> RowTexts(PdfTableCandidate candidate)
	{
		List<string> list = new List<string>();
		foreach (PdfTableRow row in candidate.Rows)
		{
			StringBuilder stringBuilder = new StringBuilder();
			foreach (PdfTableCell cell in row.Cells)
			{
				if (!cell.IsMergedContinuation)
				{
					stringBuilder.Append(cell.PlainText);
				}
			}
			list.Add(stringBuilder.ToString());
		}
		return list;
	}

	private static bool LooksLikeDirectoryEntry(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return false;
		}
		string text2 = text.TrimEnd(Array.Empty<char>());
		int num = text2.Length - 1;
		int num2 = 0;
		while (num >= 0 && char.IsDigit(text2[num]))
		{
			num--;
			num2++;
		}
		if (num2 >= 1 && num2 <= 4)
		{
			while (num >= 0 && text2[num] == ' ')
			{
				num--;
			}
			int num3 = 0;
			while (num >= 0 && (text2[num] == '.' || text2[num] == '·' || text2[num] == '…' || text2[num] == '‥'))
			{
				num--;
				num3++;
			}
			return num3 >= 4;
		}
		return false;
	}

	private static bool LooksLikeDateOnlyLine(string text)
	{
		if (!string.IsNullOrWhiteSpace(text))
		{
			string text2 = text.Trim();
			int pos = 0;
			if (ConsumeDigits(text2, ref pos, 4, 4))
			{
				if (!ConsumeChar(text2, ref pos, '年'))
				{
					return false;
				}
				if (!ConsumeDigits(text2, ref pos, 1, 2))
				{
					return false;
				}
				if (!ConsumeChar(text2, ref pos, '月'))
				{
					return false;
				}
				if (ConsumeDigits(text2, ref pos, 1, 2))
				{
					if (!ConsumeChar(text2, ref pos, '日'))
					{
						return false;
					}
					return pos == text2.Length;
				}
				return false;
			}
			return false;
		}
		return false;
	}

	private static bool ConsumeDigits(string text, ref int pos, int min, int max)
	{
		while (pos < text.Length && text[pos] == ' ')
		{
			pos++;
		}
		int num = 0;
		while (pos < text.Length && char.IsDigit(text[pos]) && num < max)
		{
			pos++;
			num++;
		}
		return num >= min;
	}

	private static bool ConsumeChar(string text, ref int pos, char expected)
	{
		while (pos < text.Length && text[pos] == ' ')
		{
			pos++;
		}
		if (pos >= text.Length || text[pos] != expected)
		{
			return false;
		}
		pos++;
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool StartsWithListNumber(string text)
	{
		if (!string.IsNullOrWhiteSpace(text))
		{
			string text2 = text.TrimStart(Array.Empty<char>());
			if (text2.Length == 0)
			{
				return false;
			}
			if (text2.Length >= 2 && "一二三四五六七八九十百千".IndexOf(text2[0]) >= 0 && text2[1] == '、')
			{
				return true;
			}
			if (text2.Length < 3 || text2[0] != '（' || "一二三四五六七八九十百千".IndexOf(text2[1]) < 0 || text2[2] != '）')
			{
				if (char.IsDigit(text2[0]))
				{
					int i;
					for (i = 0; i < text2.Length && char.IsDigit(text2[i]); i++)
					{
					}
					if (i <= 2 && i < text2.Length && (text2[i] == '、' || text2[i] == '.'))
					{
						return true;
					}
				}
				if (text2.Length >= 3 && text2[0] == '第' && (char.IsDigit(text2[1]) || "一二三四五六七八九十百千".IndexOf(text2[1]) >= 0) && (text2.IndexOf('章') == 2 || text2.IndexOf('条') == 2 || text2.IndexOf('节') == 2))
				{
					return true;
				}
				return false;
			}
			return true;
		}
		return false;
	}
}
