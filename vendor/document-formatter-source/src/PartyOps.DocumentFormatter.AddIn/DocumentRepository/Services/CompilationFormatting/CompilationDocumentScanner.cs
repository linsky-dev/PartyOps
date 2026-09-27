using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Snapshots;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.CompilationFormatting;

public static class CompilationDocumentScanner
{
	private sealed class WalkedParagraph
	{
		public string Text;

		public bool IsMarker;

		public bool IsMalformedMarker;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static CompilationDocumentScanResult Scan(Document document)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		CompilationDocumentScanResult compilationDocumentScanResult = new CompilationDocumentScanResult();
		if (!TryReadBody(document, out var bodyText, out var wordParagraphCount, compilationDocumentScanResult))
		{
			ScanOtherStories(document, compilationDocumentScanResult);
			return compilationDocumentScanResult;
		}
		WordParagraphTextMap.TextSplitResult textSplitResult = WordParagraphTextMap.SplitParagraphTexts(bodyText, wordParagraphCount);
		if (!textSplitResult.IsReliable)
		{
			compilationDocumentScanResult.ScanErrors.Add("正文故事段落走查与宿主段落数不一致。");
			ScanOtherStories(document, compilationDocumentScanResult);
			return compilationDocumentScanResult;
		}
		List<WalkedParagraph> list = new List<WalkedParagraph>();
		foreach (string paragraph in textSplitResult.Paragraphs)
		{
			string text = NormalizeStoryText(paragraph);
			list.Add(new WalkedParagraph
			{
				Text = text,
				IsMarker = CompilationMarkerParser.IsExactMarker(text),
				IsMalformedMarker = CompilationMarkerParser.IsMalformedMarkerParagraph(text)
			});
		}
		List<Microsoft.Office.Interop.Word.Range> list2 = FindMarkerHits(document, compilationDocumentScanResult);
		if (list2 != null)
		{
			List<int> list3 = new List<int>();
			for (int i = 0; i < list.Count; i++)
			{
				if (list[i].IsMarker)
				{
					list3.Add(i);
				}
				if (list[i].IsMalformedMarker)
				{
					compilationDocumentScanResult.MalformedBodyMarkers.Add("正文第 " + (i + 1) + " 个段落附近");
				}
			}
			if (list2.Count == list3.Count)
			{
				List<Paragraph> list4 = new List<Paragraph>();
				HashSet<int> hashSet = new HashSet<int>();
				Dictionary<int, int> dictionary = new Dictionary<int, int>();
				Dictionary<int, int> dictionary2 = new Dictionary<int, int>();
				Dictionary<int, int> dictionary3 = new Dictionary<int, int>();
				Dictionary<int, int> dictionary4 = new Dictionary<int, int>();
				Dictionary<int, bool> dictionary5 = new Dictionary<int, bool>();
				Dictionary<int, int> dictionary6 = new Dictionary<int, int>();
				Dictionary<int, int> dictionary7 = new Dictionary<int, int>();
				try
				{
					for (int j = 0; j < list2.Count; j++)
					{
						int num = list3[j];
						Paragraphs value = null;
						Paragraph value2 = null;
						Microsoft.Office.Interop.Word.Range value3 = null;
						try
						{
							value = list2[j].Paragraphs;
							value2 = value[1];
							value3 = value2.Range;
							string text2 = value3.Text ?? string.Empty;
							if (NormalizeStoryText(text2) != "@@汇编@@")
							{
								compilationDocumentScanResult.ScanErrors.Add("第 " + (j + 1) + " 个标记定位段与文本走查内容不一致。");
								return FinishWithStories(document, compilationDocumentScanResult, list2, list4);
							}
							bool flag = false;
							try
							{
								flag = (bool)(dynamic)value3.get_Information(WdInformation.wdWithInTable);
							}
							catch
							{
							}
							if (flag)
							{
								hashSet.Add(num);
								compilationDocumentScanResult.UnsupportedRegionMarkers.Add("表格单元格（正文第 " + (num + 1) + " 个段落附近）");
							}
							else
							{
								dictionary[num] = value3.Start;
								dictionary2[num] = value3.End;
								dictionary3[num] = list2[j].Start;
								dictionary4[num] = list2[j].End;
								dictionary5[num] = text2.IndexOf('\f') >= 0;
								int num2 = FindFirstNonEmptyAfterMarker(list, num);
								Paragraph value4 = null;
								Microsoft.Office.Interop.Word.Range value5 = null;
								try
								{
									if (num2 >= 0)
									{
										value4 = GetFollowingParagraph(value2, num2 - num);
									}
									if (value4 != null)
									{
										value5 = value4.Range;
										dictionary6[num2] = value5.Start;
										dictionary7[num2] = value5.End;
									}
								}
								finally
								{
									ComObjectRelease.Release(ref value5, "CompilationDocumentScanner.TitleRange");
									ComObjectRelease.Release(ref value4, "CompilationDocumentScanner.TitlePara");
								}
							}
							list4.Add(value2);
							value2 = null;
						}
						finally
						{
							ComObjectRelease.Release(ref value3, "CompilationDocumentScanner.HitParaRange");
							ComObjectRelease.Release(ref value2, "CompilationDocumentScanner.HitPara");
							ComObjectRelease.Release(ref value, "CompilationDocumentScanner.HitParagraphs");
						}
					}
				}
				catch (Exception)
				{
					compilationDocumentScanResult.ScanErrors.Add("标记段落锚定读取失败。");
					return FinishWithStories(document, compilationDocumentScanResult, list2, list4);
				}
				for (int k = 0; k < list.Count; k++)
				{
					if (!hashSet.Contains(k))
					{
						int count = compilationDocumentScanResult.ParagraphTexts.Count;
						compilationDocumentScanResult.ParagraphTexts.Add(list[k].Text);
						bool isMarker = list[k].IsMarker;
						int value6 = -1;
						int value7 = -1;
						if (isMarker && dictionary.TryGetValue(k, out value6))
						{
							dictionary2.TryGetValue(k, out value7);
						}
						else if (dictionary6.TryGetValue(k, out value6))
						{
							dictionary7.TryGetValue(k, out value7);
						}
						compilationDocumentScanResult.ParagraphStarts.Add(value6);
						compilationDocumentScanResult.ParagraphEnds.Add(value7);
						if (isMarker)
						{
							compilationDocumentScanResult.BodyMarkerParagraphIndexes.Add(count);
							compilationDocumentScanResult.BodyMarkerTextStarts.Add(dictionary3[k]);
							compilationDocumentScanResult.BodyMarkerTextEnds.Add(dictionary4[k]);
							dictionary5.TryGetValue(k, out var value8);
							compilationDocumentScanResult.BodyMarkerParagraphHasPageBreak.Add(value8);
						}
					}
				}
				ReleaseAll(list2);
				foreach (Paragraph item in list4)
				{
					ComObjectRelease.Release(item, "CompilationDocumentScanner.HitParas");
				}
				ScanOtherStories(document, compilationDocumentScanResult);
				return compilationDocumentScanResult;
			}
			compilationDocumentScanResult.ScanErrors.Add("标记定位与文本走查数量不一致（定位=" + list2.Count + "，走查=" + list3.Count + "）。");
			ReleaseAll(list2);
			ScanOtherStories(document, compilationDocumentScanResult);
			return compilationDocumentScanResult;
		}
		ScanOtherStories(document, compilationDocumentScanResult);
		return compilationDocumentScanResult;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static CompilationDocumentScanResult FinishWithStories(Document document, CompilationDocumentScanResult result, List<Microsoft.Office.Interop.Word.Range> markerHits, List<Paragraph> hitParagraphs)
	{
		ReleaseAll(markerHits);
		if (hitParagraphs != null)
		{
			foreach (Paragraph hitParagraph in hitParagraphs)
			{
				ComObjectRelease.Release(hitParagraph, "CompilationDocumentScanner.HitParas");
			}
		}
		ScanOtherStories(document, result);
		return result;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ReleaseAll(List<Microsoft.Office.Interop.Word.Range> ranges)
	{
		if (ranges == null)
		{
			return;
		}
		foreach (Microsoft.Office.Interop.Word.Range range in ranges)
		{
			ComObjectRelease.Release(range, "CompilationDocumentScanner.MarkerHit");
		}
		ranges.Clear();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool TryReadBody(Document document, out string bodyText, out int wordParagraphCount, CompilationDocumentScanResult result)
	{
		bodyText = null;
		wordParagraphCount = 0;
		Microsoft.Office.Interop.Word.Range value = null;
		Paragraphs value2 = null;
		try
		{
			value = document.Content;
			bodyText = value.Text ?? string.Empty;
			value2 = value.Paragraphs;
			wordParagraphCount = value2.Count;
			return true;
		}
		catch (Exception)
		{
			result.ScanErrors.Add("正文故事一次性读取失败。");
			return false;
		}
		finally
		{
			ComObjectRelease.Release(ref value2, "CompilationDocumentScanner.BodyParagraphs");
			ComObjectRelease.Release(ref value, "CompilationDocumentScanner.BodyContent");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static List<Microsoft.Office.Interop.Word.Range> FindMarkerHits(Document document, CompilationDocumentScanResult result)
	{
		List<Microsoft.Office.Interop.Word.Range> list = new List<Microsoft.Office.Interop.Word.Range>();
		Microsoft.Office.Interop.Word.Range value = null;
		Microsoft.Office.Interop.Word.Range value2 = null;
		Find value3 = null;
		try
		{
			value = document.Content;
			int end = value.End;
			value2 = value.Duplicate;
			value3 = value2.Find;
			value3.ClearFormatting();
			value3.Text = "@@汇编@@";
			value3.Forward = true;
			value3.Wrap = WdFindWrap.wdFindStop;
			value3.MatchWildcards = false;
			int num = 0;
			while (num++ < 10000)
			{
				bool flag;
				try
				{
					Find find = value3;
					object FindText = Type.Missing;
					object MatchCase = Type.Missing;
					object MatchWholeWord = Type.Missing;
					object MatchWildcards = Type.Missing;
					object MatchSoundsLike = Type.Missing;
					object MatchAllWordForms = Type.Missing;
					object Forward = Type.Missing;
					object Wrap = Type.Missing;
					object Format = Type.Missing;
					object ReplaceWith = Type.Missing;
					object Replace = Type.Missing;
					object MatchKashida = Type.Missing;
					object MatchDiacritics = Type.Missing;
					object MatchAlefHamza = Type.Missing;
					object MatchControl = Type.Missing;
					flag = find.Execute(ref FindText, ref MatchCase, ref MatchWholeWord, ref MatchWildcards, ref MatchSoundsLike, ref MatchAllWordForms, ref Forward, ref Wrap, ref Format, ref ReplaceWith, ref Replace, ref MatchKashida, ref MatchDiacritics, ref MatchAlefHamza, ref MatchControl);
				}
				catch (Exception)
				{
					result.ScanErrors.Add("标记定位执行失败。");
					ReleaseAll(list);
					return null;
				}
				if (!flag)
				{
					break;
				}
				Microsoft.Office.Interop.Word.Range value4 = value2.Duplicate;
				Paragraphs value5 = null;
				Paragraph value6 = null;
				Microsoft.Office.Interop.Word.Range value7 = null;
				try
				{
					value5 = value4.Paragraphs;
					value6 = value5[1];
					value7 = value6.Range;
					if (CompilationMarkerParser.IsExactMarker(NormalizeStoryText(value7.Text)))
					{
						list.Add(value4);
						value4 = null;
					}
				}
				finally
				{
					ComObjectRelease.Release(ref value7, "CompilationDocumentScanner.FindHitParagraphRange");
					ComObjectRelease.Release(ref value6, "CompilationDocumentScanner.FindHitParagraph");
					ComObjectRelease.Release(ref value5, "CompilationDocumentScanner.FindHitParagraphs");
					ComObjectRelease.Release(ref value4, "CompilationDocumentScanner.FindRejectedHit");
				}
				if (value2.End >= end - 1)
				{
					break;
				}
				try
				{
					value2.Start = value2.End;
				}
				catch
				{
					break;
				}
			}
			return list;
		}
		finally
		{
			ComObjectRelease.Release(ref value3, "CompilationDocumentScanner.Find");
			ComObjectRelease.Release(ref value2, "CompilationDocumentScanner.FindScope");
			ComObjectRelease.Release(ref value, "CompilationDocumentScanner.FindDocumentContent");
		}
	}

	private static int FindFirstNonEmptyAfterMarker(IList<WalkedParagraph> walked, int markerIndex)
	{
		for (int i = markerIndex + 1; i < walked.Count; i++)
		{
			if (!walked[i].IsMarker)
			{
				if (!CompilationArticleTitleResolver.IsBlankParagraph(walked[i].Text))
				{
					return i;
				}
				continue;
			}
			return -1;
		}
		return -1;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static Paragraph GetFollowingParagraph(Paragraph marker, int steps)
	{
		if (marker == null || steps <= 0)
		{
			return null;
		}
		Paragraph value = null;
		try
		{
			object Count = Type.Missing;
			value = marker.Next(ref Count);
			for (int i = 1; i < steps; i++)
			{
				if (value == null)
				{
					break;
				}
				Paragraph paragraph = value;
				Count = Type.Missing;
				Paragraph paragraph2 = paragraph.Next(ref Count);
				ComObjectRelease.Release(ref value, "CompilationDocumentScanner.TitleGapParagraph");
				value = paragraph2;
			}
			Paragraph result = value;
			value = null;
			return result;
		}
		finally
		{
			ComObjectRelease.Release(ref value, "CompilationDocumentScanner.TitleGapCurrent");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ScanOtherStories(Document document, CompilationDocumentScanResult result)
	{
		ScanStory(document, WdStoryType.wdPrimaryHeaderStory, "页眉", result);
		ScanStory(document, WdStoryType.wdPrimaryFooterStory, "页脚", result);
		ScanStory(document, WdStoryType.wdFirstPageHeaderStory, "首页页眉", result);
		ScanStory(document, WdStoryType.wdFirstPageFooterStory, "首页页脚", result);
		ScanStory(document, WdStoryType.wdEvenPagesHeaderStory, "偶数页页眉", result);
		ScanStory(document, WdStoryType.wdEvenPagesFooterStory, "偶数页页脚", result);
		ScanStory(document, WdStoryType.wdFootnotesStory, "脚注", result);
		ScanStory(document, WdStoryType.wdEndnotesStory, "尾注", result);
		ScanStory(document, WdStoryType.wdCommentsStory, "批注", result);
		ScanStory(document, WdStoryType.wdTextFrameStory, "文本框", result);
	}

	internal static string NormalizeStoryText(string text)
	{
		if (!string.IsNullOrEmpty(text))
		{
			int i;
			for (i = 0; i < text.Length && text[i] < ' ' && text[i] != '\t'; i++)
			{
			}
			int num;
			for (num = text.Length; num > i; num--)
			{
				char c = text[num - 1];
				if (c != '\r' && c != '\a' && c >= ' ')
				{
					break;
				}
			}
			return text.Substring(i, num - i);
		}
		return "";
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ScanStory(Document document, WdStoryType storyType, string label, CompilationDocumentScanResult result)
	{
		Microsoft.Office.Interop.Word.Range range = null;
		try
		{
			range = document.StoryRanges[storyType];
		}
		catch
		{
			return;
		}
		int num = 0;
		while (range != null)
		{
			num++;
			Microsoft.Office.Interop.Word.Range range2 = null;
			try
			{
				string obj2 = range.Text ?? "";
				int num2 = 0;
				string[] array = obj2.Split(new char[1] { '\r' });
				foreach (string text in array)
				{
					num2++;
					if (CompilationMarkerParser.IsExactMarker(NormalizeStoryText(text)))
					{
						result.UnsupportedRegionMarkers.Add(label + "（第 " + num + " 段范围，约第 " + num2 + " 行）");
						break;
					}
				}
			}
			catch (Exception)
			{
				result.ScanErrors.Add(label + "（第 " + num + " 段范围读取失败）");
			}
			try
			{
				range2 = range.NextStoryRange;
			}
			catch
			{
				range2 = null;
			}
			ComObjectRelease.Release(ref range, "CompilationDocumentScanner.Story");
			range = range2;
		}
	}
}
