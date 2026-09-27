using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using DocumentRepository.Services.UiText;

namespace DocumentRepository;

public class ConvertProgressForm : Form
{
	private readonly Label lblTitle = new Label();

	private readonly Label lblStep = new Label();

	private readonly ProgressBar progressBar = new ProgressBar();

	private readonly Button btnCancel = new Button();

	private readonly ToolTip toolTip = UiTextApplier.CreateToolTip();

	public bool CancelRequested { get; private set; }

	[MethodImpl(MethodImplOptions.NoInlining)]
	public ConvertProgressForm(string titleText = "正在转换文档...")
	{
		((Control)this).Text = "一键转换";
		((Form)this).StartPosition = (FormStartPosition)1;
		((Form)this).Size = new Size(460, 205);
		((Form)this).FormBorderStyle = (FormBorderStyle)3;
		((Form)this).MinimizeBox = false;
		((Form)this).MaximizeBox = false;
		((Form)this).ControlBox = false;
		((Control)this).Font = new Font("Microsoft YaHei UI", 9f);
		((Control)lblTitle).Text = titleText;
		((Control)lblTitle).Left = 22;
		((Control)lblTitle).Top = 18;
		((Control)lblTitle).Width = 400;
		((Control)lblTitle).Height = 28;
		((Control)lblTitle).Font = new Font(((Control)this).Font.FontFamily, 11f, (FontStyle)1);
		((Control)lblTitle).ForeColor = Color.FromArgb(0, 92, 170);
		((Control)this).Controls.Add((Control)(object)lblTitle);
		((Control)lblStep).Left = 22;
		((Control)lblStep).Top = 56;
		((Control)lblStep).Width = 400;
		((Control)lblStep).Height = 24;
		((Control)lblStep).ForeColor = Color.FromArgb(75, 91, 110);
		((Control)this).Controls.Add((Control)(object)lblStep);
		((Control)progressBar).Left = 22;
		((Control)progressBar).Top = 88;
		((Control)progressBar).Width = 400;
		((Control)progressBar).Height = 18;
		progressBar.Minimum = 0;
		progressBar.Maximum = 100;
		((Control)this).Controls.Add((Control)(object)progressBar);
		((Control)btnCancel).Text = "取消";
		((Control)btnCancel).Left = 332;
		((Control)btnCancel).Top = 120;
		((Control)btnCancel).Width = 90;
		((Control)btnCancel).Height = 30;
		((Control)btnCancel).Click += [MethodImpl(MethodImplOptions.NoInlining)] (object sender, EventArgs args) =>
		{
			CancelRequested = true;
			((Control)btnCancel).Enabled = false;
			((Control)btnCancel).Text = "正在取消...";
		};
		((Control)this).Controls.Add((Control)(object)btnCancel);
		UiTextApplier.ApplyTooltip(toolTip, (Control)(object)btnCancel, "Progress.CancelConversion");
		((Component)this).Disposed += delegate
		{
			((Component)(object)toolTip).Dispose();
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void SetTaskTitle(string taskName)
	{
		if (((Control)this).InvokeRequired)
		{
			((Control)this).BeginInvoke((Delegate)new Action<string>(SetTaskTitle), new object[1] { taskName });
		}
		else
		{
			string text = (((Control)this).Text = (string.IsNullOrWhiteSpace(taskName) ? "一键转换" : taskName.Trim()));
			((Control)lblTitle).Text = text;
			((Control)btnCancel).AccessibleName = "取消" + text;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void UpdateProgress(int current, int total, string message)
	{
		if (((Control)this).InvokeRequired)
		{
			((Control)this).BeginInvoke((Delegate)(Action)delegate
			{
				UpdateProgress(current, total, message);
			});
			return;
		}
		total = Math.Max(1, total);
		current = Math.Max(0, Math.Min(current, total));
		((Control)lblStep).Text = (string.IsNullOrWhiteSpace(message) ? "正在处理..." : message);
		progressBar.Value = Math.Max(0, Math.Min(100, current * 100 / total));
		Application.DoEvents();
	}
}
