using System.Drawing;
using System.Windows.Forms;

namespace DocumentRepository;

internal class RoundedPanel : Panel
{
	public int Radius { get; set; } = 12;

	public bool DrawShadow { get; set; }

	public Color BorderColor { get; set; } = UiColors.Border;

	public float BorderWidth { get; set; } = 1f;

	public RoundedPanel()
	{
		base.SetStyle((ControlStyles)141330, true);
		((Control)this).BackColor = UiColors.BgCard;
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		e.Graphics.Clear(UiDraw.ResolveBackColor(((Control)this).Parent, UiColors.BgPage));
		Rectangle rect = new Rectangle(0, 0, ((Control)this).Width - 1, ((Control)this).Height - 1);
		if (DrawShadow)
		{
			UiDraw.DrawShadow(e.Graphics, rect, Radius);
		}
		UiDraw.FillRoundedRect(e.Graphics, rect, Radius, ((Control)this).BackColor);
		if (!(BorderWidth <= 0f))
		{
			UiDraw.DrawRoundedRect(e.Graphics, rect, Radius, BorderColor, BorderWidth);
		}
	}
}
