using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DocumentRepository;

internal static class UiDraw
{
	public static Color ResolveBackColor(Control control, Color fallback)
	{
		Control val = control;
		while (val != null)
		{
			Color backColor = val.BackColor;
			if (backColor.IsEmpty || backColor.A != byte.MaxValue)
			{
				val = val.Parent;
				continue;
			}
			return backColor;
		}
		return fallback;
	}

	public static GraphicsPath RoundedRect(Rectangle rect, int radius)
	{
		GraphicsPath val = new GraphicsPath();
		int num = radius * 2;
		val.AddArc(rect.X, rect.Y, num, num, 180f, 90f);
		val.AddArc(rect.Right - num, rect.Y, num, num, 270f, 90f);
		val.AddArc(rect.Right - num, rect.Bottom - num, num, num, 0f, 90f);
		val.AddArc(rect.X, rect.Bottom - num, num, num, 90f, 90f);
		val.CloseFigure();
		return val;
	}

	public static void FillRoundedRect(Graphics g, Rectangle rect, int radius, Color color)
	{
		g.SmoothingMode = (SmoothingMode)4;
		GraphicsPath val = RoundedRect(rect, radius);
		try
		{
			SolidBrush val2 = new SolidBrush(color);
			try
			{
				g.FillPath((Brush)(object)val2, val);
			}
			finally
			{
				((IDisposable)val2)?.Dispose();
			}
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	public static void DrawRoundedRect(Graphics g, Rectangle rect, int radius, Color color, float width = 1f)
	{
		g.SmoothingMode = (SmoothingMode)4;
		GraphicsPath val = RoundedRect(rect, radius);
		try
		{
			Pen val2 = new Pen(color, width);
			try
			{
				g.DrawPath(val2, val);
			}
			finally
			{
				((IDisposable)val2)?.Dispose();
			}
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	public static void DrawShadow(Graphics g, Rectangle rect, int radius, int blur = 4)
	{
		g.SmoothingMode = (SmoothingMode)4;
		for (int num = blur; num >= 1; num--)
		{
			int alpha = 8 * (blur - num + 1);
			GraphicsPath val = RoundedRect(new Rectangle(rect.X - num, rect.Y - num, rect.Width + num * 2, rect.Height + num * 2), radius + num);
			try
			{
				SolidBrush val2 = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0));
				try
				{
					g.FillPath((Brush)(object)val2, val);
				}
				finally
				{
					((IDisposable)val2)?.Dispose();
				}
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		}
	}
}
