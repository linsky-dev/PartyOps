using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using DocumentRepository.Services.Logging;

namespace DocumentRepository.UI.Features;

internal sealed class TimedMessageForm : Form
{
	private static readonly HashSet<TimedMessageForm> ActiveForms = new HashSet<TimedMessageForm>();

	[MethodImpl(MethodImplOptions.NoInlining)]
	private TimedMessageForm(string message, string caption, int timeoutMilliseconds)
	{
		TimedMessageForm timedMessageForm = this;
		((Control)this).Text = (string.IsNullOrWhiteSpace(caption) ? "提示" : caption);
		((Form)this).Size = new Size(300, 105);
		((Form)this).StartPosition = (FormStartPosition)1;
		((Form)this).FormBorderStyle = (FormBorderStyle)3;
		((Form)this).MaximizeBox = false;
		((Form)this).MinimizeBox = false;
		((Form)this).ShowInTaskbar = false;
		((Form)this).ShowIcon = false;
		((Control)this).Font = UiFonts.Body;
		((Control)this).Controls.Add((Control)new Label
		{
			Text = (message ?? string.Empty),
			Dock = (DockStyle)5,
			TextAlign = (ContentAlignment)32,
			Font = UiFonts.Body,
			ForeColor = AppleUiColors.TextPrimary,
			BackColor = AppleUiColors.Surface,
			Padding = new Padding(12)
		});
		Timer timer = new Timer
		{
			Interval = Math.Max(500, timeoutMilliseconds)
		};
		timer.Tick += delegate
		{
			timer.Stop();
			((Form)timedMessageForm).Close();
		};
		((Form)this).FormClosed += (FormClosedEventHandler)delegate
		{
			((Component)(object)timer).Dispose();
		};
		((Form)this).Shown += delegate
		{
			timer.Start();
		};
		AppleFormStyler.Apply((Form)(object)this);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ShowMessage(string message, string caption, int timeoutMilliseconds)
	{
		TimedMessageForm form = null;
		try
		{
			form = new TimedMessageForm(message, caption, timeoutMilliseconds);
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
			LogService.Warn("TimedMessageForm.ShowMessage, exception=" + ex.GetType().Name);
		}
	}
}
