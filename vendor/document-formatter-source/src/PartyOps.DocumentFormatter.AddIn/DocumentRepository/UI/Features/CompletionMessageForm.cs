using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using DocumentRepository.Services.UiText;

namespace DocumentRepository.UI.Features;

internal sealed class CompletionMessageForm : Form
{
	private sealed class SuccessGlyphControl : Control
	{
		public SuccessGlyphControl()
		{
			base.SetStyle((ControlStyles)141314, true);
			((Control)this).BackColor = Color.Transparent;
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			e.Graphics.SmoothingMode = (SmoothingMode)4;
			Rectangle rectangle = new Rectangle(1, 1, ((Control)this).Width - 2, ((Control)this).Height - 2);
			SolidBrush val = new SolidBrush(Color.FromArgb(232, 247, 236));
			try
			{
				e.Graphics.FillEllipse((Brush)(object)val, rectangle);
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
			Pen val2 = new Pen(Green, 2.6f);
			try
			{
				val2.StartCap = (LineCap)2;
				val2.EndCap = (LineCap)2;
				e.Graphics.DrawLines(val2, new Point[3]
				{
					new Point(17, 29),
					new Point(24, 36),
					new Point(40, 19)
				});
			}
			finally
			{
				((IDisposable)val2)?.Dispose();
			}
		}
	}

	private sealed class RoundedMetadataLabel : Label
	{
		public RoundedMetadataLabel()
		{
			base.SetStyle((ControlStyles)141330, true);
			((Control)this).BackColor = Color.Transparent;
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			e.Graphics.SmoothingMode = (SmoothingMode)4;
			Rectangle rectangle = new Rectangle(0, 0, ((Control)this).Width - 1, ((Control)this).Height - 1);
			GraphicsPath val = Drawing.CreateRoundedRectangle(rectangle, ((Control)this).Height / 2);
			try
			{
				SolidBrush val2 = new SolidBrush(Color.FromArgb(247, 247, 249));
				try
				{
					e.Graphics.FillPath((Brush)(object)val2, val);
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
			TextRenderer.DrawText((IDeviceContext)(object)e.Graphics, ((Control)this).Text, ((Control)this).Font, rectangle, ((Control)this).ForeColor, (TextFormatFlags)32773);
		}
	}

	private sealed class RoundedPrimaryButton : Button
	{
		private bool hovered;

		private bool pressed;

		public RoundedPrimaryButton()
		{
			base.SetStyle((ControlStyles)139282, true);
			((Control)this).Cursor = Cursors.Hand;
			((ButtonBase)this).FlatStyle = (FlatStyle)0;
			((ButtonBase)this).FlatAppearance.BorderSize = 0;
			((ButtonBase)this).UseVisualStyleBackColor = false;
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			e.Graphics.SmoothingMode = (SmoothingMode)4;
			Rectangle rectangle = new Rectangle(0, 0, ((Control)this).Width - 1, ((Control)this).Height - 1);
			Color color = (pressed ? Color.FromArgb(0, 94, 198) : (hovered ? Color.FromArgb(0, 104, 218) : Blue));
			GraphicsPath val = Drawing.CreateRoundedRectangle(rectangle, ((Control)this).Height / 2);
			try
			{
				SolidBrush val2 = new SolidBrush(color);
				try
				{
					e.Graphics.FillPath((Brush)(object)val2, val);
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
			if (((Control)this).Focused)
			{
				Rectangle rectangle2 = new Rectangle(3, 3, ((Control)this).Width - 7, ((Control)this).Height - 7);
				GraphicsPath val3 = Drawing.CreateRoundedRectangle(rectangle2, rectangle2.Height / 2);
				try
				{
					Pen val4 = new Pen(Color.FromArgb(190, 225, 255), 1.5f);
					try
					{
						e.Graphics.DrawPath(val4, val3);
					}
					finally
					{
						((IDisposable)val4)?.Dispose();
					}
				}
				finally
				{
					((IDisposable)val3)?.Dispose();
				}
			}
			TextRenderer.DrawText((IDeviceContext)(object)e.Graphics, ((Control)this).Text, ((Control)this).Font, rectangle, Color.White, (TextFormatFlags)32773);
		}

		protected override void OnMouseEnter(EventArgs e)
		{
			hovered = true;
			((Control)this).Invalidate();
			base.OnMouseEnter(e);
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			hovered = false;
			pressed = false;
			((Control)this).Invalidate();
			base.OnMouseLeave(e);
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			pressed = true;
			((Control)this).Invalidate();
			base.OnMouseDown(e);
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			pressed = false;
			((Control)this).Invalidate();
			base.OnMouseUp(e);
		}

		protected override void OnGotFocus(EventArgs e)
		{
			((Control)this).Invalidate();
			base.OnGotFocus(e);
		}

		protected override void OnLostFocus(EventArgs e)
		{
			((Control)this).Invalidate();
			base.OnLostFocus(e);
		}
	}

	private static class Drawing
	{
		public static GraphicsPath CreateRoundedRectangle(Rectangle rectangle, int radius)
		{
			GraphicsPath val = new GraphicsPath();
			int num = Math.Max(1, Math.Min(radius, Math.Min(rectangle.Width, rectangle.Height) / 2)) * 2;
			Rectangle rectangle2 = new Rectangle(rectangle.Location, new Size(num, num));
			val.AddArc(rectangle2, 180f, 90f);
			rectangle2.X = rectangle.Right - num;
			val.AddArc(rectangle2, 270f, 90f);
			rectangle2.Y = rectangle.Bottom - num;
			val.AddArc(rectangle2, 0f, 90f);
			rectangle2.X = rectangle.Left;
			val.AddArc(rectangle2, 90f, 90f);
			val.CloseFigure();
			return val;
		}
	}

	private const int WindowRadius = 20;

	private static readonly Color WindowBackColor = Color.FromArgb(255, 255, 255);

	private static readonly Color TextPrimary = Color.FromArgb(29, 29, 31);

	private static readonly Color TextSecondary = Color.FromArgb(110, 110, 115);

	private static readonly Color TextTertiary = Color.FromArgb(142, 142, 147);

	private static readonly Color DividerColor = Color.FromArgb(229, 229, 234);

	private static readonly Color Blue = Color.FromArgb(0, 113, 227);

	private static readonly Color Green = Color.FromArgb(36, 138, 61);

	private static readonly HashSet<CompletionMessageForm> ActiveForms = new HashSet<CompletionMessageForm>();

	private readonly Font kickerFont;

	private readonly Font titleFont;

	private readonly Font summaryFont;

	private readonly Font metadataFont;

	private readonly Font buttonFont;

	private readonly Timer closeTimer;

	private readonly ToolTip toolTip;

	protected override CreateParams CreateParams
	{
		get
		{
			CreateParams createParams = base.CreateParams;
			createParams.ClassStyle |= 0x20000;
			return createParams;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private CompletionMessageForm(string caption, string summary, string detail, int timeoutMilliseconds)
	{
		kickerFont = new Font("Microsoft YaHei UI", 8.5f, (FontStyle)1, (GraphicsUnit)3);
		titleFont = new Font("Microsoft YaHei UI", 16f, (FontStyle)1, (GraphicsUnit)3);
		summaryFont = new Font("Microsoft YaHei UI", 10f, (FontStyle)0, (GraphicsUnit)3);
		metadataFont = new Font("Microsoft YaHei UI", 8.5f, (FontStyle)0, (GraphicsUnit)3);
		buttonFont = new Font("Microsoft YaHei UI", 9f, (FontStyle)1, (GraphicsUnit)3);
		toolTip = UiTextApplier.CreateToolTip();
		((ContainerControl)this).AutoScaleDimensions = new SizeF(96f, 96f);
		((ContainerControl)this).AutoScaleMode = (AutoScaleMode)2;
		((Control)this).BackColor = WindowBackColor;
		((Form)this).ClientSize = new Size(440, 266);
		((Form)this).FormBorderStyle = (FormBorderStyle)0;
		((Form)this).MaximizeBox = false;
		((Form)this).MinimizeBox = false;
		((Form)this).ShowIcon = false;
		((Form)this).ShowInTaskbar = false;
		((Form)this).StartPosition = (FormStartPosition)1;
		((Control)this).Text = (string.IsNullOrWhiteSpace(caption) ? "排版完成" : caption.Trim());
		base.SetStyle((ControlStyles)139282, true);
		SuccessGlyphControl successGlyphControl = new SuccessGlyphControl();
		((Control)successGlyphControl).AccessibleDescription = "排版任务已成功完成";
		((Control)successGlyphControl).AccessibleName = "完成";
		((Control)successGlyphControl).Location = new Point(28, 24);
		((Control)successGlyphControl).Size = new Size(56, 56);
		((Control)successGlyphControl).TabStop = false;
		SuccessGlyphControl successGlyphControl2 = successGlyphControl;
		Label val = new Label
		{
			AutoEllipsis = true,
			BackColor = Color.Transparent,
			Font = kickerFont,
			ForeColor = Green,
			Location = new Point(98, 25),
			Size = new Size(300, 18),
			Text = "任务已完成"
		};
		Label val2 = new Label
		{
			AutoEllipsis = true,
			BackColor = Color.Transparent,
			Font = titleFont,
			ForeColor = TextPrimary,
			Location = new Point(96, 43),
			Size = new Size(316, 38),
			Text = ((Control)this).Text
		};
		Label val3 = new Label
		{
			AutoEllipsis = true,
			BackColor = Color.Transparent,
			Font = summaryFont,
			ForeColor = TextPrimary,
			Location = new Point(28, 104),
			Size = new Size(384, 26),
			Text = (string.IsNullOrWhiteSpace(summary) ? "本次任务已全部完成" : summary.Trim())
		};
		RoundedMetadataLabel roundedMetadataLabel = new RoundedMetadataLabel();
		((Control)roundedMetadataLabel).AccessibleName = "任务耗时";
		((Control)roundedMetadataLabel).BackColor = Color.Transparent;
		((Control)roundedMetadataLabel).Font = metadataFont;
		((Control)roundedMetadataLabel).ForeColor = TextSecondary;
		((Control)roundedMetadataLabel).Location = new Point(28, 142);
		((Control)roundedMetadataLabel).Size = new Size(156, 34);
		((Control)roundedMetadataLabel).Text = (string.IsNullOrWhiteSpace(detail) ? "处理完成" : detail.Trim());
		((Label)roundedMetadataLabel).TextAlign = (ContentAlignment)32;
		RoundedMetadataLabel roundedMetadataLabel2 = roundedMetadataLabel;
		Panel val4 = new Panel
		{
			BackColor = DividerColor,
			Location = new Point(28, 193),
			Size = new Size(384, 1),
			TabStop = false
		};
		Label val5 = new Label
		{
			AutoEllipsis = true,
			BackColor = Color.Transparent,
			Font = metadataFont,
			ForeColor = TextTertiary,
			Location = new Point(28, 214),
			Size = new Size(250, 22),
			Text = "窗口将在片刻后自动关闭"
		};
		RoundedPrimaryButton roundedPrimaryButton = new RoundedPrimaryButton();
		((Control)roundedPrimaryButton).AccessibleDescription = "关闭排版完成提示";
		((Control)roundedPrimaryButton).AccessibleName = "完成";
		((Button)roundedPrimaryButton).DialogResult = (DialogResult)1;
		((Control)roundedPrimaryButton).Font = buttonFont;
		((Control)roundedPrimaryButton).Location = new Point(316, 207);
		((Control)roundedPrimaryButton).Size = new Size(96, 44);
		((Control)roundedPrimaryButton).TabIndex = 0;
		((Control)roundedPrimaryButton).Text = "完成";
		RoundedPrimaryButton closeButton = roundedPrimaryButton;
		((Control)closeButton).Click += delegate
		{
			((Form)this).Close();
		};
		UiTextApplier.ApplyTooltip(toolTip, (Control)(object)closeButton, "Dialog.Close");
		((Form)this).AcceptButton = (IButtonControl)(object)closeButton;
		((Form)this).CancelButton = (IButtonControl)(object)closeButton;
		((Control)this).Controls.Add((Control)(object)successGlyphControl2);
		((Control)this).Controls.Add((Control)(object)val);
		((Control)this).Controls.Add((Control)(object)val2);
		((Control)this).Controls.Add((Control)(object)val3);
		((Control)this).Controls.Add((Control)(object)roundedMetadataLabel2);
		((Control)this).Controls.Add((Control)(object)val4);
		((Control)this).Controls.Add((Control)(object)val5);
		((Control)this).Controls.Add((Control)(object)closeButton);
		closeTimer = new Timer
		{
			Interval = Math.Max(500, timeoutMilliseconds)
		};
		closeTimer.Tick += delegate
		{
			closeTimer.Stop();
			((Form)this).Close();
		};
		((Form)this).Shown += delegate
		{
			((Control)closeButton).Select();
			closeTimer.Start();
		};
		UpdateWindowRegion();
	}

	public static void ShowMessage(string caption, string summary, string detail, int timeoutMilliseconds)
	{
		CompletionMessageForm form = new CompletionMessageForm(caption, summary, detail, timeoutMilliseconds);
		ActiveForms.Add(form);
		((Form)form).FormClosed += (FormClosedEventHandler)delegate
		{
			ActiveForms.Remove(form);
			((Component)(object)form).Dispose();
		};
		((Control)form).Show();
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);
		e.Graphics.SmoothingMode = (SmoothingMode)4;
		GraphicsPath val = Drawing.CreateRoundedRectangle(new Rectangle(0, 0, ((Form)this).ClientSize.Width - 1, ((Form)this).ClientSize.Height - 1), 20);
		try
		{
			Pen val2 = new Pen(Color.FromArgb(214, 214, 220));
			try
			{
				e.Graphics.DrawPath(val2, val);
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

	protected override void OnSizeChanged(EventArgs e)
	{
		base.OnSizeChanged(e);
		UpdateWindowRegion();
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			((Component)(object)closeTimer).Dispose();
			kickerFont.Dispose();
			titleFont.Dispose();
			summaryFont.Dispose();
			metadataFont.Dispose();
			buttonFont.Dispose();
			((Component)(object)toolTip).Dispose();
		}
		base.Dispose(disposing);
	}

	private void UpdateWindowRegion()
	{
		if (((Form)this).ClientSize.Width <= 0 || ((Form)this).ClientSize.Height <= 0)
		{
			return;
		}
		GraphicsPath val = Drawing.CreateRoundedRectangle(new Rectangle(0, 0, ((Form)this).ClientSize.Width, ((Form)this).ClientSize.Height), 20);
		try
		{
			Region region = ((Control)this).Region;
			((Control)this).Region = new Region(val);
			if (region != null)
			{
				region.Dispose();
			}
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}
}
