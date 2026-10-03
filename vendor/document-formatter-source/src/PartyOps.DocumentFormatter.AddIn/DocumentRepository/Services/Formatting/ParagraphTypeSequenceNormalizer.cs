using DocumentRepository.Models;

namespace DocumentRepository.Services.Formatting;

internal static class ParagraphTypeSequenceNormalizer
{
	public static void NormalizeOpeningTitleRegion(ElementType[] types)
	{
		if (types == null || types.Length == 0)
		{
			return;
		}
		bool flag = true;
		bool flag2 = false;
		bool flag3 = false;
		for (int i = 0; i < types.Length; i++)
		{
			ElementType elementType = types[i];
			if (elementType == ElementType.Unknown || elementType == ElementType.DocumentNumber)
			{
				continue;
			}
			if (flag)
			{
				switch (elementType)
				{
				case ElementType.SubTitle:
					flag3 = true;
					flag2 = true;
					continue;
				case ElementType.MainTitle:
					if (flag3)
					{
						types[i] = ElementType.Body;
					}
					else
					{
						flag2 = true;
					}
					continue;
				}
				if (flag2 || elementType == ElementType.Salutation || IsBodyHeadingOrAttachment(elementType))
				{
					flag = false;
				}
			}
			if (!flag && (elementType == ElementType.MainTitle || elementType == ElementType.SubTitle))
			{
				types[i] = ElementType.Body;
			}
		}
	}

	public static bool ShouldKeepTitleAfterSpacing(ElementType current, ElementType[] allTypes, int index)
	{
		if (allTypes != null)
		{
			for (int i = index + 1; i < allTypes.Length; i++)
			{
				ElementType elementType = allTypes[i];
				if (elementType == ElementType.Unknown)
				{
					continue;
				}
				switch (current)
				{
				case ElementType.SubTitle:
					return elementType != ElementType.SubTitle;
				default:
					return true;
				case ElementType.AttachmentTitle:
					if (elementType == ElementType.AttachmentTitle)
					{
						return false;
					}
					return elementType != ElementType.AttachmentSubTitle;
				case ElementType.AttachmentSubTitle:
					return elementType != ElementType.AttachmentSubTitle;
				case ElementType.MainTitle:
					if (elementType != ElementType.MainTitle)
					{
						return elementType != ElementType.SubTitle;
					}
					return false;
				}
			}
			return true;
		}
		return true;
	}

	public static bool IsTitleSpacingType(ElementType type)
	{
		if (type != ElementType.MainTitle && type != ElementType.SubTitle && type != ElementType.AttachmentTitle)
		{
			return type == ElementType.AttachmentSubTitle;
		}
		return true;
	}

	private static bool IsBodyHeadingOrAttachment(ElementType type)
	{
		if (type != ElementType.Level1Title && type != ElementType.Level2Title && type != ElementType.Level3Title && type != ElementType.Body && type != ElementType.AttachmentMarker && type != ElementType.AttachmentTitle)
		{
			return type == ElementType.AttachmentSubTitle;
		}
		return true;
	}
}
