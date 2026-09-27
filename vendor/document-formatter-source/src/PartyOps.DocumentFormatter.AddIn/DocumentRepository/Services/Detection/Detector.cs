using DocumentRepository.Models;

namespace DocumentRepository.Services.Detection;

public static class Detector
{
	public static ElementType DetectParagraphType(string text, FormatConfig cfg = null)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return ElementType.Unknown;
		}
		text = text.Trim();
		if (AttachmentDetector.IsAttachmentParagraphText(text))
		{
			return ElementType.AttachmentMarker;
		}
		if (!TitleDetector.IsDocumentNumber(text))
		{
			ElementType elementType = HeadingLevelDetector.DetectConfiguredHeading(text, cfg);
			if (elementType != ElementType.Unknown)
			{
				return elementType;
			}
			if (!IsSubTitleCandidate(text, cfg))
			{
				if (IsMainTitleCandidate(text, cfg))
				{
					return ElementType.MainTitle;
				}
				if (IsSalutation(text, cfg))
				{
					return ElementType.Salutation;
				}
				return ElementType.Body;
			}
			return ElementType.SubTitle;
		}
		return ElementType.DocumentNumber;
	}

	public static bool IsMainTitleCandidate(string text, FormatConfig cfg = null)
	{
		return MainTitleCandidateDetector.IsMainTitleCandidate(text, cfg);
	}

	public static bool IsSubTitleCandidate(string text, FormatConfig cfg = null)
	{
		return MainTitleCandidateDetector.IsSubTitleCandidate(text, cfg);
	}

	public static bool IsLevel1Title(string text, FormatConfig cfg)
	{
		return HeadingLevelDetector.IsLevel1Title(text, cfg);
	}

	public static bool IsLevel2Title(string text, FormatConfig cfg)
	{
		return HeadingLevelDetector.IsLevel2Title(text, cfg);
	}

	public static bool IsLevel3Title(string text, FormatConfig cfg)
	{
		return HeadingLevelDetector.IsLevel3Title(text, cfg);
	}

	public static bool IsConfiguredHeading(string text, FormatConfig cfg)
	{
		return HeadingLevelDetector.IsConfiguredHeading(text, cfg);
	}

	public static bool IsSalutation(string text, FormatConfig cfg = null)
	{
		return SalutationDetector.IsSalutation(text, cfg);
	}
}
