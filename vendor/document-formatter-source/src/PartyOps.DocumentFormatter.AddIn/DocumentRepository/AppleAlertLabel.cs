using System;
using System.Drawing;
using System.Windows.Forms;

namespace DocumentRepository;

internal class AppleAlertLabel : Label
{
	public int Radius { get; set; } = 8;

	public AppleAlertLabel()
	{
		base.SetStyle((ControlStyles)141330, true);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		e.Graphics.Clear(UiDraw.ResolveBackColor(((Control)this).Parent, AppleUiColors.Window));
		UiDraw.FillRoundedRect(e.Graphics, new Rectangle(0, 0, ((Control)this).Width - 1, ((Control)this).Height - 1), Radius, ((Control)this).BackColor);
		Padding padding = ((Control)this).Padding;
		int left = padding.Left;
		padding = ((Control)this).Padding;
		int top = padding.Top;
		int width = ((Control)this).Width;
		padding = ((Control)this).Padding;
		int width2 = Math.Max(0, width - padding.Horizontal);
		int height = ((Control)this).Height;
		padding = ((Control)this).Padding;
		Rectangle rectangle = new Rectangle(left, top, width2, Math.Max(0, height - padding.Vertical));
		TextRenderer.DrawText((IDeviceContext)(object)e.Graphics, ((Control)this).Text, ((Control)this).Font, rectangle, ((Control)this).ForeColor, (TextFormatFlags)32772);
	}
}
