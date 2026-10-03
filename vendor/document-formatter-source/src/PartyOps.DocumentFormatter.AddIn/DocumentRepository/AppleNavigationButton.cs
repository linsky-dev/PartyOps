using System;
using System.Drawing;
using System.Windows.Forms;

namespace DocumentRepository;

internal class AppleNavigationButton : Button
{
	private bool selected;

	private bool hover;

	public bool Selected
	{
		get
		{
			return selected;
		}
		set
		{
			if (selected != value)
			{
				selected = value;
				((Control)this).Invalidate();
			}
		}
	}

	public AppleNavigationButton()
	{
		base.SetStyle((ControlStyles)141330, true);
		((ButtonBase)this).FlatStyle = (FlatStyle)0;
		((ButtonBase)this).FlatAppearance.BorderSize = 0;
		((Control)this).BackColor = Color.Transparent;
		((Control)this).ForeColor = AppleUiColors.TextPrimary;
		((Control)this).Font = UiFonts.Body;
		((Control)this).Cursor = Cursors.Hand;
		((ButtonBase)this).TextAlign = (ContentAlignment)16;
		((Control)this).Height = 40;
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		e.Graphics.Clear(UiDraw.ResolveBackColor(((Control)this).Parent, AppleUiColors.SurfaceMuted));
		Rectangle rect = new Rectangle(0, 0, ((Control)this).Width - 1, ((Control)this).Height - 1);
		if (Selected)
		{
			UiDraw.FillRoundedRect(e.Graphics, rect, 8, AppleUiColors.AccentSoft);
		}
		else if (hover)
		{
			UiDraw.FillRoundedRect(e.Graphics, rect, 8, AppleUiColors.SurfaceHover);
		}
		Rectangle rectangle = new Rectangle(14, 0, Math.Max(0, ((Control)this).Width - 22), ((Control)this).Height);
		TextRenderer.DrawText((IDeviceContext)(object)e.Graphics, ((Control)this).Text, ((Control)this).Font, rectangle, Selected ? AppleUiColors.Accent : AppleUiColors.TextPrimary, (TextFormatFlags)32772);
		if (((Control)this).Focused && base.ShowFocusCues)
		{
			ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(rect, -3, -3));
		}
	}

	protected override void OnMouseEnter(EventArgs e)
	{
		hover = true;
		((Control)this).Invalidate();
		base.OnMouseEnter(e);
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		hover = false;
		((Control)this).Invalidate();
		base.OnMouseLeave(e);
	}
}
