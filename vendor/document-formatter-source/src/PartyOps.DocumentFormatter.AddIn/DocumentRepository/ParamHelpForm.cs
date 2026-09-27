using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace DocumentRepository;

internal class ParamHelpForm : Form
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public ParamHelpForm()
	{
		((Control)this).Text = "参数说明";
		((Form)this).StartPosition = (FormStartPosition)4;
		((Form)this).ClientSize = new Size(820, 640);
		((Form)this).MinimizeBox = false;
		((Control)this).BackColor = Color.FromArgb(238, 248, 255);
		Label val = new Label
		{
			Text = "partyops公文排版助手参数界面说明",
			Location = new Point(28, 22),
			Size = new Size(760, 36),
			Font = new Font("Microsoft YaHei UI", 18f, (FontStyle)1),
			ForeColor = Color.FromArgb(8, 96, 160)
		};
		((Control)this).Controls.Add((Control)(object)val);
		TextBox val2 = new TextBox
		{
			Location = new Point(28, 72),
			Size = new Size(760, 532),
			Multiline = true,
			ReadOnly = true,
			ScrollBars = (ScrollBars)2,
			BorderStyle = (BorderStyle)1,
			BackColor = Color.White,
			Font = new Font("Microsoft YaHei UI", 10f),
			Text = HelpText()
		};
		((Control)this).Controls.Add((Control)(object)val2);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string HelpText()
	{
		return "核心功能位于三个按钮。\r\n\r\n1. 一键排版。未选中文字时对全文按照预定格式自动排版；选中部分文字后点击，则仅对选中区域执行快速排版。\r\n2. 自定义参数。对排版的格式参数如标题、正文、页码、落款等进行自定义设置。参数本身带有默认值，基本覆盖各地常规设置，用户也可根据本单位实际进行个性设置。按钮处带有下拉箭头选项，可快速切换模板。\r\n\r\n（1）字体参数。可设置主标题、一级标题、二级标题、三级标题、正文和页码的字体、字号、加粗、行距、缩进、段距、对齐及大纲层次。默认大纲为：主标题和一级标题1级、二级标题2级、三级标题3级、正文为正文文本。\r\n\r\n（2）页边距参数。可设置页面的上下左右边距，以及页眉距和页脚距。默认：上边距3.7cm、下边距3.5cm、左边距2.8cm、右边距2.6cm、页眉距1.5cm、页脚距2.5cm。\r\n\r\n（3）文档网格。勾选后按“网格参数”设置每页行数和每行字数，行距根据纸张和页边距动态计算；默认参数为22行、每行28字。默认不勾选。\r\n\r\n（4）修正分号。勾选后，排版时会自动在“二是（二要）”“三是（三要）”等序号前添加分号。默认不勾选。\r\n\r\n（5）落款排版。勾选后按“落款参数”设置盖章模式和落款前空行数；不勾选时署名和日期按普通正文处理。默认勾选。\r\n\r\n（6）关键词加粗。可分别设置“一是至十是”“一要至十要”“第一至第十”三类关键词的加粗方式，选项包括“不加粗”“短语加粗”“整句加粗”。\r\n\r\n（7）页码设置。可设置页码的对齐方式、是否开启页码，以及页码左右两侧的翅膀装饰符号，左右翅膀可独立设置，如❤1—，或—1—。\r\n\r\n（8）清理选项。可设置是否删除 AI 符号，并清理 DeepSeek 等网页复制在中文句间留下的不间断空格。默认开启。\r\n\r\n（9）是否删除空格。英文文档建议关闭，避免破坏单词间距。默认不勾选。\r\n\r\n（10）英文和数字字体。以三级标题的中文字体为匹配条件；一键排版后凡使用同一中文字体的内容，其中的英文和数字会使用这里设置的字体。\r\n\r\n（11）模板管理。系统内置“系统默认”模板及三个自定义模板。用户可保存多组排版参数，针对不同单位、不同文种快速切换。系统默认模板不可修改。";
	}
}
