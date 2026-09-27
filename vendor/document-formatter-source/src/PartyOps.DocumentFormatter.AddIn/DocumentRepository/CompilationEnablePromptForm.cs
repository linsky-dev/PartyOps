using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using DocumentRepository.Services.UiText;

namespace DocumentRepository;

public class CompilationEnablePromptForm : Form
{
	public const string MarkerText = "@@汇编@@";

	private readonly ToolTip toolTip = UiTextApplier.CreateToolTip();

	public CompilationEnablePromptForm()
	{
		InitializeComponent();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void InitializeComponent()
	{
		((Control)this).Text = "开启汇编排版";
		((Form)this).StartPosition = (FormStartPosition)4;
		((Form)this).FormBorderStyle = (FormBorderStyle)3;
		((Form)this).MaximizeBox = false;
		((Form)this).MinimizeBox = false;
		((Form)this).ClientSize = new Size(640, 270);
		((Control)this).BackColor = UiColors.BgPage;
		Label val = new Label
		{
			Text = "汇编排版使用说明",
			Location = new Point(24, 18),
			Size = new Size(592, 28),
			Font = UiFonts.Title,
			ForeColor = UiColors.TextTitle
		};
		Label val2 = new Label
		{
			Text = "使用说明:\n1.因汇编文章题目字体字号格式各异，插件无法独立识别主标题，需人工辅助识别。\n2.请在汇编文章内部每篇文章大标题之前插入一行识别标识——@@汇编@@\n3.标记必须独占一行，前后不要加空格或全角空格:\n4.标记下一段必须是该篇文章的非空主标题:\n5.表格、页眉页脚、脚注、尾注、批注、文本框中的标记不会被识别;",
			Location = new Point(24, 56),
			Size = new Size(592, 135),
			Font = UiFonts.Body,
			ForeColor = UiColors.TextBody
		};
		Button val3 = new Button
		{
			Text = "复制标记",
			Location = new Point(24, 206),
			Size = new Size(120, 34),
			BackColor = UiColors.Success,
			ForeColor = Color.White,
			FlatStyle = (FlatStyle)0,
			Font = UiFonts.Body
		};
		((ButtonBase)val3).FlatAppearance.BorderSize = 0;
		((Control)val3).Click += [MethodImpl(MethodImplOptions.NoInlining)] (object s, EventArgs e) =>
		{
			try
			{
				Clipboard.SetText("@@汇编@@");
			}
			catch
			{
			}
		};
		Button val4 = new Button
		{
			Text = "确认开启",
			Location = new Point(396, 206),
			Size = new Size(100, 34),
			BackColor = UiColors.PrimaryLight,
			ForeColor = Color.White,
			FlatStyle = (FlatStyle)0,
			Font = UiFonts.Body,
			DialogResult = (DialogResult)1
		};
		((ButtonBase)val4).FlatAppearance.BorderSize = 0;
		Button val5 = new Button
		{
			Text = "取消",
			Location = new Point(516, 206),
			Size = new Size(100, 34),
			BackColor = UiColors.BgDanger,
			ForeColor = UiColors.Danger,
			FlatStyle = (FlatStyle)0,
			Font = UiFonts.Body,
			DialogResult = (DialogResult)2
		};
		((ButtonBase)val5).FlatAppearance.BorderSize = 0;
		((Control)this).Controls.Add((Control)(object)val);
		((Control)this).Controls.Add((Control)(object)val2);
		((Control)this).Controls.Add((Control)(object)val3);
		((Control)this).Controls.Add((Control)(object)val4);
		((Control)this).Controls.Add((Control)(object)val5);
		UiTextApplier.ApplyTooltip(toolTip, (Control)(object)val3, "Compilation.Enable.CopyMarker");
		UiTextApplier.ApplyTooltip(toolTip, (Control)(object)val4, "Compilation.Enable.Confirm");
		UiTextApplier.ApplyTooltip(toolTip, (Control)(object)val5, "Compilation.Enable.Cancel");
		((Component)this).Disposed += delegate
		{
			((Component)(object)toolTip).Dispose();
		};
		((Form)this).AcceptButton = (IButtonControl)(object)val4;
		((Form)this).CancelButton = (IButtonControl)(object)val5;
	}
}
