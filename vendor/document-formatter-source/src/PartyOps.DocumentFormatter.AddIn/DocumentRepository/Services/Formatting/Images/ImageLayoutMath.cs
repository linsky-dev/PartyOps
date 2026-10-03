using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Images;

namespace DocumentRepository.Services.Formatting.Images;

public static class ImageLayoutMath
{
	private const float PointsPerCentimeter = 28.346457f;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ImageSizeResult Calculate(float currentWidth, float currentHeight, float availableWidth, float availableHeight, ImageFormatOptions options)
	{
		if (options != null)
		{
			if (!(currentWidth > 0f) || currentHeight <= 0f)
			{
				throw new ArgumentOutOfRangeException("currentWidth", "图片尺寸必须大于零。");
			}
			if (options.SizeMode == "Preserve")
			{
				return new ImageSizeResult
				{
					WidthPoints = currentWidth,
					HeightPoints = currentHeight
				};
			}
			if (options.SizeMode == "OriginalScalePercent")
			{
				return new ImageSizeResult
				{
					WidthPoints = currentWidth,
					HeightPoints = currentHeight,
					ShouldResize = true,
					UseOriginalScalePercent = true
				};
			}
			float num = PositiveOrInfinity(availableWidth);
			float num2 = PositiveOrInfinity(availableHeight);
			float num3 = currentWidth;
			float num4 = currentHeight;
			if (!(options.SizeMode == "FixedWidth"))
			{
				if (options.SizeMode == "FixedSize")
				{
					float num5 = Math.Min(CentimetersToPoints(options.WidthCm), num);
					float num6 = Math.Min(CentimetersToPoints(options.HeightCm), num2);
					if (!options.KeepAspectRatio)
					{
						num3 = num5;
						num4 = num6;
					}
					else
					{
						float num7 = Math.Min(num5 / currentWidth, num6 / currentHeight);
						num3 = currentWidth * num7;
						num4 = currentHeight * num7;
					}
				}
				else
				{
					float num8 = num;
					float num9 = num2;
					if (options.SizeMode == "LimitMax")
					{
						num8 = Math.Min(num8, PositiveOrInfinity(CentimetersToPoints(options.MaxWidthCm)));
						num9 = Math.Min(num9, PositiveOrInfinity(CentimetersToPoints(options.MaxHeightCm)));
					}
					float val = Math.Min(num8 / currentWidth, num9 / currentHeight);
					val = Math.Min(1f, val);
					num3 = currentWidth * val;
					num4 = currentHeight * val;
				}
			}
			else
			{
				num3 = Math.Min(CentimetersToPoints(options.WidthCm), num);
				num4 = (options.KeepAspectRatio ? (currentHeight * num3 / currentWidth) : currentHeight);
				if (options.KeepAspectRatio && num4 > num2)
				{
					float num10 = num2 / num4;
					num3 *= num10;
					num4 = num2;
				}
			}
			return new ImageSizeResult
			{
				WidthPoints = Math.Max(1f, num3),
				HeightPoints = Math.Max(1f, num4),
				ShouldResize = (Math.Abs(num3 - currentWidth) > 0.05f || Math.Abs(num4 - currentHeight) > 0.05f)
			};
		}
		throw new ArgumentNullException("options");
	}

	public static float CentimetersToPoints(float centimeters)
	{
		if (!(centimeters <= 0f))
		{
			return centimeters * 28.346457f;
		}
		return 0f;
	}

	public static float CalculateRotatedFitScale(float width, float height, float rotationDegrees, float availableWidth, float availableHeight)
	{
		if (!(width <= 0f) && height > 0f)
		{
			double num = (double)rotationDegrees * Math.PI / 180.0;
			double num2 = Math.Abs(Math.Cos(num));
			double num3 = Math.Abs(Math.Sin(num));
			double num4 = (double)width * num2 + (double)height * num3;
			double num5 = (double)width * num3 + (double)height * num2;
			double val = ((availableWidth > 0f) ? ((double)availableWidth / num4) : 1.0);
			double val2 = ((availableHeight > 0f) ? ((double)availableHeight / num5) : 1.0);
			return (float)Math.Min(1.0, Math.Min(val, val2));
		}
		return 1f;
	}

	private static float PositiveOrInfinity(float value)
	{
		if (!(value > 0f))
		{
			return float.PositiveInfinity;
		}
		return value;
	}
}
