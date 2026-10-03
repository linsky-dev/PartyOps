using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Formatting.Images;

internal static class ImageLayoutResolver
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ImageAvailableArea Resolve(Microsoft.Office.Interop.Word.Range anchor, ImageFormatOptions options)
	{
		if (anchor == null)
		{
			throw new ArgumentNullException("anchor");
		}
		if (options == null)
		{
			throw new ArgumentNullException("options");
		}
		HostThreadRuntime.AssertAccess("ImageLayoutResolver.Resolve");
		float num = ImageLayoutMath.CentimetersToPoints((options.MaxWidthCm > 0f) ? options.MaxWidthCm : 14.5f);
		float num2 = ImageLayoutMath.CentimetersToPoints((options.MaxHeightCm > 0f) ? options.MaxHeightCm : 20f);
		float num3 = num;
		float num4 = num2;
		Sections value = null;
		Section value2 = null;
		PageSetup value3 = null;
		ParagraphFormat value4 = null;
		Cells value5 = null;
		Cell value6 = null;
		Microsoft.Office.Interop.Word.Tables value7 = null;
		Table value8 = null;
		try
		{
			value = anchor.Sections;
			if (value != null && value.Count > 0)
			{
				value2 = value[1];
				value3 = value2.PageSetup;
				num3 = value3.PageWidth - value3.LeftMargin - value3.RightMargin;
				num4 = value3.PageHeight - value3.TopMargin - value3.BottomMargin;
			}
			value4 = anchor.ParagraphFormat;
			if (value4 != null)
			{
				num3 -= Math.Max(0f, value4.LeftIndent) + Math.Max(0f, value4.RightIndent);
			}
			if ((bool)Convert.ToBoolean((dynamic)anchor.get_Information(WdInformation.wdWithInTable)))
			{
				value5 = anchor.Cells;
				value7 = anchor.Tables;
				if (value5 != null && value5.Count > 0)
				{
					value6 = value5[1];
					float num5 = value6.Width;
					if (value7 != null && value7.Count > 0)
					{
						value8 = value7[1];
						num5 -= Math.Max(0f, value8.LeftPadding) + Math.Max(0f, value8.RightPadding);
					}
					num3 = Math.Min(num3, num5);
				}
			}
		}
		catch (Exception ex)
		{
			LogService.Warn("ImageLayoutResolver 使用安全回退尺寸", ex);
		}
		finally
		{
			ComObjectRelease.Release(ref value8, "ImageLayoutResolver.table");
			ComObjectRelease.Release(ref value7, "ImageLayoutResolver.tables");
			ComObjectRelease.Release(ref value6, "ImageLayoutResolver.cell");
			ComObjectRelease.Release(ref value5, "ImageLayoutResolver.cells");
			ComObjectRelease.Release(ref value4, "ImageLayoutResolver.paragraphFormat");
			ComObjectRelease.Release(ref value3, "ImageLayoutResolver.pageSetup");
			ComObjectRelease.Release(ref value2, "ImageLayoutResolver.section");
			ComObjectRelease.Release(ref value, "ImageLayoutResolver.sections");
		}
		return new ImageAvailableArea
		{
			WidthPoints = ((num3 > 1f) ? num3 : num),
			HeightPoints = ((num4 > 1f) ? num4 : num2)
		};
	}
}
