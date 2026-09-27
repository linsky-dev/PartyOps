using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DocumentRepository;

internal class AppleToggleSwitch : CheckBox
{
	private bool hover;

	public AppleToggleSwitch()
	{
		base.SetStyle((ControlStyles)141330, true);
		((Control)this).BackColor = Color.Transparent;
		((Control)this).ForeColor = AppleUiColors.TextPrimary;
		((Control)this).Font = UiFonts.Body;
		((Control)this).Cursor = Cursors.Hand;
		((CheckBox)this).AutoCheck = true;
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		e.Graphics.Clear(UiDraw.ResolveBackColor(((Control)this).Parent, AppleUiColors.Surface));
		e.Graphics.SmoothingMode = (SmoothingMode)4;
		int num = 36;
		int num2 = 22;
		int num3 = Math.Max(0, ((Control)this).Width - num - 2);
		int num4 = Math.Max(0, (((Control)this).Height - num2) / 2);
		Rectangle rectangle = new Rectangle(0, 0, Math.Max(0, num3 - 10), ((Control)this).Height);
		TextRenderer.DrawText((IDeviceContext)(object)e.Graphics, ((Control)this).Text, ((Control)this).Font, rectangle, ((Control)this).Enabled ? AppleUiColors.TextPrimary : AppleUiColors.TextTertiary, (TextFormatFlags)32772);
		Color color = (((CheckBox)this).Checked ? AppleUiColors.Success : AppleUiColors.SwitchOff);
		if (!((Control)this).Enabled)
		{
			color = AppleUiColors.SeparatorStrong;
		}
		else if (hover && ((CheckBox)this).Checked)
		{
			color = AppleUiColors.SuccessHover;
		}
		UiDraw.FillRoundedRect(e.Graphics, new Rectangle(num3, num4, num, num2), 11, color);
		int num5 = 18;
		int num6 = (((CheckBox)this).Checked ? (num3 + num - num5 - 2) : (num3 + 2));
		SolidBrush val = new SolidBrush(Color.FromArgb(34, 0, 0, 0));
		try
		{
			e.Graphics.FillEllipse((Brush)(object)val, num6, num4 + 3, num5, num5);
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
		SolidBrush val2 = new SolidBrush(Color.White);
		try
		{
			e.Graphics.FillEllipse((Brush)(object)val2, num6, num4 + 2, num5, num5);
		}
		finally
		{
			((IDisposable)val2)?.Dispose();
		}
		if (((Control)this).Focused && base.ShowFocusCues)
		{
			ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(((Control)this).ClientRectangle, -2, -2));
		}
	}

	protected override void OnCheckedChanged(EventArgs e)
	{
		((Control)this).Invalidate();
		base.OnCheckedChanged(e);
	}

	protected override void OnEnabledChanged(EventArgs e)
	{
		((Control)this).Invalidate();
		base.OnEnabledChanged(e);
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
