using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DocumentRepository.Models;
using DocumentRepository.Services.Detection;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Interop;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Rename;

public static class RenameTitleRecognitionService
{
	private const int MaximumFirstPageParagraphs = 24;

	private const int MaximumTitleParagraphs = 3;

	private const float FontSizeEqualityTolerance = 0.01f;

	private static readonly Regex CopyNumberPattern = new Regex("^\\d{1,6}$", RegexOptions.Compiled);

	private static readonly Regex SecurityMarkPattern = new Regex("^(秘密|机密|绝密)([★*].+)?$", RegexOptions.Compiled);

	private static readonly Regex UrgencyMarkPattern = new Regex("^(加急|特急)$", RegexOptions.Compiled);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static RenameTitleEvidence Recognize(Document document, IList<DocumentElement> items)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		if (items == null)
		{
			throw new ArgumentNullException("items");
		}
		List<RenameTitleParagraphFact> list = new List<RenameTitleParagraphFact>();
		foreach (DocumentElement item in items)
		{
			if (item != null && !item.IsEmpty && !IsExcludedObject(item))
			{
				RenameTitleParagraphFact renameTitleParagraphFact = ReadMetric(document, item);
				if (renameTitleParagraphFact.PageNumber > 1)
				{
					break;
				}
				list.Add(renameTitleParagraphFact);
				if (list.Count >= 24)
				{
					break;
				}
			}
		}
		return Recognize(list);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static RenameTitleEvidence Recognize(IList<RenameTitleParagraphFact> facts)
	{
		if (facts == null)
		{
			throw new ArgumentNullException("facts");
		}
		RenameTitleEvidence renameTitleEvidence = RecognizeOpeningLargestFont(facts);
		if (renameTitleEvidence == null)
		{
			renameTitleEvidence = RecognizeFormattingTitle(facts);
			if (renameTitleEvidence != null)
			{
				return renameTitleEvidence;
			}
			return RecognizeFirstEffectiveParagraph(facts);
		}
		return renameTitleEvidence;
	}

	private static RenameTitleEvidence RecognizeOpeningLargestFont(IList<RenameTitleParagraphFact> metrics)
	{
		if (metrics.Count == 0)
		{
			return null;
		}
		RenameTitleParagraphFact[] array = (from x in metrics.Where(IsTitleCandidate)
			orderby x.ParagraphIndex
			select x).ToArray();
		float[] array2 = (from x in array.Select((RenameTitleParagraphFact x) => x.FontSize).Where(IsValidFontSize)
			orderby x
			select x).ToArray();
		if (array2.Length >= 2)
		{
			float num = array2[array2.Length - 1];
			List<List<RenameTitleParagraphFact>> list = new List<List<RenameTitleParagraphFact>>();
			List<RenameTitleParagraphFact> list2 = null;
			RenameTitleParagraphFact[] array3 = array;
			foreach (RenameTitleParagraphFact renameTitleParagraphFact in array3)
			{
				if (Math.Abs(renameTitleParagraphFact.FontSize - num) <= 0.01f)
				{
					if (list2 == null || renameTitleParagraphFact.ParagraphIndex != list2[list2.Count - 1].ParagraphIndex + 1 || !HasSameTitleFont(list2[list2.Count - 1], renameTitleParagraphFact))
					{
						list2 = new List<RenameTitleParagraphFact>();
						list.Add(list2);
					}
					list2.Add(renameTitleParagraphFact);
				}
			}
			List<RenameTitleParagraphFact> list3 = (from x in list
				where x.Count > 0 && x.Count <= 3
				orderby x[0].ParagraphIndex
				select x).FirstOrDefault();
			if (list3 == null)
			{
				return null;
			}
			string text = JoinTitle(list3.Select((RenameTitleParagraphFact x) => x.Text));
			if (string.IsNullOrWhiteSpace(text))
			{
				return null;
			}
			RenameTitleEvidence renameTitleEvidence = new RenameTitleEvidence();
			renameTitleEvidence.Title = text;
			renameTitleEvidence.Tier = RenameTitleRecognitionTier.OpeningLargestFont;
			renameTitleEvidence.Confidence = 0.96f;
			renameTitleEvidence.ParagraphIndexes.AddRange(list3.Select((RenameTitleParagraphFact x) => x.ParagraphIndex));
			return renameTitleEvidence;
		}
		return null;
	}

	private static RenameTitleEvidence RecognizeFormattingTitle(IList<RenameTitleParagraphFact> items)
	{
		List<RenameTitleParagraphFact> list = new List<RenameTitleParagraphFact>();
		foreach (RenameTitleParagraphFact item in items)
		{
			if (item == null || (list.Count == 0 && IsHeaderPreamble(item)))
			{
				continue;
			}
			if (item.Type == ElementType.MainTitle && IsTitleCandidate(item))
			{
				if (list.Count > 0 && item.ParagraphIndex != list[list.Count - 1].ParagraphIndex + 1)
				{
					break;
				}
				list.Add(item);
			}
			else if (list.Count > 0 || IsHardTitleBoundary(item.Type) || IsTitleCandidate(item))
			{
				break;
			}
		}
		string text = JoinTitle(list.Select((RenameTitleParagraphFact x) => x.Text));
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		RenameTitleEvidence renameTitleEvidence = new RenameTitleEvidence();
		renameTitleEvidence.Title = text;
		renameTitleEvidence.Tier = RenameTitleRecognitionTier.FormattingClassifier;
		renameTitleEvidence.Confidence = 0.9f;
		renameTitleEvidence.ParagraphIndexes.AddRange(list.Select((RenameTitleParagraphFact x) => x.ParagraphIndex));
		return renameTitleEvidence;
	}

	private static RenameTitleEvidence RecognizeFirstEffectiveParagraph(IList<RenameTitleParagraphFact> items)
	{
		RenameTitleParagraphFact renameTitleParagraphFact = items.FirstOrDefault(IsTitleCandidate);
		string text = ((renameTitleParagraphFact == null) ? string.Empty : NormalizeNamePart(renameTitleParagraphFact.Text));
		if (string.IsNullOrWhiteSpace(text))
		{
			return new RenameTitleEvidence
			{
				Title = string.Empty,
				Tier = RenameTitleRecognitionTier.None
			};
		}
		return new RenameTitleEvidence
		{
			Title = text,
			Tier = RenameTitleRecognitionTier.FirstEffectiveParagraph,
			Confidence = 0.55f,
			ParagraphIndexes = { renameTitleParagraphFact.ParagraphIndex }
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static RenameTitleParagraphFact ReadMetric(Document document, DocumentElement item)
	{
		Microsoft.Office.Interop.Word.Range value = null;
		Microsoft.Office.Interop.Word.Range value2 = null;
		Font value3 = null;
		try
		{
			int num = Math.Max(item.RangeStart + 1, Math.Min(item.RangeEnd - 1, item.RangeStart + 2));
			object Start = item.RangeStart;
			object End = Math.Max(item.RangeStart, item.RangeEnd - 1);
			value = document.Range(ref Start, ref End);
			End = item.RangeStart;
			Start = num;
			value2 = document.Range(ref End, ref Start);
			value3 = value2.Font;
			float num2 = Convert.ToSingle(value3.Size);
			if (!IsValidFontSize(num2))
			{
				ComObjectRelease.Release(ref value3, "RenameTitleRecognitionService.MixedFont");
				value3 = value.Font;
				num2 = Convert.ToSingle(value3.Size);
			}
			int pageNumber = WordPageOrdinalService.ReadPhysicalPageOrdinal(value2, "一键命名标题候选段落");
			string text = Convert.ToString(value3.NameFarEast);
			if (string.IsNullOrWhiteSpace(text))
			{
				text = Convert.ToString(value3.Name);
			}
			return new RenameTitleParagraphFact
			{
				ParagraphIndex = item.ParagraphIndex,
				Text = item.Text,
				Type = item.Type,
				FontSize = num2,
				FontName = text,
				PageNumber = pageNumber,
				IsBlackFont = IsBlackFont(value3)
			};
		}
		finally
		{
			ComObjectRelease.Release(ref value3, "RenameTitleRecognitionService.Font");
			ComObjectRelease.Release(ref value2, "RenameTitleRecognitionService.Sample");
			ComObjectRelease.Release(ref value, "RenameTitleRecognitionService.Range");
		}
	}

	private static bool IsValidFontSize(float size)
	{
		if (!float.IsNaN(size) && !float.IsInfinity(size) && size > 0f)
		{
			return size < 200f;
		}
		return false;
	}

	private static bool IsBlackFont(Font font)
	{
		if (font != null)
		{
			int num = Convert.ToInt32(font.Color);
			int num2 = Convert.ToInt32(font.ColorIndex);
			if (num != 0 && num != -16777216 && num2 != 1)
			{
				return num2 == 0;
			}
			return true;
		}
		return false;
	}

	private static bool HasSameTitleFont(RenameTitleParagraphFact left, RenameTitleParagraphFact right)
	{
		string a = (left?.FontName ?? string.Empty).Trim();
		string b = (right?.FontName ?? string.Empty).Trim();
		return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsTitleCandidate(RenameTitleParagraphFact item)
	{
		if (item == null || item.PageNumber != 1 || !item.IsBlackFont)
		{
			return false;
		}
		if (!IsValidFontSize(item.FontSize))
		{
			return false;
		}
		if (item.Type == ElementType.DocumentNumber || item.Type == ElementType.SubTitle || IsHardTitleBoundary(item.Type))
		{
			return false;
		}
		if (!Detector.IsSalutation(item.Text))
		{
			if (IsSubtitleText(item.Text) || IsTopMarkText(item.Text))
			{
				return false;
			}
			return true;
		}
		return false;
	}

	private static bool IsHeaderPreamble(RenameTitleParagraphFact item)
	{
		if (item == null)
		{
			return true;
		}
		if (item.Type != ElementType.DocumentNumber && item.IsBlackFont)
		{
			return IsTopMarkText(item.Text);
		}
		return true;
	}

	private static bool IsTopMarkText(string text)
	{
		string input = NormalizeNamePart(text);
		if (!CopyNumberPattern.IsMatch(input) && !SecurityMarkPattern.IsMatch(input))
		{
			return UrgencyMarkPattern.IsMatch(input);
		}
		return true;
	}

	private static bool IsExcludedObject(DocumentElement item)
	{
		if (!item.IsInTable && !item.HasInlineShape && !item.HasShape && item.Type != ElementType.Table)
		{
			return item.Type == ElementType.Image;
		}
		return true;
	}

	private static bool IsHardTitleBoundary(ElementType type)
	{
		if (type != ElementType.Salutation && type != ElementType.Level1Title && type != ElementType.Level2Title && type != ElementType.Level3Title && type != ElementType.AttachmentMarker && type != ElementType.AttachmentTitle && type != ElementType.AttachmentListFirst && type != ElementType.AttachmentListSingle && type != ElementType.AttachmentListContinuation && type != ElementType.Signature && type != ElementType.SignatureDate)
		{
			return type == ElementType.Imprint;
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool IsSubtitleText(string text)
	{
		string text2 = NormalizeNamePart(text);
		if (!text2.StartsWith("——", StringComparison.Ordinal))
		{
			return text2.StartsWith("--", StringComparison.Ordinal);
		}
		return true;
	}

	private static string JoinTitle(IEnumerable<string> parts)
	{
		return string.Concat(from x in parts.Select(NormalizeNamePart)
			where !string.IsNullOrWhiteSpace(x)
			select x);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string NormalizeNamePart(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return string.Empty;
		}
		return text.Replace("\r", string.Empty).Replace("\n", string.Empty).Replace("\a", string.Empty)
			.Replace("\t", string.Empty)
			.Replace(" ", string.Empty)
			.Replace("\u3000", string.Empty)
			.Trim();
	}
}
