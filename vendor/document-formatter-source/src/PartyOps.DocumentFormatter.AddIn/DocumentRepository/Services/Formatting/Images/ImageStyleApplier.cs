using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Images;
using DocumentRepository.Services.Interop;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Formatting.Images;

internal static class ImageStyleApplier
{
	private const float ShapeCenter = -999995f;

	private const float ShapeRight = -999996f;

	private const float ShapeLeft = -999998f;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ApplyInline(InlineShape image, ImageFormatOptions options)
	{
		if (image != null)
		{
			if (options == null)
			{
				throw new ArgumentNullException("options");
			}
			HostThreadRuntime.AssertAccess("ImageStyleApplier.ApplyInline");
			Microsoft.Office.Interop.Word.Range value = null;
			try
			{
				value = image.Range;
				ImageAvailableArea available = ImageLayoutResolver.Resolve(value, options);
				ApplyInlineSize(image, available, options);
				ApplyInlineBorder(image, options.BorderMode);
				ApplyInlineAlignment(value, options.AlignmentMode);
				return;
			}
			finally
			{
				ComObjectRelease.Release(ref value, "ImageStyleApplier.inlineAnchor");
			}
		}
		throw new ArgumentNullException("image");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ApplyFloating(Shape image, ImageFormatOptions options)
	{
		if (image == null)
		{
			throw new ArgumentNullException("image");
		}
		if (options == null)
		{
			throw new ArgumentNullException("options");
		}
		HostThreadRuntime.AssertAccess("ImageStyleApplier.ApplyFloating");
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			value = image.Anchor;
			ImageAvailableArea available = ImageLayoutResolver.Resolve(value, options);
			ApplyFloatingSize(image, available, options);
			ApplyFloatingWrap(image, options.WrapMode);
			ApplyFloatingWrapDistances(image, options);
			ApplyFloatingRotation(image, options, available);
			ApplyFloatingBorder(image, options.BorderMode);
			ApplyFloatingAlignment(image, options.AlignmentMode);
		}
		finally
		{
			ComObjectRelease.Release(ref value, "ImageStyleApplier.floatingAnchor");
		}
	}

	private static void ApplyInlineSize(InlineShape image, ImageAvailableArea available, ImageFormatOptions options)
	{
		if (options.KeepAspectRatio)
		{
			image.LockAspectRatio = OfficeInteropCompatibility.MsoTrue;
		}
		ImageSizeResult imageSizeResult = ImageLayoutMath.Calculate(image.Width, image.Height, available.WidthPoints, available.HeightPoints, options);
		if (imageSizeResult.ShouldResize)
		{
			if (imageSizeResult.UseOriginalScalePercent)
			{
				float scaleHeight = (image.ScaleWidth = Math.Max(10f, Math.Min(300f, options.ScalePercent)));
				image.ScaleHeight = scaleHeight;
				ClampInlineToAvailableArea(image, available, options.KeepAspectRatio);
			}
			else if (!options.KeepAspectRatio)
			{
				image.Width = imageSizeResult.WidthPoints;
				image.Height = imageSizeResult.HeightPoints;
			}
			else
			{
				image.Width = imageSizeResult.WidthPoints;
			}
		}
	}

	private static void ApplyFloatingSize(Shape image, ImageAvailableArea available, ImageFormatOptions options)
	{
		if (options.KeepAspectRatio)
		{
			image.LockAspectRatio = OfficeInteropCompatibility.MsoTrue;
		}
		ImageSizeResult imageSizeResult = ImageLayoutMath.Calculate(image.Width, image.Height, available.WidthPoints, available.HeightPoints, options);
		if (!imageSizeResult.ShouldResize)
		{
			return;
		}
		if (!imageSizeResult.UseOriginalScalePercent)
		{
			if (options.KeepAspectRatio)
			{
				image.Width = imageSizeResult.WidthPoints;
				return;
			}
			image.Width = imageSizeResult.WidthPoints;
			image.Height = imageSizeResult.HeightPoints;
		}
		else
		{
			float factor = Math.Max(10f, Math.Min(300f, options.ScalePercent)) / 100f;
			image.ScaleWidth(factor, OfficeInteropCompatibility.MsoTrue);
			image.ScaleHeight(factor, OfficeInteropCompatibility.MsoTrue);
			ClampFloatingToAvailableArea(image, available, options.KeepAspectRatio);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ClampInlineToAvailableArea(InlineShape image, ImageAvailableArea available, bool keepAspectRatio)
	{
		ImageFormatOptions options = new ImageFormatOptions
		{
			SizeMode = "ShrinkToFit",
			KeepAspectRatio = keepAspectRatio
		};
		ImageSizeResult imageSizeResult = ImageLayoutMath.Calculate(image.Width, image.Height, available.WidthPoints, available.HeightPoints, options);
		if (imageSizeResult.ShouldResize)
		{
			image.Width = imageSizeResult.WidthPoints;
			if (!keepAspectRatio)
			{
				image.Height = imageSizeResult.HeightPoints;
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ClampFloatingToAvailableArea(Shape image, ImageAvailableArea available, bool keepAspectRatio)
	{
		ImageFormatOptions options = new ImageFormatOptions
		{
			SizeMode = "ShrinkToFit",
			KeepAspectRatio = keepAspectRatio
		};
		ImageSizeResult imageSizeResult = ImageLayoutMath.Calculate(image.Width, image.Height, available.WidthPoints, available.HeightPoints, options);
		if (imageSizeResult.ShouldResize)
		{
			image.Width = imageSizeResult.WidthPoints;
			if (!keepAspectRatio)
			{
				image.Height = imageSizeResult.HeightPoints;
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ApplyInlineAlignment(Microsoft.Office.Interop.Word.Range anchor, string mode)
	{
		if (mode == "Preserve")
		{
			return;
		}
		if (!IsImageOnlyParagraph(anchor))
		{
			LogService.Info("图片对齐已跳过：图片与正文位于同一段，避免改变正文段落对齐。位置=" + anchor.Start);
			return;
		}
		ParagraphFormat value = null;
		try
		{
			value = anchor.ParagraphFormat;
			value.Alignment = ToParagraphAlignment(mode);
			value.FirstLineIndent = 0f;
			value.LeftIndent = 0f;
			value.RightIndent = 0f;
		}
		finally
		{
			ComObjectRelease.Release(ref value, "ImageStyleApplier.inlineParagraphFormat");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool IsImageOnlyParagraph(Microsoft.Office.Interop.Word.Range anchor)
	{
		Paragraphs value = null;
		Paragraph value2 = null;
		Microsoft.Office.Interop.Word.Range value3 = null;
		try
		{
			value = anchor.Paragraphs;
			if (value == null || value.Count != 1)
			{
				return false;
			}
			value2 = value[1];
			value3 = value2.Range;
			return (value3.Text ?? string.Empty).Replace("\r", string.Empty).Replace("\a", string.Empty).Replace("\u0001", string.Empty)
				.Replace("￼", string.Empty)
				.Trim()
				.Length == 0;
		}
		finally
		{
			ComObjectRelease.Release(ref value3, "ImageStyleApplier.imageParagraphRange");
			ComObjectRelease.Release(ref value2, "ImageStyleApplier.imageParagraph");
			ComObjectRelease.Release(ref value, "ImageStyleApplier.imageParagraphs");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ApplyFloatingAlignment(Shape image, string mode)
	{
		if (!(mode == "Preserve"))
		{
			image.RelativeHorizontalPosition = WdRelativeHorizontalPosition.wdRelativeHorizontalPositionMargin;
			switch (mode)
			{
			case "Left":
				image.Left = -999998f;
				break;
			case "Right":
				image.Left = -999996f;
				break;
			case "Center":
				image.Left = -999995f;
				break;
			default:
				throw new FormatException("未知图片对齐方式：" + mode);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ApplyFloatingWrap(Shape image, string mode)
	{
		if (mode == "Preserve" || mode == "Inline")
		{
			return;
		}
		WrapFormat value = null;
		try
		{
			value = image.WrapFormat;
			switch (mode)
			{
			case "Behind":
				value.Type = WdWrapType.wdWrapBehind;
				break;
			case "Tight":
				value.Type = WdWrapType.wdWrapTight;
				break;
			case "TopBottom":
				value.Type = WdWrapType.wdWrapTopBottom;
				break;
			case "Square":
				value.Type = WdWrapType.wdWrapSquare;
				break;
			case "Front":
				value.Type = WdWrapType.wdWrapNone;
				break;
			case "Through":
				value.Type = WdWrapType.wdWrapThrough;
				break;
			default:
				throw new FormatException("未知图片环绕方式：" + mode);
			}
		}
		finally
		{
			ComObjectRelease.Release(ref value, "ImageStyleApplier.wrapFormat");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ApplyFloatingWrapDistances(Shape image, ImageFormatOptions options)
	{
		if (!options.ApplyWrapDistances || options.WrapMode == "Inline")
		{
			return;
		}
		WrapFormat value = null;
		try
		{
			value = image.WrapFormat;
			if (value.Type != WdWrapType.wdWrapBehind && value.Type != WdWrapType.wdWrapNone && value.Type != WdWrapType.wdWrapInline)
			{
				value.DistanceTop = ImageLayoutMath.CentimetersToPoints(options.DistanceTopCm);
				value.DistanceBottom = ImageLayoutMath.CentimetersToPoints(options.DistanceBottomCm);
				value.DistanceLeft = ImageLayoutMath.CentimetersToPoints(options.DistanceLeftCm);
				value.DistanceRight = ImageLayoutMath.CentimetersToPoints(options.DistanceRightCm);
			}
		}
		finally
		{
			ComObjectRelease.Release(ref value, "ImageStyleApplier.wrapDistances");
		}
	}

	private static void ApplyFloatingRotation(Shape image, ImageFormatOptions options, ImageAvailableArea available)
	{
		if (!options.ApplyRotation)
		{
			return;
		}
		image.Rotation = options.RotationDegrees;
		float num = ImageLayoutMath.CalculateRotatedFitScale(image.Width, image.Height, image.Rotation, available.WidthPoints, available.HeightPoints);
		if (!(num >= 0.9999f))
		{
			float width = image.Width * num;
			float height = image.Height * num;
			image.Width = width;
			if (!options.KeepAspectRatio)
			{
				image.Height = height;
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ApplyInlineBorder(InlineShape image, string mode)
	{
		if (mode == "Preserve")
		{
			return;
		}
		LineFormat value = null;
		try
		{
			value = image.Line;
			value.Visible = ((mode == "Add") ? OfficeInteropCompatibility.MsoTrue : OfficeInteropCompatibility.MsoFalse);
			if (mode == "Add")
			{
				value.ForeColor.RGB = 0;
				value.Weight = 0.5f;
			}
		}
		catch (Exception ex)
		{
			LogService.Warn("InlineShape.Line 不可用，改用 InlineShape.Borders", ex);
			ApplyInlineBordersFallback(image, mode);
		}
		finally
		{
			ComObjectRelease.Release(ref value, "ImageStyleApplier.inlineLine");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ApplyInlineBordersFallback(InlineShape image, string mode)
	{
		Borders value = null;
		try
		{
			value = image.Borders;
			value.Enable = ((mode == "Add") ? 1 : 0);
			if (mode == "Add")
			{
				value.OutsideLineStyle = WdLineStyle.wdLineStyleSingle;
				value.OutsideLineWidth = WdLineWidth.wdLineWidth050pt;
				value.OutsideColor = WdColor.wdColorBlack;
			}
		}
		finally
		{
			ComObjectRelease.Release(ref value, "ImageStyleApplier.inlineBorders");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ApplyFloatingBorder(Shape image, string mode)
	{
		if (mode == "Preserve")
		{
			return;
		}
		LineFormat value = null;
		try
		{
			value = image.Line;
			value.Visible = ((mode == "Add") ? OfficeInteropCompatibility.MsoTrue : OfficeInteropCompatibility.MsoFalse);
			if (mode == "Add")
			{
				value.ForeColor.RGB = 0;
				value.Weight = 0.5f;
			}
		}
		finally
		{
			ComObjectRelease.Release(ref value, "ImageStyleApplier.floatingLine");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WdParagraphAlignment ToParagraphAlignment(string mode)
	{
		return mode switch
		{
			"Left" => WdParagraphAlignment.wdAlignParagraphLeft, 
			"Right" => WdParagraphAlignment.wdAlignParagraphRight, 
			"Center" => WdParagraphAlignment.wdAlignParagraphCenter, 
			_ => throw new FormatException("未知图片对齐方式：" + mode), 
		};
	}
}
