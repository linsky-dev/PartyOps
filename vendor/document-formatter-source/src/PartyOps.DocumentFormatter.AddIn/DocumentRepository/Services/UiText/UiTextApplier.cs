using System;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.Layout;

namespace DocumentRepository.Services.UiText;

public static class UiTextApplier
{
	public static ToolTip CreateToolTip()
	{
		ToolTip toolTip = new ToolTip
		{
			AutoPopDelay = 12000,
			InitialDelay = 250,
			ReshowDelay = 100,
			ShowAlways = true,
			OwnerDraw = true,
			BackColor = AppleUiColors.Surface,
			ForeColor = AppleUiColors.TextPrimary
		};
		toolTip.Popup += delegate(object sender, PopupEventArgs e)
		{
			Size size = TextRenderer.MeasureText(toolTip.GetToolTip(e.AssociatedControl) ?? string.Empty, UiFonts.Caption, new Size(420, 0), (TextFormatFlags)268435472);
			e.ToolTipSize = new Size(Math.Max(72, size.Width + 24), size.Height + 18);
		};
		toolTip.Draw += DrawToolTip;
		return toolTip;
	}

	private static void DrawToolTip(object sender, DrawToolTipEventArgs e)
	{
		e.Graphics.Clear(AppleUiColors.Surface);
		using (Pen borderPen = new Pen(AppleUiColors.SeparatorStrong))
		{
			e.Graphics.DrawRectangle(borderPen, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
		}
		TextRenderer.DrawText((IDeviceContext)(object)e.Graphics, e.ToolTipText, UiFonts.Caption,
			new Rectangle(12, 8, e.Bounds.Width - 24, e.Bounds.Height - 16),
			AppleUiColors.TextPrimary, (TextFormatFlags)268435476);
	}

	public static void ApplyTooltip(ToolTip toolTip, Control control, string key)
	{
		if (toolTip != null && control != null)
		{
			UiTextMeta uiTextMeta = UiTextRegistry.Get(key);
			toolTip.SetToolTip(control, uiTextMeta.TooltipEnabled ? uiTextMeta.Tooltip : "");
		}
	}

	public static void ApplyParameterTooltip(ToolTip toolTip, Control root, string key, string caption, params Control[] controls)
	{
		if (toolTip == null || root == null)
		{
			return;
		}
		ApplyTooltipToCaption(toolTip, root, key, caption);
		if (controls != null)
		{
			foreach (Control control in controls)
			{
				ApplyTooltip(toolTip, control, key);
			}
		}
	}

	private static void ApplyTooltipToCaption(ToolTip toolTip, Control root, string key, string caption)
	{
		if (string.IsNullOrWhiteSpace(caption))
		{
			return;
		}
		if ((root is Label || root is CheckBox || root is GroupBox) && string.Equals((root.Text ?? "").Trim(), caption.Trim(), StringComparison.Ordinal))
		{
			ApplyTooltip(toolTip, root, key);
		}
		foreach (Control item in (ArrangedElementCollection)root.Controls)
		{
			Control root2 = item;
			ApplyTooltipToCaption(toolTip, root2, key, caption);
		}
	}

	public static void ApplyTooltipText(ToolTip toolTip, Control control, string tooltip)
	{
		if (toolTip != null && control != null)
		{
			toolTip.SetToolTip(control, tooltip ?? "");
		}
	}
}
