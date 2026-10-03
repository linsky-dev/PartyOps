using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace PartyOps.DocumentFormatter.AddInRepair;

internal sealed class MainForm : Form
{
	private readonly AddInRepairService _service = new AddInRepairService();

	private readonly RichTextBox _resultBox;

	private readonly Label _statusLabel;

	private readonly Button _repairButton;

	private DiagnosticReport _lastReport;

	public MainForm()
	{
		((Control)this).Text = "partyops公文排版助手 - 加载项检测与修复";
		((Form)this).StartPosition = (FormStartPosition)1;
		((Control)this).MinimumSize = new Size(780, 540);
		((Form)this).Size = new Size(900, 650);
		((Control)this).Font = new Font("Microsoft YaHei UI", 9f, (FontStyle)0, (GraphicsUnit)3);
		Label val = new Label
		{
			AutoSize = true,
			Font = new Font(((Control)this).Font.FontFamily, 16f, (FontStyle)1),
			ForeColor = Color.FromArgb(24, 93, 165),
			Location = new Point(24, 20),
			Text = "加载项检测与修复"
		};
		Label val2 = new Label
		{
			AutoSize = false,
			Location = new Point(27, 60),
			Size = new Size(830, 42),
			Text = "本工具只检查并修复partyops公文排版助手自身，不会清空 Word 的全部禁用记录，也不会修改其他加载项。"
		};
		_statusLabel = new Label
		{
			AutoSize = false,
			Location = new Point(27, 105),
			Size = new Size(830, 28),
			Font = new Font(((Control)this).Font, (FontStyle)1)
		};
		_resultBox = new RichTextBox
		{
			Anchor = (AnchorStyles)15,
			BackColor = Color.White,
			BorderStyle = (BorderStyle)1,
			Font = new Font("Consolas", 9f),
			Location = new Point(27, 137),
			ReadOnly = true,
			Size = new Size(830, 420),
			WordWrap = true
		};
		Button val3 = CreateButton("重新检测", 27, 575, 105);
		((Control)val3).Anchor = (AnchorStyles)6;
		((Control)val3).Click += delegate
		{
			RunDiagnosis();
		};
		_repairButton = CreateButton("修复本插件", 142, 575, 115);
		((Control)_repairButton).Anchor = (AnchorStyles)6;
		((Control)_repairButton).BackColor = Color.FromArgb(32, 111, 203);
		((Control)_repairButton).ForeColor = Color.White;
		((ButtonBase)_repairButton).FlatStyle = (FlatStyle)0;
		((Control)_repairButton).Click += delegate
		{
			RunRepair();
		};
		Button val4 = CreateButton("复制结果", 267, 575, 105);
		((Control)val4).Anchor = (AnchorStyles)6;
		((Control)val4).Click += delegate
		{
			if (!string.IsNullOrWhiteSpace(((Control)_resultBox).Text))
			{
				Clipboard.SetText(((Control)_resultBox).Text);
			}
		};
		Button val5 = CreateButton("打开备份目录", 382, 575, 125);
		((Control)val5).Anchor = (AnchorStyles)6;
		((Control)val5).Click += delegate
		{
			OpenBackupDirectory();
		};
		Button val6 = CreateButton("开启加载日志", 517, 575, 125);
		((Control)val6).Anchor = (AnchorStyles)6;
		((Control)val6).Click += delegate
		{
			EnableVstoDiagnostics();
		};
		Button val7 = CreateButton("关闭", 752, 575, 105);
		((Control)val7).Anchor = (AnchorStyles)10;
		((Control)val7).Click += delegate
		{
			((Form)this).Close();
		};
		((Control)this).Controls.Add((Control)(object)val);
		((Control)this).Controls.Add((Control)(object)val2);
		((Control)this).Controls.Add((Control)(object)_statusLabel);
		((Control)this).Controls.Add((Control)(object)_resultBox);
		((Control)this).Controls.Add((Control)(object)val3);
		((Control)this).Controls.Add((Control)(object)_repairButton);
		((Control)this).Controls.Add((Control)(object)val4);
		((Control)this).Controls.Add((Control)(object)val5);
		((Control)this).Controls.Add((Control)(object)val6);
		((Control)this).Controls.Add((Control)(object)val7);
		((Form)this).Shown += delegate
		{
			RunDiagnosis();
		};
	}

	private static Button CreateButton(string text, int left, int top, int width)
	{
		return new Button
		{
			Location = new Point(left, top),
			Size = new Size(width, 34),
			Text = text,
			UseVisualStyleBackColor = true
		};
	}

	private void RunDiagnosis()
	{
		try
		{
			((Control)this).Cursor = Cursors.WaitCursor;
			_lastReport = _service.Diagnose();
			ShowReport(_lastReport);
		}
		catch (Exception ex)
		{
			ShowFailure("检测失败：" + ex.Message);
		}
		finally
		{
			((Control)this).Cursor = Cursors.Default;
		}
	}

	private void RunRepair()
	{
		if ((int)MessageBox.Show((IWin32Window)(object)this, "修复前会备份本插件的注册信息。修复只影响partyops公文排版助手，是否继续？", "确认修复", (MessageBoxButtons)4, (MessageBoxIcon)32) != 6)
		{
			return;
		}
		try
		{
			((Control)this).Cursor = Cursors.WaitCursor;
			_lastReport = _service.Repair();
			ShowReport(_lastReport);
			MessageBox.Show((IWin32Window)(object)this, "修复完成。请完全退出 Word/WPS（包括后台进程）后重新打开。", "partyops公文排版助手", (MessageBoxButtons)0, (MessageBoxIcon)64);
		}
		catch (Exception ex)
		{
			ShowFailure("修复失败：" + ex.Message);
		}
		finally
		{
			((Control)this).Cursor = Cursors.Default;
		}
	}

	private void ShowReport(DiagnosticReport report)
	{
		((Control)_resultBox).Text = report.ToDisplayText();
		((Control)_statusLabel).Text = (report.HasErrors ? "发现需要处理的问题" : (report.HasWarnings ? "发现需要进一步核实的提醒" : "加载项基础状态正常"));
		((Control)_statusLabel).ForeColor = (report.HasErrors ? Color.Firebrick : (report.HasWarnings ? Color.DarkOrange : Color.ForestGreen));
		((Control)_repairButton).Enabled = report.HasRepairableProblems;
	}

	private void EnableVstoDiagnostics()
	{
		if ((int)MessageBox.Show((IWin32Window)(object)this, "将为当前 Windows 用户开启微软 VSTO 启动错误日志和错误提示。完全退出 Word/WPS 后重新打开并尝试启用插件，是否继续？", "开启加载日志", (MessageBoxButtons)4, (MessageBoxIcon)32) != 6)
		{
			return;
		}
		try
		{
			_service.EnableVstoDiagnostics();
			RunDiagnosis();
			MessageBox.Show((IWin32Window)(object)this, "加载日志已开启。请完全退出 Word/WPS（包括后台进程），重新打开 Word 并尝试启用插件。失败后再次运行本工具并复制检测结果，同时提供安装目录中的 PartyOps.DocumentFormatter.AddIn.vsto.log；若该目录没有日志，请到系统临时目录查找。", "partyops公文排版助手", (MessageBoxButtons)0, (MessageBoxIcon)64);
		}
		catch (Exception ex)
		{
			ShowFailure("无法开启 VSTO 加载日志：" + ex.Message);
		}
	}

	private void ShowFailure(string message)
	{
		((Control)_statusLabel).Text = message;
		((Control)_statusLabel).ForeColor = Color.Firebrick;
		MessageBox.Show((IWin32Window)(object)this, message, "partyops公文排版助手", (MessageBoxButtons)0, (MessageBoxIcon)16);
	}

	private void OpenBackupDirectory()
	{
		try
		{
			Directory.CreateDirectory(_service.BackupDirectory);
			Process.Start("explorer.exe", _service.BackupDirectory);
		}
		catch (Exception ex)
		{
			ShowFailure("无法打开备份目录：" + ex.Message);
		}
	}
}
