using System;
using System.Drawing;
using System.Windows.Forms;

namespace DocumentRepository;

internal class StyledButton : Button
{
	private bool _hover;

	private bool _pressed;

	public Color HoverBackColor { get; set; }

	public Color PressBackColor { get; set; }

	public int Radius { get; set; } = 8;

	public StyledButton()
	{
		base.SetStyle((ControlStyles)139266, true);
		((ButtonBase)this).FlatStyle = (FlatStyle)0;
		((ButtonBase)this).FlatAppearance.BorderSize = 0;
		((Control)this).Cursor = Cursors.Hand;
		((Control)this).Font = UiFonts.BodyBold;
		Radius = 8;
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		graphics.Clear(UiDraw.ResolveBackColor(((Control)this).Parent, UiColors.BgPage));
		Rectangle rect = new Rectangle(0, 0, ((Control)this).Width - 1, ((Control)this).Height - 1);
		Color color = (_pressed ? PressBackColor : (_hover ? HoverBackColor : ((Control)this).BackColor));
		UiDraw.FillRoundedRect(graphics, rect, Radius, color);
		SizeF sizeF = graphics.MeasureString(((Control)this).Text, ((Control)this).Font);
		SolidBrush val = new SolidBrush(((Control)this).ForeColor);
		try
		{
			graphics.DrawString(((Control)this).Text, ((Control)this).Font, (Brush)(object)val, ((float)((Control)this).Width - sizeF.Width) / 2f, ((float)((Control)this).Height - sizeF.Height) / 2f + 1f);
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	protected override void OnMouseEnter(EventArgs e)
	{
		_hover = true;
		((Control)this).Invalidate();
		base.OnMouseEnter(e);
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		_hover = false;
		_pressed = false;
		((Control)this).Invalidate();
		base.OnMouseLeave(e);
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		_pressed = true;
		((Control)this).Invalidate();
		base.OnMouseDown(e);
	}

	protected override void OnMouseUp(MouseEventArgs e)
	{
		_pressed = false;
		((Control)this).Invalidate();
		base.OnMouseUp(e);
	}
}
