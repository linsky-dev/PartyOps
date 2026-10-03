using System;
using UglyToad.PdfPig.Core;

namespace DocumentRepository.Services.Conversion.PdfToWord;

internal static class PdfRotationTransform
{
	public static void ToUser(double xd, double yd, int rotation, double dw, double dh, out double xu, out double yu)
	{
		switch (rotation)
		{
		case 90:
			xu = dh - yd;
			yu = xd;
			break;
		default:
			xu = yd;
			yu = dw - xd;
			break;
		case 180:
			xu = dw - xd;
			yu = dh - yd;
			break;
		}
	}

	public static void RectToUser(double left, double bottom, double right, double top, int rotation, double dw, double dh, out double userLeft, out double userBottom, out double userRight, out double userTop)
	{
		ToUser(left, bottom, rotation, dw, dh, out var xu, out var yu);
		ToUser(right, bottom, rotation, dw, dh, out var xu2, out var yu2);
		ToUser(right, top, rotation, dw, dh, out var xu3, out var yu3);
		ToUser(left, top, rotation, dw, dh, out var xu4, out var yu4);
		userLeft = Math.Min(Math.Min(xu, xu2), Math.Min(xu3, xu4));
		userRight = Math.Max(Math.Max(xu, xu2), Math.Max(xu3, xu4));
		userBottom = Math.Min(Math.Min(yu, yu2), Math.Min(yu3, yu4));
		userTop = Math.Max(Math.Max(yu, yu2), Math.Max(yu3, yu4));
	}

	public static void RectToUser(PdfRectangle rect, int rotation, double dw, double dh, out double userLeft, out double userBottom, out double userRight, out double userTop)
	{
		RectToUser(rect.Left, rect.Bottom, rect.Right, rect.Top, rotation, dw, dh, out userLeft, out userBottom, out userRight, out userTop);
	}

	public static void NormalizeLineFrame(PdfLineFrame frame, int rotation, double dw, double dh)
	{
		ToUser(frame.StartX, frame.StartY, rotation, dw, dh, out var xu, out var yu);
		ToUser(frame.EndX, frame.EndY, rotation, dw, dh, out var xu2, out var yu2);
		bool flag = frame.Orientation == PdfLineFrameOrientation.Horizontal;
		if (rotation != 180)
		{
			flag = !flag;
		}
		if (!flag)
		{
			frame.Orientation = PdfLineFrameOrientation.Vertical;
			float endX = (frame.StartX = (float)((xu + xu2) / 2.0));
			frame.EndX = endX;
			frame.StartY = (float)Math.Min(yu, yu2);
			frame.EndY = (float)Math.Max(yu, yu2);
		}
		else
		{
			frame.Orientation = PdfLineFrameOrientation.Horizontal;
			float num2 = (float)((yu + yu2) / 2.0);
			frame.StartX = (float)Math.Min(xu, xu2);
			frame.EndX = (float)Math.Max(xu, xu2);
			frame.StartY = num2;
			frame.EndY = num2;
		}
	}
}
