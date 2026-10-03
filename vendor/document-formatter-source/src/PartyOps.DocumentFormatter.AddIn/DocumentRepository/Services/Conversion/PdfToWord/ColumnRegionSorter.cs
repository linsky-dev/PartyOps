using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DocumentRepository.Services.Logging;

namespace DocumentRepository.Services.Conversion.PdfToWord;

internal static class ColumnRegionSorter
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static List<ColumnReadingSegment> SplitColumns(List<ReconstructedLine> lines, float pageWidth, LayoutReconstructionOptions options)
	{
		List<ColumnReadingSegment> result = new List<ColumnReadingSegment>
		{
			new ColumnReadingSegment
			{
				Lines = lines
			}
		};
		if (lines == null || lines.Count < options.ColumnMinLines)
		{
			return result;
		}
		float num = lines.Min((ReconstructedLine l) => l.StartX);
		float num2 = lines.Max((ReconstructedLine l) => l.EndX) - num;
		if (!(num2 >= 200f))
		{
			return result;
		}
		if (TryFindGutter(lines, options, num, num2, out var gutterLeft, out var gutterRight, out var gutterCenter))
		{
			if (gutterRight - gutterLeft >= options.ColumnGutterMinWidth)
			{
				float num3 = (gutterCenter - num) / num2;
				if (!(num3 >= options.ColumnCenterMinRatio) || !(num3 <= options.ColumnCenterMaxRatio))
				{
					return result;
				}
				List<ColumnReadingSegment> list = new List<ColumnReadingSegment>();
				List<ReconstructedLine> list2 = new List<ReconstructedLine>();
				bool foundColumns = false;
				foreach (ReconstructedLine line in lines)
				{
					bool flag = false;
					List<PdfTextElement> list3 = new List<PdfTextElement>();
					List<PdfTextElement> list4 = new List<PdfTextElement>();
					foreach (PdfTextElement element in line.Elements)
					{
						float num4 = element.X + element.Width / 2f;
						if (num4 < gutterLeft || !(num4 <= gutterRight))
						{
							if (!(num4 < gutterLeft))
							{
								list4.Add(element);
							}
							else
							{
								list3.Add(element);
							}
							continue;
						}
						flag = true;
						break;
					}
					if (flag)
					{
						FlushColumnFragments(list2, gutterLeft, gutterRight, options, list, ref foundColumns);
						list.Add(new ColumnReadingSegment
						{
							Lines = new List<ReconstructedLine> { line }
						});
						continue;
					}
					if (list3.Count > 0)
					{
						list2.Add(LayoutReconstructionService.FinalizeLine(list3));
					}
					if (list4.Count > 0)
					{
						list2.Add(LayoutReconstructionService.FinalizeLine(list4));
					}
				}
				FlushColumnFragments(list2, gutterLeft, gutterRight, options, list, ref foundColumns);
				if (!foundColumns)
				{
					return result;
				}
				try
				{
					LogService.Info("ColumnRegionSorter.TwoColumnZone segments=" + list.Count + " lines=" + lines.Count);
				}
				catch
				{
				}
				return list;
			}
			return result;
		}
		return result;
	}

	private static void FlushColumnFragments(List<ReconstructedLine> fragments, float gutterLeft, float gutterRight, LayoutReconstructionOptions options, List<ColumnReadingSegment> result, ref bool foundColumns)
	{
		if (fragments.Count == 0)
		{
			return;
		}
		List<ReconstructedLine> list = (from l in fragments
			where (l.StartX + l.EndX) / 2f < (gutterLeft + gutterRight) / 2f
			orderby l.Baseline descending
			select l).ToList();
		List<ReconstructedLine> list2 = (from l in fragments
			where (l.StartX + l.EndX) / 2f >= (gutterLeft + gutterRight) / 2f
			orderby l.Baseline descending
			select l).ToList();
		if (list.Count >= options.ColumnMinLines && list2.Count >= options.ColumnMinLines && list.Count + list2.Count == fragments.Count && HasProseDensity(list, options.ColumnProseDensityRatio) && HasProseDensity(list2, options.ColumnProseDensityRatio))
		{
			float columnZoneTop = ((fragments.Count > 0) ? fragments.Max((ReconstructedLine l) => l.Baseline + l.FontSize) : 0f);
			result.Add(new ColumnReadingSegment
			{
				Lines = list,
				ColumnOrdinal = 0,
				ColumnZoneTop = columnZoneTop
			});
			result.Add(new ColumnReadingSegment
			{
				Lines = list2,
				ColumnOrdinal = 1,
				ColumnZoneTop = columnZoneTop
			});
			foundColumns = true;
		}
		else
		{
			result.Add(new ColumnReadingSegment
			{
				Lines = (from l in fragments
					orderby l.Baseline descending, l.StartX
					select l).ToList()
			});
		}
		fragments.Clear();
	}

	private static bool HasProseDensity(List<ReconstructedLine> columnLines, float ratio)
	{
		if (columnLines.Count == 0)
		{
			return false;
		}
		float num = columnLines.Min((ReconstructedLine l) => l.StartX);
		float num2 = columnLines.Max((ReconstructedLine l) => l.EndX) - num;
		if (!(num2 > 0f))
		{
			return false;
		}
		return columnLines.Average((ReconstructedLine l) => l.EndX - l.StartX) / num2 >= ratio;
	}

	private static bool TryFindGutter(List<ReconstructedLine> lines, LayoutReconstructionOptions options, float contentLeft, float contentWidth, out float gutterLeft, out float gutterRight, out float gutterCenter)
	{
		gutterLeft = (gutterRight = (gutterCenter = 0f));
		List<KeyValuePair<float[], int>> list = new List<KeyValuePair<float[], int>>();
		int num = 0;
		foreach (ReconstructedLine line in lines)
		{
			List<PdfTextElement> elements = line.Elements;
			List<float[]> list2 = new List<float[]>();
			for (int i = 1; i < elements.Count; i++)
			{
				float num2 = elements[i - 1].X + elements[i - 1].Width;
				float x = elements[i].X;
				if (!(x - num2 <= options.BlockGapThreshold))
				{
					list2.Add(new float[2] { num2, x });
				}
			}
			if (list2.Count == 0)
			{
				continue;
			}
			num++;
			foreach (float[] item in list2)
			{
				bool flag = false;
				for (int j = 0; j < list.Count; j++)
				{
					float[] key = list[j].Key;
					if (item[0] <= key[1] && item[1] >= key[0])
					{
						key[0] = Math.Max(key[0], item[0]);
						key[1] = Math.Min(key[1], item[1]);
						list[j] = new KeyValuePair<float[], int>(key, list[j].Value + 1);
						flag = true;
						break;
					}
				}
				if (!flag)
				{
					list.Add(new KeyValuePair<float[], int>(item, 1));
				}
			}
		}
		if (num < options.ColumnMinLines)
		{
			return false;
		}
		if (list.Count != 0)
		{
			int num3 = Math.Max(options.ColumnMinLines, (int)Math.Ceiling((double)num * 0.5));
			KeyValuePair<float[], int> keyValuePair = new KeyValuePair<float[], int>(null, -1);
			foreach (KeyValuePair<float[], int> item2 in list)
			{
				if (!(item2.Key[1] - item2.Key[0] <= 0f) && item2.Value >= num3 && (keyValuePair.Key == null || item2.Value > keyValuePair.Value))
				{
					keyValuePair = item2;
				}
			}
			if (keyValuePair.Key == null)
			{
				return false;
			}
			gutterLeft = keyValuePair.Key[0];
			gutterRight = keyValuePair.Key[1];
			gutterCenter = (gutterLeft + gutterRight) / 2f;
			return true;
		}
		return false;
	}
}
