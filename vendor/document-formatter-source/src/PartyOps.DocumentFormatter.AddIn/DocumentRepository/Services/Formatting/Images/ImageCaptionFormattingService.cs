using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Services.Hosting;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Formatting.Images;

internal static class ImageCaptionFormattingService
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool FormatFollowingCaption(Document document, int imageAnchorStart, int scopeStart, int scopeEnd, ImageFormatOptions options)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		if (options == null)
		{
			throw new ArgumentNullException("options");
		}
		if (options.FormatExistingCaptions)
		{
			HostThreadRuntime.AssertAccess("ImageCaptionFormattingService.FormatFollowingCaption");
			Microsoft.Office.Interop.Word.Range value = null;
			Paragraphs value2 = null;
			Paragraph value3 = null;
			Paragraph value4 = null;
			Microsoft.Office.Interop.Word.Range value5 = null;
			Font value6 = null;
			ParagraphFormat value7 = null;
			try
			{
				if (imageAnchorStart < scopeStart || imageAnchorStart > scopeEnd)
				{
					return false;
				}
				object Start = imageAnchorStart;
				object End = imageAnchorStart;
				value = document.Range(ref Start, ref End);
				value2 = value.Paragraphs;
				if (value2 != null && value2.Count != 0)
				{
					value3 = value2[1];
					object Count = 1;
					value4 = value3.Next(ref Count);
					if (value4 != null)
					{
						value5 = value4.Range;
						if (value5.Start >= scopeStart && value5.End <= scopeEnd)
						{
							if (ImageCaptionPolicy.IsExistingCaption(value5.Text))
							{
								value6 = value5.Font;
								value7 = value5.ParagraphFormat;
								value6.Name = options.CaptionFontName;
								value6.NameFarEast = options.CaptionFontName;
								value6.Size = FontSizeHelper.ToPoints(options.CaptionFontSize);
								value6.Bold = (options.CaptionBold ? 1 : 0);
								value7.Alignment = WdParagraphAlignment.wdAlignParagraphCenter;
								value7.FirstLineIndent = 0f;
								value7.LeftIndent = 0f;
								value7.RightIndent = 0f;
								value7.SpaceBefore = options.CaptionSpaceBefore;
								value7.SpaceAfter = options.CaptionSpaceAfter;
								return true;
							}
							return false;
						}
						return false;
					}
					return false;
				}
				return false;
			}
			finally
			{
				ComObjectRelease.Release(ref value7, "ImageCaptionFormattingService.format");
				ComObjectRelease.Release(ref value6, "ImageCaptionFormattingService.font");
				ComObjectRelease.Release(ref value5, "ImageCaptionFormattingService.captionRange");
				ComObjectRelease.Release(ref value4, "ImageCaptionFormattingService.next");
				ComObjectRelease.Release(ref value3, "ImageCaptionFormattingService.paragraph");
				ComObjectRelease.Release(ref value2, "ImageCaptionFormattingService.paragraphs");
				ComObjectRelease.Release(ref value, "ImageCaptionFormattingService.anchor");
			}
		}
		return false;
	}
}
