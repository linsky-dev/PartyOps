using System;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Services.Configuration;

public static class FormatOptionMigrationPolicy
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Normalize(FormatConfig cfg)
	{
		if (cfg != null)
		{
			if (cfg.DocumentGridOptions == null)
			{
				cfg.DocumentGridOptions = new DocumentGridOptions();
			}
			if (cfg.DocumentGridOptions.OptionsVersion < 1)
			{
				cfg.DocumentGridOptions.LinesPerPage = 22;
				cfg.DocumentGridOptions.CharsPerLine = 28;
				cfg.DocumentGridOptions.OptionsVersion = 1;
			}
			if (cfg.DocumentGridOptions.LinesPerPage < 1 || cfg.DocumentGridOptions.LinesPerPage > 50)
			{
				cfg.DocumentGridOptions.LinesPerPage = 22;
			}
			if (cfg.DocumentGridOptions.CharsPerLine < 1 || cfg.DocumentGridOptions.CharsPerLine > 50)
			{
				cfg.DocumentGridOptions.CharsPerLine = 28;
			}
			if (cfg.SignatureOptions == null)
			{
				cfg.SignatureOptions = new SignatureFormatOptions();
			}
			if (cfg.SignatureOptions.OptionsVersion < 1)
			{
				cfg.SignatureOptions.WithSeal = cfg.EnableSignatureWithSeal;
				cfg.SignatureOptions.BlankLinesBefore = ((!cfg.EnableSignatureWithSeal) ? 1 : 2);
				cfg.SignatureOptions.OptionsVersion = 1;
			}
			if (cfg.SignatureOptions.BlankLinesBefore < 0 || cfg.SignatureOptions.BlankLinesBefore > 10)
			{
				cfg.SignatureOptions.BlankLinesBefore = ((!cfg.SignatureOptions.WithSeal) ? 1 : 2);
			}
			cfg.EnableSignatureWithSeal = cfg.SignatureOptions.WithSeal;
			if (cfg.ImageOptions == null)
			{
				cfg.ImageOptions = new ImageFormatOptions();
			}
			if (HasLegacyImageOptions(cfg.ImageOptions))
			{
				MigrateLegacyImageOptions(cfg.ImageOptions);
			}
			if (cfg.ImageOptions.OptionsVersion < 1)
			{
				ApplySafeImageDefaults(cfg.ImageOptions);
				cfg.ImageOptions.OptionsVersion = 1;
			}
			if (cfg.ImageOptions.OptionsVersion < 2)
			{
				ApplyAdvancedImageDefaults(cfg.ImageOptions);
				cfg.ImageOptions.OptionsVersion = 2;
			}
			if (!ImageSizeModes.IsValid(cfg.ImageOptions.SizeMode))
			{
				cfg.ImageOptions.SizeMode = "ShrinkToFit";
			}
			if (!ImageWrapModes.IsValid(cfg.ImageOptions.WrapMode))
			{
				cfg.ImageOptions.WrapMode = "Preserve";
			}
			if (!ImageAlignmentModes.IsValid(cfg.ImageOptions.AlignmentMode))
			{
				cfg.ImageOptions.AlignmentMode = "Preserve";
			}
			if (!ImageBorderModes.IsValid(cfg.ImageOptions.BorderMode))
			{
				cfg.ImageOptions.BorderMode = "Preserve";
			}
			if (!ImageParagraphFilterModes.IsValid(cfg.ImageOptions.ParagraphFilterMode))
			{
				cfg.ImageOptions.ParagraphFilterMode = "All";
			}
			if (!(cfg.ImageOptions.WidthCm > 0f) || cfg.ImageOptions.WidthCm > 100f)
			{
				cfg.ImageOptions.WidthCm = 14.5f;
			}
			if (cfg.ImageOptions.HeightCm <= 0f || !(cfg.ImageOptions.HeightCm <= 100f))
			{
				cfg.ImageOptions.HeightCm = 20f;
			}
			if (!(cfg.ImageOptions.MaxWidthCm > 0f) || !(cfg.ImageOptions.MaxWidthCm <= 100f))
			{
				cfg.ImageOptions.MaxWidthCm = 14.5f;
			}
			if (!(cfg.ImageOptions.MaxHeightCm > 0f) || cfg.ImageOptions.MaxHeightCm > 100f)
			{
				cfg.ImageOptions.MaxHeightCm = 20f;
			}
			if (!(cfg.ImageOptions.ScalePercent >= 10f) || cfg.ImageOptions.ScalePercent > 300f)
			{
				cfg.ImageOptions.ScalePercent = 100f;
			}
			if (cfg.ImageOptions.MinimumWidthCm < 0f || !(cfg.ImageOptions.MinimumWidthCm <= 100f))
			{
				cfg.ImageOptions.MinimumWidthCm = 0f;
			}
			if (!(cfg.ImageOptions.MinimumHeightCm >= 0f) || cfg.ImageOptions.MinimumHeightCm > 100f)
			{
				cfg.ImageOptions.MinimumHeightCm = 0f;
			}
			if (!(cfg.ImageOptions.DistanceTopCm >= 0f) || cfg.ImageOptions.DistanceTopCm > 20f)
			{
				cfg.ImageOptions.DistanceTopCm = 0f;
			}
			if (cfg.ImageOptions.DistanceBottomCm < 0f || cfg.ImageOptions.DistanceBottomCm > 20f)
			{
				cfg.ImageOptions.DistanceBottomCm = 0f;
			}
			if (!(cfg.ImageOptions.DistanceLeftCm >= 0f) || cfg.ImageOptions.DistanceLeftCm > 20f)
			{
				cfg.ImageOptions.DistanceLeftCm = 0.32f;
			}
			if (!(cfg.ImageOptions.DistanceRightCm >= 0f) || cfg.ImageOptions.DistanceRightCm > 20f)
			{
				cfg.ImageOptions.DistanceRightCm = 0.32f;
			}
			if (!(cfg.ImageOptions.RotationDegrees >= -180f) || !(cfg.ImageOptions.RotationDegrees <= 180f))
			{
				cfg.ImageOptions.RotationDegrees = 0f;
			}
			if (string.IsNullOrWhiteSpace(cfg.ImageOptions.CaptionFontName))
			{
				cfg.ImageOptions.CaptionFontName = "宋体";
			}
			if (string.IsNullOrWhiteSpace(cfg.ImageOptions.CaptionFontSize))
			{
				cfg.ImageOptions.CaptionFontSize = "小四";
			}
			if (cfg.ImageOptions.CaptionSpaceBefore < 0 || cfg.ImageOptions.CaptionSpaceBefore > 100)
			{
				cfg.ImageOptions.CaptionSpaceBefore = 0;
			}
			if (cfg.ImageOptions.CaptionSpaceAfter < 0 || cfg.ImageOptions.CaptionSpaceAfter > 100)
			{
				cfg.ImageOptions.CaptionSpaceAfter = 6;
			}
			return;
		}
		throw new ArgumentNullException("cfg");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool HasLegacyImageOptions(ImageFormatOptions options)
	{
		if (!options.LegacyFitPageWidth.HasValue && !options.LegacyCenterAlign.HasValue && !options.LegacyConvertFloatingToInline.HasValue && !options.LegacyUseBorder.HasValue && string.IsNullOrWhiteSpace(options.LegacyWrapType) && !string.Equals(options.SizeMode, "FitPageWidth", StringComparison.OrdinalIgnoreCase))
		{
			return string.Equals(options.SizeMode, "ScalePercent", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void MigrateLegacyImageOptions(ImageFormatOptions options)
	{
		options.SizeMode = MapLegacySizeMode(options.SizeMode, options.LegacyFitPageWidth);
		options.WrapMode = MapLegacyWrapMode(options.LegacyWrapType, options.LegacyConvertFloatingToInline, options.WrapMode);
		if (options.LegacyCenterAlign.HasValue)
		{
			options.AlignmentMode = (options.LegacyCenterAlign.Value ? "Center" : "Preserve");
		}
		if (options.LegacyUseBorder.HasValue)
		{
			options.BorderMode = (options.LegacyUseBorder.Value ? "Add" : "Preserve");
		}
		options.OptionsVersion = 2;
		options.LegacyFitPageWidth = null;
		options.LegacyCenterAlign = null;
		options.LegacyConvertFloatingToInline = null;
		options.LegacyUseBorder = null;
		options.LegacyWrapType = null;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string MapLegacySizeMode(string value, bool? fitPageWidth)
	{
		if (!ImageSizeModes.IsValid(value))
		{
			if (!string.Equals(value, "FitPageWidth", StringComparison.OrdinalIgnoreCase))
			{
				if (!string.Equals(value, "ScalePercent", StringComparison.OrdinalIgnoreCase))
				{
					if (!(fitPageWidth ?? true))
					{
						return "LimitMax";
					}
					return "ShrinkToFit";
				}
				return "OriginalScalePercent";
			}
			return "ShrinkToFit";
		}
		return value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string MapLegacyWrapMode(string legacyWrapType, bool? convertToInline, string current)
	{
		if (convertToInline != true)
		{
			if (string.Equals(legacyWrapType, "Keep", StringComparison.OrdinalIgnoreCase))
			{
				return "Preserve";
			}
			if (ImageWrapModes.IsValid(legacyWrapType))
			{
				return legacyWrapType;
			}
			if (!ImageWrapModes.IsValid(current))
			{
				return "Preserve";
			}
			return current;
		}
		return "Inline";
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ApplySafeImageDefaults(ImageFormatOptions options)
	{
		options.SizeMode = "ShrinkToFit";
		options.WrapMode = "Preserve";
		options.AlignmentMode = "Preserve";
		options.BorderMode = "Preserve";
		options.KeepAspectRatio = true;
		options.MainStoryOnly = true;
		options.IncludeTableCellImages = true;
		options.WidthCm = 14.5f;
		options.HeightCm = 20f;
		options.MaxWidthCm = 14.5f;
		options.MaxHeightCm = 20f;
		options.ScalePercent = 100f;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ApplyAdvancedImageDefaults(ImageFormatOptions options)
	{
		options.IncludeLinkedPictures = true;
		options.ParagraphFilterMode = "All";
		options.MinimumWidthCm = 0f;
		options.MinimumHeightCm = 0f;
		options.ApplyWrapDistances = false;
		options.DistanceTopCm = 0f;
		options.DistanceBottomCm = 0f;
		options.DistanceLeftCm = 0.32f;
		options.DistanceRightCm = 0.32f;
		options.ApplyRotation = false;
		options.RotationDegrees = 0f;
		options.FormatExistingCaptions = false;
		options.CaptionFontName = "宋体";
		options.CaptionFontSize = "小四";
		options.CaptionBold = false;
		options.CaptionSpaceBefore = 0;
		options.CaptionSpaceAfter = 6;
	}
}
