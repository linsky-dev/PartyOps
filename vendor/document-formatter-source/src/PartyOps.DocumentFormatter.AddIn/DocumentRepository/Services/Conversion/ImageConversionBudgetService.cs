using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Conversion;

namespace DocumentRepository.Services.Conversion;

public static class ImageConversionBudgetService
{
	public const int MaxImageDimension = 65000;

	public const long MaxLongImagePixels = 260000000L;

	public const long Process32BitBudgetBytes = 440401920L;

	public const long Process64BitBudgetBytes = 1468006400L;

	private const double AdaptiveSafetyFactor = 0.9;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ImageConversionBudget Evaluate(IEnumerable<ImagePageDimensions> pageDimensions, bool longImage, bool is64BitProcess)
	{
		if (pageDimensions == null)
		{
			throw new ArgumentNullException("pageDimensions");
		}
		List<ImagePageDimensions> list = pageDimensions.Select(CloneAndValidate).ToList();
		if (list.Count == 0)
		{
			throw new InvalidOperationException("图片转换预算缺少页面尺寸。");
		}
		bool flag = longImage && list.Count > 1;
		int num = list.Max((ImagePageDimensions p) => p.Width);
		long num2 = (flag ? ((IEnumerable<ImagePageDimensions>)list).Sum((Func<ImagePageDimensions, long>)((ImagePageDimensions p) => p.Height)) : ((IEnumerable<ImagePageDimensions>)list).Max((Func<ImagePageDimensions, long>)((ImagePageDimensions p) => p.Height)));
		checked
		{
			long num3 = num * num2;
			long num4 = list.Max((ImagePageDimensions p) => unchecked((long)p.Width) * unchecked((long)p.Height));
			long num5 = (is64BitProcess ? 1468006400 : 440401920);
			long num6 = num4 * 4;
			long num7 = (flag ? (num3 * 4) : 0);
			long num8 = Math.Max(33554432L, flag ? unchecked(num7 / 3) : num4);
			long num9 = (flag ? (num7 + num6 + num8) : (num6 + num8));
			ImageConversionBudget result = new ImageConversionBudget
			{
				Allowed = true,
				IsLongImage = longImage,
				Is64BitProcess = is64BitProcess,
				PageCount = list.Count,
				OutputWidth = num,
				OutputHeight = num2,
				OutputPixels = num3,
				EstimatedPeakBytes = num9,
				ProcessBudgetBytes = num5,
				Pages = list
			};
			if (num <= 65000 && num2 <= 65000)
			{
				if (!longImage || num3 <= 260000000)
				{
					if (num9 > num5)
					{
						return Reject(result, "预计峰值内存约 " + ToMegabytes(num9) + " MB，超过当前 " + (is64BitProcess ? "64 位" : "32 位") + " 进程预算 " + ToMegabytes(num5) + " MB。");
					}
					return result;
				}
				return Reject(result, "长图总像素约为 " + num3 + "，超过可靠转换上限。");
			}
			return Reject(result, "图片尺寸将达到 " + num + " x " + num2 + " 像素，超过图像组件允许的尺寸。");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ImageConversionBudget FitLongImage(IEnumerable<ImagePageDimensions> pageDimensions, bool is64BitProcess, out double scale)
	{
		if (pageDimensions == null)
		{
			throw new ArgumentNullException("pageDimensions");
		}
		List<ImagePageDimensions> list = pageDimensions.Select(CloneAndValidate).ToList();
		if (list.Count != 0)
		{
			ImageConversionBudget imageConversionBudget = Evaluate(list, longImage: true, is64BitProcess);
			if (!imageConversionBudget.Allowed && list.Count != 1)
			{
				double num = 0.01;
				double num2 = 1.0;
				double num3 = 0.0;
				ImageConversionBudget imageConversionBudget2 = null;
				for (int i = 0; i < 40; i++)
				{
					double num4 = (num + num2) / 2.0;
					ImageConversionBudget imageConversionBudget3 = Evaluate(ScalePages(list, num4), longImage: true, is64BitProcess);
					if (!imageConversionBudget3.Allowed)
					{
						num2 = num4;
						continue;
					}
					num3 = num4;
					imageConversionBudget2 = imageConversionBudget3;
					num = num4;
				}
				if (imageConversionBudget2 == null)
				{
					scale = 1.0;
					return imageConversionBudget;
				}
				scale = Math.Max(0.01, num3 * 0.9);
				return Evaluate(ScalePages(list, scale), longImage: true, is64BitProcess);
			}
			scale = 1.0;
			return imageConversionBudget;
		}
		throw new InvalidOperationException("图片转换预算缺少页面尺寸。");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void EnsureAllowed(ImageConversionBudget budget)
	{
		if (budget != null)
		{
			if (budget.Allowed)
			{
				return;
			}
			throw ConvertOperationException.Create(ConvertFailureReasonCode.ImageResourceBudgetExceeded, ConvertFailureStage.Execute);
		}
		throw new ArgumentNullException("budget");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ImagePageDimensions CloneAndValidate(ImagePageDimensions value)
	{
		if (value == null || value.Width <= 0 || value.Height <= 0)
		{
			throw new InvalidOperationException("图片转换预算包含无效页面尺寸。");
		}
		return new ImagePageDimensions
		{
			Width = value.Width,
			Height = value.Height
		};
	}

	private static List<ImagePageDimensions> ScalePages(IEnumerable<ImagePageDimensions> pages, double scale)
	{
		return pages.Select((ImagePageDimensions p) => new ImagePageDimensions
		{
			Width = Math.Max(1, (int)Math.Floor((double)p.Width * scale)),
			Height = Math.Max(1, (int)Math.Floor((double)p.Height * scale))
		}).ToList();
	}

	private static ImageConversionBudget Reject(ImageConversionBudget result, string reason)
	{
		result.Allowed = false;
		result.RejectionReason = reason;
		return result;
	}

	private static long ToMegabytes(long bytes)
	{
		return Math.Max(1L, bytes / 1048576);
	}
}
