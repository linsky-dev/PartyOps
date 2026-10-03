using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Images;

namespace DocumentRepository.Services.Formatting.Images;

public static class ImageFormattingPlanBuilder
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ImageFormattingPlan BuildProtectionOnly(ImageAnalysisSnapshot snapshot, int scopeStart = 0, int scopeEnd = int.MaxValue)
	{
		if (snapshot == null)
		{
			throw new ArgumentNullException("snapshot");
		}
		ImageFormattingPlan imageFormattingPlan = new ImageFormattingPlan
		{
			SourceSnapshot = snapshot,
			Options = new ImageFormatOptions(),
			ScopeStart = scopeStart,
			ScopeEnd = scopeEnd
		};
		foreach (ImageObjectSnapshot @object in snapshot.Objects)
		{
			if (@object != null && @object.IsEligible)
			{
				imageFormattingPlan.ProtectedEligibleObjects.Add(@object);
			}
		}
		return imageFormattingPlan;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ImageFormattingPlan BuildWithProtectionBaseline(ImageAnalysisSnapshot currentSnapshot, ImageAnalysisSnapshot protectionBaseline, ImageFormatOptions sourceOptions, int scopeStart = 0, int scopeEnd = int.MaxValue)
	{
		if (protectionBaseline == null)
		{
			throw new ArgumentNullException("protectionBaseline");
		}
		ImageFormattingPlan imageFormattingPlan = Build(currentSnapshot, sourceOptions, scopeStart, scopeEnd);
		ImageFormattingPlan imageFormattingPlan2 = Build(protectionBaseline, sourceOptions, scopeStart, scopeEnd);
		imageFormattingPlan.SourceSnapshot = protectionBaseline;
		imageFormattingPlan.ProtectedEligibleObjects.Clear();
		imageFormattingPlan.ProtectedEligibleObjects.AddRange(imageFormattingPlan2.ProtectedEligibleObjects);
		return imageFormattingPlan;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ImageFormattingPlan Build(ImageAnalysisSnapshot snapshot, ImageFormatOptions sourceOptions, int scopeStart = 0, int scopeEnd = int.MaxValue)
	{
		if (snapshot == null)
		{
			throw new ArgumentNullException("snapshot");
		}
		if (sourceOptions == null)
		{
			throw new ArgumentNullException("sourceOptions");
		}
		ImageFormatOptions imageFormatOptions = CloneOptions(sourceOptions);
		ImageFormattingPlan imageFormattingPlan = new ImageFormattingPlan
		{
			SourceSnapshot = snapshot,
			Options = imageFormatOptions,
			ScopeStart = scopeStart,
			ScopeEnd = scopeEnd
		};
		foreach (ImageObjectSnapshot @object in snapshot.Objects)
		{
			if (@object == null || !@object.IsEligible)
			{
				continue;
			}
			if (!ShouldInclude(@object, imageFormatOptions))
			{
				imageFormattingPlan.ProtectedEligibleObjects.Add(@object);
				continue;
			}
			ImageFormattingTarget imageFormattingTarget = new ImageFormattingTarget
			{
				SourceOrdinal = @object.Ordinal,
				SourceKind = @object.Kind,
				TypeCode = @object.TypeCode,
				AnchorStart = @object.AnchorStart,
				AnchorEnd = @object.AnchorEnd,
				Name = @object.Name,
				SourceWidthPoints = @object.WidthPoints,
				SourceHeightPoints = @object.HeightPoints,
				IsInTable = @object.IsInTable,
				IsStandaloneParagraph = @object.IsStandaloneParagraph
			};
			imageFormattingPlan.Targets.Add(imageFormattingTarget);
			if (imageFormattingTarget.SourceIsInline && RequiresFloating(imageFormatOptions.WrapMode))
			{
				imageFormattingPlan.InlineToFloatingCount++;
			}
			else if (!imageFormattingTarget.SourceIsInline && imageFormatOptions.WrapMode == "Inline")
			{
				imageFormattingPlan.FloatingToInlineCount++;
			}
		}
		imageFormattingPlan.Targets.Sort(delegate(ImageFormattingTarget left, ImageFormattingTarget right)
		{
			int num = right.AnchorStart.CompareTo(left.AnchorStart);
			return (num == 0) ? right.SourceOrdinal.CompareTo(left.SourceOrdinal) : num;
		});
		return imageFormattingPlan;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool ShouldInclude(ImageObjectSnapshot item, ImageFormatOptions options)
	{
		if (item.IsInTable && !options.IncludeTableCellImages)
		{
			return false;
		}
		if ((item.Kind == ImageObjectKind.InlineLinkedPicture || item.Kind == ImageObjectKind.FloatingLinkedPicture) && !options.IncludeLinkedPictures)
		{
			return false;
		}
		if (options.ParagraphFilterMode == "StandaloneOnly" && !item.IsStandaloneParagraph)
		{
			return false;
		}
		float num = ImageLayoutMath.CentimetersToPoints(options.MinimumWidthCm);
		float num2 = ImageLayoutMath.CentimetersToPoints(options.MinimumHeightCm);
		if (!(num > 0f) || item.WidthPoints >= num)
		{
			if (num2 > 0f && !(item.HeightPoints >= num2))
			{
				return false;
			}
			return true;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool RequiresFloating(string wrapMode)
	{
		switch (wrapMode)
		{
		default:
			return wrapMode == "Front";
		case "Square":
		case "Tight":
		case "Through":
		case "TopBottom":
		case "Behind":
			return true;
		}
	}

	private static ImageFormatOptions CloneOptions(ImageFormatOptions source)
	{
		return new ImageFormatOptions
		{
			OptionsVersion = source.OptionsVersion,
			SizeMode = source.SizeMode,
			WrapMode = source.WrapMode,
			AlignmentMode = source.AlignmentMode,
			BorderMode = source.BorderMode,
			KeepAspectRatio = source.KeepAspectRatio,
			MainStoryOnly = source.MainStoryOnly,
			IncludeTableCellImages = source.IncludeTableCellImages,
			IncludeLinkedPictures = source.IncludeLinkedPictures,
			ParagraphFilterMode = source.ParagraphFilterMode,
			MinimumWidthCm = source.MinimumWidthCm,
			MinimumHeightCm = source.MinimumHeightCm,
			WidthCm = source.WidthCm,
			HeightCm = source.HeightCm,
			MaxWidthCm = source.MaxWidthCm,
			MaxHeightCm = source.MaxHeightCm,
			ScalePercent = source.ScalePercent,
			ApplyWrapDistances = source.ApplyWrapDistances,
			DistanceTopCm = source.DistanceTopCm,
			DistanceBottomCm = source.DistanceBottomCm,
			DistanceLeftCm = source.DistanceLeftCm,
			DistanceRightCm = source.DistanceRightCm,
			ApplyRotation = source.ApplyRotation,
			RotationDegrees = source.RotationDegrees,
			FormatExistingCaptions = source.FormatExistingCaptions,
			CaptionFontName = source.CaptionFontName,
			CaptionFontSize = source.CaptionFontSize,
			CaptionBold = source.CaptionBold,
			CaptionSpaceBefore = source.CaptionSpaceBefore,
			CaptionSpaceAfter = source.CaptionSpaceAfter
		};
	}
}
