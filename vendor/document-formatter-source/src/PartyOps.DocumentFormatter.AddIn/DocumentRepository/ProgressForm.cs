using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using DocumentRepository.Services.UiText;

namespace DocumentRepository;

public class ProgressForm : Form
{
	private enum StatusGlyphTone
	{
		Working,
		Cancelling,
		Success,
		Failed
	}

	private sealed class StatusGlyphControl : Control
	{
		private StatusGlyphTone tone;

		public StatusGlyphTone Tone
		{
			get
			{
				return tone;
			}
			set
			{
				if (tone != value)
				{
					tone = value;
					((Control)this).Invalidate();
				}
			}
		}

		public StatusGlyphControl()
		{
			base.SetStyle((ControlStyles)141314, true);
			((Control)this).BackColor = Color.Transparent;
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			e.Graphics.SmoothingMode = (SmoothingMode)4;
			Color color;
			Color color2;
			switch (tone)
			{
			case StatusGlyphTone.Failed:
				color = Red;
				color2 = Color.FromArgb(255, 235, 234);
				break;
			default:
				color = Blue;
				color2 = Color.FromArgb(232, 244, 255);
				break;
			case StatusGlyphTone.Cancelling:
				color = Orange;
				color2 = Color.FromArgb(255, 243, 224);
				break;
			case StatusGlyphTone.Success:
				color = Green;
				color2 = Color.FromArgb(232, 247, 236);
				break;
			}
			Rectangle rectangle = new Rectangle(1, 1, ((Control)this).Width - 2, ((Control)this).Height - 2);
			SolidBrush val = new SolidBrush(color2);
			try
			{
				e.Graphics.FillEllipse((Brush)(object)val, rectangle);
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
			Pen val2 = new Pen(color, 2.2f);
			try
			{
				val2.StartCap = (LineCap)2;
				val2.EndCap = (LineCap)2;
				if (tone != StatusGlyphTone.Success)
				{
					if (tone != StatusGlyphTone.Failed)
					{
						if (tone != StatusGlyphTone.Cancelling)
						{
							e.Graphics.DrawLine(val2, 24, 13, 24, 35);
							e.Graphics.DrawLine(val2, 13, 24, 35, 24);
							Pen val3 = new Pen(color, 1.5f);
							try
							{
								e.Graphics.DrawLine(val3, 17, 17, 31, 31);
								e.Graphics.DrawLine(val3, 31, 17, 17, 31);
								return;
							}
							finally
							{
								((IDisposable)val3)?.Dispose();
							}
						}
						e.Graphics.DrawLine(val2, 19, 17, 19, 31);
						e.Graphics.DrawLine(val2, 29, 17, 29, 31);
					}
					else
					{
						e.Graphics.DrawLine(val2, 17, 17, 31, 31);
						e.Graphics.DrawLine(val2, 31, 17, 17, 31);
					}
				}
				else
				{
					e.Graphics.DrawLines(val2, new Point[3]
					{
						new Point(15, 25),
						new Point(21, 31),
						new Point(34, 17)
					});
				}
			}
			finally
			{
				((IDisposable)val2)?.Dispose();
			}
		}
	}

	private sealed class LightweightProgressBar : Control
	{
		private int maximum = 100;

		private int value;

		private bool completed;

		public int Maximum
		{
			get
			{
				return maximum;
			}
			set
			{
				int num = Math.Max(1, value);
				if (maximum != num)
				{
					maximum = num;
					if (this.value > maximum)
					{
						this.value = maximum;
					}
					((Control)this).Invalidate();
				}
			}
		}

		public int Value
		{
			get
			{
				return value;
			}
			set
			{
				int num = Math.Max(0, Math.Min(maximum, value));
				if (this.value != num)
				{
					this.value = num;
					((Control)this).Invalidate();
				}
			}
		}

		public bool Completed
		{
			get
			{
				return completed;
			}
			set
			{
				if (completed != value)
				{
					completed = value;
					((Control)this).Invalidate();
				}
			}
		}

		public LightweightProgressBar()
		{
			base.SetStyle((ControlStyles)139282, true);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			e.Graphics.SmoothingMode = (SmoothingMode)4;
			Rectangle rectangle = new Rectangle(0, 0, Math.Max(1, ((Control)this).Width - 1), Math.Max(1, ((Control)this).Height - 1));
			GraphicsPath val = Drawing.CreateRoundedRectangle(rectangle, Math.Max(1, ((Control)this).Height / 2));
			try
			{
				SolidBrush val2 = new SolidBrush(Color.FromArgb(229, 229, 234));
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
			if (value <= 0 || maximum <= 0)
			{
				return;
			}
			int val3 = Math.Max(((Control)this).Height, (int)Math.Round((double)rectangle.Width * (double)value / (double)maximum));
			val3 = Math.Min(rectangle.Width, val3);
			Rectangle rectangle2 = new Rectangle(rectangle.X, rectangle.Y, val3, rectangle.Height);
			Color color = (completed ? Color.FromArgb(36, 138, 61) : Blue);
			Color color2 = (completed ? Color.FromArgb(52, 199, 89) : Color.FromArgb(49, 161, 255));
			GraphicsPath val4 = Drawing.CreateRoundedRectangle(rectangle2, Math.Max(1, ((Control)this).Height / 2));
			try
			{
				LinearGradientBrush val5 = new LinearGradientBrush(rectangle2, color, color2, (LinearGradientMode)0);
				try
				{
					e.Graphics.FillPath((Brush)(object)val5, val4);
				}
				finally
				{
					((IDisposable)val5)?.Dispose();
				}
			}
			finally
			{
				((IDisposable)val4)?.Dispose();
			}
		}
	}

	private sealed class StageTimelineControl : Control
	{
		private readonly Font stageFont;

		private int phase;

		private bool completed;

		[MethodImpl(MethodImplOptions.NoInlining)]
		public StageTimelineControl()
		{
			stageFont = new Font("Microsoft YaHei UI", 8.5f, (FontStyle)0, (GraphicsUnit)3);
			base.SetStyle((ControlStyles)141330, true);
			((Control)this).BackColor = Color.Transparent;
		}

		public void SetProgress(int percent)
		{
			int num = ((percent >= 34) ? ((percent < 92) ? 1 : 2) : 0);
			bool flag = percent >= 100;
			if (phase != num || completed != flag)
			{
				phase = num;
				completed = flag;
				((Control)this).Invalidate();
			}
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				stageFont.Dispose();
			}
			base.Dispose(disposing);
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			e.Graphics.SmoothingMode = (SmoothingMode)4;
			string[] array = new string[3] { "分析文档", "应用格式", "检查结果" };
			int num = 12;
			int num2 = 16;
			int num3 = ((Control)this).Width / 6;
			int num4 = ((Control)this).Width / 2;
			int num5 = ((Control)this).Width * 5 / 6;
			int[] array2 = new int[3] { num3, num4, num5 };
			Pen val = new Pen(Color.FromArgb(218, 218, 223), 2f);
			try
			{
				e.Graphics.DrawLine(val, array2[0] + num, num2, array2[1] - num, num2);
				e.Graphics.DrawLine(val, array2[1] + num, num2, array2[2] - num, num2);
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
			for (int i = 0; i < array2.Length; i++)
			{
				bool flag = completed || i < phase;
				bool flag2 = !completed && i == phase;
				Color color = (flag ? Green : (flag2 ? Blue : Color.FromArgb(247, 247, 249)));
				Color color2 = ((flag || flag2) ? Color.White : TextTertiary);
				Color color3 = ((flag2 || (completed && i == 2)) ? TextPrimary : TextSecondary);
				Rectangle rectangle = new Rectangle(array2[i] - num, num2 - num, num * 2, num * 2);
				SolidBrush val2 = new SolidBrush(color);
				try
				{
					e.Graphics.FillEllipse((Brush)(object)val2, rectangle);
				}
				finally
				{
					((IDisposable)val2)?.Dispose();
				}
				if (!flag && !flag2)
				{
					Pen val3 = new Pen(Color.FromArgb(218, 218, 223));
					try
					{
						e.Graphics.DrawEllipse(val3, rectangle);
					}
					finally
					{
						((IDisposable)val3)?.Dispose();
					}
				}
				string text = (flag ? "✓" : (i + 1).ToString());
				TextRenderer.DrawText((IDeviceContext)(object)e.Graphics, text, stageFont, rectangle, color2, (TextFormatFlags)268435461);
				Rectangle rectangle2 = new Rectangle(array2[i] - 58, 38, 116, 22);
				TextRenderer.DrawText((IDeviceContext)(object)e.Graphics, array[i], stageFont, rectangle2, color3, (TextFormatFlags)32769);
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

	private sealed class RoundedActionButton : Button
	{
		private bool hovered;

		private bool pressed;

		public RoundedActionButton()
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
			Color color = ((!((Control)this).Enabled) ? Color.FromArgb(242, 242, 247) : (pressed ? Color.FromArgb(224, 224, 229) : (hovered ? Color.FromArgb(235, 235, 240) : Color.FromArgb(247, 247, 249))));
			Color color2 = (((Control)this).Enabled ? TextPrimary : TextTertiary);
			Color color3 = ((((Control)this).Focused && ((Control)this).Enabled) ? Blue : Color.FromArgb(214, 214, 220));
			GraphicsPath val = Drawing.CreateRoundedRectangle(rectangle, ((Control)this).Height / 2);
			try
			{
				SolidBrush val2 = new SolidBrush(color);
				try
				{
					Pen val3 = new Pen(color3, (((Control)this).Focused && ((Control)this).Enabled) ? 1.6f : 1f);
					try
					{
						e.Graphics.FillPath((Brush)(object)val2, val);
						e.Graphics.DrawPath(val3, val);
					}
					finally
					{
						((IDisposable)val3)?.Dispose();
					}
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
			TextRenderer.DrawText((IDeviceContext)(object)e.Graphics, ((Control)this).Text, ((Control)this).Font, rectangle, color2, (TextFormatFlags)32773);
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

		protected override void OnEnabledChanged(EventArgs e)
		{
			if (!((Control)this).Enabled)
			{
				hovered = false;
				pressed = false;
				((Control)this).Cursor = Cursors.Default;
			}
			else
			{
				((Control)this).Cursor = Cursors.Hand;
			}
			((Control)this).Invalidate();
			base.OnEnabledChanged(e);
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

	private const int WindowRadius = 18;

	private static readonly Color WindowBackColor = Color.FromArgb(255, 255, 255);

	private static readonly Color TextPrimary = Color.FromArgb(29, 29, 31);

	private static readonly Color TextSecondary = Color.FromArgb(110, 110, 115);

	private static readonly Color TextTertiary = Color.FromArgb(142, 142, 147);

	private static readonly Color DividerColor = Color.FromArgb(229, 229, 234);

	private static readonly Color Blue = Color.FromArgb(0, 113, 227);

	private static readonly Color Orange = Color.FromArgb(201, 52, 0);

	private static readonly Color Green = Color.FromArgb(36, 138, 61);

	private static readonly Color Red = Color.FromArgb(215, 0, 21);

	private readonly Font titleFont;

	private readonly Font kickerFont;

	private readonly Font messageFont;

	private readonly Font metadataFont;

	private readonly Font buttonFont;

	private readonly StatusGlyphControl statusGlyph;

	private readonly Label lblKicker;

	private readonly Label lblTitle;

	private readonly RoundedMetadataLabel lblElapsed;

	private readonly Label lblMsg;

	private readonly LightweightProgressBar progressBar;

	private readonly Label lblPercent;

	private readonly Label lblEstimate;

	private readonly StageTimelineControl stageTimeline;

	private readonly Label lblHint;

	private readonly RoundedActionButton btnCancel;

	private readonly ToolTip toolTip;

	private int maximum = 100;

	private int currentValue;

	private string taskTitle = "一键排版";

	public bool CancelRequested { get; private set; }

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
	public ProgressForm()
	{
		titleFont = new Font("Microsoft YaHei UI", 14.5f, (FontStyle)1, (GraphicsUnit)3);
		kickerFont = new Font("Microsoft YaHei UI", 8.5f, (FontStyle)1, (GraphicsUnit)3);
		messageFont = new Font("Microsoft YaHei UI", 10f, (FontStyle)1, (GraphicsUnit)3);
		metadataFont = new Font("Microsoft YaHei UI", 8.5f, (FontStyle)0, (GraphicsUnit)3);
		buttonFont = new Font("Microsoft YaHei UI", 9f, (FontStyle)1, (GraphicsUnit)3);
		toolTip = UiTextApplier.CreateToolTip();
		((ContainerControl)this).AutoScaleDimensions = new SizeF(96f, 96f);
		((ContainerControl)this).AutoScaleMode = (AutoScaleMode)2;
		((Control)this).BackColor = WindowBackColor;
		((Form)this).ClientSize = new Size(560, 350);
		((Form)this).FormBorderStyle = (FormBorderStyle)0;
		((Form)this).MaximizeBox = false;
		((Form)this).MinimizeBox = false;
		((Form)this).ShowIcon = false;
		((Form)this).ShowInTaskbar = false;
		((Form)this).StartPosition = (FormStartPosition)1;
		((Control)this).Text = "一键排版进度";
		base.SetStyle((ControlStyles)139282, true);
		StatusGlyphControl statusGlyphControl = new StatusGlyphControl();
		((Control)statusGlyphControl).Location = new Point(28, 25);
		((Control)statusGlyphControl).Size = new Size(48, 48);
		statusGlyphControl.Tone = StatusGlyphTone.Working;
		((Control)statusGlyphControl).TabStop = false;
		statusGlyph = statusGlyphControl;
		lblKicker = new Label
		{
			AutoEllipsis = true,
			BackColor = Color.Transparent,
			Font = kickerFont,
			ForeColor = Blue,
			Location = new Point(90, 24),
			Size = new Size(270, 18),
			Text = "正在准备"
		};
		lblTitle = new Label
		{
			AutoEllipsis = true,
			BackColor = Color.Transparent,
			Font = titleFont,
			ForeColor = TextPrimary,
			Location = new Point(88, 41),
			Size = new Size(300, 32),
			Text = "一键排版"
		};
		RoundedMetadataLabel roundedMetadataLabel = new RoundedMetadataLabel();
		((Control)roundedMetadataLabel).BackColor = Color.Transparent;
		((Control)roundedMetadataLabel).Font = metadataFont;
		((Control)roundedMetadataLabel).ForeColor = TextSecondary;
		((Control)roundedMetadataLabel).Location = new Point(414, 33);
		((Control)roundedMetadataLabel).Size = new Size(118, 30);
		((Control)roundedMetadataLabel).Text = "刚刚开始";
		((Label)roundedMetadataLabel).TextAlign = (ContentAlignment)32;
		lblElapsed = roundedMetadataLabel;
		lblMsg = new Label
		{
			AutoEllipsis = true,
			BackColor = Color.Transparent,
			Font = messageFont,
			ForeColor = TextPrimary,
			Location = new Point(28, 94),
			Size = new Size(504, 25),
			Text = "正在读取文档"
		};
		LightweightProgressBar lightweightProgressBar = new LightweightProgressBar();
		((Control)lightweightProgressBar).Location = new Point(28, 128);
		lightweightProgressBar.Maximum = maximum;
		((Control)lightweightProgressBar).Size = new Size(504, 9);
		((Control)lightweightProgressBar).TabStop = false;
		lightweightProgressBar.Value = 0;
		progressBar = lightweightProgressBar;
		lblPercent = new Label
		{
			BackColor = Color.Transparent,
			Font = metadataFont,
			ForeColor = TextPrimary,
			Location = new Point(28, 147),
			Size = new Size(80, 19),
			Text = "0%"
		};
		lblEstimate = new Label
		{
			BackColor = Color.Transparent,
			Font = metadataFont,
			ForeColor = TextSecondary,
			Location = new Point(330, 147),
			Size = new Size(202, 19),
			Text = "正在估算时间",
			TextAlign = (ContentAlignment)4
		};
		StageTimelineControl stageTimelineControl = new StageTimelineControl();
		((Control)stageTimelineControl).BackColor = Color.Transparent;
		((Control)stageTimelineControl).Location = new Point(28, 182);
		((Control)stageTimelineControl).Size = new Size(504, 64);
		((Control)stageTimelineControl).TabStop = false;
		stageTimeline = stageTimelineControl;
		Panel val = new Panel
		{
			BackColor = DividerColor,
			Location = new Point(28, 264),
			Size = new Size(504, 1),
			TabStop = false
		};
		lblHint = new Label
		{
			AutoEllipsis = true,
			BackColor = Color.Transparent,
			Font = metadataFont,
			ForeColor = TextTertiary,
			Location = new Point(28, 282),
			Size = new Size(360, 43),
			Text = "处理期间请勿关闭 Word；需要停止时可安全取消任务。"
		};
		RoundedActionButton roundedActionButton = new RoundedActionButton();
		((Control)roundedActionButton).AccessibleDescription = "请求安全停止当前一键排版任务";
		((Control)roundedActionButton).AccessibleName = "取消一键排版";
		((Control)roundedActionButton).Font = buttonFont;
		((Control)roundedActionButton).Location = new Point(414, 279);
		((Control)roundedActionButton).Size = new Size(118, 44);
		((Control)roundedActionButton).TabIndex = 0;
		((Control)roundedActionButton).Text = "取消任务";
		btnCancel = roundedActionButton;
		((Control)btnCancel).Click += OnCancelClick;
		((Form)this).CancelButton = (IButtonControl)(object)btnCancel;
		((Control)this).Controls.Add((Control)(object)statusGlyph);
		((Control)this).Controls.Add((Control)(object)lblKicker);
		((Control)this).Controls.Add((Control)(object)lblTitle);
		((Control)this).Controls.Add((Control)(object)lblElapsed);
		((Control)this).Controls.Add((Control)(object)lblMsg);
		((Control)this).Controls.Add((Control)(object)progressBar);
		((Control)this).Controls.Add((Control)(object)lblPercent);
		((Control)this).Controls.Add((Control)(object)lblEstimate);
		((Control)this).Controls.Add((Control)(object)stageTimeline);
		((Control)this).Controls.Add((Control)(object)val);
		((Control)this).Controls.Add((Control)(object)lblHint);
		((Control)this).Controls.Add((Control)(object)btnCancel);
		UiTextApplier.ApplyTooltip(toolTip, (Control)(object)btnCancel, "Progress.CancelFormatting");
		UpdateWindowRegion();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void SetTaskTitle(string taskName)
	{
		if (!((Control)this).InvokeRequired)
		{
			taskTitle = (string.IsNullOrWhiteSpace(taskName) ? "正在处理" : taskName.Trim());
			((Control)this).Text = taskTitle + "进度";
			((Control)btnCancel).AccessibleDescription = "请求安全停止当前" + taskTitle + "任务";
			((Control)btnCancel).AccessibleName = "取消" + taskTitle;
			if (!CancelRequested && currentValue < maximum)
			{
				SetLabelText(lblTitle, taskTitle);
			}
		}
		else
		{
			((Control)this).BeginInvoke((Delegate)new Action<string>(SetTaskTitle), new object[1] { taskName });
		}
	}

	public void SetStep(string step)
	{
		if (!((Control)this).InvokeRequired)
		{
			ApplyMessage(step);
			return;
		}
		((Control)this).BeginInvoke((Delegate)new Action<string>(SetStep), new object[1] { step });
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void SetProgress(int percent, string step)
	{
		if (!((Control)this).InvokeRequired)
		{
			int num = (currentValue = Math.Max(0, Math.Min(maximum, percent)));
			ApplyMessage(step);
			if (progressBar.Value != num)
			{
				progressBar.Value = num;
			}
			int num2 = ((maximum > 0) ? (num * 100 / maximum) : 0);
			SetLabelText(lblPercent, num2 + "%");
			stageTimeline.SetProgress(num2);
			if (CancelRequested)
			{
				SetCancellationState();
			}
			else if (ContainsFailureText(step))
			{
				((Control)lblKicker).ForeColor = Red;
				SetLabelText(lblKicker, "处理失败");
				SetLabelText(lblTitle, taskTitle + "未完成");
				SetLabelText(lblEstimate, "已安全停止");
				statusGlyph.Tone = StatusGlyphTone.Failed;
			}
			else if (num2 >= 100)
			{
				((Control)lblKicker).ForeColor = Green;
				SetLabelText(lblKicker, "处理完成");
				SetLabelText(lblTitle, taskTitle + "已完成");
				SetLabelText(lblEstimate, "已完成");
				statusGlyph.Tone = StatusGlyphTone.Success;
				progressBar.Completed = true;
			}
			else
			{
				SetLabelText(lblTitle, taskTitle);
				SetLabelText(lblEstimate, (num2 <= 2) ? "正在估算时间" : "长文档处理中");
				statusGlyph.Tone = StatusGlyphTone.Working;
				progressBar.Completed = false;
			}
		}
		else
		{
			((Control)this).BeginInvoke((Delegate)new Action<int, string>(SetProgress), new object[2] { percent, step });
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void SetMaximum(int max)
	{
		if (((Control)this).InvokeRequired)
		{
			((Control)this).BeginInvoke((Delegate)new Action<int>(SetMaximum), new object[1] { max });
			return;
		}
		maximum = ((max <= 0) ? 1 : max);
		currentValue = 0;
		progressBar.Maximum = maximum;
		progressBar.Value = 0;
		progressBar.Completed = false;
		stageTimeline.SetProgress(0);
		SetLabelText(lblPercent, "0%");
	}

	public void Increment()
	{
		if (((Control)this).InvokeRequired)
		{
			((Control)this).BeginInvoke((Delegate)new Action(Increment));
		}
		else if (currentValue < maximum)
		{
			SetProgress(currentValue + 1, ((Control)lblMsg).Text);
		}
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);
		e.Graphics.SmoothingMode = (SmoothingMode)4;
		GraphicsPath val = Drawing.CreateRoundedRectangle(new Rectangle(0, 0, ((Form)this).ClientSize.Width - 1, ((Form)this).ClientSize.Height - 1), 18);
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
			titleFont.Dispose();
			kickerFont.Dispose();
			messageFont.Dispose();
			metadataFont.Dispose();
			buttonFont.Dispose();
			((Component)(object)toolTip).Dispose();
		}
		base.Dispose(disposing);
	}

	private void OnCancelClick(object sender, EventArgs e)
	{
		if (!CancelRequested)
		{
			CancelRequested = true;
			SetCancellationState();
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void SetCancellationState()
	{
		((Control)lblKicker).ForeColor = Orange;
		SetLabelText(lblKicker, "正在取消");
		SetLabelText(lblTitle, "正在安全停止任务");
		SetLabelText(lblEstimate, "请稍候");
		SetLabelText(lblHint, "正在完成当前步骤并恢复安全状态，请勿关闭 Word。");
		statusGlyph.Tone = StatusGlyphTone.Cancelling;
		((Control)btnCancel).Enabled = false;
		((Control)btnCancel).Text = "取消中…";
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void ApplyMessage(string message)
	{
		string text = (string.IsNullOrWhiteSpace(message) ? "请稍候" : message.Trim());
		string[] array = text.Split(new char[1] { '｜' }, StringSplitOptions.RemoveEmptyEntries);
		string text2 = ((array.Length != 0) ? array[0].Trim() : text);
		string value = null;
		string text3 = text2;
		int num = text2.IndexOf('：');
		if (num < 0)
		{
			num = text2.IndexOf(':');
		}
		if (num > 0 && num < text2.Length - 1)
		{
			value = text2.Substring(0, num).Trim();
			text3 = text2.Substring(num + 1).Trim();
		}
		string text4 = null;
		for (int i = 1; i < array.Length; i++)
		{
			string text5 = array[i].Trim();
			if (!text5.StartsWith("已用时", StringComparison.Ordinal))
			{
				if (!string.IsNullOrWhiteSpace(text5))
				{
					text3 = text3 + " · " + text5;
				}
			}
			else
			{
				text4 = text5;
			}
		}
		if (!CancelRequested && !string.IsNullOrWhiteSpace(value))
		{
			((Control)lblKicker).ForeColor = Blue;
			SetLabelText(lblKicker, value);
		}
		SetLabelText(lblMsg, string.IsNullOrWhiteSpace(text3) ? "请稍候" : text3);
		SetLabelText((Label)(object)lblElapsed, string.IsNullOrWhiteSpace(text4) ? "刚刚开始" : text4);
	}

	private void UpdateWindowRegion()
	{
		if (((Form)this).ClientSize.Width <= 0 || ((Form)this).ClientSize.Height <= 0)
		{
			return;
		}
		GraphicsPath val = Drawing.CreateRoundedRectangle(new Rectangle(0, 0, ((Form)this).ClientSize.Width, ((Form)this).ClientSize.Height), 18);
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

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool ContainsFailureText(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		if (text.IndexOf("失败", StringComparison.Ordinal) < 0)
		{
			return text.IndexOf("未完成", StringComparison.Ordinal) >= 0;
		}
		return true;
	}

	private static void SetLabelText(Label label, string value)
	{
		if (label != null && !((Control)label).IsDisposed)
		{
			string text = value ?? string.Empty;
			if (!string.Equals(((Control)label).Text, text, StringComparison.Ordinal))
			{
				((Control)label).Text = text;
			}
		}
	}
}
