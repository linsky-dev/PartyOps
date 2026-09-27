using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using DocumentRepository.Models.Recovery;
using DocumentRepository.Services.Configuration;
using DocumentRepository.Services.Recovery;
using DocumentRepository.Services.UiText;

namespace DocumentRepository.UI.Features;

internal sealed class RecoveryAssistantForm : Form
{
	private readonly string recoveryDirectory;

	private readonly string copyFilePath;

	private readonly StyledButton copyPathButton;

	private readonly Timer copyFeedbackTimer;

	private readonly ToolTip toolTip;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private RecoveryAssistantForm(string caption, string message, string recoveryId)
	{
		string text = null;
		if (!string.IsNullOrWhiteSpace(recoveryId))
		{
			try
			{
				recoveryDirectory = ApplicationDataPaths.RecoveryDirectory(recoveryId);
				RecoveryManifest recoveryManifest = RecoveryManifestStore.TryRead(recoveryDirectory);
				if (recoveryManifest == null)
				{
					recoveryDirectory = ApplicationDataPaths.RecoveryFallbackDirectory(recoveryId);
					recoveryManifest = RecoveryManifestStore.TryRead(recoveryDirectory);
				}
				if (recoveryManifest != null)
				{
					text = recoveryManifest.SourceFullPath;
					if (!string.IsNullOrWhiteSpace(recoveryManifest.CopyFileName))
					{
						string text2 = Path.Combine(recoveryDirectory, recoveryManifest.CopyFileName);
						if (File.Exists(text2) && new FileInfo(text2).Length == recoveryManifest.SourceLength)
						{
							copyFilePath = text2;
						}
					}
				}
			}
			catch
			{
				recoveryDirectory = null;
				copyFilePath = null;
			}
		}
		((Control)this).Text = "需要人工恢复文档";
		((Form)this).StartPosition = (FormStartPosition)1;
		((Form)this).FormBorderStyle = (FormBorderStyle)3;
		((Form)this).MaximizeBox = false;
		((Form)this).MinimizeBox = false;
		((Form)this).ShowIcon = false;
		((Form)this).ShowInTaskbar = false;
		((Control)this).Font = UiFonts.Body;
		string text3 = (string.IsNullOrWhiteSpace(caption) ? "文档处理" : caption.Trim());
		Size size = TextRenderer.MeasureText(message ?? string.Empty, UiFonts.Body, new Size(400, 0), (TextFormatFlags)268435472);
		bool num = !string.IsNullOrWhiteSpace(text);
		bool flag = !string.IsNullOrWhiteSpace(copyFilePath);
		int num2 = (num ? 40 : 0) + (flag ? 40 : 0);
		((Form)this).ClientSize = new Size(520, Math.Max(210, size.Height + 130 + num2 + 64));
		RoundedPanel roundedPanel = new RoundedPanel();
		((Control)roundedPanel).Location = new Point(24, 22);
		((Control)roundedPanel).Size = new Size(36, 36);
		roundedPanel.Radius = 18;
		roundedPanel.BorderWidth = 0f;
		((Control)roundedPanel).BackColor = AppleUiColors.DangerSoft;
		RoundedPanel roundedPanel2 = roundedPanel;
		((Control)roundedPanel2).Controls.Add((Control)new Label
		{
			Dock = (DockStyle)5,
			Text = "×",
			Font = UiFonts.SubHeading,
			ForeColor = AppleUiColors.Danger,
			TextAlign = (ContentAlignment)32,
			BackColor = Color.Transparent
		});
		((Control)this).Controls.Add((Control)(object)roundedPanel2);
		((Control)this).Controls.Add((Control)new Label
		{
			Text = text3 + "未能安全完成",
			AutoEllipsis = true,
			Location = new Point(76, 20),
			Size = new Size(400, 28),
			Font = UiFonts.Heading,
			ForeColor = AppleUiColors.TextPrimary
		});
		((Control)this).Controls.Add((Control)new Label
		{
			Text = (message ?? string.Empty),
			Location = new Point(76, 54),
			Size = new Size(412, Math.Max(42, size.Height + 6)),
			Font = UiFonts.Body,
			ForeColor = AppleUiColors.TextSecondary
		});
		int num3 = 54 + Math.Max(42, size.Height + 6) + 10;
		if (num)
		{
			((Control)this).Controls.Add((Control)new Label
			{
				Text = "原始文件：" + text,
				AutoEllipsis = true,
				Location = new Point(24, num3),
				Size = new Size(472, 34),
				Font = UiFonts.Small,
				ForeColor = AppleUiColors.TextPrimary
			});
			num3 += 40;
		}
		if (flag)
		{
			((Control)this).Controls.Add((Control)new Label
			{
				Text = "恢复副本：" + copyFilePath,
				AutoEllipsis = true,
				Location = new Point(24, num3),
				Size = new Size(472, 34),
				Font = UiFonts.Small,
				ForeColor = AppleUiColors.TextPrimary
			});
			num3 += 40;
		}
		int y = ((Form)this).ClientSize.Height - 56;
		StyledButton styledButton = new StyledButton();
		((Control)styledButton).Text = "关闭";
		((Control)styledButton).Location = new Point(((Form)this).ClientSize.Width - 24 - 88, y);
		((Control)styledButton).Size = new Size(88, 36);
		((Button)styledButton).DialogResult = (DialogResult)2;
		StyledButton styledButton2 = styledButton;
		((Control)this).Controls.Add((Control)(object)styledButton2);
		((Form)this).AcceptButton = (IButtonControl)(object)styledButton2;
		((Form)this).CancelButton = (IButtonControl)(object)styledButton2;
		StyledButton styledButton3 = new StyledButton();
		((Control)styledButton3).Text = "复制副本路径";
		((Control)styledButton3).Location = new Point(((Form)this).ClientSize.Width - 24 - 88 - 10 - 118, y);
		((Control)styledButton3).Size = new Size(118, 36);
		((Control)styledButton3).Enabled = flag;
		copyPathButton = styledButton3;
		((Control)copyPathButton).Click += CopyPathButton_Click;
		((Control)this).Controls.Add((Control)(object)copyPathButton);
		StyledButton styledButton4 = new StyledButton();
		((Control)styledButton4).Text = "打开副本文件夹";
		((Control)styledButton4).Location = new Point(((Form)this).ClientSize.Width - 24 - 88 - 10 - 118 - 10 - 128, y);
		((Control)styledButton4).Size = new Size(128, 36);
		((Control)styledButton4).Enabled = flag;
		StyledButton styledButton5 = styledButton4;
		((Control)styledButton5).Click += delegate
		{
			bool flag2 = false;
			if (!string.IsNullOrWhiteSpace(copyFilePath) && File.Exists(copyFilePath))
			{
				flag2 = SafeProcessLauncher.RevealSensitiveFileInExplorer(copyFilePath);
			}
			if (!flag2 && !string.IsNullOrWhiteSpace(recoveryDirectory))
			{
				SafeProcessLauncher.OpenSensitiveFileOrDirectory(recoveryDirectory);
			}
		};
		((Control)this).Controls.Add((Control)(object)styledButton5);
		copyFeedbackTimer = new Timer
		{
			Interval = 1600
		};
		copyFeedbackTimer.Tick += [MethodImpl(MethodImplOptions.NoInlining)] (object sender, EventArgs args) =>
		{
			copyFeedbackTimer.Stop();
			((Control)copyPathButton).Text = "复制副本路径";
		};
		toolTip = UiTextApplier.CreateToolTip();
		UiTextApplier.ApplyTooltip(toolTip, (Control)(object)styledButton2, "Dialog.Close");
		UiTextApplier.ApplyTooltip(toolTip, (Control)(object)copyPathButton, "Recovery.CopyPath");
		UiTextApplier.ApplyTooltip(toolTip, (Control)(object)styledButton5, "Recovery.OpenFolder");
		AppleFormStyler.Apply((Form)(object)this, toolTip);
	}

	public static void ShowAssistant(string caption, string message, string recoveryId)
	{
		RecoveryAssistantForm recoveryAssistantForm = new RecoveryAssistantForm(caption, message, recoveryId);
		try
		{
			((Form)recoveryAssistantForm).ShowDialog();
		}
		finally
		{
			((IDisposable)(object)recoveryAssistantForm)?.Dispose();
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void CopyPathButton_Click(object sender, EventArgs e)
	{
		if (string.IsNullOrWhiteSpace(copyFilePath))
		{
			return;
		}
		try
		{
			Clipboard.SetText(copyFilePath);
			((Control)copyPathButton).Text = "已复制 ✓";
			copyFeedbackTimer.Stop();
			copyFeedbackTimer.Start();
		}
		catch
		{
			((Control)copyPathButton).Text = "复制失败";
			copyFeedbackTimer.Stop();
			copyFeedbackTimer.Start();
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			((Component)(object)copyFeedbackTimer).Dispose();
			((Component)(object)toolTip).Dispose();
		}
		base.Dispose(disposing);
	}
}
