using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using DocumentRepository.Services.UiText;

namespace DocumentRepository;

internal sealed class AppleTextInputDialog : Form
{
	private readonly TextBox input = new TextBox();

	private readonly AppleAlertLabel validation = new AppleAlertLabel();

	[MethodImpl(MethodImplOptions.NoInlining)]
	private AppleTextInputDialog(string title, string prompt, string initialValue)
	{
		((Control)this).Text = title;
		((Form)this).StartPosition = (FormStartPosition)4;
		((Form)this).FormBorderStyle = (FormBorderStyle)3;
		((Form)this).MaximizeBox = false;
		((Form)this).MinimizeBox = false;
		((Form)this).ShowInTaskbar = false;
		((Form)this).ClientSize = new Size(430, 196);
		((Control)this).Controls.Add((Control)new Label
		{
			Text = title,
			Location = new Point(24, 18),
			Size = new Size(380, 28),
			Font = UiFonts.Heading,
			ForeColor = AppleUiColors.TextPrimary
		});
		((Control)this).Controls.Add((Control)new Label
		{
			Text = prompt,
			Location = new Point(24, 52),
			Size = new Size(380, 22),
			Font = UiFonts.Caption,
			ForeColor = AppleUiColors.TextSecondary
		});
		((Control)input).Location = new Point(24, 82);
		((Control)input).Size = new Size(380, 28);
		((TextBoxBase)input).MaxLength = 24;
		((Control)input).Text = initialValue ?? string.Empty;
		((TextBoxBase)input).SelectAll();
		((Control)this).Controls.Add((Control)(object)input);
		((Control)validation).Location = new Point(24, 116);
		((Control)validation).Size = new Size(232, 34);
		((Control)validation).Padding = new Padding(10, 4, 10, 4);
		((Control)validation).BackColor = AppleUiColors.DangerSoft;
		((Control)validation).ForeColor = AppleUiColors.Danger;
		((Control)validation).Font = UiFonts.Small;
		((Control)validation).Visible = false;
		((Control)this).Controls.Add((Control)(object)validation);
		StyledButton styledButton = new StyledButton();
		((Control)styledButton).Text = "确定";
		((Control)styledButton).Location = new Point(260, 128);
		((Control)styledButton).Size = new Size(68, 38);
		StyledButton styledButton2 = styledButton;
		StyledButton styledButton3 = new StyledButton();
		((Control)styledButton3).Text = "取消";
		((Control)styledButton3).Location = new Point(340, 128);
		((Control)styledButton3).Size = new Size(68, 38);
		((Button)styledButton3).DialogResult = (DialogResult)2;
		StyledButton styledButton4 = styledButton3;
		((Control)styledButton2).Click += [MethodImpl(MethodImplOptions.NoInlining)] (object s, EventArgs e) =>
		{
			if (string.IsNullOrWhiteSpace(((Control)input).Text))
			{
				((Control)validation).Text = "名称不能为空。";
				((Control)validation).Visible = true;
				((Control)input).Focus();
			}
			else
			{
				((Form)this).DialogResult = (DialogResult)1;
			}
		};
		((Control)this).Controls.Add((Control)(object)styledButton2);
		((Control)this).Controls.Add((Control)(object)styledButton4);
		((Form)this).AcceptButton = (IButtonControl)(object)styledButton2;
		((Form)this).CancelButton = (IButtonControl)(object)styledButton4;
		ToolTip toolTip = UiTextApplier.CreateToolTip();
		toolTip.SetToolTip((Control)(object)input, "输入用于模板列表显示的名称，最多 24 个字符。系统默认模板不可改名。");
		toolTip.SetToolTip((Control)(object)styledButton2, "保存名称并返回模板设置页面。");
		toolTip.SetToolTip((Control)(object)styledButton4, "放弃本次名称修改。");
		AppleFormStyler.Apply((Form)(object)this, toolTip);
		((Component)this).Disposed += delegate
		{
			((Component)(object)toolTip).Dispose();
		};
	}

	public static bool TryShow(IWin32Window owner, string title, string prompt, string initialValue, out string value)
	{
		AppleTextInputDialog appleTextInputDialog = new AppleTextInputDialog(title, prompt, initialValue);
		try
		{
			bool flag = ((owner == null) ? ((int)((Form)appleTextInputDialog).ShowDialog() == 1) : ((int)((Form)appleTextInputDialog).ShowDialog(owner) == 1));
			value = (flag ? ((Control)appleTextInputDialog.input).Text.Trim() : null);
			return flag;
		}
		finally
		{
			((IDisposable)(object)appleTextInputDialog)?.Dispose();
		}
	}
}
