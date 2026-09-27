using System.ComponentModel;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Forms;
using DocumentRepository.Models.CompilationFormatting;
using DocumentRepository.Services.UiText;

namespace DocumentRepository;

public class CompilationConfirmationForm : Form
{
	private readonly CompilationConfirmationRequest request;

	private readonly bool offerExpand;

	private CompilationConfirmationResult result;

	private readonly ToolTip toolTip = UiTextApplier.CreateToolTip();

	public CompilationConfirmationForm(CompilationConfirmationRequest request, bool offerExpand = false)
	{
		this.request = request ?? new CompilationConfirmationRequest();
		this.offerExpand = offerExpand;
		InitializeComponent();
	}

	public CompilationConfirmationResult GetResult()
	{
		return result ?? new CompilationConfirmationResult
		{
			Confirmed = false
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void InitializeComponent()
	{
		((Control)this).Text = (request.IsFullDocument ? "全文汇编排版确认" : "部分汇编排版确认");
		((Form)this).StartPosition = (FormStartPosition)4;
		((Form)this).FormBorderStyle = (FormBorderStyle)3;
		((Form)this).MaximizeBox = false;
		((Form)this).MinimizeBox = false;
		((Form)this).ClientSize = new Size(560, 520);
		((Control)this).BackColor = UiColors.BgPage;
		int num = 18;
		Label val = new Label
		{
			Text = BuildSummary(),
			Location = new Point(24, num),
			Size = new Size(512, 44),
			Font = UiFonts.BodyBold,
			ForeColor = UiColors.TextTitle
		};
		num += 54;
		ListBox listBox = new ListBox
		{
			Location = new Point(24, num),
			Size = new Size(512, 180),
			Font = UiFonts.Body,
			ForeColor = UiColors.TextBody,
			BackColor = Color.White,
			BorderStyle = (BorderStyle)1
		};
		foreach (CompilationArticleInfo article in request.Articles)
		{
			listBox.Items.Add((object)$"第 {article.OrderIndex} 篇\u3000{article.Title}");
		}
		UiTextApplier.ApplyTooltip(toolTip, (Control)(object)listBox, "Compilation.Confirmation.ArticleList");
		int hoveredArticleIndex = -1;
		((Control)listBox).MouseMove += (MouseEventHandler)([MethodImpl(MethodImplOptions.NoInlining)] (object s, MouseEventArgs e) =>
		{
			int num2 = listBox.IndexFromPoint(e.Location);
			if (num2 != hoveredArticleIndex)
			{
				hoveredArticleIndex = num2;
				UiTextApplier.ApplyTooltipText(toolTip, (Control)(object)listBox, (num2 >= 0 && num2 < listBox.Items.Count) ? listBox.Items[num2].ToString() : UiTextRegistry.Get("Compilation.Confirmation.ArticleList").Tooltip);
			}
		});
		num += 192;
		Label val2 = new Label
		{
			Text = BuildParamsText(),
			Location = new Point(24, num),
			Size = new Size(512, 90),
			Font = UiFonts.Body,
			ForeColor = UiColors.TextBody
		};
		num += 100;
		if (offerExpand)
		{
			Label val3 = new Label
			{
				Text = "提示：当前选区首尾截在某篇文章内部。只有选择“扩展到完整范围并排版”才会按完整文章处理；所有识别标记将转换为内部文章边界并从正文中删除，未选中文章正文不会被排版。",
				Location = new Point(24, num),
				Size = new Size(512, 36),
				Font = UiFonts.Body,
				ForeColor = UiColors.Warning
			};
			((Control)this).Controls.Add((Control)(object)val3);
			num += 42;
		}
		Button val4 = new Button
		{
			Text = (request.IsFullDocument ? "确认无误，开始排版" : "确认并排版"),
			Location = new Point(offerExpand ? 24 : 200, 470),
			Size = new Size(140, 34),
			BackColor = UiColors.PrimaryLight,
			ForeColor = Color.White,
			FlatStyle = (FlatStyle)0,
			Font = UiFonts.Body,
			DialogResult = (DialogResult)1,
			Visible = !offerExpand
		};
		((ButtonBase)val4).FlatAppearance.BorderSize = 0;
		((Control)val4).Click += delegate
		{
			result = new CompilationConfirmationResult
			{
				Confirmed = true,
				ExpandToFullArticles = false
			};
		};
		Button val5 = new Button
		{
			Text = "扩展到完整范围并排版",
			Location = new Point(offerExpand ? 60 : 180, 470),
			Size = new Size(180, 34),
			BackColor = UiColors.Accent,
			ForeColor = Color.White,
			FlatStyle = (FlatStyle)0,
			Font = UiFonts.Body,
			Visible = offerExpand
		};
		((ButtonBase)val5).FlatAppearance.BorderSize = 0;
		((Control)val5).Click += delegate
		{
			result = new CompilationConfirmationResult
			{
				Confirmed = true,
				ExpandToFullArticles = true
			};
			((Form)this).DialogResult = (DialogResult)1;
			((Form)this).Close();
		};
		Button val6 = new Button
		{
			Text = (request.IsFullDocument ? "返回文档检查标记" : "取消并返回文档"),
			Location = new Point(340, 470),
			Size = new Size(140, 34),
			BackColor = UiColors.BgDanger,
			ForeColor = UiColors.Danger,
			FlatStyle = (FlatStyle)0,
			Font = UiFonts.Body,
			DialogResult = (DialogResult)2
		};
		((ButtonBase)val6).FlatAppearance.BorderSize = 0;
		((Control)val6).Click += delegate
		{
			result = new CompilationConfirmationResult
			{
				Confirmed = false
			};
		};
		((Control)this).Controls.Add((Control)(object)val);
		((Control)this).Controls.Add((Control)(object)listBox);
		((Control)this).Controls.Add((Control)(object)val2);
		((Control)this).Controls.Add((Control)(object)val4);
		((Control)this).Controls.Add((Control)(object)val5);
		((Control)this).Controls.Add((Control)(object)val6);
		UiTextApplier.ApplyTooltip(toolTip, (Control)(object)val4, request.IsFullDocument ? "Compilation.Confirmation.Full" : "Compilation.Confirmation.Partial");
		UiTextApplier.ApplyTooltip(toolTip, (Control)(object)val5, "Compilation.Confirmation.Expand");
		UiTextApplier.ApplyTooltip(toolTip, (Control)(object)val6, "Compilation.Confirmation.Cancel");
		((Component)this).Disposed += delegate
		{
			((Component)(object)toolTip).Dispose();
		};
		((Form)this).AcceptButton = (IButtonControl)(object)(offerExpand ? val5 : val4);
		((Form)this).CancelButton = (IButtonControl)(object)val6;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private string BuildSummary()
	{
		if (request.IsFullDocument)
		{
			return $"本次读取到 {request.Articles.Count} 篇文章：";
		}
		return $"本次将对以下 {request.Articles.Count} 篇文章分别执行汇编排版：";
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private string BuildParamsText()
	{
		StringBuilder stringBuilder = new StringBuilder();
		if (request.IsFullDocument)
		{
			stringBuilder.AppendLine("前置内容：" + request.FrontMatterModeText);
			stringBuilder.AppendLine("目录：" + (request.GenerateToc ? "生成" : "不生成"));
			stringBuilder.AppendLine("页码：" + request.PageNumberModeText);
			stringBuilder.Append("每篇文章：" + (request.StartEachArticleOnNewPage ? "另起一页" : "连续排版"));
		}
		else
		{
			stringBuilder.AppendLine("本次属于部分文章排版，不会生成或更新目录，也不会重新设置全文页码。");
			stringBuilder.Append("结构变化说明：所有识别标记将转换为内部文章边界并从正文中删除（属于允许的全局结构变化）；未选中文章的正文内容与格式不会被修改。");
		}
		return stringBuilder.ToString();
	}
}
