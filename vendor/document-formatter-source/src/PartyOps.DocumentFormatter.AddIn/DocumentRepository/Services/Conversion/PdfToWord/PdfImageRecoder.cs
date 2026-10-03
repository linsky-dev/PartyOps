using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DocumentRepository.Services.Logging;

namespace DocumentRepository.Services.Conversion.PdfToWord;

public static class PdfImageRecoder
{
	public const int MaxPixels = 25000000;

	public const long MaxDecodedBytes = 268435456L;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool Resolve(PdfImage image, out string reasonCode)
	{
		reasonCode = null;
		if (image != null)
		{
			if ((long)Math.Max(0, image.PixelWidth) * (long)Math.Max(0, image.PixelHeight) <= 25000000)
			{
				if (image.Bytes == null || image.Bytes.LongLength <= 268435456)
				{
					if (image.Bytes != null && image.Bytes.Length >= 4)
					{
						if (IsJpeg(image.Bytes))
						{
							image.Resolution = PdfImageResolutionKind.PassthroughJpeg;
							image.ResolvedBytes = image.Bytes;
							image.ResolvedExtension = ".jpg";
							return true;
						}
						if (IsPng(image.Bytes))
						{
							image.Resolution = PdfImageResolutionKind.PassthroughPng;
							image.ResolvedBytes = image.Bytes;
							image.ResolvedExtension = ".png";
							return true;
						}
					}
					if (image.Source != null)
					{
						byte[] bytes = null;
						bool flag = false;
						try
						{
							flag = image.Source.TryGetPng(out bytes);
						}
						catch (Exception)
						{
							flag = false;
						}
						if (flag && bytes != null && bytes.Length >= 8 && IsPng(bytes))
						{
							image.Resolution = PdfImageResolutionKind.RecodedPng;
							image.ResolvedBytes = bytes;
							image.ResolvedExtension = ".png";
							return true;
						}
					}
					if (image.Bytes != null && image.Bytes.Length != 0 && image.PixelWidth > 0 && image.PixelHeight > 0)
					{
						if (image.BitsPerComponent != 8)
						{
							image.Resolution = PdfImageResolutionKind.Unsupported;
							reasonCode = "bpc-unsupported";
							return false;
						}
						if (image.ComponentsPerPixel == 1 && IsGrayColorSpace(image.ColorSpaceName))
						{
							byte[] png = EncodeGrayToPng(image.Bytes, image.PixelWidth, image.PixelHeight, out reasonCode);
							return FinishRecoded(image, png, ref reasonCode);
						}
						if (image.ComponentsPerPixel != 3 || !IsRgbColorSpace(image.ColorSpaceName))
						{
							image.Resolution = PdfImageResolutionKind.Unsupported;
							reasonCode = "colorspace-unsupported";
							return false;
						}
						byte[] png2 = EncodeRgbToPng(image.Bytes, image.PixelWidth, image.PixelHeight, out reasonCode);
						return FinishRecoded(image, png2, ref reasonCode);
					}
					image.Resolution = PdfImageResolutionKind.Unsupported;
					reasonCode = "no-image-bytes";
					return false;
				}
				image.Resolution = PdfImageResolutionKind.TooLarge;
				reasonCode = "image-bytes-too-large";
				return false;
			}
			image.Resolution = PdfImageResolutionKind.TooLarge;
			reasonCode = "image-too-large";
			return false;
		}
		reasonCode = "image-null";
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ResolveAllOrThrow(IList<PdfPageContent> pages)
	{
		if (pages == null)
		{
			return;
		}
		foreach (PdfPageContent page in pages)
		{
			foreach (PdfImage image in page.Images)
			{
				if (!(image.Width < 3f) && !(image.Height < 3f) && !Resolve(image, out var reasonCode))
				{
					try
					{
						LogService.Info("PdfImageRecoder.Unresolved page=" + (image.PageIndex + 1) + " reason=" + reasonCode + " pixels=" + image.PixelWidth + "x" + image.PixelHeight);
					}
					catch
					{
					}
					throw new LocalPdfToWordException(PdfToWordFailureReason.Unsupported, "PDF 第 " + (image.PageIndex + 1) + " 页包含无法转换的图片（原因码 " + reasonCode + "），已回退宿主导入。", null);
				}
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool FinishRecoded(PdfImage image, byte[] png, ref string reasonCode)
	{
		if (png != null)
		{
			image.Resolution = PdfImageResolutionKind.RecodedPng;
			image.ResolvedBytes = png;
			image.ResolvedExtension = ".png";
			return true;
		}
		image.Resolution = PdfImageResolutionKind.Unsupported;
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static byte[] EncodeGrayToPng(byte[] samples, int width, int height, out string reasonCode)
	{
		reasonCode = null;
		long num = (long)width * (long)height;
		if (samples.LongLength < num)
		{
			reasonCode = "pixel-buffer-mismatch";
			return null;
		}
		byte[] array = new byte[num * 3];
		long num2 = 0L;
		long num3 = 0L;
		while (num2 < num)
		{
			array[num3 + 2] = (array[num3 + 1] = (array[num3] = samples[num2]));
			num2++;
			num3 += 3;
		}
		return Encode24bppToPng(array, width, height, out reasonCode);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static byte[] EncodeRgbToPng(byte[] samples, int width, int height, out string reasonCode)
	{
		reasonCode = null;
		long num = (long)width * (long)height * 3;
		if (samples.LongLength < num)
		{
			reasonCode = "pixel-buffer-mismatch";
			return null;
		}
		byte[] array = new byte[num];
		for (long num2 = 0L; num2 < num; num2 += 3)
		{
			array[num2] = samples[num2 + 2];
			array[num2 + 1] = samples[num2 + 1];
			array[num2 + 2] = samples[num2];
		}
		return Encode24bppToPng(array, width, height, out reasonCode);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static byte[] Encode24bppToPng(byte[] rgb, int width, int height, out string reasonCode)
	{
		reasonCode = null;
		try
		{
			Bitmap val = new Bitmap(width, height, (PixelFormat)137224);
			try
			{
				Rectangle rectangle = new Rectangle(0, 0, width, height);
				BitmapData val2 = val.LockBits(rectangle, (ImageLockMode)2, (PixelFormat)137224);
				try
				{
					int num = width * 3;
					for (int i = 0; i < height; i++)
					{
						Marshal.Copy(rgb, i * num, IntPtr.Add(val2.Scan0, i * val2.Stride), num);
					}
				}
				finally
				{
					val.UnlockBits(val2);
				}
				using MemoryStream memoryStream = new MemoryStream();
				((Image)val).Save((Stream)memoryStream, ImageFormat.Png);
				return memoryStream.ToArray();
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		}
		catch (Exception)
		{
			reasonCode = "png-encode-failed";
			return null;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool IsGrayColorSpace(string colorSpaceName)
	{
		if (!string.IsNullOrEmpty(colorSpaceName) && !(colorSpaceName == "DeviceGray"))
		{
			return colorSpaceName == "CalGray";
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool IsRgbColorSpace(string colorSpaceName)
	{
		if (!string.IsNullOrEmpty(colorSpaceName) && !(colorSpaceName == "DeviceRGB"))
		{
			return colorSpaceName == "CalRGB";
		}
		return true;
	}

	private static bool IsJpeg(byte[] bytes)
	{
		if (bytes.Length >= 3 && bytes[0] == byte.MaxValue && bytes[1] == 216)
		{
			return bytes[2] == byte.MaxValue;
		}
		return false;
	}

	private static bool IsPng(byte[] bytes)
	{
		if (bytes.Length >= 8 && bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71 && bytes[4] == 13 && bytes[5] == 10 && bytes[6] == 26)
		{
			return bytes[7] == 10;
		}
		return false;
	}
}
