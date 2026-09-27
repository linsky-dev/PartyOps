using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using System.Windows.Forms.Layout;

namespace DocumentRepository;

internal sealed class AppleMessageDialog : Form
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	private AppleMessageDialog(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
	{
		((Control)this).Text = caption;
		((Form)this).StartPosition = (FormStartPosition)4;
		((Form)this).FormBorderStyle = (FormBorderStyle)3;
		((Form)this).MaximizeBox = false;
		((Form)this).MinimizeBox = false;
		((Form)this).ShowInTaskbar = false;
		((Control)this).Font = UiFonts.Body;
		Size size = TextRenderer.MeasureText(text ?? string.Empty, UiFonts.Body, new Size(400, 0), (TextFormatFlags)268435472);
		((Form)this).ClientSize = new Size(480, Math.Max(176, size.Height + 126));
		string text2 = (((int)icon == 16) ? "×" : (((int)icon == 48) ? "!" : (((int)icon == 32) ? "?" : "i")));
		Color foreColor = (((int)icon == 16) ? AppleUiColors.Danger : (((int)icon == 48) ? AppleUiColors.Warning : AppleUiColors.Accent));
		RoundedPanel roundedPanel = new RoundedPanel();
		((Control)roundedPanel).Location = new Point(24, 24);
		((Control)roundedPanel).Size = new Size(36, 36);
		roundedPanel.Radius = 18;
		roundedPanel.BorderWidth = 0f;
		((Control)roundedPanel).BackColor = (((int)icon == 16) ? AppleUiColors.DangerSoft : (((int)icon == 48) ? AppleUiColors.WarningSoft : AppleUiColors.AccentSoft));
		RoundedPanel roundedPanel2 = roundedPanel;
		((Control)roundedPanel2).Controls.Add((Control)new Label
		{
			Dock = (DockStyle)5,
			Text = text2,
			Font = UiFonts.SubHeading,
			ForeColor = foreColor,
			TextAlign = (ContentAlignment)32,
			BackColor = Color.Transparent
		});
		((Control)this).Controls.Add((Control)(object)roundedPanel2);
		((Control)this).Controls.Add((Control)new Label
		{
			Text = (string.IsNullOrWhiteSpace(caption) ? "提示" : caption),
			Location = new Point(76, 22),
			Size = new Size(372, 30),
			Font = UiFonts.Heading,
			ForeColor = AppleUiColors.TextPrimary
		});
		((Control)this).Controls.Add((Control)new Label
		{
			Text = (text ?? string.Empty),
			Location = new Point(76, 58),
			Size = new Size(372, Math.Max(48, size.Height + 8)),
			Font = UiFonts.Body,
			ForeColor = AppleUiColors.TextSecondary
		});
		AddButtons(buttons);
		AppleFormStyler.Apply((Form)(object)this);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void AddButtons(MessageBoxButtons buttons)
	{
		string[] array;
		DialogResult[] array2;
		if ((int)buttons != 1)
		{
			if ((int)buttons == 4)
			{
				array = new string[2] { "否", "是" };
				array2 = (DialogResult[])(object)new DialogResult[2]
				{
					(DialogResult)7,
					(DialogResult)6
				};
			}
			else if ((int)buttons != 3)
			{
				array = new string[1] { "确定" };
				array2 = (DialogResult[])(object)new DialogResult[1] { (DialogResult)1 };
			}
			else
			{
				array = new string[3] { "取消", "否", "是" };
				// 反编译器未能恢复静态数组初始化数据；按按钮文本恢复标准 WinForms 结果。
				array2 = new DialogResult[3] { DialogResult.Cancel, DialogResult.No, DialogResult.Yes };
			}
		}
		else
		{
			array = new string[2] { "取消", "确定" };
			array2 = (DialogResult[])(object)new DialogResult[2]
			{
				(DialogResult)2,
				(DialogResult)1
			};
		}
		int num = 76;
		int num2 = 10;
		int num3 = ((Form)this).ClientSize.Width - 24 - array.Length * num - (array.Length - 1) * num2;
		int y = ((Form)this).ClientSize.Height - 54;
		for (int i = 0; i < array.Length; i++)
		{
			StyledButton styledButton = new StyledButton();
			((Control)styledButton).Text = array[i];
			((Control)styledButton).Location = new Point(num3 + i * (num + num2), y);
			((Control)styledButton).Size = new Size(num, 36);
			((Button)styledButton).DialogResult = array2[i];
			StyledButton styledButton2 = styledButton;
			((Control)this).Controls.Add((Control)(object)styledButton2);
			if ((int)array2[i] == 1 || (int)array2[i] == 6)
			{
				((Form)this).AcceptButton = (IButtonControl)(object)styledButton2;
			}
			if ((int)array2[i] == 2 || (int)array2[i] == 7)
			{
				((Form)this).CancelButton = (IButtonControl)(object)styledButton2;
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void ReplaceWithChoiceButtons(string primaryLabel, string secondaryLabel, string cancelLabel)
	{
		for (int num = ((ArrangedElementCollection)((Control)this).Controls).Count - 1; num >= 0; num--)
		{
			if (((Control)this).Controls[num] is StyledButton)
			{
				Control obj = ((Control)this).Controls[num];
				((Control)this).Controls.RemoveAt(num);
				((Component)(object)obj).Dispose();
			}
		}
		List<string> list = new List<string>();
		List<DialogResult> list2 = new List<DialogResult>();
		if (!string.IsNullOrWhiteSpace(cancelLabel))
		{
			list.Add(cancelLabel);
			list2.Add((DialogResult)2);
		}
		if (!string.IsNullOrWhiteSpace(secondaryLabel))
		{
			list.Add(secondaryLabel);
			list2.Add((DialogResult)7);
		}
		list.Add(string.IsNullOrWhiteSpace(primaryLabel) ? "继续" : primaryLabel);
		list2.Add((DialogResult)6);
		List<int> list3 = new List<int>();
		int num2 = 0;
		for (int i = 0; i < list.Count; i++)
		{
			int val = TextRenderer.MeasureText(list[i], UiFonts.Body).Width + 28;
			int num3 = Math.Max(76, Math.Min(190, val));
			list3.Add(num3);
			num2 += num3;
		}
		num2 += (list.Count - 1) * 10;
		int num4 = ((Form)this).ClientSize.Width - 24 - num2;
		int y = ((Form)this).ClientSize.Height - 54;
		for (int j = 0; j < list.Count; j++)
		{
			DialogResult choiceResult = list2[j];
			StyledButton styledButton = new StyledButton();
			((Control)styledButton).Text = list[j];
			((Control)styledButton).Location = new Point(num4, y);
			((Control)styledButton).Size = new Size(list3[j], 36);
			((Button)styledButton).DialogResult = choiceResult;
			StyledButton styledButton2 = styledButton;
			((Control)styledButton2).Click += delegate
			{
				((Form)this).DialogResult = choiceResult;
				((Form)this).Close();
			};
			((Control)this).Controls.Add((Control)(object)styledButton2);
			num4 += list3[j] + 10;
			if ((int)choiceResult == 6)
			{
				((Form)this).AcceptButton = (IButtonControl)(object)styledButton2;
			}
			if ((int)choiceResult == 2)
			{
				((Form)this).CancelButton = (IButtonControl)(object)styledButton2;
			}
		}
	}

	public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
	{
		return Show(null, text, caption, buttons, icon);
	}

	public static DialogResult Show(string text, string caption)
	{
		return Show(null, text, caption, (MessageBoxButtons)0, (MessageBoxIcon)64);
	}

	public static DialogResult Show(IWin32Window owner, string text, string caption)
	{
		return Show(owner, text, caption, (MessageBoxButtons)0, (MessageBoxIcon)64);
	}

	public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
	{
		AppleMessageDialog appleMessageDialog = new AppleMessageDialog(text, caption, buttons, icon);
		try
		{
			return (owner == null) ? ((Form)appleMessageDialog).ShowDialog() : ((Form)appleMessageDialog).ShowDialog(owner);
		}
		finally
		{
			((IDisposable)(object)appleMessageDialog)?.Dispose();
		}
	}

	public static DialogResult ShowChoices(IWin32Window owner, string text, string caption, string primaryLabel, string secondaryLabel, string cancelLabel, MessageBoxIcon icon)
	{
		AppleMessageDialog appleMessageDialog = new AppleMessageDialog(text, caption, (MessageBoxButtons)3, icon);
		try
		{
			appleMessageDialog.ReplaceWithChoiceButtons(primaryLabel, secondaryLabel, cancelLabel);
			return (owner == null) ? ((Form)appleMessageDialog).ShowDialog() : ((Form)appleMessageDialog).ShowDialog(owner);
		}
		finally
		{
			((IDisposable)(object)appleMessageDialog)?.Dispose();
		}
	}
}
