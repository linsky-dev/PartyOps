using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace DocumentRepository.Services.Conversion.PdfToWord;

public static class DecorationRegionDetector
{
	private sealed class LineView
	{
		public float Baseline;

		public float StartX;

		public float FontSize;

		public string NormalizedText;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static DecorationDetectionResult Detect(IList<PdfPageContent> pages, LayoutReconstructionOptions options)
	{
		DecorationDetectionResult decorationDetectionResult = new DecorationDetectionResult();
		if (pages == null || pages.Count < 2 || options == null)
		{
			return decorationDetectionResult;
		}
		List<DecorationOccurrence> list = new List<DecorationOccurrence>();
		float page0BodyMedianFontSize = 0f;
		for (int i = 0; i < pages.Count; i++)
		{
			PdfPageContent page = pages[i];
			if (page.Height <= 0f)
			{
				continue;
			}
			float edge = Math.Max(options.DecorationEdgePoints, page.Height * options.DecorationEdgeRatio);
			List<LineView> list2 = BuildLineViews(page);
			List<float[]> regions = BuildFramedRegions(page);
			if (i == 0)
			{
				page0BodyMedianFontSize = MedianFontSize(list2.Where((LineView l) => l.Baseline < page.Height - edge && l.Baseline > edge).ToList());
			}
			foreach (LineView item in list2)
			{
				bool isFooter;
				if (!(item.Baseline < page.Height - edge))
				{
					isFooter = false;
				}
				else
				{
					if (item.Baseline > edge)
					{
						continue;
					}
					isFooter = true;
				}
				if (!LooksLikePurePageNumber(item.NormalizedText) && !IsStructuralLine(item.NormalizedText) && !IsInsideFramedRegion(item, regions))
				{
					string text = StripEdgePageNumbers(item.NormalizedText);
					if (text.Length >= 2)
					{
						list.Add(new DecorationOccurrence
						{
							PageIndex = page.PageIndex,
							Baseline = item.Baseline,
							StartX = item.StartX,
							FontSize = item.FontSize,
							Fingerprint = text,
							IsFooter = isFooter,
							Removed = false
						});
					}
				}
			}
			list = TrimToEdgePositions(list, page.PageIndex, isFooter: false);
			list = TrimToEdgePositions(list, page.PageIndex, isFooter: true);
		}
		Dictionary<string, List<DecorationOccurrence>> dictionary = new Dictionary<string, List<DecorationOccurrence>>(StringComparer.Ordinal);
		foreach (DecorationOccurrence item2 in list)
		{
			string key = (item2.IsFooter ? "F:" : "H:") + item2.Fingerprint;
			if (!dictionary.TryGetValue(key, out var value))
			{
				value = new List<DecorationOccurrence>();
				dictionary.Add(key, value);
			}
			value.Add(item2);
		}
		float height = pages[0].Height;
		int num = RequiredPages(pages.Count, options);
		foreach (List<DecorationOccurrence> value2 in dictionary.Values)
		{
			HashSet<int> hashSet = new HashSet<int>();
			foreach (DecorationOccurrence item3 in value2)
			{
				hashSet.Add(item3.PageIndex);
			}
			if (hashSet.Count < num || (pages.Count < 4 && !HasConsecutivePages(hashSet)))
			{
				continue;
			}
			float num2 = float.MaxValue;
			float num3 = float.MinValue;
			float num4 = float.MaxValue;
			float num5 = float.MinValue;
			foreach (DecorationOccurrence item4 in value2)
			{
				if (!(item4.StartX >= num2))
				{
					num2 = item4.StartX;
				}
				if (!(item4.StartX <= num3))
				{
					num3 = item4.StartX;
				}
				if (!(item4.FontSize >= num4))
				{
					num4 = item4.FontSize;
				}
				if (!(item4.FontSize <= num5))
				{
					num5 = item4.FontSize;
				}
			}
			if (num3 - num2 > options.DecorationXStabilityTolerance || num5 - num4 > options.DecorationFontSizeTolerance)
			{
				continue;
			}
			double meanBaseline = ((IEnumerable<DecorationOccurrence>)value2).Average((Func<DecorationOccurrence, double>)((DecorationOccurrence o) => o.Baseline));
			if (Math.Sqrt(value2.Average((DecorationOccurrence o) => ((double)o.Baseline - meanBaseline) * ((double)o.Baseline - meanBaseline))) > (double)(height * options.DecorationYStdDevRatio))
			{
				continue;
			}
			decorationDetectionResult.ConfirmedGroupCount++;
			foreach (DecorationOccurrence item5 in value2)
			{
				item5.Removed = true;
				decorationDetectionResult.Occurrences.Add(item5);
			}
		}
		foreach (DecorationOccurrence occurrence in decorationDetectionResult.Occurrences)
		{
			if (occurrence.PageIndex == 0 && HasFirstPageStructuralEvidence(occurrence, page0BodyMedianFontSize))
			{
				occurrence.Removed = false;
			}
		}
		return decorationDetectionResult;
	}

	public static string StripEdgePageNumbers(string normalizedText)
	{
		if (!string.IsNullOrEmpty(normalizedText))
		{
			string text = normalizedText;
			bool flag = false;
			bool flag2 = false;
			while (text.Length > 0 && text[0] >= '0' && text[0] <= '9')
			{
				text = text.Substring(1);
				flag = true;
			}
			while (text.Length > 0 && text[text.Length - 1] >= '0' && text[text.Length - 1] <= '9')
			{
				text = text.Substring(0, text.Length - 1);
				flag2 = true;
			}
			if (flag && text.Length > 0 && IsDashDecoration(text[0]))
			{
				text = text.Substring(1);
			}
			if (flag2 && text.Length > 0 && IsDashDecoration(text[text.Length - 1]))
			{
				text = text.Substring(0, text.Length - 1);
			}
			return text;
		}
		return string.Empty;
	}

	private static List<float[]> BuildFramedRegions(PdfPageContent page)
	{
		List<float[]> list = new List<float[]>();
		if (page.LineFrames == null || page.LineFrames.Count == 0)
		{
			return list;
		}
		List<float> list2 = new List<float>();
		List<float[]> list3 = new List<float[]>();
		foreach (PdfLineFrame frame in page.LineFrames)
		{
			if (frame.Orientation != PdfLineFrameOrientation.Horizontal)
			{
				if (frame.Length < 10f)
				{
					continue;
				}
				float[] array = null;
				foreach (float[] item in list3)
				{
					if (Math.Abs(item[0] - frame.StartX) > 2f)
					{
						continue;
					}
					array = item;
					break;
				}
				if (array != null)
				{
					array[1] = Math.Min(array[1], frame.StartY);
					array[2] = Math.Max(array[2], frame.EndY);
					continue;
				}
				array = new float[3] { frame.StartX, frame.StartY, frame.EndY };
				list3.Add(array);
			}
			else if (frame.Length >= 30f && !list2.Any((float y) => Math.Abs(y - frame.StartY) <= 2f))
			{
				list2.Add(frame.StartY);
			}
		}
		if (list2.Count < 2 || list3.Count < 2)
		{
			return list;
		}
		list2.Sort((float a, float b) => b.CompareTo(a));
		list3.Sort((float[] a, float[] b) => a[0].CompareTo(b[0]));
		for (int num = 0; num + 1 < list2.Count; num++)
		{
			float num2 = list2[num];
			float num3 = list2[num + 1];
			List<float> list4 = new List<float>();
			foreach (float[] item2 in list3)
			{
				if (!(item2[1] > num3 + 2f) && !(item2[2] < num2 - 2f))
				{
					list4.Add(item2[0]);
				}
			}
			if (list4.Count >= 2)
			{
				list.Add(new float[4]
				{
					list4.Min(),
					list4.Max(),
					num3,
					num2
				});
			}
		}
		return list;
	}

	private static bool IsInsideFramedRegion(LineView line, List<float[]> regions)
	{
		foreach (float[] region in regions)
		{
			if (line.Baseline > region[3] + 2f || line.Baseline <= region[2] - 2f || line.StartX < region[0] - 2f || !(line.StartX <= region[1] + 2f))
			{
				continue;
			}
			return true;
		}
		return false;
	}

	private static List<LineView> BuildLineViews(PdfPageContent page)
	{
		List<LineView> list = new List<LineView>();
		List<List<PdfTextElement>> list2 = new List<List<PdfTextElement>>();
		foreach (PdfTextElement item in page.Elements.OrderByDescending((PdfTextElement e) => e.Baseline))
		{
			List<PdfTextElement> list3 = null;
			foreach (List<PdfTextElement> item2 in list2)
			{
				if (!(Math.Abs(item2.Average((PdfTextElement e) => e.Baseline) - item.Baseline) > 3f))
				{
					list3 = item2;
					break;
				}
			}
			if (list3 == null)
			{
				list3 = new List<PdfTextElement>();
				list2.Add(list3);
			}
			list3.Add(item);
		}
		foreach (List<PdfTextElement> item3 in list2)
		{
			string text = Normalize(string.Join("", from e in item3
				orderby e.X
				select e.Text ?? ""));
			if (text.Length != 0)
			{
				list.Add(new LineView
				{
					Baseline = item3.Average((PdfTextElement e) => e.Baseline),
					StartX = item3.Min((PdfTextElement e) => e.X),
					FontSize = item3.Max((PdfTextElement e) => e.FontSize),
					NormalizedText = text
				});
			}
		}
		return list;
	}

	private static List<DecorationOccurrence> TrimToEdgePositions(List<DecorationOccurrence> candidates, int pageIndex, bool isFooter)
	{
		List<DecorationOccurrence> list = new List<DecorationOccurrence>();
		List<float> list2 = new List<float>();
		List<DecorationOccurrence> list3 = new List<DecorationOccurrence>();
		foreach (DecorationOccurrence item in from o in candidates
			where o.PageIndex == pageIndex && o.IsFooter == isFooter
			orderby (!isFooter) ? o.Baseline : (0f - o.Baseline) descending
			select o)
		{
			bool flag = false;
			foreach (float item2 in list2)
			{
				if (Math.Abs(item2 - item.Baseline) > 3f)
				{
					continue;
				}
				flag = true;
				break;
			}
			if (!flag)
			{
				if (list2.Count >= 5)
				{
					continue;
				}
				list2.Add(item.Baseline);
			}
			list3.Add(item);
		}
		foreach (DecorationOccurrence candidate in candidates)
		{
			if (candidate.PageIndex != pageIndex || candidate.IsFooter != isFooter)
			{
				list.Add(candidate);
			}
		}
		list.AddRange(list3);
		return list;
	}

	private static int RequiredPages(int totalPages, LayoutReconstructionOptions options)
	{
		if (totalPages >= 2)
		{
			if (totalPages < 4)
			{
				return 2;
			}
			return Math.Max(options.DecorationMinFrequencyPages, (int)Math.Ceiling((float)totalPages * options.DecorationFrequencyRatio));
		}
		return int.MaxValue;
	}

	private static bool HasConsecutivePages(HashSet<int> pageIndexes)
	{
		foreach (int pageIndex in pageIndexes)
		{
			if (pageIndexes.Contains(pageIndex + 1))
			{
				return true;
			}
		}
		return false;
	}

	private static bool HasFirstPageStructuralEvidence(DecorationOccurrence occurrence, float page0BodyMedianFontSize)
	{
		if (page0BodyMedianFontSize > 0f && !(occurrence.FontSize < page0BodyMedianFontSize * 1.2f + 0.5f))
		{
			return true;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool IsStructuralLine(string normalizedText)
	{
		if (!string.IsNullOrEmpty(normalizedText))
		{
			if (normalizedText.Length < 2 || "一二三四五六七八九十百千".IndexOf(normalizedText[0]) < 0 || normalizedText[1] != '、')
			{
				if (normalizedText.Length < 3 || normalizedText[0] != '（' || "一二三四五六七八九十百千".IndexOf(normalizedText[1]) < 0 || normalizedText[2] != '）')
				{
					if (char.IsDigit(normalizedText[0]))
					{
						int i;
						for (i = 0; i < normalizedText.Length && char.IsDigit(normalizedText[i]); i++)
						{
						}
						if (i <= 2 && i < normalizedText.Length && (normalizedText[i] == '、' || normalizedText[i] == '.'))
						{
							return true;
						}
					}
					if (!normalizedText.StartsWith("附件", StringComparison.Ordinal))
					{
						if ((normalizedText.IndexOf('〔') >= 0 && normalizedText.IndexOf('〕') > normalizedText.IndexOf('〔')) || (normalizedText.IndexOf('【') >= 0 && normalizedText.IndexOf('】') > normalizedText.IndexOf('【')))
						{
							int num = Math.Max(normalizedText.IndexOf('〔'), normalizedText.IndexOf('【'));
							if (Math.Max(normalizedText.IndexOf('〕'), normalizedText.IndexOf('】')) - num == 5)
							{
								return true;
							}
						}
						return false;
					}
					return true;
				}
				return true;
			}
			return true;
		}
		return false;
	}

	private static bool LooksLikePurePageNumber(string normalizedText)
	{
		if (string.IsNullOrEmpty(normalizedText))
		{
			return false;
		}
		if (normalizedText.Length > 8)
		{
			return false;
		}
		string text = StripEdgePageNumbers(normalizedText);
		foreach (char c in text)
		{
			if (!IsDashDecoration(c) && !char.IsWhiteSpace(c))
			{
				return false;
			}
		}
		text = normalizedText;
		foreach (char c2 in text)
		{
			if (c2 >= '0' && c2 <= '9')
			{
				return true;
			}
		}
		return false;
	}

	private static bool IsDashDecoration(char c)
	{
		if (c != '—' && c != '-' && c != '–' && c != '~' && c != '·')
		{
			return c == '.';
		}
		return true;
	}

	private static float MedianFontSize(List<LineView> lines)
	{
		if (lines.Count == 0)
		{
			return 0f;
		}
		List<float> list = (from l in lines
			select l.FontSize into v
			orderby v
			select v).ToList();
		return list[list.Count / 2];
	}

	private static string Normalize(string text)
	{
		if (!string.IsNullOrEmpty(text))
		{
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
		return string.Empty;
	}

	public static string NormalizeForMatch(string text)
	{
		return Normalize(text);
	}
}
