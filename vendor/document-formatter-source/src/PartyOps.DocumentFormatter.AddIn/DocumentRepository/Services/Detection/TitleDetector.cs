using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DocumentRepository.Services.Hosting;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Detection;

public static class TitleDetector
{
	public class TitleResult
	{
		public string MainTitle { get; set; }

		public string Subtitle { get; set; }
	}

	internal class TitleCandidate
	{
		public int Index;

		public string Text;

		public float FontSize;
	}

	private static readonly Regex DocNumberRegex = new Regex("^\\s*(?<num>[一-龥A-Za-z]{1,20}\\s*[\\[{【（(〔]\\s*\\d{4}\\s*[\\]}】）)〕]\\s*\\d{1,4}\\s*号)(?:\\s+签发人\\s*[:：]\\s*\\S+.*)?\\s*$", RegexOptions.Compiled);

	public static TitleResult Extract(Document doc, FormatConfig cfg = null)
	{
		TitleResult titleResult = new TitleResult();
		titleResult.MainTitle = ExtractMainTitle(doc, cfg);
		if (!string.IsNullOrWhiteSpace(titleResult.MainTitle))
		{
			titleResult.Subtitle = ExtractSubtitle(doc, titleResult.MainTitle, cfg);
		}
		return titleResult;
	}

	public static string ExtractMainTitle(Document doc, FormatConfig cfg = null)
	{
		string text = Rule1_FontSize(doc);
		if (!string.IsNullOrWhiteSpace(text))
		{
			return text;
		}
		return Rule2_Sequential(doc, cfg);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string Rule1_FontSize(Document doc)
	{
		Paragraphs paragraphs = null;
		try
		{
			paragraphs = doc.Paragraphs;
			List<TitleCandidate> list = new List<TitleCandidate>();
			int num = Math.Min(paragraphs.Count, 80);
			int num2 = 0;
			for (int i = 1; i <= num; i++)
			{
				Paragraph paragraph = null;
				Microsoft.Office.Interop.Word.Range range = null;
				try
				{
					paragraph = paragraphs[i];
					range = paragraph.Range;
					string text = NormalizeText(range.Text);
					if (string.IsNullOrWhiteSpace(text))
					{
						continue;
					}
					num2++;
					if (num2 > 24)
					{
						break;
					}
					if (!IsDocumentNumber(text) && text.Length <= 120 && IsBlack(range))
					{
						float size = GetSize(range);
						if (!(size <= 0f) && size <= 80f)
						{
							list.Add(new TitleCandidate
							{
								Index = i,
								Text = text,
								FontSize = size
							});
						}
					}
				}
				finally
				{
					if (range != null)
					{
						ComObjectRelease.ReleaseOwned(range, "Rule1_FontSize", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\Services\\Detection\\TitleDetector.cs", 75);
					}
					if (paragraph != null)
					{
						ComObjectRelease.ReleaseOwned(paragraph, "Rule1_FontSize", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\Services\\Detection\\TitleDetector.cs", 76);
					}
				}
			}
			if (list.Count == 0)
			{
				return "";
			}
			float num3 = 0f;
			foreach (TitleCandidate item in list)
			{
				if (item.FontSize > num3)
				{
					num3 = item.FontSize;
				}
			}
			int num4 = 0;
			foreach (TitleCandidate item2 in list)
			{
				if (item2.FontSize < num3 - 0.25f)
				{
					num4++;
				}
			}
			if (num4 == 0)
			{
				return "";
			}
			int num5 = -1;
			foreach (TitleCandidate item3 in list)
			{
				if (!(Math.Abs(item3.FontSize - num3) > 0.25f))
				{
					num5 = item3.Index;
					break;
				}
			}
			if (num5 < 0)
			{
				return "";
			}
			List<string> list2 = new List<string>();
			for (int j = num5; j <= num; j++)
			{
				Paragraph paragraph2 = null;
				Microsoft.Office.Interop.Word.Range range2 = null;
				try
				{
					paragraph2 = paragraphs[j];
					range2 = paragraph2.Range;
					string text2 = NormalizeText(range2.Text);
					if (!string.IsNullOrWhiteSpace(text2) && !IsDocumentNumber(text2) && text2.Length <= 120 && IsBlack(range2) && Math.Abs(GetSize(range2) - num3) <= 0.25f)
					{
						list2.Add(text2);
						continue;
					}
				}
				finally
				{
					if (range2 != null)
					{
						ComObjectRelease.ReleaseOwned(range2, "Rule1_FontSize", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\Services\\Detection\\TitleDetector.cs", 112);
					}
					if (paragraph2 != null)
					{
						ComObjectRelease.ReleaseOwned(paragraph2, "Rule1_FontSize", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\Services\\Detection\\TitleDetector.cs", 113);
					}
				}
				break;
			}
			return string.Join("", list2.ToArray()).Trim();
		}
		finally
		{
			if (paragraphs != null)
			{
				ComObjectRelease.ReleaseOwned(paragraphs, "Rule1_FontSize", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\Services\\Detection\\TitleDetector.cs", 120);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string Rule2_Sequential(Document doc, FormatConfig cfg)
	{
		Paragraphs paragraphs = null;
		try
		{
			paragraphs = doc.Paragraphs;
			List<string> list = new List<string>();
			bool flag = false;
			int num = Math.Min(paragraphs.Count, 16);
			int num2 = 0;
			for (int i = 1; i <= num; i++)
			{
				Paragraph paragraph = null;
				Microsoft.Office.Interop.Word.Range range = null;
				try
				{
					paragraph = paragraphs[i];
					range = paragraph.Range;
					string text = NormalizeText(range.Text);
					if (string.IsNullOrWhiteSpace(text))
					{
						continue;
					}
					num2++;
					if (IsDocumentNumber(text))
					{
						continue;
					}
					if (flag || !Detector.IsMainTitleCandidate(text, cfg))
					{
						if (flag)
						{
							if (Detector.IsSubTitleCandidate(text) || MainTitleCandidateDetector.IsParenthesizedSubTitleCandidate(text, cfg) || !Detector.IsMainTitleCandidate(text, cfg) || list.Count >= 3)
							{
								break;
							}
							list.Add(text);
						}
						else if (num2 >= 6)
						{
							break;
						}
					}
					else
					{
						flag = true;
						list.Add(text);
					}
				}
				finally
				{
					if (range != null)
					{
						ComObjectRelease.ReleaseOwned(range, "Rule2_Sequential", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\Services\\Detection\\TitleDetector.cs", 166);
					}
					if (paragraph != null)
					{
						ComObjectRelease.ReleaseOwned(paragraph, "Rule2_Sequential", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\Services\\Detection\\TitleDetector.cs", 167);
					}
				}
			}
			return string.Join("", list.ToArray()).Trim();
		}
		finally
		{
			if (paragraphs != null)
			{
				ComObjectRelease.ReleaseOwned(paragraphs, "Rule2_Sequential", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\Services\\Detection\\TitleDetector.cs", 174);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string ExtractSubtitle(Document doc, string mainTitle, FormatConfig cfg)
	{
		if (string.IsNullOrWhiteSpace(mainTitle))
		{
			return "";
		}
		Paragraphs paragraphs = null;
		try
		{
			paragraphs = doc.Paragraphs;
			int num = Math.Min(paragraphs.Count, 16);
			bool flag = false;
			List<string> list = new List<string>();
			for (int i = 1; i <= num; i++)
			{
				Paragraph paragraph = null;
				Microsoft.Office.Interop.Word.Range range = null;
				try
				{
					paragraph = paragraphs[i];
					range = paragraph.Range;
					string text = NormalizeText(range.Text);
					if (string.IsNullOrWhiteSpace(text))
					{
						continue;
					}
					if (!flag && text == mainTitle)
					{
						flag = true;
					}
					else
					{
						if (flag || !text.Contains(mainTitle))
						{
							if (!flag || IsDocumentNumber(text))
							{
								continue;
							}
							bool flag2 = MainTitleCandidateDetector.IsParenthesizedSubTitleCandidate(text, cfg);
							if (!(Detector.IsSubTitleCandidate(text) || flag2))
							{
								if (!Detector.IsMainTitleCandidate(text, cfg) || list.Count != 0)
								{
									break;
								}
							}
							else
							{
								list.Add(text);
							}
							continue;
						}
						flag = true;
					}
				}
				finally
				{
					if (range != null)
					{
						ComObjectRelease.ReleaseOwned(range, "ExtractSubtitle", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\Services\\Detection\\TitleDetector.cs", 220);
					}
					if (paragraph != null)
					{
						ComObjectRelease.ReleaseOwned(paragraph, "ExtractSubtitle", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\Services\\Detection\\TitleDetector.cs", 221);
					}
				}
			}
			return string.Join("", list.ToArray()).Trim();
		}
		finally
		{
			if (paragraphs != null)
			{
				ComObjectRelease.ReleaseOwned(paragraphs, "ExtractSubtitle", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\Services\\Detection\\TitleDetector.cs", 228);
			}
		}
	}

	public static bool IsDocumentNumber(string text)
	{
		return DocNumberRegex.IsMatch(text ?? "");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool IsBlack(Microsoft.Office.Interop.Word.Range range)
	{
		Font font = null;
		try
		{
			font = range.Font;
			int num = Convert.ToInt32(font.Color);
			return num == 0 || num == 0 || num == -16777216 || num == 9999999;
		}
		catch
		{
			return true;
		}
		finally
		{
			if (font != null)
			{
				ComObjectRelease.ReleaseOwned(font, "IsBlack", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\Services\\Detection\\TitleDetector.cs", 256);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static float GetSize(Microsoft.Office.Interop.Word.Range range)
	{
		Font font = null;
		try
		{
			font = range.Font;
			return font.Size;
		}
		catch
		{
			return 0f;
		}
		finally
		{
			if (font != null)
			{
				ComObjectRelease.ReleaseOwned(font, "GetSize", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\Services\\Detection\\TitleDetector.cs", 268);
			}
		}
	}

	public static string NormalizeText(string text)
	{
		return (text ?? "").Trim(new char[] { '\r', '\n', '\u0007', ' ', '\t', '\u00A0' });
	}
}
