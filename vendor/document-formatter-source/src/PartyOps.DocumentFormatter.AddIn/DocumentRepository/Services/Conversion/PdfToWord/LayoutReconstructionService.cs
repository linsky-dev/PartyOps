using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using DocumentRepository.Services.Conversion.PdfToWord.Tables;
using DocumentRepository.Services.Logging;

namespace DocumentRepository.Services.Conversion.PdfToWord;

public static class LayoutReconstructionService
{
	internal static Func<string, string, IReadOnlyList<string>, CrossPageTableConservationReport> CrossPageConservationVerifierForTesting = null;

	private static readonly Regex PageNumberWhitespacePattern = new Regex("[\\s\u3000]+", RegexOptions.CultureInvariant);

	private static readonly Regex PageNumberSyntaxPattern = new Regex("^(?:[0-9]{1,7}|[\\-–—―－]{1,2}[0-9]{1,7}[\\-–—―－]{1,2}|第[0-9]{1,7}页|[0-9]{1,7}[/／][0-9]{1,7})$", RegexOptions.CultureInvariant);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ReconstructedDocument Reconstruct(IList<PdfPageContent> pages, LayoutReconstructionOptions options, Action<int, int> reconstructProgress = null)
	{
		if (pages != null)
		{
			if (options == null)
			{
				options = new LayoutReconstructionOptions();
			}
			TableAnalysisOptions tableOptions = CreateTableAnalysisOptions(options);
			DecorationDetectionResult decorations = TryDetectDecorations(pages, options);
			ReconstructedDocument reconstructedDocument = new ReconstructedDocument();
			List<PageTableFragmentFacts> list = new List<PageTableFragmentFacts>(pages.Count);
			Dictionary<CrossPageTableId, TableBlock> dictionary = new Dictionary<CrossPageTableId, TableBlock>();
			for (int i = 0; i < pages.Count; i++)
			{
				int count = list.Count;
				ReconstructPage(reconstructedDocument, pages[i], options, tableOptions, decorations, list, dictionary);
				if (list.Count == count)
				{
					list.Add(new PageTableFragmentFacts(pages[i].PageIndex, pages[i].Width, pages[i].Height, pages[i].Rotation, null));
				}
				reconstructProgress?.Invoke(i + 1, pages.Count);
			}
			TryApplyCrossPageTableContinuations(reconstructedDocument, list, dictionary);
			MergeCrossPageParagraphs(reconstructedDocument.Blocks);
			return reconstructedDocument;
		}
		throw new ArgumentNullException("pages");
	}

	private static TableAnalysisOptions CreateTableAnalysisOptions(LayoutReconstructionOptions options)
	{
		return new TableAnalysisOptions
		{
			LineBaselineTolerance = options.LineBaselineTolerance,
			BlockGapThreshold = options.BlockGapThreshold,
			ColumnClusterTolerance = options.ColumnClusterTolerance,
			ColumnSupportRatio = options.ColumnSupportRatio,
			MinColumnSupportRows = options.MinColumnSupportRows,
			MinRows = Math.Max(1, options.MinTableRows)
		};
	}

	private static void ReconstructPage(ReconstructedDocument document, PdfPageContent page, LayoutReconstructionOptions options, TableAnalysisOptions tableOptions, DecorationDetectionResult decorations, List<PageTableFragmentFacts> tableFragmentPages, Dictionary<CrossPageTableId, TableBlock> tableFragmentBlocks)
	{
		List<ReconstructedLine> lines = BuildLines(page.Elements, options);
		lines = RemovePageNumberLines(lines, page.Height, options);
		if (decorations != null && decorations.RemovedCount > 0)
		{
			lines = RemoveDecorationLines(lines, page, decorations);
		}
		List<KeyValuePair<ReadingOrderKey, DocumentBlock>> list = new List<KeyValuePair<ReadingOrderKey, DocumentBlock>>();
		int sequence = 0;
		if (lines.Count > 0)
		{
			TableAnalysisResult tableAnalysisResult = TryAnalyzeTables(page, tableOptions);
			List<PdfTableRegion> list2 = new List<PdfTableRegion>();
			List<PdfTableRegion> list3 = new List<PdfTableRegion>();
			List<KeyValuePair<PdfTableLayoutPlan, TableBlock>> list4 = new List<KeyValuePair<PdfTableLayoutPlan, TableBlock>>();
			int num = 0;
			if (tableAnalysisResult != null)
			{
				foreach (PdfTableLayoutPlan confirmedTable in tableAnalysisResult.ConfirmedTables)
				{
					list2.Add(confirmedTable.Region);
					list3.Add(confirmedTable.Region);
					TableBlock value = BuildTableBlock(confirmedTable);
					list4.Add(new KeyValuePair<PdfTableLayoutPlan, TableBlock>(confirmedTable, value));
					list.Add(new KeyValuePair<ReadingOrderKey, DocumentBlock>(new ReadingOrderKey(page.PageIndex, confirmedTable.AnchorY, 0, 0, 0, sequence++), value));
				}
				foreach (PdfTableCandidate candidate in tableAnalysisResult.Candidates)
				{
					if (candidate.Confidence == PdfTableConfidence.Medium && candidate.Region != null)
					{
						num++;
						if (candidate.HasWireframe && candidate.ClosedGridFrame)
						{
							list3.Add(candidate.Region);
						}
					}
				}
				if (num > 0)
				{
					LogMediumCandidates(page, tableAnalysisResult);
				}
			}
			List<ReconstructedLine> list5 = PartitionLines(lines, list2);
			foreach (PdfImage image in page.Images)
			{
				if (!(image.Width < 3f) && !(image.Height < 3f))
				{
					list3.Add(new PdfTableRegion(page.PageIndex, image.X, image.X + image.Width, image.Y + image.Height, image.Y));
				}
			}
			CollectTableFragmentFacts(page, tableAnalysisResult, list4, list5, tableFragmentPages, tableFragmentBlocks);
			if (list5.Count > 0)
			{
				foreach (List<ReconstructedLine> item in SplitByTableRegions(list5, list3))
				{
					foreach (ColumnReadingSegment item2 in ColumnRegionSorter.SplitColumns(item, page.Width, options))
					{
						int regionTypeOrdinal = 0;
						int columnOrdinal = 0;
						float regionAnchorY = 0f;
						if (item2.ColumnOrdinal >= 0)
						{
							regionTypeOrdinal = 1 + item2.ColumnOrdinal;
							columnOrdinal = item2.ColumnOrdinal;
							regionAnchorY = item2.ColumnZoneTop;
						}
						BuildParagraphsInto(list, item2.Lines, page, options, regionTypeOrdinal, columnOrdinal, regionAnchorY, ref sequence);
					}
				}
			}
		}
		foreach (PdfImage image2 in page.Images)
		{
			ImageBlock value2 = new ImageBlock
			{
				PageIndex = page.PageIndex,
				Image = image2
			};
			list.Add(new KeyValuePair<ReadingOrderKey, DocumentBlock>(new ReadingOrderKey(page.PageIndex, image2.Y + image2.Height, 0, 0, 0, sequence++), value2));
		}
		foreach (KeyValuePair<ReadingOrderKey, DocumentBlock> item3 in list.OrderBy((KeyValuePair<ReadingOrderKey, DocumentBlock> p) => p.Key))
		{
			document.Blocks.Add(item3.Value);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static DecorationDetectionResult TryDetectDecorations(IList<PdfPageContent> pages, LayoutReconstructionOptions options)
	{
		try
		{
			DecorationDetectionResult decorationDetectionResult = DecorationRegionDetector.Detect(pages, options);
			if (decorationDetectionResult != null && decorationDetectionResult.RemovedCount > 0)
			{
				try
				{
					LogService.Info("DecorationRegionDetector pages=" + pages.Count + " groups=" + decorationDetectionResult.ConfirmedGroupCount + " removedLines=" + decorationDetectionResult.RemovedCount);
				}
				catch
				{
				}
			}
			return decorationDetectionResult;
		}
		catch (Exception ex)
		{
			try
			{
				LogService.Warn("LayoutReconstructionService.DecorationDetectFailed_KeepAllLines " + ((ex == null) ? "unknown" : ex.GetType().Name));
			}
			catch
			{
			}
			return null;
		}
	}

	private static List<ReconstructedLine> RemoveDecorationLines(List<ReconstructedLine> lines, PdfPageContent page, DecorationDetectionResult decorations)
	{
		List<ReconstructedLine> list = new List<ReconstructedLine>(lines.Count);
		foreach (ReconstructedLine line in lines)
		{
			string normalizedText = DecorationRegionDetector.NormalizeForMatch(line.Text);
			if (!decorations.ShouldRemove(page.PageIndex, line.Baseline, normalizedText))
			{
				list.Add(line);
			}
		}
		return list;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static TableAnalysisResult TryAnalyzeTables(PdfPageContent page, TableAnalysisOptions options)
	{
		try
		{
			return TableCandidateAnalyzer.Analyze(page, options);
		}
		catch (Exception ex)
		{
			try
			{
				LogService.Warn("LayoutReconstructionService.TableAnalysisFailed_FallbackToParagraphs " + ((ex == null) ? "unknown" : ex.GetType().Name));
			}
			catch
			{
			}
			return null;
		}
	}

	private static List<ReconstructedLine> PartitionLines(List<ReconstructedLine> lines, List<PdfTableRegion> highRegions)
	{
		List<ReconstructedLine> list = new List<ReconstructedLine>();
		foreach (ReconstructedLine line in lines)
		{
			List<PdfTextElement> list2 = null;
			if (highRegions.Count > 0)
			{
				list2 = new List<PdfTextElement>(line.Elements.Count);
				foreach (PdfTextElement element in line.Elements)
				{
					bool flag = false;
					foreach (PdfTableRegion highRegion in highRegions)
					{
						if (!highRegion.Contains(element.CenterX, element.Baseline, 1.5f))
						{
							continue;
						}
						flag = true;
						break;
					}
					if (!flag)
					{
						list2.Add(element);
					}
				}
				if (list2.Count == 0)
				{
					continue;
				}
			}
			ReconstructedLine item = line;
			if (list2 != null && list2.Count < line.Elements.Count)
			{
				item = FinalizeLine(list2);
			}
			list.Add(item);
		}
		return list;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void LogMediumCandidates(PdfPageContent page, TableAnalysisResult analysis)
	{
		try
		{
			int num = 0;
			int num2 = 0;
			foreach (PdfTableCandidate candidate in analysis.Candidates)
			{
				if (candidate.Confidence == PdfTableConfidence.Medium)
				{
					if (!candidate.HasWireframe || !candidate.ClosedGridFrame)
					{
						num2++;
					}
					else
					{
						num++;
					}
				}
			}
			LogService.Info("LayoutReconstructionService.MediumTableCandidates page=" + (page.PageIndex + 1) + " closedWireframe=" + num + " other=" + num2);
		}
		catch
		{
		}
	}

	private static List<List<ReconstructedLine>> SplitByTableRegions(List<ReconstructedLine> lines, List<PdfTableRegion> hardBoundaryRegions)
	{
		List<List<ReconstructedLine>> list = new List<List<ReconstructedLine>>();
		List<ReconstructedLine> list2 = new List<ReconstructedLine>();
		foreach (ReconstructedLine line in lines)
		{
			if (list2.Count > 0 && hardBoundaryRegions.Count > 0)
			{
				float baseline = list2[list2.Count - 1].Baseline;
				bool flag = false;
				foreach (PdfTableRegion hardBoundaryRegion in hardBoundaryRegions)
				{
					if (SideOfRegion(baseline, hardBoundaryRegion) != SideOfRegion(line.Baseline, hardBoundaryRegion))
					{
						flag = true;
						break;
					}
				}
				if (flag)
				{
					list.Add(list2);
					list2 = new List<ReconstructedLine>();
				}
			}
			list2.Add(line);
		}
		if (list2.Count > 0)
		{
			list.Add(list2);
		}
		return list;
	}

	private static int SideOfRegion(float baseline, PdfTableRegion region)
	{
		if (baseline >= region.TopY - 1f)
		{
			return 0;
		}
		if (baseline > region.BottomY + 1f)
		{
			return 1;
		}
		return 2;
	}

	private static TableBlock BuildTableBlock(PdfTableLayoutPlan plan)
	{
		TableBlock tableBlock = new TableBlock
		{
			PageIndex = plan.PageIndex,
			ColumnCount = plan.ColumnCount,
			BorderStyle = ResolveTableBorderStyle(plan)
		};
		if (plan.ColumnWidths != null && plan.ColumnWidths.Count == plan.ColumnCount)
		{
			tableBlock.ColumnWidths = new List<float>(plan.ColumnWidths);
		}
		foreach (PdfTableRow row in plan.Rows)
		{
			TableRow tableRow = new TableRow();
			tableRow.IsHeader = IsHeaderRow(plan, row);
			foreach (PdfTableCell cell in row.Cells)
			{
				if (!cell.IsMergedContinuation)
				{
					TableCell tableCell = new TableCell
					{
						GridSpan = ((cell.ColumnSpan <= 1) ? 1 : cell.ColumnSpan),
						VerticalMerge = ((cell.VerticalMerge == PdfCellVerticalMerge.Restart) ? VerticalMerge.Restart : ((cell.VerticalMerge == PdfCellVerticalMerge.Continue) ? VerticalMerge.Continue : VerticalMerge.None)),
						VerticalAlign = CellVerticalAlignment.Center
					};
					BuildCellRuns(cell, tableCell.Runs);
					tableRow.Cells.Add(tableCell);
				}
			}
			tableBlock.Rows.Add(tableRow);
		}
		return tableBlock;
	}

	private static TableBorderStyle ResolveTableBorderStyle(PdfTableLayoutPlan plan)
	{
		if (plan != null && plan.Evidence != null)
		{
			if (plan.Evidence.Any([MethodImpl(MethodImplOptions.NoInlining)] (PdfTableDetectionEvidence e) => e != null && string.Equals(e.Name, "BorderlessStableGrid", StringComparison.Ordinal)))
			{
				return TableBorderStyle.None;
			}
			if (plan.Evidence.Any([MethodImpl(MethodImplOptions.NoInlining)] (PdfTableDetectionEvidence e) => e != null && string.Equals(e.Name, "ThreeLineTable", StringComparison.Ordinal)))
			{
				return TableBorderStyle.ThreeLine;
			}
		}
		return TableBorderStyle.FullGrid;
	}

	private static bool IsHeaderRow(PdfTableLayoutPlan plan, PdfTableRow row)
	{
		if (plan.ColumnCount < 3)
		{
			return false;
		}
		if (row.Index > 1)
		{
			return false;
		}
		if (row.NonEmptyCellCount < 2)
		{
			return false;
		}
		if (!IsMergedTitleRow(plan, plan.Rows[0]))
		{
			if (row.Index != 0)
			{
				return false;
			}
			if (!HasBoldEvidence(plan, row))
			{
				return HasContentTypeEvidence(plan, row);
			}
			return true;
		}
		if (row.Index == 1)
		{
			if (!HasBoldEvidence(plan, row))
			{
				return HasContentTypeEvidence(plan, row);
			}
			return true;
		}
		return false;
	}

	private static bool IsMergedTitleRow(PdfTableLayoutPlan plan, PdfTableRow first)
	{
		int num = 0;
		int num2 = 0;
		foreach (PdfTableCell cell in first.Cells)
		{
			if (!cell.IsMergedContinuation)
			{
				num++;
				num2 = Math.Max(num2, cell.ColumnSpan);
			}
		}
		if (num == 1)
		{
			return num2 >= plan.ColumnCount;
		}
		return false;
	}

	private static bool HasBoldEvidence(PdfTableLayoutPlan plan, PdfTableRow candidateRow)
	{
		if (plan.Rows.Count >= 2)
		{
			float num = BoldCellRatio(candidateRow);
			float num2 = 0f;
			int num3 = 0;
			for (int i = candidateRow.Index + 1; i < plan.Rows.Count; i++)
			{
				num2 += BoldCellRatio(plan.Rows[i]);
				num3++;
			}
			if (num3 == 0)
			{
				return false;
			}
			float num4 = num2 / (float)num3;
			if (num >= 0.5f)
			{
				return num > num4 + 0.2f;
			}
			return false;
		}
		return false;
	}

	private static float BoldCellRatio(PdfTableRow row)
	{
		int num = 0;
		int num2 = 0;
		foreach (PdfTableCell cell in row.Cells)
		{
			if (!cell.IsMergedContinuation && cell.TextLines.Count > 0 && cell.TextLines[0].Count > 0)
			{
				num++;
				if (cell.TextLines[0][0].Bold)
				{
					num2++;
				}
			}
		}
		if (num != 0)
		{
			return (float)num2 * 1f / (float)num;
		}
		return 0f;
	}

	private static bool HasContentTypeEvidence(PdfTableLayoutPlan plan, PdfTableRow candidateRow)
	{
		if (plan.Rows.Count < 2)
		{
			return false;
		}
		float num = NumericCellRatio(candidateRow);
		float num2 = 0f;
		int num3 = 0;
		for (int i = candidateRow.Index + 1; i < plan.Rows.Count; i++)
		{
			num2 += NumericCellRatio(plan.Rows[i]);
			num3++;
		}
		if (num3 == 0)
		{
			return false;
		}
		return num2 / (float)num3 >= num + 0.3f;
	}

	private static float NumericCellRatio(PdfTableRow row)
	{
		int num = 0;
		int num2 = 0;
		foreach (PdfTableCell cell in row.Cells)
		{
			if (!cell.IsMergedContinuation && cell.HasText)
			{
				num++;
				if (IsNumericCellText(cell.PlainText))
				{
					num2++;
				}
			}
		}
		if (num != 0)
		{
			return (float)num2 * 1f / (float)num;
		}
		return 0f;
	}

	private static bool IsNumericCellText(string text)
	{
		if (!string.IsNullOrWhiteSpace(text))
		{
			int num = 0;
			string text2 = text.Trim();
			foreach (char c in text2)
			{
				if (char.IsDigit(c))
				{
					num++;
				}
				else if (c != ',' && c != '.' && c != '%' && c != '-' && c != '+' && c != '，' && c != '。' && c != ' ' && c != '\u00a0')
				{
					return false;
				}
			}
			return num > 0;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void BuildCellRuns(PdfTableCell planCell, IList<TextRun> runs)
	{
		bool flag = true;
		foreach (IReadOnlyList<PdfTextElement> textLine in planCell.TextLines)
		{
			if (!flag && runs.Count > 0)
			{
				runs[runs.Count - 1].Text += "\n";
			}
			flag = false;
			foreach (PdfTextElement item in textLine)
			{
				if (!string.IsNullOrEmpty(item.Text))
				{
					string text = FontNameResolver.ToWordFontName(item.FontName);
					TextRun textRun = ((runs.Count > 0) ? runs[runs.Count - 1] : null);
					if (textRun != null && string.Equals(textRun.FontName, text, StringComparison.Ordinal) && textRun.FontSize == item.FontSize && textRun.Bold == item.Bold && textRun.Italic == item.Italic)
					{
						textRun.Text += item.Text;
						continue;
					}
					runs.Add(new TextRun
					{
						Text = item.Text,
						FontName = text,
						FontSize = item.FontSize,
						Bold = item.Bold,
						Italic = item.Italic
					});
				}
			}
		}
	}

	private static List<ReconstructedLine> BuildLines(IList<PdfTextElement> elements, LayoutReconstructionOptions options)
	{
		List<ReconstructedLine> list = new List<ReconstructedLine>();
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
				if (item.Baseline < num - options.LineBaselineTolerance || !(item.Baseline <= num2 + options.LineBaselineTolerance))
				{
					list.Add(FinalizeLine(list3));
					list3 = new List<PdfTextElement>();
					list3.Add(item);
					num = item.Baseline;
					num2 = item.Baseline;
					continue;
				}
				list3.Add(item);
				if (item.Baseline < num)
				{
					num = item.Baseline;
				}
				if (item.Baseline > num2)
				{
					num2 = item.Baseline;
				}
			}
			else
			{
				list3.Add(item);
				num = item.Baseline;
				num2 = item.Baseline;
			}
		}
		if (list3.Count > 0)
		{
			list.Add(FinalizeLine(list3));
		}
		return list.OrderByDescending((ReconstructedLine l) => l.Baseline).ToList();
	}

	internal static ReconstructedLine FinalizeLine(List<PdfTextElement> elements)
	{
		elements.Sort((PdfTextElement a, PdfTextElement b) => a.X.CompareTo(b.X));
		ReconstructedLine obj = new ReconstructedLine
		{
			Elements = elements,
			Baseline = elements.Average((PdfTextElement e) => e.Baseline),
			StartX = elements.Min((PdfTextElement e) => e.X),
			EndX = elements.Max((PdfTextElement e) => e.X + e.Width)
		};
		(float Size, string FontName, string WordFont) tuple = DominantFormat(elements);
		float item = tuple.Size;
		string item2 = tuple.WordFont;
		obj.FontSize = item;
		obj.WordFontName = item2;
		BuildLineText(obj);
		return obj;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static (float Size, string FontName, string WordFont) DominantFormat(List<PdfTextElement> elements)
	{
		var grouping = (from e in elements
			group e by new { e.FontSize, e.FontName } into g
			orderby g.Count() descending
			select g).FirstOrDefault();
		if (grouping == null)
		{
			return (Size: 0f, FontName: string.Empty, WordFont: "宋体");
		}
		return (Size: grouping.Key.FontSize, FontName: grouping.Key.FontName, WordFont: FontNameResolver.ToWordFontName(grouping.Key.FontName));
	}

	private static void BuildLineText(ReconstructedLine line)
	{
		StringBuilder stringBuilder = new StringBuilder();
		List<TextRun> list = new List<TextRun>();
		TextRun textRun = null;
		for (int i = 0; i < line.Elements.Count; i++)
		{
			PdfTextElement pdfTextElement = line.Elements[i];
			if (i > 0)
			{
				PdfTextElement pdfTextElement2 = line.Elements[i - 1];
				float gap = pdfTextElement.X - (pdfTextElement2.X + EstimateAdvance(pdfTextElement2));
				string text = DecideSpace(pdfTextElement2, pdfTextElement, gap, pdfTextElement2.FontSize);
				if (text != null)
				{
					stringBuilder.Append(text);
					if (textRun != null)
					{
						textRun.Text += text;
					}
				}
			}
			stringBuilder.Append(pdfTextElement.Text);
			string text2 = FontNameResolver.ToWordFontName(pdfTextElement.FontName);
			if (textRun != null && string.Equals(textRun.FontName, text2, StringComparison.Ordinal) && textRun.FontSize == pdfTextElement.FontSize && textRun.Bold == pdfTextElement.Bold && textRun.Italic == pdfTextElement.Italic)
			{
				textRun.Text += pdfTextElement.Text;
				continue;
			}
			textRun = new TextRun
			{
				Text = pdfTextElement.Text,
				FontName = text2,
				FontSize = pdfTextElement.FontSize,
				Bold = pdfTextElement.Bold,
				Italic = pdfTextElement.Italic
			};
			list.Add(textRun);
		}
		line.Text = stringBuilder.ToString();
		line.Runs = list;
	}

	private static float EstimateAdvance(PdfTextElement element)
	{
		if (element == null)
		{
			return 0f;
		}
		if (!string.IsNullOrEmpty(element.Text))
		{
			if (!(element.FontSize <= 0.1f))
			{
				if (IsCjkText(element.Text))
				{
					return element.FontSize;
				}
				if (element.Text.Length == 1 && element.Text[0] == ' ')
				{
					return 0f;
				}
				return element.FontSize * 0.5f;
			}
			return 10f;
		}
		return element.FontSize;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string DecideSpace(PdfTextElement prev, PdfTextElement cur, float gap, float fontSize)
	{
		if (gap <= 0.001f)
		{
			return null;
		}
		if (fontSize <= 0.1f)
		{
			fontSize = 10f;
		}
		if (!IsWhitespaceText(prev.Text) && !IsWhitespaceText(cur.Text))
		{
			bool flag = IsCjkText(prev.Text);
			bool flag2 = IsCjkText(cur.Text);
			if (!(flag && flag2))
			{
				if (!(flag || flag2))
				{
					if (gap <= 0.3f * fontSize)
					{
						return null;
					}
					if (gap <= 1.2f * fontSize)
					{
						return " ";
					}
					return "\u3000";
				}
				if (!(gap > 0.45f * fontSize))
				{
					return null;
				}
				if (gap > 1.2f * fontSize)
				{
					return "\u3000";
				}
				return " ";
			}
			if (gap <= 0.6f * fontSize)
			{
				return null;
			}
			return "\u3000";
		}
		return null;
	}

	private static List<ReconstructedLine> RemovePageNumberLines(List<ReconstructedLine> lines, float pageHeight, LayoutReconstructionOptions options)
	{
		if (lines != null && lines.Count != 0)
		{
			float num = pageHeight * options.PageNumberBottomRatio;
			List<ReconstructedLine> list = new List<ReconstructedLine>(lines.Count);
			{
				foreach (ReconstructedLine line in lines)
				{
					if (!(line.Baseline <= num) || !LooksLikePageNumber(line.Text))
					{
						list.Add(line);
					}
				}
				return list;
			}
		}
		return lines;
	}

	private static bool LooksLikePageNumber(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		string text2 = PageNumberWhitespacePattern.Replace(text, string.Empty);
		if (text2.Length == 0 || text2.Length > 20)
		{
			return false;
		}
		return PageNumberSyntaxPattern.IsMatch(text2);
	}

	private static ParagraphAlignment DetectAlignment(ReconstructedLine line, float pageWidth, float marginX)
	{
		float num = line.EndX - line.StartX;
		if (!(num > 0f))
		{
			return ParagraphAlignment.Left;
		}
		float num2 = pageWidth - marginX;
		float num3 = num2 - marginX;
		if (!(num3 > 0f))
		{
			return ParagraphAlignment.Left;
		}
		float num4 = line.StartX - marginX;
		float num5 = num2 - line.EndX;
		if (num4 > 8f && !(num5 <= 8f) && !(num >= num3 * 0.85f) && Math.Abs(num4 - num5) < 20f)
		{
			return ParagraphAlignment.Center;
		}
		if (num5 < 6f && num4 > 8f && num < num3 * 0.6f)
		{
			return ParagraphAlignment.Right;
		}
		return ParagraphAlignment.Left;
	}

	private static void BuildParagraphsInto(List<KeyValuePair<ReadingOrderKey, DocumentBlock>> pageBlocks, List<ReconstructedLine> lines, PdfPageContent page, LayoutReconstructionOptions options, int regionTypeOrdinal, int columnOrdinal, float regionAnchorY, ref int sequence)
	{
		List<float> list = new List<float>();
		for (int i = 1; i < lines.Count; i++)
		{
			float num = lines[i - 1].Baseline - lines[i].Baseline;
			if (!(num <= 0f))
			{
				list.Add(num);
			}
		}
		float num2 = ((list.Count > 0) ? Median(list) : ((lines.Count > 0) ? (lines[0].FontSize * 1.3f) : 20f));
		float typicalFontSize = Median((from l in lines
			where l.FontSize > 0.1f
			select l.FontSize).ToList());
		float num3 = lines.Min((ReconstructedLine l) => l.StartX);
		ParagraphBlock paragraphBlock = null;
		float num4 = 0f;
		float num5 = 0f;
		int num6 = 0;
		for (int num7 = 0; num7 < lines.Count; num7++)
		{
			ReconstructedLine reconstructedLine = lines[num7];
			bool flag = num7 == 0;
			bool flag2 = reconstructedLine.StartX > num3 + options.IndentThreshold;
			bool flag3 = !flag && lines[num7 - 1].StartX > num3 + options.IndentThreshold;
			float num8 = (flag ? 0f : (lines[num7 - 1].Baseline - reconstructedLine.Baseline));
			float val = ((num5 > num8 * 1.35f || num5 > num2 * 2f) ? num2 : num5);
			bool flag4 = !flag && num8 > Math.Max(num2, val) * options.ParagraphGapFactor && num8 - num2 > 4f;
			bool flag5 = !flag && HaveCompatibleBoundaryRuns(lines[num7 - 1], reconstructedLine);
			bool flag6 = !flag && (Math.Abs(reconstructedLine.FontSize - lines[num7 - 1].FontSize) > 1f || !string.Equals(reconstructedLine.WordFontName, lines[num7 - 1].WordFontName, StringComparison.Ordinal)) && !flag5;
			bool flag7 = !flag && Math.Abs(reconstructedLine.FontSize - lines[num7 - 1].FontSize) > 4f;
			float num9 = page.Width - num3 - num3;
			bool flag8 = reconstructedLine.EndX - reconstructedLine.StartX < num9 * 0.75f;
			bool flag9 = !flag && lines[num7 - 1].EndX - lines[num7 - 1].StartX < num9 * 0.75f;
			bool flag10 = flag6 && (flag4 || flag8 || flag7 || flag2 != flag3);
			string text = (flag ? string.Empty : lines[num7 - 1].Text);
			bool flag11 = !flag && IsNumberedItemStart(text);
			bool flag12 = !flag && EndsWithTerminalPunctuation(text);
			bool flag13 = !flag && IsNumberedItemStart(text) && (flag12 || !flag9);
			bool flag14 = !flag && IsCenteredHeadingLine(lines[num7 - 1], page.Width, typicalFontSize) && IsCenteredHeadingLine(reconstructedLine, page.Width, typicalFontSize) && Math.Abs(lines[num7 - 1].FontSize - reconstructedLine.FontSize) <= 1f && string.Equals(lines[num7 - 1].WordFontName, reconstructedLine.WordFontName, StringComparison.Ordinal) && num8 <= Math.Max(num2 * 1.5f, reconstructedLine.FontSize * 2.2f);
			bool flag15 = flag2 && !flag3 && !flag14 && !flag13;
			bool flag16 = IsNumberedItemStart(reconstructedLine.Text) && (flag || flag12 || flag11 || flag8);
			bool flag17 = IsAttachmentItemStart(reconstructedLine.Text);
			bool flag18 = !flag && IsDateOnlyLine(reconstructedLine.Text);
			bool flag19 = !flag && IsSignatureLine(reconstructedLine, num3, num9);
			bool flag20 = !flag && flag9 && IsNumberedItemStart(text) && !EndsWithTerminalPunctuation(text) && flag6;
			bool flag21 = !flag && flag9 && flag12 && flag2;
			if (!(flag || flag4 || flag10 || flag15 || flag16 || flag17 || flag18 || flag19 || flag20 || flag21))
			{
				AppendLine(paragraphBlock, reconstructedLine);
			}
			else
			{
				ParagraphAlignment paragraphAlignment = DetectAlignment(reconstructedLine, page.Width, num3);
				int num10 = ((flag2 && paragraphAlignment == ParagraphAlignment.Left) ? ((int)Math.Round((reconstructedLine.StartX - num3) / Math.Max(1f, reconstructedLine.FontSize))) : 0);
				if (num10 < 1 || num10 > 4)
				{
					num10 = 0;
				}
				paragraphBlock = new ParagraphBlock
				{
					PageIndex = page.PageIndex,
					SpaceBefore = ((flag4 && !flag) ? Math.Max(0f, num8 - num2) : 0f),
					Alignment = paragraphAlignment,
					FirstLineIndentChars = num10,
					LeftEdgeX = num3,
					RightEdgeX = page.Width - num3,
					FirstLineFontSize = reconstructedLine.FontSize,
					LastPageIndex = page.PageIndex
				};
				num4 = reconstructedLine.Baseline + reconstructedLine.FontSize;
				float anchorY = ((regionTypeOrdinal > 0) ? regionAnchorY : num4);
				pageBlocks.Add(new KeyValuePair<ReadingOrderKey, DocumentBlock>(new ReadingOrderKey(page.PageIndex, anchorY, regionTypeOrdinal, columnOrdinal, num6++, sequence++), paragraphBlock));
				AppendLine(paragraphBlock, reconstructedLine);
			}
			paragraphBlock.LastLineStartX = reconstructedLine.StartX;
			paragraphBlock.LastLineEndX = reconstructedLine.EndX;
			paragraphBlock.LastLineFontSize = reconstructedLine.FontSize;
			if (!flag)
			{
				num5 = num8;
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void AppendLine(ParagraphBlock paragraph, ReconstructedLine line)
	{
		string text = ((paragraph.Runs.Count > 0) ? paragraph.Runs[paragraph.Runs.Count - 1].Text : null);
		if (paragraph.Runs.Count > 0 && line.Runs.Count > 0)
		{
			char c = text[text.Length - 1];
			char c2 = line.Runs[0].Text[0];
			if (IsAsciiAlnum(c) && IsAsciiAlnum(c2))
			{
				paragraph.Runs[paragraph.Runs.Count - 1].Text += " ";
			}
		}
		foreach (TextRun run in line.Runs)
		{
			TextRun textRun = ((paragraph.Runs.Count > 0) ? paragraph.Runs[paragraph.Runs.Count - 1] : null);
			if (textRun == null || !string.Equals(textRun.FontName, run.FontName, StringComparison.Ordinal) || textRun.FontSize != run.FontSize || textRun.Bold != run.Bold || textRun.Italic != run.Italic)
			{
				paragraph.Runs.Add(new TextRun
				{
					Text = run.Text,
					FontName = run.FontName,
					FontSize = run.FontSize,
					Bold = run.Bold,
					Italic = run.Italic
				});
			}
			else
			{
				textRun.Text += run.Text;
			}
		}
	}

	private static void CollectTableFragmentFacts(PdfPageContent page, TableAnalysisResult analysis, List<KeyValuePair<PdfTableLayoutPlan, TableBlock>> confirmedPairs, List<ReconstructedLine> paragraphLines, List<PageTableFragmentFacts> fragmentPages, Dictionary<CrossPageTableId, TableBlock> fragmentBlocks)
	{
		if (confirmedPairs == null || confirmedPairs.Count == 0)
		{
			return;
		}
		List<KeyValuePair<PdfTableLayoutPlan, TableBlock>> list = confirmedPairs.OrderByDescending((KeyValuePair<PdfTableLayoutPlan, TableBlock> p) => p.Key.AnchorY).ToList();
		List<TableFragmentFacts> list2 = new List<TableFragmentFacts>(list.Count);
		for (int num = 0; num < list.Count; num++)
		{
			PdfTableLayoutPlan key = list[num].Key;
			TableBlock value = list[num].Value;
			PdfTableRegion region = key.Region;
			bool flag = false;
			bool flag2 = false;
			bool flag3 = false;
			if (paragraphLines != null)
			{
				foreach (ReconstructedLine paragraphLine in paragraphLines)
				{
					if (paragraphLine.Baseline > region.TopY + 2f)
					{
						flag = true;
					}
					else if (paragraphLine.Baseline >= region.BottomY - 2f)
					{
						flag3 = true;
					}
					else
					{
						flag2 = true;
					}
					if (flag && flag2 && flag3)
					{
						break;
					}
				}
			}
			foreach (PdfImage image in page.Images)
			{
				if (!(image.Width < 3f) && image.Height >= 3f)
				{
					float num2 = image.Y + image.Height;
					if (!(image.Y < region.TopY - 2f))
					{
						flag = true;
					}
					else if (num2 <= region.BottomY + 2f)
					{
						flag2 = true;
					}
					else
					{
						flag3 = true;
					}
				}
			}
			TryGetWireframeFlags(analysis, region, out var hasWireframe, out var closedGrid);
			int headerRowIndex = -1;
			CrossPageTableRowSignature headerSignature = null;
			int num3 = Math.Min(2, key.Rows.Count);
			for (int num4 = 0; num4 < num3; num4++)
			{
				if (IsHeaderRow(key, key.Rows[num4]))
				{
					headerRowIndex = num4;
					headerSignature = CrossPageTableRowSignature.FromRow(key.Rows[num4]);
					break;
				}
			}
			CrossPageTableRowSignature firstRowSignature = ((key.Rows.Count > 0) ? CrossPageTableRowSignature.FromRow(key.Rows[0]) : null);
			CrossPageTableId crossPageTableId = new CrossPageTableId(page.PageIndex, num);
			fragmentBlocks[crossPageTableId] = value;
			SequenceColumnEvidence sequence = SequenceColumnEvidence.None;
			try
			{
				sequence = TableFragmentFacts.ComputeSequenceEvidence(key, headerRowIndex);
			}
			catch
			{
			}
			list2.Add(new TableFragmentFacts(crossPageTableId, PdfTableConfidence.High, hasWireframe, closedGrid, region.LeftX, region.RightX, region.TopY, region.BottomY, key.ColumnCount, NormalizeColumnBoundaries(key), key.Rows.Count, headerRowIndex, headerSignature, firstRowSignature, flag, flag2, flag3, sequence));
		}
		fragmentPages.Add(new PageTableFragmentFacts(page.PageIndex, page.Width, page.Height, page.Rotation, list2));
	}

	private static List<float> NormalizeColumnBoundaries(PdfTableLayoutPlan plan)
	{
		List<float> list = new List<float>();
		if (plan.ColumnWidths == null || plan.ColumnWidths.Count != plan.ColumnCount)
		{
			return list;
		}
		float num = 0f;
		foreach (float columnWidth in plan.ColumnWidths)
		{
			num += columnWidth;
		}
		if (num <= 0f)
		{
			return list;
		}
		list.Add(0f);
		float num2 = 0f;
		foreach (float columnWidth2 in plan.ColumnWidths)
		{
			num2 += columnWidth2;
			list.Add(num2 / num);
		}
		return list;
	}

	private static void TryGetWireframeFlags(TableAnalysisResult analysis, PdfTableRegion region, out bool hasWireframe, out bool closedGrid)
	{
		hasWireframe = false;
		closedGrid = false;
		if (analysis == null || analysis.Candidates == null || region == null)
		{
			return;
		}
		foreach (PdfTableCandidate candidate in analysis.Candidates)
		{
			PdfTableRegion pdfTableRegion = candidate?.Region;
			if (pdfTableRegion != null && pdfTableRegion.PageIndex == region.PageIndex && pdfTableRegion.LeftX == region.LeftX && pdfTableRegion.RightX == region.RightX && pdfTableRegion.TopY == region.TopY && pdfTableRegion.BottomY == region.BottomY)
			{
				hasWireframe = candidate.HasWireframe;
				closedGrid = candidate.ClosedGridFrame;
				break;
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void TryApplyCrossPageTableContinuations(ReconstructedDocument document, List<PageTableFragmentFacts> fragmentPages, Dictionary<CrossPageTableId, TableBlock> fragmentBlocks)
	{
		if (document == null || fragmentPages == null || fragmentPages.Count < 2 || fragmentBlocks == null || fragmentBlocks.Count == 0)
		{
			return;
		}
		CrossPageTableContinuationResult crossPageTableContinuationResult;
		try
		{
			crossPageTableContinuationResult = CrossPageTableContinuationPlanner.Plan(fragmentPages);
		}
		catch (Exception ex)
		{
			try
			{
				LogService.Warn("LayoutReconstructionService.CrossPageTablePlanFailed_KeepSeparate " + ((ex == null) ? "unknown" : ex.GetType().Name));
				return;
			}
			catch
			{
				return;
			}
		}
		if (crossPageTableContinuationResult == null)
		{
			return;
		}
		MarkRejectedCrossPageTableBoundaries(document, crossPageTableContinuationResult.Evaluations, fragmentBlocks);
		if (crossPageTableContinuationResult.Plans.Count == 0)
		{
			return;
		}
		try
		{
			int num = ApplyCrossPageTableContinuations(document, crossPageTableContinuationResult.Plans, fragmentBlocks);
			if (num > 0)
			{
				try
				{
					LogService.Info("LayoutReconstructionService.CrossPageTableContinuation merged=" + num);
					return;
				}
				catch
				{
					return;
				}
			}
		}
		catch (Exception ex2)
		{
			try
			{
				LogService.Warn("LayoutReconstructionService.CrossPageTableApplyFailed_KeepRemainingSeparate " + ((ex2 == null) ? "unknown" : ex2.GetType().Name));
			}
			catch
			{
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ApplyCrossPageTableContinuations(ReconstructedDocument document, IReadOnlyList<CrossPageTableContinuationPlan> plans, Dictionary<CrossPageTableId, TableBlock> fragmentBlocks)
	{
		Dictionary<CrossPageTableId, TableBlock> dictionary = new Dictionary<CrossPageTableId, TableBlock>();
		Dictionary<TableBlock, int> dictionary2 = new Dictionary<TableBlock, int>();
		foreach (KeyValuePair<CrossPageTableId, TableBlock> fragmentBlock in fragmentBlocks)
		{
			dictionary[fragmentBlock.Key] = fragmentBlock.Value;
			dictionary2[fragmentBlock.Value] = fragmentBlock.Value.Rows.Count;
		}
		int num = 0;
		foreach (CrossPageTableContinuationPlan plan in plans)
		{
			if (plan == null || !dictionary.TryGetValue(plan.PreviousTable, out var value) || !fragmentBlocks.TryGetValue(plan.NextTable, out var value2) || value == null || value2 == null || value == value2)
			{
				continue;
			}
			if (!dictionary2.TryGetValue(value, out var value3) || value.Rows.Count != value3 || value2.Rows.Count != plan.NextRowCount || plan.RepeatedHeaderRowCount != 1 || value2.Rows.Count <= plan.RepeatedHeaderRowCount)
			{
				MarkFollowingTableBoundaryIfAdjacent(document, value, value2);
				continue;
			}
			if (value.ColumnCount != value2.ColumnCount)
			{
				MarkFollowingTableBoundaryIfAdjacent(document, value, value2);
				continue;
			}
			int num2 = document.Blocks.IndexOf(value);
			int num3 = document.Blocks.IndexOf(value2);
			if (num2 >= 0 && num3 == num2 + 1)
			{
				TableBlock tableBlock = fragmentBlocks[plan.PreviousTable];
				if (plan.PreviousHeaderRowIndex >= 0 && plan.PreviousHeaderRowIndex < tableBlock.Rows.Count)
				{
					bool flag = true;
					for (int i = 0; i < plan.RepeatedHeaderRowCount; i++)
					{
						if (!TableRowsTextEqual(tableBlock.Rows[plan.PreviousHeaderRowIndex], value2.Rows[i]))
						{
							flag = false;
							break;
						}
					}
					if (!flag)
					{
						MarkFollowingTableBoundaryIfAdjacent(document, value, value2);
						continue;
					}
					string sourceText = TableBlockText(value) + TableBlockText(value2);
					List<string> list = new List<string>(plan.RepeatedHeaderRowCount);
					for (int j = 0; j < plan.RepeatedHeaderRowCount; j++)
					{
						list.Add(TableRowText(value2.Rows[j]));
					}
					string outputText = TableBlockText(value) + TableRowsText(value2.Rows, plan.RepeatedHeaderRowCount, value2.Rows.Count);
					CrossPageTableConservationReport crossPageTableConservationReport = VerifyCrossPageConservation(sourceText, outputText, list);
					if (!crossPageTableConservationReport.Passed)
					{
						MarkFollowingTableBoundaryIfAdjacent(document, value, value2);
						LogConservationFailure("before", crossPageTableConservationReport);
						continue;
					}
					int count = value.Rows.Count;
					bool nextRemoved = false;
					CrossPageTableConservationReport crossPageTableConservationReport2 = null;
					try
					{
						for (int k = plan.RepeatedHeaderRowCount; k < value2.Rows.Count; k++)
						{
							value.Rows.Add(value2.Rows[k]);
						}
						document.Blocks.RemoveAt(num3);
						nextRemoved = true;
						crossPageTableConservationReport2 = VerifyCrossPageConservation(sourceText, TableBlockText(value), list);
					}
					catch (Exception ex)
					{
						RollbackCrossPageContinuation(document, value, value2, num3, count, nextRemoved);
						MarkFollowingTableBoundaryIfAdjacent(document, value, value2);
						try
						{
							LogService.Warn("LayoutReconstructionService.CrossPageTableAtomicApplyFailed " + ex.GetType().Name);
						}
						catch
						{
						}
						continue;
					}
					if (crossPageTableConservationReport2 != null && crossPageTableConservationReport2.Passed)
					{
						value.NeedsFollowingBoundary = false;
						dictionary[plan.NextTable] = value;
						dictionary2[value] = value3 + plan.NextRowCount - plan.RepeatedHeaderRowCount;
						num++;
					}
					else
					{
						RollbackCrossPageContinuation(document, value, value2, num3, count, nextRemoved);
						MarkFollowingTableBoundaryIfAdjacent(document, value, value2);
						LogConservationFailure("after", crossPageTableConservationReport2);
					}
				}
				else
				{
					MarkFollowingTableBoundaryIfAdjacent(document, value, value2);
				}
			}
			else
			{
				MarkFollowingTableBoundaryIfAdjacent(document, value, value2);
			}
		}
		return num;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void MarkRejectedCrossPageTableBoundaries(ReconstructedDocument document, IReadOnlyList<CrossPageTableContinuationEvidence> evaluations, Dictionary<CrossPageTableId, TableBlock> fragmentBlocks)
	{
		if (document == null || evaluations == null || fragmentBlocks == null)
		{
			return;
		}
		foreach (CrossPageTableContinuationEvidence evaluation in evaluations)
		{
			if (evaluation != null && !string.Equals(evaluation.ReasonCode, "ContinuationConfirmed", StringComparison.Ordinal) && fragmentBlocks.TryGetValue(evaluation.PreviousTableId, out var value) && fragmentBlocks.TryGetValue(evaluation.NextTableId, out var value2))
			{
				MarkFollowingTableBoundaryIfAdjacent(document, value, value2);
			}
		}
	}

	private static bool HaveCompatibleBoundaryRuns(ReconstructedLine previous, ReconstructedLine current)
	{
		TextRun textRun = ((previous == null || previous.Runs == null) ? null : previous.Runs.LastOrDefault((TextRun r) => r != null && !string.IsNullOrWhiteSpace(r.Text)));
		TextRun textRun2 = ((current == null || current.Runs == null) ? null : current.Runs.FirstOrDefault((TextRun r) => r != null && !string.IsNullOrWhiteSpace(r.Text)));
		if (textRun == null || textRun2 == null)
		{
			return false;
		}
		if (string.Equals(textRun.FontName, textRun2.FontName, StringComparison.OrdinalIgnoreCase))
		{
			return Math.Abs(textRun.FontSize - textRun2.FontSize) <= 1f;
		}
		return false;
	}

	private static void MarkFollowingTableBoundaryIfAdjacent(ReconstructedDocument document, TableBlock previous, TableBlock next)
	{
		if (document != null && previous != null && next != null)
		{
			int num = document.Blocks.IndexOf(previous);
			if (num >= 0 && num + 1 < document.Blocks.Count && document.Blocks[num + 1] == next)
			{
				previous.NeedsFollowingBoundary = true;
			}
		}
	}

	private static CrossPageTableConservationReport VerifyCrossPageConservation(string sourceText, string outputText, IReadOnlyList<string> provenHeaders)
	{
		Func<string, string, IReadOnlyList<string>, CrossPageTableConservationReport> crossPageConservationVerifierForTesting = CrossPageConservationVerifierForTesting;
		if (crossPageConservationVerifierForTesting == null)
		{
			return CrossPageTableContinuationVerifier.VerifyTextConservation(sourceText, outputText, provenHeaders);
		}
		return crossPageConservationVerifierForTesting(sourceText, outputText, provenHeaders);
	}

	private static void RollbackCrossPageContinuation(ReconstructedDocument document, TableBlock previousBlock, TableBlock nextBlock, int nextIndex, int originalPreviousRowCount, bool nextRemoved)
	{
		while (previousBlock.Rows.Count > originalPreviousRowCount)
		{
			previousBlock.Rows.RemoveAt(previousBlock.Rows.Count - 1);
		}
		if (nextRemoved && document.Blocks.IndexOf(nextBlock) < 0)
		{
			document.Blocks.Insert(Math.Min(nextIndex, document.Blocks.Count), nextBlock);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void LogConservationFailure(string stage, CrossPageTableConservationReport report)
	{
		try
		{
			LogService.Warn("LayoutReconstructionService.CrossPageTableConservationFailed stage=" + stage + ((report == null) ? " report=missing" : (" " + report)));
		}
		catch
		{
		}
	}

	private static string TableBlockText(TableBlock table)
	{
		if (table != null)
		{
			return TableRowsText(table.Rows, 0, table.Rows.Count);
		}
		return string.Empty;
	}

	private static string TableRowsText(IList<TableRow> rows, int start, int end)
	{
		StringBuilder stringBuilder = new StringBuilder();
		if (rows == null)
		{
			return string.Empty;
		}
		for (int i = Math.Max(0, start); i < Math.Min(end, rows.Count); i++)
		{
			stringBuilder.Append(TableRowText(rows[i]));
		}
		return stringBuilder.ToString();
	}

	private static string TableRowText(TableRow row)
	{
		StringBuilder stringBuilder = new StringBuilder();
		if (row == null)
		{
			return string.Empty;
		}
		foreach (TableCell cell in row.Cells)
		{
			stringBuilder.Append(TableCellText(cell));
		}
		return stringBuilder.ToString();
	}

	private static bool TableRowsTextEqual(TableRow a, TableRow b)
	{
		if (a != null && b != null)
		{
			if (a.Cells.Count == b.Cells.Count)
			{
				for (int i = 0; i < a.Cells.Count; i++)
				{
					if (a.Cells[i].GridSpan != b.Cells[i].GridSpan)
					{
						return false;
					}
					string a2 = CrossPageTableRowSignature.NormalizeCellText(TableCellText(a.Cells[i]));
					string b2 = CrossPageTableRowSignature.NormalizeCellText(TableCellText(b.Cells[i]));
					if (!string.Equals(a2, b2, StringComparison.Ordinal))
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

	private static string TableCellText(TableCell cell)
	{
		if (cell == null)
		{
			return string.Empty;
		}
		StringBuilder stringBuilder = new StringBuilder();
		foreach (TextRun run in cell.Runs)
		{
			if (run != null && !string.IsNullOrEmpty(run.Text))
			{
				stringBuilder.Append(run.Text);
			}
		}
		return stringBuilder.ToString();
	}

	private static void MergeCrossPageParagraphs(IList<DocumentBlock> blocks)
	{
		for (int i = 0; i < blocks.Count - 1; i++)
		{
			ParagraphBlock paragraphBlock = blocks[i] as ParagraphBlock;
			ParagraphBlock paragraphBlock2 = blocks[i + 1] as ParagraphBlock;
			if (paragraphBlock != null && paragraphBlock2 != null && CanMergeAcrossPage(paragraphBlock, paragraphBlock2))
			{
				MergeParagraph(paragraphBlock, paragraphBlock2);
				blocks.RemoveAt(i + 1);
				i--;
			}
		}
	}

	private static bool CanMergeAcrossPage(ParagraphBlock a, ParagraphBlock b)
	{
		if (b.PageIndex == a.LastPageIndex + 1)
		{
			if (!IsNumberedItemStart(b.PlainText))
			{
				if (b.FirstLineIndentChars == 0)
				{
					if (a.Alignment != ParagraphAlignment.Left || b.Alignment != ParagraphAlignment.Left)
					{
						return false;
					}
					if (b.SpaceBefore <= 1f)
					{
						if (!(a.LastLineFontSize > 0f) || !(b.FirstLineFontSize > 0f) || !(Math.Abs(a.LastLineFontSize - b.FirstLineFontSize) > 2f))
						{
							if (!EndsWithTerminalPunctuation(a.PlainText))
							{
								float num = a.RightEdgeX - a.LeftEdgeX;
								if (!(num > 0f))
								{
									return false;
								}
								return a.LastLineEndX - a.LastLineStartX > num * 0.8f;
							}
							return false;
						}
						return false;
					}
					return false;
				}
				return false;
			}
			return false;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void MergeParagraph(ParagraphBlock a, ParagraphBlock b)
	{
		if (a.Runs.Count > 0 && b.Runs.Count > 0)
		{
			string text = a.Runs[a.Runs.Count - 1].Text;
			string text2 = b.Runs[0].Text;
			if (text.Length > 0 && text2.Length > 0)
			{
				char c = text[text.Length - 1];
				char c2 = text2[0];
				if (IsAsciiAlnum(c) && IsAsciiAlnum(c2))
				{
					a.Runs[a.Runs.Count - 1].Text += " ";
				}
			}
		}
		foreach (TextRun run in b.Runs)
		{
			TextRun textRun = ((a.Runs.Count > 0) ? a.Runs[a.Runs.Count - 1] : null);
			if (textRun == null || !string.Equals(textRun.FontName, run.FontName, StringComparison.Ordinal) || textRun.FontSize != run.FontSize || textRun.Bold != run.Bold || textRun.Italic != run.Italic)
			{
				a.Runs.Add(new TextRun
				{
					Text = run.Text,
					FontName = run.FontName,
					FontSize = run.FontSize,
					Bold = run.Bold,
					Italic = run.Italic
				});
			}
			else
			{
				textRun.Text += run.Text;
			}
		}
		a.LastLineStartX = b.LastLineStartX;
		a.LastLineEndX = b.LastLineEndX;
		a.LastLineFontSize = b.LastLineFontSize;
		a.LeftEdgeX = b.LeftEdgeX;
		a.RightEdgeX = b.RightEdgeX;
		a.LastPageIndex = b.LastPageIndex;
	}

	private static bool EndsWithTerminalPunctuation(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		char c = text.TrimEnd(Array.Empty<char>())[text.TrimEnd(Array.Empty<char>()).Length - 1];
		if (c != '。' && c != '！' && c != '？' && c != '!')
		{
			return c == '?';
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool IsNumberedItemStart(string text)
	{
		if (!string.IsNullOrWhiteSpace(text))
		{
			string text2 = RemoveAllSpaces(text);
			if (text2.Length < 3 || "一二三四五六七八九十".IndexOf(text2[0]) < 0 || text2[1] != '、')
			{
				if (text2.Length < 4 || text2[0] != '（' || "一二三四五六七八九十".IndexOf(text2[1]) < 0 || text2[2] != '）')
				{
					if (text2.Length >= 4 && text2[0] == '第' && ("一二三四五六七八九十".IndexOf(text2[1]) >= 0 || char.IsDigit(text2[1])))
					{
						int num = Math.Min(5, text2.Length - 1);
						for (int i = 2; i <= num; i++)
						{
							if (text2[i] == '章' || text2[i] == '节' || text2[i] == '条')
							{
								return text2.Length > i + 1;
							}
						}
					}
					if (char.IsDigit(text2[0]))
					{
						int j;
						for (j = 0; j < text2.Length && char.IsDigit(text2[j]); j++)
						{
						}
						if (j >= 1 && j <= 2 && j < text2.Length)
						{
							if (text2[j] == '、')
							{
								return true;
							}
							if (text2[j] == '.' && j + 1 < text2.Length && !char.IsDigit(text2[j + 1]) && text2[j + 1] != '%' && text2[j + 1] != '％')
							{
								return true;
							}
						}
					}
					return false;
				}
				return true;
			}
			return true;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool IsAttachmentItemStart(string text)
	{
		if (!string.IsNullOrWhiteSpace(text))
		{
			string text2 = text.TrimStart(Array.Empty<char>());
			if (!text2.StartsWith("附件：", StringComparison.Ordinal) && !text2.StartsWith("附件:", StringComparison.Ordinal))
			{
				if (text2.Length < 3 || text2[0] != '附' || text2[1] != '件' || !char.IsDigit(text2[2]))
				{
					if (text2.Length >= 4 && text2.StartsWith("附件 ", StringComparison.Ordinal) && char.IsDigit(text2[3]))
					{
						return true;
					}
					return false;
				}
				return true;
			}
			return true;
		}
		return false;
	}

	private static bool IsDateOnlyLine(string text)
	{
		if (!string.IsNullOrWhiteSpace(text))
		{
			string text2 = RemoveAllSpaces(text);
			if (text2.Length >= 6 && text2.Length <= 16)
			{
				int pos = 0;
				if (!ConsumeDigits(text2, ref pos, 4, 4))
				{
					return false;
				}
				if (!ConsumeChar(text2, ref pos, '年'))
				{
					return false;
				}
				if (ConsumeDigits(text2, ref pos, 1, 2))
				{
					if (ConsumeChar(text2, ref pos, '月'))
					{
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
			return false;
		}
		return false;
	}

	private static string RemoveAllSpaces(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return string.Empty;
		}
		StringBuilder stringBuilder = new StringBuilder(text.Length);
		foreach (char c in text)
		{
			if (!char.IsWhiteSpace(c) && c != '\u3000')
			{
				stringBuilder.Append(c);
			}
		}
		return stringBuilder.ToString();
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
		if (pos < text.Length && text[pos] == expected)
		{
			pos++;
			return true;
		}
		return false;
	}

	private static bool IsCenteredHeadingLine(ReconstructedLine line, float pageWidth, float typicalFontSize)
	{
		if (line == null || pageWidth <= 0f)
		{
			return false;
		}
		bool num = Math.Abs((line.StartX + line.EndX) / 2f - pageWidth / 2f) <= 20f;
		bool flag = typicalFontSize > 0.1f && line.FontSize >= typicalFontSize + 2f;
		return num && flag;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool IsSignatureLine(ReconstructedLine line, float marginX, float contentWidth)
	{
		string text = ((line.Text == null) ? string.Empty : line.Text.TrimEnd(Array.Empty<char>()));
		if (text.Length < 4 || text.Length > 30)
		{
			return false;
		}
		float num = line.EndX - line.StartX;
		if (contentWidth > 0f && num < contentWidth * 0.6f)
		{
			if (!(line.StartX > marginX + contentWidth * 0.4f))
			{
				return false;
			}
			if (!text.EndsWith("董事会", StringComparison.Ordinal) && !text.EndsWith("办公室", StringComparison.Ordinal) && !text.EndsWith("委员会", StringComparison.Ordinal) && !text.EndsWith("人民政府", StringComparison.Ordinal) && !text.EndsWith("办公厅", StringComparison.Ordinal))
			{
				char value = text[text.Length - 1];
				if ("司局部院校厅署行".IndexOf(value) >= 0 && (text.IndexOf("公司") >= 0 || text.IndexOf("人民") >= 0 || text.IndexOf("集团") >= 0 || text.IndexOf("政府") >= 0))
				{
					return true;
				}
				return false;
			}
			return true;
		}
		return false;
	}

	private static bool IsWhitespaceText(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return false;
		}
		for (int i = 0; i < text.Length; i++)
		{
			if (!char.IsWhiteSpace(text[i]))
			{
				return false;
			}
		}
		return true;
	}

	private static bool IsCjkText(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return false;
		}
		for (int i = 0; i < text.Length; i++)
		{
			if (IsCjkChar(text[i]))
			{
				return true;
			}
		}
		return false;
	}

	private static bool IsCjkChar(char c)
	{
		if ((c < '一' || c > '鿿') && (c < '㐀' || c > '䶿') && (c < 131072 || c > 173791) && (c < 173824 || c > 177983) && (c < 177984 || c > 178207) && (c < 178208 || c > 183983) && (c < 183984 || c > 191471) && (c < '\u3000' || c > '〿') && (c < '\uff00' || c > '\uffef'))
		{
			switch (c)
			{
			default:
				return c == '…';
			case '–':
			case '—':
			case '‘':
			case '’':
			case '‚':
			case '‛':
			case '“':
			case '”':
			case '„':
			case '‟':
				break;
			}
		}
		return true;
	}

	private static bool IsAsciiAlnum(char c)
	{
		if ((c < 'a' || c > 'z') && (c < 'A' || c > 'Z'))
		{
			if (c < '0')
			{
				return false;
			}
			return c <= '9';
		}
		return true;
	}

	private static float Median(List<float> values)
	{
		List<float> list = values.OrderBy((float v) => v).ToList();
		int count = list.Count;
		if (count == 0)
		{
			return 0f;
		}
		if (count % 2 == 1)
		{
			return list[count / 2];
		}
		return (list[count / 2 - 1] + list[count / 2]) / 2f;
	}
}
