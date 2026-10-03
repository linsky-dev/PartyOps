using System.Runtime.CompilerServices;
using DocumentRepository.Models.Images;

namespace DocumentRepository.Services.Detection.Images;

public static class ImageObjectClassifier
{
	public const int MainTextStoryType = 1;

	public const int InlinePictureType = 3;

	public const int InlineLinkedPictureType = 4;

	public const int FloatingLinkedPictureType = 11;

	public const int FloatingPictureType = 13;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ImageObjectClassification ClassifyInline(int typeCode, int storyTypeCode)
	{
		ImageObjectKind kind;
		if (typeCode != 3)
		{
			if (typeCode != 4)
			{
				return Skip(ImageObjectKind.UnsupportedInlineObject, "不是可排版的嵌入图片");
			}
			kind = ImageObjectKind.InlineLinkedPicture;
		}
		else
		{
			kind = ImageObjectKind.InlinePicture;
		}
		if (storyTypeCode != 1)
		{
			return Skip(kind, "图片不在正文故事范围内");
		}
		return Allow(kind);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ImageObjectClassification ClassifyFloating(int typeCode, int storyTypeCode)
	{
		ImageObjectKind kind;
		if (typeCode != 13)
		{
			if (typeCode != 11)
			{
				return Skip(ImageObjectKind.UnsupportedFloatingObject, "不是可排版的浮动图片");
			}
			kind = ImageObjectKind.FloatingLinkedPicture;
		}
		else
		{
			kind = ImageObjectKind.FloatingPicture;
		}
		if (storyTypeCode != 1)
		{
			return Skip(kind, "图片不在正文故事范围内");
		}
		return Allow(kind);
	}

	private static ImageObjectClassification Allow(ImageObjectKind kind)
	{
		return new ImageObjectClassification
		{
			Kind = kind,
			IsEligible = true,
			SkipReason = string.Empty
		};
	}

	private static ImageObjectClassification Skip(ImageObjectKind kind, string reason)
	{
		return new ImageObjectClassification
		{
			Kind = kind,
			IsEligible = false,
			SkipReason = reason
		};
	}
}
