using System;
using System.Drawing;
using System.Windows.Forms;

namespace DocumentRepository;

internal sealed class AppleGroupBox : GroupBox
{
	public int Radius { get; set; } = 12;

	public AppleGroupBox()
	{
		base.SetStyle((ControlStyles)139282, true);
		((Control)this).BackColor = AppleUiColors.Surface;
		((Control)this).ForeColor = AppleUiColors.TextPrimary;
		((Control)this).Font = UiFonts.BodyBold;
		((Control)this).Padding = new Padding(14, 24, 14, 12);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		e.Graphics.Clear(UiDraw.ResolveBackColor(((Control)this).Parent, AppleUiColors.Window));
		Rectangle rect = new Rectangle(0, 8, Math.Max(1, ((Control)this).Width - 1), Math.Max(1, ((Control)this).Height - 9));
		UiDraw.FillRoundedRect(e.Graphics, rect, Radius, AppleUiColors.Surface);
		UiDraw.DrawRoundedRect(e.Graphics, rect, Radius, AppleUiColors.Separator);
		if (!string.IsNullOrWhiteSpace(((Control)this).Text))
		{
			Rectangle rectangle = new Rectangle(14, 0, Math.Min(TextRenderer.MeasureText(((Control)this).Text, ((Control)this).Font, new Size(Math.Max(1, ((Control)this).Width - 36), 24), (TextFormatFlags)268468224).Width + 12, Math.Max(1, ((Control)this).Width - 28)), 22);
			SolidBrush val = new SolidBrush(AppleUiColors.Surface);
			try
			{
				e.Graphics.FillRectangle((Brush)(object)val, rectangle);
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
			TextRenderer.DrawText((IDeviceContext)(object)e.Graphics, ((Control)this).Text.Trim(), ((Control)this).Font, new Rectangle(18, 1, rectangle.Width - 8, 20), AppleUiColors.TextPrimary, (TextFormatFlags)32772);
		}
	}
}
