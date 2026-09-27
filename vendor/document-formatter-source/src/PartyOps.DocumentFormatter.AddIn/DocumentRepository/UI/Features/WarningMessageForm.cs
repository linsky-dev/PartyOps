using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using DocumentRepository.Services.Logging;

namespace DocumentRepository.UI.Features;

internal sealed class WarningMessageForm : Form
{
	private static readonly HashSet<WarningMessageForm> ActiveForms = new HashSet<WarningMessageForm>();

	private readonly Timer closeTimer;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private WarningMessageForm(string caption, string message, int timeoutMilliseconds)
	{
		((Control)this).Text = (string.IsNullOrWhiteSpace(caption) ? "提示" : caption.Trim());
		((Form)this).StartPosition = (FormStartPosition)1;
		((Form)this).FormBorderStyle = (FormBorderStyle)3;
		((Form)this).MaximizeBox = false;
		((Form)this).MinimizeBox = false;
		((Form)this).ShowIcon = false;
		((Form)this).ShowInTaskbar = false;
		((Control)this).Font = UiFonts.Body;
		Size size = TextRenderer.MeasureText(message ?? string.Empty, UiFonts.Body, new Size(400, 0), (TextFormatFlags)268435472);
		((Form)this).ClientSize = new Size(480, Math.Max(164, size.Height + 122));
		RoundedPanel roundedPanel = new RoundedPanel();
		((Control)roundedPanel).Location = new Point(24, 22);
		((Control)roundedPanel).Size = new Size(36, 36);
		roundedPanel.Radius = 18;
		roundedPanel.BorderWidth = 0f;
		((Control)roundedPanel).BackColor = AppleUiColors.WarningSoft;
		RoundedPanel roundedPanel2 = roundedPanel;
		((Control)roundedPanel2).Controls.Add((Control)new Label
		{
			Dock = (DockStyle)5,
			Text = "!",
			Font = UiFonts.SubHeading,
			ForeColor = AppleUiColors.Warning,
			TextAlign = (ContentAlignment)32,
			BackColor = Color.Transparent
		});
		((Control)this).Controls.Add((Control)(object)roundedPanel2);
		((Control)this).Controls.Add((Control)new Label
		{
			Text = ((Control)this).Text,
			AutoEllipsis = true,
			Location = new Point(76, 20),
			Size = new Size(372, 28),
			Font = UiFonts.Heading,
			ForeColor = AppleUiColors.TextPrimary
		});
		((Control)this).Controls.Add((Control)new Label
		{
			Text = (message ?? string.Empty),
			Location = new Point(76, 54),
			Size = new Size(372, Math.Max(42, size.Height + 6)),
			Font = UiFonts.Body,
			ForeColor = AppleUiColors.TextSecondary
		});
		((Control)this).Controls.Add((Control)new Label
		{
			Text = "此提示将自动关闭",
			AutoEllipsis = true,
			Location = new Point(24, ((Form)this).ClientSize.Height - 49),
			Size = new Size(220, 22),
			Font = UiFonts.Small,
			ForeColor = AppleUiColors.TextTertiary
		});
		StyledButton styledButton = new StyledButton();
		((Control)styledButton).Text = "知道了";
		((Control)styledButton).Location = new Point(((Form)this).ClientSize.Width - 24 - 88, ((Form)this).ClientSize.Height - 54);
		((Control)styledButton).Size = new Size(88, 36);
		StyledButton styledButton2 = styledButton;
		((Control)styledButton2).Click += delegate
		{
			((Form)this).Close();
		};
		((Control)this).Controls.Add((Control)(object)styledButton2);
		((Form)this).AcceptButton = (IButtonControl)(object)styledButton2;
		((Form)this).CancelButton = (IButtonControl)(object)styledButton2;
		closeTimer = new Timer
		{
			Interval = Math.Max(1500, timeoutMilliseconds)
		};
		closeTimer.Tick += delegate
		{
			closeTimer.Stop();
			((Form)this).Close();
		};
		((Form)this).Shown += delegate
		{
			closeTimer.Start();
		};
		AppleFormStyler.Apply((Form)(object)this);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ShowMessage(string caption, string message, int timeoutMilliseconds)
	{
		WarningMessageForm form = null;
		try
		{
			form = new WarningMessageForm(caption, message, timeoutMilliseconds);
			ActiveForms.Add(form);
			((Form)form).FormClosed += (FormClosedEventHandler)delegate
			{
				ActiveForms.Remove(form);
				((Component)(object)form).Dispose();
			};
			((Control)form).Show();
		}
		catch (Exception ex)
		{
			if (form != null)
			{
				ActiveForms.Remove(form);
				((Component)(object)form).Dispose();
			}
			LogService.Warn("WarningMessageForm.ShowMessage, exception=" + ex.GetType().Name);
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			((Component)(object)closeTimer).Dispose();
		}
		base.Dispose(disposing);
	}
}
