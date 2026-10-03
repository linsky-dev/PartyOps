using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Services.Detection;

namespace DocumentRepository.Services.Analysis;

public static class ParagraphElementClassifier
{
	public static ElementType Classify(string text, FormatConfig cfg, ParagraphClassificationState state, ParagraphClassificationOptions options)
	{
		state = state ?? new ParagraphClassificationState();
		options = options ?? new ParagraphClassificationOptions();
		text = ParagraphTextAnalysisService.CleanParagraphText(text);
		if (string.IsNullOrWhiteSpace(text))
		{
			state.InAttachmentList = false;
			return ElementType.Unknown;
		}
		ElementType elementType;
		if (options.PreferSignaturePair && IsSelectedSignatureLine(text, cfg) && options.NextParagraphIsDateLine)
		{
			elementType = ElementType.Signature;
			state.InAttachmentList = false;
		}
		else if (options.PreferSignaturePair && SignatureDetector.IsDateLine(text) && options.PreviousParagraphIsSignatureLine)
		{
			elementType = ElementType.SignatureDate;
			state.InAttachmentList = false;
		}
		else if (AttachmentDetector.IsAttachmentListMarkerAloneText(text))
		{
			elementType = ElementType.AttachmentListFirst;
			state.InAttachmentList = true;
			state.InAttachmentBody = false;
		}
		else if (!AttachmentDetector.IsAttachmentListFirstText(text))
		{
			if (!state.InAttachmentList || !AttachmentDetector.IsAttachmentListItemText(text))
			{
				if (AttachmentDetector.IsAttachmentListSingleText(text))
				{
					elementType = ElementType.AttachmentListSingle;
					state.InAttachmentList = false;
					state.InAttachmentBody = false;
				}
				else if (AttachmentDetector.IsAttachmentMarkerText(text))
				{
					elementType = ElementType.AttachmentMarker;
					state.BeginAttachmentBody();
				}
				else if (state.InAttachmentBody)
				{
					elementType = ClassifyAttachmentBodyParagraph(text, cfg, state);
					state.InAttachmentList = false;
				}
				else
				{
					elementType = Detector.DetectParagraphType(text, cfg);
					state.InAttachmentList = false;
				}
			}
			else
			{
				elementType = ElementType.AttachmentListContinuation;
			}
		}
		else
		{
			elementType = ElementType.AttachmentListFirst;
			state.InAttachmentList = true;
			state.InAttachmentBody = false;
		}
		if (options.ForceFirstParagraphAsTitle && !state.FirstNonEmptySeen && elementType != ElementType.DocumentNumber)
		{
			elementType = ElementType.MainTitle;
		}
		if (!state.InAttachmentBody)
		{
			elementType = NormalizeOpeningTitleType(text, cfg, elementType, state);
		}
		if (elementType != ElementType.DocumentNumber)
		{
			state.FirstNonEmptySeen = true;
		}
		return elementType;
	}

	private static ElementType NormalizeOpeningTitleType(string text, FormatConfig cfg, ElementType detected, ParagraphClassificationState state)
	{
		if (!state.OpeningTitleRegionOpen)
		{
			return detected;
		}
		if (detected != ElementType.Unknown && detected != ElementType.DocumentNumber)
		{
			if (!state.OpeningTitleSeen || !state.OpeningPreviousWasTitle || !MainTitleCandidateDetector.IsParenthesizedSubTitleCandidate(text, cfg))
			{
				if (detected == ElementType.MainTitle || detected == ElementType.SubTitle)
				{
					state.OpeningTitleSeen = true;
					state.OpeningPreviousWasTitle = true;
					return detected;
				}
				state.OpeningPreviousWasTitle = false;
				if (state.OpeningTitleSeen || IsOpeningTitleBoundary(detected))
				{
					state.OpeningTitleRegionOpen = false;
				}
				return detected;
			}
			state.OpeningPreviousWasTitle = true;
			return ElementType.SubTitle;
		}
		return detected;
	}

	private static bool IsOpeningTitleBoundary(ElementType type)
	{
		if (type != ElementType.Body && type != ElementType.Salutation && type != ElementType.Level1Title && type != ElementType.Level2Title && type != ElementType.Level3Title && type != ElementType.AttachmentMarker && type != ElementType.AttachmentTitle && type != ElementType.AttachmentSubTitle && type != ElementType.AttachmentListFirst && type != ElementType.AttachmentListSingle && type != ElementType.AttachmentListContinuation && type != ElementType.Signature)
		{
			return type == ElementType.SignatureDate;
		}
		return true;
	}

	private static ElementType ClassifyAttachmentBodyParagraph(string text, FormatConfig cfg, ParagraphClassificationState state)
	{
		ElementType elementType = Detector.DetectParagraphType(text, cfg);
		if (state.AttachmentTitleScanLeft > 0)
		{
			state.AttachmentTitleScanLeft--;
			if (state.AttachmentMainTitleParaCount > 0 && !state.AttachmentSubTitleChecked && IsAttachmentSubTitleCandidate(text, cfg, elementType))
			{
				state.AttachmentMainTitleFinished = true;
				state.AttachmentSubTitleChecked = true;
				state.AttachmentLastWasMainTitle = false;
				state.AttachmentTitleScanLeft = 0;
				return ElementType.AttachmentSubTitle;
			}
			if (!state.AttachmentMainTitleFinished && state.AttachmentMainTitleParaCount < 3)
			{
				if (elementType == ElementType.MainTitle)
				{
					state.AttachmentMainTitleParaCount++;
					state.AttachmentLastWasMainTitle = true;
					return ElementType.AttachmentTitle;
				}
				state.AttachmentMainTitleFinished = true;
			}
			if (state.AttachmentLastWasMainTitle && !state.AttachmentSubTitleChecked)
			{
				state.AttachmentSubTitleChecked = true;
				state.AttachmentLastWasMainTitle = false;
				if (elementType == ElementType.SubTitle)
				{
					state.AttachmentTitleScanLeft = 0;
					return ElementType.AttachmentSubTitle;
				}
			}
			if (state.AttachmentTitleScanLeft <= 0)
			{
				state.EndAttachmentBodyTitleScan();
			}
			return SuppressOpeningTitleAfterAttachmentTitle(elementType);
		}
		return SuppressOpeningTitleAfterAttachmentTitle(elementType);
	}

	private static bool IsAttachmentSubTitleCandidate(string text, FormatConfig cfg, ElementType detected)
	{
		if (detected != ElementType.SubTitle)
		{
			return MainTitleCandidateDetector.IsSubTitleCandidate(text, cfg);
		}
		return true;
	}

	private static ElementType SuppressOpeningTitleAfterAttachmentTitle(ElementType detected)
	{
		if (detected == ElementType.MainTitle || detected == ElementType.SubTitle)
		{
			return ElementType.Body;
		}
		return detected;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool IsFirstParagraphTitleMode(FormatConfig cfg)
	{
		return (cfg?.MainTitle?.RecognitionStyle ?? string.Empty).IndexOf("首段", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool IsSelectedSignatureLine(string text, FormatConfig cfg)
	{
		if (!string.IsNullOrWhiteSpace(text))
		{
			string text2 = text.Trim();
			if (SignatureDetector.IsDateLine(text2))
			{
				return false;
			}
			if (TitleDetector.IsDocumentNumber(text2))
			{
				return false;
			}
			if (Detector.IsConfiguredHeading(text2, cfg))
			{
				return false;
			}
			if (Detector.IsSalutation(text2))
			{
				return false;
			}
			if (!SignatureDetector.IsSignatureLine(text2))
			{
				if (SignatureDetector.HasOrgSuffix(text2) && text2.Length <= 30 && !text2.EndsWith("。", StringComparison.Ordinal) && !text2.EndsWith("，", StringComparison.Ordinal) && !char.IsDigit(text2[text2.Length - 1]))
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
