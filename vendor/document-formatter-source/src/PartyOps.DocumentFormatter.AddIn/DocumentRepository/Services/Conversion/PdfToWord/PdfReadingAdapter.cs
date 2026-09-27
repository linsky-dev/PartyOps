using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using DocumentRepository.Services.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics;
using UglyToad.PdfPig.Tokens;
using UglyToad.PdfPig.XObjects;

namespace DocumentRepository.Services.Conversion.PdfToWord;

public sealed class PdfReadingAdapter : IDisposable
{
	private readonly PdfDocument _document;

	public int PageCount => _document.NumberOfPages;

	private PdfReadingAdapter(PdfDocument document)
	{
		_document = document;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static PdfReadingAdapter Open(string pdfPath)
	{
		if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
		{
			throw new PdfReadingException("PDF 源文件不存在或格式无效。");
		}
		try
		{
			return new PdfReadingAdapter(PdfDocument.Open(pdfPath));
		}
		catch (PdfReadingException)
		{
			throw;
		}
		catch (Exception ex2)
		{
			throw new PdfReadingException("PDF 无法打开：" + ex2.Message, ex2);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public PdfPageContent ReadPage(int pageIndex)
	{
		if (pageIndex >= 0 && pageIndex < _document.NumberOfPages)
		{
			Page page = _document.GetPage(pageIndex + 1);
			PdfPageContent obj = new PdfPageContent
			{
				PageIndex = pageIndex,
				Width = (float)page.Width,
				Height = (float)page.Height
			};
			_ = page.Rotation;
			obj.Rotation = page.Rotation.Value;
			PdfPageContent pdfPageContent = obj;
			bool flag = TryNormalizeRotation(page, pdfPageContent);
			ReadLetters(page, pageIndex, pdfPageContent, flag);
			ReadImages(page, pageIndex, pdfPageContent, flag);
			ReadLineFrames(page, pageIndex, pdfPageContent, flag);
			if (flag)
			{
				if (pdfPageContent.Rotation != 180)
				{
					float width = pdfPageContent.Width;
					pdfPageContent.Width = pdfPageContent.Height;
					pdfPageContent.Height = width;
				}
				pdfPageContent.Rotation = 0;
			}
			return pdfPageContent;
		}
		throw new ArgumentOutOfRangeException("pageIndex");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool TryNormalizeRotation(Page page, PdfPageContent content)
	{
		int rotation = content.Rotation;
		if (rotation != 90 && rotation != 180 && rotation != 270)
		{
			return false;
		}
		foreach (Letter letter in page.Letters)
		{
			if (string.IsNullOrEmpty(letter.Value))
			{
				continue;
			}
			PdfRotationTransform.ToUser(letter.StartBaseLine.X, letter.StartBaseLine.Y, rotation, content.Width, content.Height, out var _, out var yu);
			PdfRotationTransform.ToUser(letter.EndBaseLine.X, letter.EndBaseLine.Y, rotation, content.Width, content.Height, out var _, out var yu2);
			if (Math.Abs(yu - yu2) > 0.5)
			{
				try
				{
					LogService.Info("PdfReadingAdapter.RotationNormalizeSkipped page=" + (content.PageIndex + 1) + " rotation=" + rotation + " reason=inline-rotated-text");
				}
				catch
				{
				}
				return false;
			}
		}
		return true;
	}

	private static void ReadLineFrames(Page page, int pageIndex, PdfPageContent content, bool normalize)
	{
		IReadOnlyList<PdfPath> paths;
		try
		{
			paths = page.ExperimentalAccess.Paths;
		}
		catch (Exception)
		{
			return;
		}
		if (paths == null)
		{
			return;
		}
		foreach (PdfPath item in paths)
		{
			if (item == null || item.IsClipping)
			{
				continue;
			}
			content.VectorPathCount++;
			float strokeWidth = ((item.LineWidth > 0.0) ? ((float)item.LineWidth) : 0.5f);
			bool filled = item.IsFilled && !item.IsStroked;
			foreach (PdfSubpath item2 in item)
			{
				try
				{
					CollectSubpathLines(item2, page, pageIndex, content, strokeWidth, filled);
				}
				catch (Exception)
				{
				}
			}
		}
		if (!normalize)
		{
			return;
		}
		foreach (PdfLineFrame lineFrame in content.LineFrames)
		{
			PdfRotationTransform.NormalizeLineFrame(lineFrame, content.Rotation, content.Width, content.Height);
		}
	}

	private static void CollectSubpathLines(PdfSubpath subpath, Page page, int pageIndex, PdfPageContent content, float strokeWidth, bool filled)
	{
		PdfRectangle? drawnRectangle = subpath.GetDrawnRectangle();
		if (!drawnRectangle.HasValue)
		{
			double num = 0.0;
			double num2 = 0.0;
			double x = 0.0;
			double y = 0.0;
			bool flag = false;
			{
				foreach (PdfSubpath.IPathCommand command in subpath.Commands)
				{
					if (command is PdfSubpath.Move { Location: var location } move)
					{
						num = location.X;
						num2 = move.Location.Y;
						x = num;
						y = num2;
						flag = true;
					}
					else if (!(command is PdfSubpath.Line { From: { X: var x2 }, From: { Y: var y2 }, To: { X: var x3 }, To: var to2 } line))
					{
						if (command is PdfSubpath.Close && flag)
						{
							TryEmitAxisLine(num, num2, x, y, pageIndex, content, strokeWidth, filled);
						}
					}
					else
					{
						TryEmitAxisLine(x2, y2, x3, to2.Y, pageIndex, content, strokeWidth, filled);
						num = line.To.X;
						num2 = line.To.Y;
						flag = true;
					}
				}
				return;
			}
		}
		EmitRectangleEdges(drawnRectangle.Value, page, pageIndex, content, strokeWidth, filled);
	}

	private static void EmitRectangleEdges(PdfRectangle rect, Page page, int pageIndex, PdfPageContent content, float strokeWidth, bool filled)
	{
		float num = (float)rect.Width;
		float num2 = (float)rect.Height;
		if (num < 0.5f && !(num2 >= 0.5f))
		{
			return;
		}
		float num3 = (float)rect.Left;
		float num4 = (float)rect.Right;
		float num5 = (float)rect.Top;
		float num6 = (float)rect.Bottom;
		if (!(num2 <= 3f) || !(num >= 0.5f))
		{
			if (!(num > 3f) && num2 >= 0.5f)
			{
				float num7 = (float)rect.Centroid.X;
				AddLineFrame(content, PdfLineFrameOrientation.Vertical, num7, num6, num7, num5, (num > 0f) ? num : strokeWidth, filled, pageIndex);
			}
			else if (!((double)num > page.Width * 0.95) || (double)num2 <= page.Height * 0.95)
			{
				AddLineFrame(content, PdfLineFrameOrientation.Horizontal, num3, num5, num4, num5, strokeWidth, filled, pageIndex);
				AddLineFrame(content, PdfLineFrameOrientation.Horizontal, num3, num6, num4, num6, strokeWidth, filled, pageIndex);
				AddLineFrame(content, PdfLineFrameOrientation.Vertical, num3, num6, num3, num5, strokeWidth, filled, pageIndex);
				AddLineFrame(content, PdfLineFrameOrientation.Vertical, num4, num6, num4, num5, strokeWidth, filled, pageIndex);
			}
		}
		else
		{
			float num8 = (float)rect.Centroid.Y;
			AddLineFrame(content, PdfLineFrameOrientation.Horizontal, num3, num8, num4, num8, (num2 > 0f) ? num2 : strokeWidth, filled, pageIndex);
		}
	}

	private static void TryEmitAxisLine(double x0, double y0, double x1, double y1, int pageIndex, PdfPageContent content, float strokeWidth, bool filled)
	{
		double value = x1 - x0;
		double value2 = y1 - y0;
		if (!(Math.Abs(value2) > 0.75) && Math.Abs(value) >= 0.5)
		{
			float num = (float)((y0 + y1) / 2.0);
			float startX = (float)Math.Min(x0, x1);
			float endX = (float)Math.Max(x0, x1);
			AddLineFrame(content, PdfLineFrameOrientation.Horizontal, startX, num, endX, num, strokeWidth, filled, pageIndex);
		}
		else if (Math.Abs(value) <= 0.75 && Math.Abs(value2) >= 0.5)
		{
			float num2 = (float)((x0 + x1) / 2.0);
			float startY = (float)Math.Min(y0, y1);
			float endY = (float)Math.Max(y0, y1);
			AddLineFrame(content, PdfLineFrameOrientation.Vertical, num2, startY, num2, endY, strokeWidth, filled, pageIndex);
		}
	}

	private static void AddLineFrame(PdfPageContent content, PdfLineFrameOrientation orientation, float startX, float startY, float endX, float endY, float lineWidth, bool filled, int pageIndex)
	{
		content.LineFrames.Add(new PdfLineFrame
		{
			Orientation = orientation,
			StartX = startX,
			StartY = startY,
			EndX = endX,
			EndY = endY,
			LineWidth = lineWidth,
			IsFilledRect = filled,
			PageIndex = pageIndex
		});
	}

	public IList<PdfPageContent> ReadAllPages()
	{
		List<PdfPageContent> list = new List<PdfPageContent>();
		for (int i = 0; i < _document.NumberOfPages; i++)
		{
			list.Add(ReadPage(i));
		}
		return list;
	}

	private static void ReadLetters(Page page, int pageIndex, PdfPageContent content, bool normalize)
	{
		foreach (Letter letter in page.Letters)
		{
			if (!string.IsNullOrEmpty(letter.Value))
			{
				PdfTextElement pdfTextElement = new PdfTextElement
				{
					Text = letter.Value,
					FontSize = (float)letter.PointSize,
					FontName = (string.IsNullOrEmpty(letter.FontName) ? string.Empty : letter.FontName),
					PageIndex = pageIndex
				};
				if (!normalize)
				{
					pdfTextElement.X = (float)letter.Location.X;
					pdfTextElement.Y = (float)letter.Location.Y;
					pdfTextElement.Baseline = (float)letter.StartBaseLine.Y;
					PdfRectangle glyphRectangle = letter.GlyphRectangle;
					pdfTextElement.Width = (float)Math.Max(0.0, glyphRectangle.Width);
					pdfTextElement.Height = (float)Math.Max(0.0, glyphRectangle.Height);
				}
				else
				{
					PdfRotationTransform.ToUser(letter.StartBaseLine.X, letter.StartBaseLine.Y, content.Rotation, content.Width, content.Height, out var xu, out var yu);
					PdfRotationTransform.ToUser(letter.EndBaseLine.X, letter.EndBaseLine.Y, content.Rotation, content.Width, content.Height, out var xu2, out var _);
					pdfTextElement.Baseline = (float)yu;
					pdfTextElement.X = (float)Math.Min(xu, xu2);
					PdfRotationTransform.RectToUser(letter.GlyphRectangle, content.Rotation, content.Width, content.Height, out var userLeft, out var userBottom, out var userRight, out var userTop);
					pdfTextElement.Y = (float)userBottom;
					pdfTextElement.Width = (float)Math.Max(0.0, userRight - userLeft);
					pdfTextElement.Height = (float)Math.Max(0.0, userTop - userBottom);
				}
				pdfTextElement.Bold = IsBoldFontName(letter.FontName);
				pdfTextElement.Italic = IsItalicFontName(letter.FontName);
				content.Elements.Add(pdfTextElement);
			}
		}
	}

	private static void ReadImages(Page page, int pageIndex, PdfPageContent content, bool normalize)
	{
		IEnumerable<IPdfImage> images;
		try
		{
			images = page.GetImages();
		}
		catch (Exception)
		{
			return;
		}
		foreach (IPdfImage item in images)
		{
			PdfRectangle bounds = item.Bounds;
			float num = (float)Math.Max(0.0, bounds.Width);
			float num2 = (float)Math.Max(0.0, bounds.Height);
			if (!(num < 3f) && !(num2 < 3f))
			{
				PdfImage pdfImage = new PdfImage
				{
					PageIndex = pageIndex
				};
				if (!normalize)
				{
					pdfImage.X = (float)bounds.Left;
					pdfImage.Y = (float)bounds.Bottom;
					pdfImage.Width = num;
					pdfImage.Height = num2;
				}
				else
				{
					PdfRotationTransform.RectToUser(bounds, content.Rotation, content.Width, content.Height, out var userLeft, out var userBottom, out var userRight, out var userTop);
					pdfImage.X = (float)userLeft;
					pdfImage.Y = (float)userBottom;
					pdfImage.Width = (float)Math.Max(0.0, userRight - userLeft);
					pdfImage.Height = (float)Math.Max(0.0, userTop - userBottom);
				}
				pdfImage.PixelWidth = item.WidthInSamples;
				pdfImage.PixelHeight = item.HeightInSamples;
				pdfImage.BitsPerComponent = item.BitsPerComponent;
				pdfImage.Source = item;
				if (item.ColorSpaceDetails != null)
				{
					pdfImage.ComponentsPerPixel = item.ColorSpaceDetails.NumberOfColorComponents;
					pdfImage.ColorSpaceName = item.ColorSpaceDetails.Type.ToString();
				}
				if (TryGetImageBytes(item, out var bytes) && bytes != null && bytes.Length != 0)
				{
					pdfImage.Bytes = bytes;
					pdfImage.Format = InferImageFormat(item, bytes);
				}
				content.Images.Add(pdfImage);
			}
		}
	}

	private static bool TryGetImageBytes(IPdfImage image, out byte[] bytes)
	{
		if (!image.TryGetBytesAsMemory(out var memory))
		{
			byte[] array = image.RawBytes.ToArray();
			if (array != null && array.Length != 0)
			{
				bytes = array;
				return true;
			}
			bytes = null;
			return false;
		}
		bytes = memory.ToArray();
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string InferImageFormat(IPdfImage image, byte[] bytes)
	{
		if (bytes != null && bytes.Length >= 4)
		{
			if (bytes[0] == byte.MaxValue && bytes[1] == 216 && bytes[2] == byte.MaxValue)
			{
				return ".jpg";
			}
			if (bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71)
			{
				return ".png";
			}
			if (bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0 && bytes[3] == 12)
			{
				return ".jp2";
			}
		}
		if (!(image is XObjectImage { IsJpxEncoded: not false }))
		{
			switch (GetFilterName(image))
			{
			case "DCTDecode":
				return ".jpg";
			case "JPXDecode":
				return ".jp2";
			case "FlateDecode":
			case "LZWDecode":
				return ".png";
			default:
				return null;
			}
		}
		return ".jp2";
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string GetFilterName(IPdfImage image)
	{
		DictionaryToken imageDictionary = image.ImageDictionary;
		if (imageDictionary == null)
		{
			return null;
		}
		if (!imageDictionary.TryGet(NameToken.Filter, out var token))
		{
			return null;
		}
		string text = token?.ToString();
		if (string.IsNullOrEmpty(text))
		{
			return null;
		}
		if (text.IndexOf("DCT", StringComparison.OrdinalIgnoreCase) < 0)
		{
			if (text.IndexOf("JPX", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return "JPXDecode";
			}
			if (text.IndexOf("Flate", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return "FlateDecode";
			}
			if (text.IndexOf("LZW", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return "LZWDecode";
			}
			return null;
		}
		return "DCTDecode";
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool IsBoldFontName(string fontName)
	{
		if (string.IsNullOrEmpty(fontName))
		{
			return false;
		}
		string text = fontName.ToUpperInvariant();
		if (text.IndexOf("BOLD", StringComparison.Ordinal) < 0 && text.IndexOf("BLACK", StringComparison.Ordinal) < 0)
		{
			return text.IndexOf("HEAVY", StringComparison.Ordinal) >= 0;
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool IsItalicFontName(string fontName)
	{
		if (string.IsNullOrEmpty(fontName))
		{
			return false;
		}
		string text = fontName.ToUpperInvariant();
		if (text.IndexOf("ITALIC", StringComparison.Ordinal) < 0)
		{
			return text.IndexOf("OBLIQUE", StringComparison.Ordinal) >= 0;
		}
		return true;
	}

	public void Dispose()
	{
		_document?.Dispose();
	}

	void IDisposable.Dispose()
	{
		this.Dispose();
	}
}
