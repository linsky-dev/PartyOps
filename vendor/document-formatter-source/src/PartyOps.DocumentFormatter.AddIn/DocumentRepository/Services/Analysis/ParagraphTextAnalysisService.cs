using System.Text.RegularExpressions;
using DocumentRepository.Models;
using DocumentRepository.Services.Detection;

namespace DocumentRepository.Services.Analysis;

public static class ParagraphTextAnalysisService
{
	private static readonly Regex EnglishNumberRegex = new Regex("[A-Za-z0-9\\.,%\\+\\$\\u00a5\\uffe5\\u00b0\\u2030\\u2103\\-]", RegexOptions.Compiled);

	public static void SetPostProcessFacts(DocumentElement element)
	{
		if (element != null)
		{
			string obj = element.Text ?? string.Empty;
			bool flag = obj.IndexOf('\v') >= 0;
			string text = (element.Text = CleanParagraphText(obj));
			bool flag2 = AllowsInlinePostProcess(element.Type, element.IsEmpty, element.IsInTable, element.HasInlineShape, element.HasShape);
			bool flag3 = AllowsBodyPostProcess(element.Type, element.IsEmpty, element.IsInTable, element.HasInlineShape, element.HasShape);
			element.AllowsInlinePostProcess = flag2;
			element.AllowsBodyPostProcess = flag3;
			element.HasEnglishNumbers = flag2 && HasEnglishNumber(text);
			bool flag4 = KeywordCandidatePolicy.IsEligible(element.Type, text, flag3);
			element.HasKeywordCandidate = flag4 && HasKeywordCandidate(text);
			element.HasSemicolonCandidate = flag4 && HasSemicolonCandidate(text);
			element.HasTabs = flag2 && HasTab(text);
			element.HasOrphanCandidate = flag3 && !flag && HasOrphanCandidate(text);
		}
	}

	public static void UpdateResultFlags(DocumentAnalysisResult result, DocumentElement element)
	{
		if (result != null && element != null)
		{
			string text = CleanParagraphText(element.Text);
			if (!result.HasAttachments && IsAttachmentText(text))
			{
				result.HasAttachments = true;
			}
			if (!result.HasEnglishNumbers && element.HasEnglishNumbers)
			{
				result.HasEnglishNumbers = true;
			}
			if (!result.HasKeywordCandidates && element.HasKeywordCandidate)
			{
				result.HasKeywordCandidates = true;
			}
			if (!result.HasSemicolonCandidates && element.HasSemicolonCandidate)
			{
				result.HasSemicolonCandidates = true;
			}
			if (!result.HasTabs && element.HasTabs)
			{
				result.HasTabs = true;
			}
			if (!result.HasOrphanCandidates && element.HasOrphanCandidate)
			{
				result.HasOrphanCandidates = true;
			}
		}
	}

	public static bool IsAttachmentText(string text)
	{
		if (!string.IsNullOrWhiteSpace(text))
		{
			if (!AttachmentDetector.IsAttachmentMarkerText(text) && !AttachmentDetector.IsAttachmentListMarkerAloneText(text) && !AttachmentDetector.IsAttachmentListFirstText(text))
			{
				return AttachmentDetector.IsAttachmentListSingleText(text);
			}
			return true;
		}
		return false;
	}

	public static bool HasEnglishNumber(string text)
	{
		if (!string.IsNullOrEmpty(text))
		{
			return EnglishNumberRegex.IsMatch(text);
		}
		return false;
	}

	public static bool HasKeywordCandidate(string text)
	{
		return KeywordPhraseCatalog.ContainsCandidate(text);
	}

	public static void RefreshKeywordFactsAfterTypeNormalization(DocumentAnalysisResult result)
	{
		if (result == null || result.Elements == null || result.Elements.Items == null)
		{
			return;
		}
		bool hasKeywordCandidates = false;
		bool hasSemicolonCandidates = false;
		foreach (DocumentElement item in result.Elements.Items)
		{
			if (item != null)
			{
				string text = CleanParagraphText(item.Text);
				bool flag = KeywordCandidatePolicy.IsEligible(bodyPostProcessAllowed: item.AllowsBodyPostProcess = AllowsBodyPostProcess(item.Type, item.IsEmpty, item.IsInTable, item.HasInlineShape, item.HasShape), type: item.Type, text: text);
				item.HasKeywordCandidate = flag && HasKeywordCandidate(text);
				item.HasSemicolonCandidate = flag && HasSemicolonCandidate(text);
				if (item.HasKeywordCandidate)
				{
					hasKeywordCandidates = true;
				}
				if (item.HasSemicolonCandidate)
				{
					hasSemicolonCandidates = true;
				}
			}
		}
		result.HasKeywordCandidates = hasKeywordCandidates;
		result.HasSemicolonCandidates = hasSemicolonCandidates;
		result.Elements.HasKeywordCandidates = hasKeywordCandidates;
		result.Elements.HasSemicolonCandidates = hasSemicolonCandidates;
	}

	public static bool HasSemicolonCandidate(string text)
	{
		if (!string.IsNullOrEmpty(text))
		{
			if (HasKeywordCandidate(text))
			{
				if (text.IndexOf('，') < 0 && text.IndexOf('、') < 0 && text.IndexOf('。') < 0 && text.IndexOf('！') < 0)
				{
					return text.IndexOf('!') >= 0;
				}
				return true;
			}
			return false;
		}
		return false;
	}

	public static bool HasTab(string text)
	{
		if (!string.IsNullOrEmpty(text))
		{
			return text.IndexOf('\t') >= 0;
		}
		return false;
	}

	public static bool HasOrphanCandidate(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		string text2 = text.Trim();
		if (text2.IndexOf('\v') < 0)
		{
			if (EstimateHalfWidthUnits(text2) < 24)
			{
				return false;
			}
			int num = text2.Length - 1;
			while (num >= 0)
			{
				char c = text2[num];
				if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c))
				{
					num--;
					continue;
				}
				if (c >= '一')
				{
					return c <= '鿿';
				}
				return false;
			}
			return false;
		}
		return false;
	}

	private static int EstimateHalfWidthUnits(string text)
	{
		int num = 0;
		string text2 = text ?? string.Empty;
		foreach (char c in text2)
		{
			if (!char.IsWhiteSpace(c))
			{
				num += ((c <= '\u007f') ? 1 : 2);
			}
		}
		return num;
	}

	public static bool AllowsInlinePostProcess(ElementType type, bool isEmpty, bool isInTable, bool hasInlineShape, bool hasShape)
	{
		if (!(isEmpty || isInTable || hasInlineShape || hasShape))
		{
			if (type == ElementType.Table || type == ElementType.TableHeader || type == ElementType.Image)
			{
				return false;
			}
			return true;
		}
		return false;
	}

	public static bool AllowsBodyPostProcess(ElementType type, bool isEmpty, bool isInTable, bool hasInlineShape, bool hasShape)
	{
		if (!AllowsInlinePostProcess(type, isEmpty, isInTable, hasInlineShape, hasShape))
		{
			return false;
		}
		switch (type)
		{
		case ElementType.MainTitle:
		case ElementType.SubTitle:
		case ElementType.Level1Title:
		case ElementType.Level2Title:
		case ElementType.Level3Title:
		case ElementType.Signature:
		case ElementType.SignatureDate:
		case ElementType.AttachmentMarker:
		case ElementType.AttachmentTitle:
		case ElementType.AttachmentSubTitle:
		case ElementType.DocumentNumber:
		case ElementType.RedHeader:
		case ElementType.Imprint:
			return false;
		default:
			return true;
		}
	}

	public static string CleanParagraphText(string text)
	{
		return ParagraphIdentityTextNormalizer.Normalize(text);
	}
}
