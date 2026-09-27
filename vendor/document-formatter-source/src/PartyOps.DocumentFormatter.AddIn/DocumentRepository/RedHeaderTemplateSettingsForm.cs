using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using DocumentRepository.Models.RedHeader;
using DocumentRepository.Models.Rules;
using DocumentRepository.Services.RedHeader;
using DocumentRepository.Services.Rules;
using DocumentRepository.Services.UiText;

namespace DocumentRepository;

public class RedHeaderTemplateSettingsForm : Form
{
	private sealed class ScrollFriendlyNumericUpDown : NumericUpDown
	{
		protected override void OnMouseWheel(MouseEventArgs e)
		{
			RouteMouseWheelToScrollableParent((Control)(object)this, e);
		}
	}

	private sealed class ScrollFriendlyComboBox : ComboBox
	{
		protected override void OnMouseWheel(MouseEventArgs e)
		{
			RouteMouseWheelToScrollableParent((Control)(object)this, e);
		}
	}

	private const int HeaderColorBlack = 0;

	private const int HeaderColorRed = 255;

	private const int HeaderColorWhite = 16777215;

	private RedHeaderTemplateSet templateSet;

	private bool storeRecoveryDeclined;

	private readonly ListBox lstTemplates = new ListBox();

	private readonly Panel panel;

	private readonly TextBox txtName;

	private readonly TextBox txtHeaderText;

	private readonly TextBox txtDocumentNumber;

	private readonly CheckBox chkCopyNumber;

	private readonly TextBox txtCopyNumber;

	private readonly ComboBox cboSecurityLevel;

	private readonly ComboBox cboConfidentialityPeriod;

	private readonly ComboBox cboUrgencyLevel;

	private readonly TextBox txtTopMarkFont;

	private readonly NumericUpDown numTopMarkSize;

	private readonly NumericUpDown numTopMarkLineSpacing;

	private readonly NumericUpDown numTopMarkIndent;

	private readonly ComboBox cboTopMarkColor;

	private readonly TextBox txtHeaderFont;

	private readonly NumericUpDown numHeaderSize;

	private readonly ComboBox cboHeaderBold;

	private readonly ComboBox cboHeaderColor;

	private readonly ComboBox cboHeaderAlignment;

	private readonly NumericUpDown numHeaderLineSpacing;

	private readonly NumericUpDown numHeaderBefore;

	private readonly NumericUpDown numHeaderAfter;

	private readonly NumericUpDown numHeaderIndent;

	private readonly NumericUpDown numHeaderWidth;

	private readonly NumericUpDown numHeaderScale;

	private readonly ComboBox cboHeaderFitMode;

	private readonly NumericUpDown numHeaderMinimumScale;

	private readonly NumericUpDown numHeaderSpacing;

	private readonly TextBox txtDocNumberFont;

	private readonly NumericUpDown numDocNumberSize;

	private readonly NumericUpDown numDocNumberLineSpacing;

	private readonly NumericUpDown numDocNumberBefore;

	private readonly NumericUpDown numDocNumberAfter;

	private readonly ComboBox cboRedLineStyle;

	private readonly NumericUpDown numRedLineWidth;

	private readonly NumericUpDown numRedLineThickness;

	private readonly ComboBox cboRedLineColor;

	private readonly NumericUpDown numRedLineBefore;

	private readonly NumericUpDown numRedLineAfter;

	private readonly NumericUpDown numTitleGapLines;

	private readonly NumericUpDown numTitleGapLineSpacing;

	private readonly CheckBox chkImprintEnabled;

	private readonly CheckBox chkImprintEvenPage;

	private readonly TextBox txtImprintFont;

	private readonly ComboBox cboImprintSize;

	private readonly NumericUpDown numImprintBottomOffset;

	private readonly NumericUpDown numImprintCellPadding;

	private readonly TextBox txtImprintSend;

	private readonly TextBox txtImprintOffice;

	private readonly TextBox txtImprintDate;

	private readonly ComboBox cboImprintDateMode;

	private readonly CheckBox chkUseTimesNewRomanForNumbers;

	private readonly ToolTip toolTip;

	private readonly Dictionary<Control, Label> fieldLabels;

	private RedHeaderTemplate CurrentTemplate
	{
		get
		{
			if (((ListControl)lstTemplates).SelectedIndex < 0 || ((ListControl)lstTemplates).SelectedIndex >= templateSet.Templates.Count)
			{
				return null;
			}
			return templateSet.Templates[((ListControl)lstTemplates).SelectedIndex];
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public RedHeaderTemplateSettingsForm()
	{
		RoundedPanel obj = new RoundedPanel
		{
			Radius = 12,
			BorderColor = AppleUiColors.Separator
		};
		((Control)obj).BackColor = AppleUiColors.Surface;
		panel = (Panel)(object)obj;
		txtName = new TextBox();
		txtHeaderText = new TextBox();
		txtDocumentNumber = new TextBox();
		chkCopyNumber = new CheckBox();
		txtCopyNumber = new TextBox();
		cboSecurityLevel = CreateCombo("无", "秘密", "机密", "绝密");
		cboConfidentialityPeriod = (ComboBox)(object)new ScrollFriendlyComboBox();
		cboUrgencyLevel = CreateCombo("无", "加急", "特急");
		txtTopMarkFont = new TextBox();
		numTopMarkSize = CreateNumber(5m, 48m, 0.5m);
		numTopMarkLineSpacing = CreateNumber(1m, 120m, 0.5m);
		numTopMarkIndent = CreateNumber(0m, 20m, 0.5m);
		cboTopMarkColor = CreateCombo("黑色", "红色");
		txtHeaderFont = new TextBox();
		numHeaderSize = CreateNumber(5m, 96m, 0.5m);
		cboHeaderBold = CreateCombo("否", "是");
		cboHeaderColor = CreateCombo("红色", "黑色", "白色");
		cboHeaderAlignment = CreateCombo("居中", "左对齐", "右对齐", "两端对齐", "分散对齐");
		numHeaderLineSpacing = CreateNumber(0m, 120m, 0.5m);
		numHeaderBefore = CreateNumber(0m, 240m, 0.5m);
		numHeaderAfter = CreateNumber(0m, 120m, 0.5m);
		numHeaderIndent = CreateNumber(0m, 20m, 0.5m);
		numHeaderWidth = CreateNumber(20m, 200m, 1m);
		numHeaderScale = CreateNumber(20m, 200m, 1m);
		cboHeaderFitMode = CreateCombo("手动", "自动适应一行", "允许换行");
		numHeaderMinimumScale = CreateNumber(20m, 200m, 1m);
		numHeaderSpacing = CreateNumber(-20m, 40m, 0.1m);
		txtDocNumberFont = new TextBox();
		numDocNumberSize = CreateNumber(5m, 48m, 0.5m);
		numDocNumberLineSpacing = CreateNumber(0m, 120m, 0.5m);
		numDocNumberBefore = CreateNumber(0m, 240m, 0.5m);
		numDocNumberAfter = CreateNumber(0m, 120m, 0.5m);
		cboRedLineStyle = (ComboBox)(object)new ScrollFriendlyComboBox();
		numRedLineWidth = CreateNumber(20m, 200m, 1m);
		numRedLineThickness = CreateNumber(0.5m, 10m, 0.1m);
		cboRedLineColor = CreateCombo("红色", "黑色", "白色");
		numRedLineBefore = CreateNumber(0m, 120m, 0.5m);
		numRedLineAfter = CreateNumber(0m, 120m, 0.5m);
		numTitleGapLines = CreateNumber(0m, 5m, 1m);
		numTitleGapLineSpacing = CreateNumber(0m, 80m, 0.5m);
		chkImprintEnabled = new CheckBox();
		chkImprintEvenPage = new CheckBox();
		txtImprintFont = new TextBox();
		cboImprintSize = CreateCombo("二号", "小二", "三号", "小三", "四号", "小四", "五号", "小五", "六号");
		numImprintBottomOffset = CreateNumber(-36m, 36m, 1m);
		numImprintCellPadding = CreateNumber(0m, 1m, 0.01m);
		txtImprintSend = new TextBox();
		txtImprintOffice = new TextBox();
		txtImprintDate = new TextBox();
		cboImprintDateMode = CreateCombo("手动输入", "自动使用当天日期");
		chkUseTimesNewRomanForNumbers = new CheckBox();
		toolTip = UiTextApplier.CreateToolTip();
		fieldLabels = new Dictionary<Control, Label>();
		RuleLoadResult<RedHeaderTemplateSet> ruleLoadResult = RedHeaderTemplateService.LoadResult();
		if (!ruleLoadResult.Usable && !RuleStoreResetPrompt.ConfirmAndReset((IWin32Window)(object)this, ruleLoadResult, "套红模板库", RedHeaderTemplateService.ResetToDefault))
		{
			storeRecoveryDeclined = true;
			templateSet = new RedHeaderTemplateSet
			{
				ActiveTemplateId = ""
			};
		}
		else
		{
			templateSet = RedHeaderTemplateService.LoadResult().Value;
		}
		InitializeUi();
		LoadTemplateList();
	}

	protected override void OnLoad(EventArgs e)
	{
		base.OnLoad(e);
		if (storeRecoveryDeclined)
		{
			((Form)this).Close();
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void InitializeUi()
	{
		((Control)this).Text = "红头模板设置";
		((Form)this).StartPosition = (FormStartPosition)4;
		((Form)this).ClientSize = new Size(900, 640);
		((Control)this).MinimumSize = new Size(900, 640);
		((Control)this).Font = new Font("Microsoft YaHei UI", 9f);
		((Control)this).BackColor = AppleUiColors.Window;
		((Control)this).Controls.Add((Control)new Label
		{
			Text = "红头模板设置",
			Location = new Point(16, 16),
			AutoSize = true,
			ForeColor = AppleUiColors.TextPrimary,
			Font = new Font("Microsoft YaHei UI", 14f, (FontStyle)1)
		});
		((Control)lstTemplates).Location = new Point(16, 56);
		((Control)lstTemplates).Size = new Size(180, 500);
		lstTemplates.SelectedIndexChanged += delegate
		{
			LoadCurrentTemplate();
		};
		((Control)this).Controls.Add((Control)(object)lstTemplates);
		((Control)panel).Location = new Point(210, 56);
		((Control)panel).Size = new Size(660, 500);
		((ScrollableControl)panel).AutoScroll = true;
		((Control)panel).BackColor = AppleUiColors.Surface;
		((Control)this).Controls.Add((Control)(object)panel);
		ConfigureRedLineStyleSelector();
		int y = 0;
		AddSection("基础", ref y);
		((TextBoxBase)txtHeaderText).Multiline = true;
		((Control)txtHeaderText).Height = 38;
		AddRow("模板名称", (Control)(object)txtName, "发文机关", (Control)(object)txtHeaderText, ref y);
		AddFullWidthRow("发文字号", (Control)(object)txtDocumentNumber, ref y);
		((Control)chkUseTimesNewRomanForNumbers).Text = "启用";
		AddRow("数字使用新罗马", (Control)(object)chkUseTimesNewRomanForNumbers, null, null, ref y);
		AddSection("版头附加标识", ref y);
		((Control)chkCopyNumber).Text = "启用";
		AddRow("启用份号", (Control)(object)chkCopyNumber, "公文份号", (Control)(object)txtCopyNumber, ref y);
		ConfigureConfidentialityPeriod();
		AddRow("公文密级", (Control)(object)cboSecurityLevel, "保密期限", (Control)(object)cboConfidentialityPeriod, ref y);
		AddRow("紧急程度", (Control)(object)cboUrgencyLevel, null, null, ref y);
		AddRow("字体", (Control)(object)txtTopMarkFont, "字号", (Control)(object)numTopMarkSize, ref y);
		AddRow("行距", (Control)(object)numTopMarkLineSpacing, "左缩进字符", (Control)(object)numTopMarkIndent, ref y);
		AddRow("颜色", (Control)(object)cboTopMarkColor, null, null, ref y);
		AddSection("发文机关参数", ref y);
		AddRow("字体", (Control)(object)txtHeaderFont, "字号", (Control)(object)numHeaderSize, ref y);
		AddRow("加粗", (Control)(object)cboHeaderBold, "颜色", (Control)(object)cboHeaderColor, ref y);
		AddRow("对齐", (Control)(object)cboHeaderAlignment, "行距", (Control)(object)numHeaderLineSpacing, ref y);
		AddRow("段前", (Control)(object)numHeaderBefore, "段后", (Control)(object)numHeaderAfter, ref y);
		AddRow("左缩进字符", (Control)(object)numHeaderIndent, "排布宽度比例", (Control)(object)numHeaderWidth, ref y);
		AddRow("字符缩放比例", (Control)(object)numHeaderScale, "适配方式", (Control)(object)cboHeaderFitMode, ref y);
		AddRow("最低缩放比例", (Control)(object)numHeaderMinimumScale, "字间距", (Control)(object)numHeaderSpacing, ref y);
		AddSection("发文字号参数", ref y);
		AddRow("字体", (Control)(object)txtDocNumberFont, "字号", (Control)(object)numDocNumberSize, ref y);
		AddRow("行距", (Control)(object)numDocNumberLineSpacing, "段前", (Control)(object)numDocNumberBefore, ref y);
		AddRow("段后", (Control)(object)numDocNumberAfter, null, null, ref y);
		AddSection("红线参数", ref y);
		AddRow("红线样式", (Control)(object)cboRedLineStyle, "线宽比例", (Control)(object)numRedLineWidth, ref y);
		AddRow("粗细", (Control)(object)numRedLineThickness, "颜色", (Control)(object)cboRedLineColor, ref y);
		AddRow("段前", (Control)(object)numRedLineBefore, "段后", (Control)(object)numRedLineAfter, ref y);
		AddSection("主标题与红头线距离", ref y);
		AddRow("空行数", (Control)(object)numTitleGapLines, "空行行距", (Control)(object)numTitleGapLineSpacing, ref y);
		AddSection("版记", ref y);
		((Control)chkImprintEnabled).Text = "启用";
		((Control)chkImprintEvenPage).Text = "版记放在最后偶数页";
		AddRow("启用版记", (Control)(object)chkImprintEnabled, "偶数页版记", (Control)(object)chkImprintEvenPage, ref y);
		AddRow("版记字体", (Control)(object)txtImprintFont, "版记字号", (Control)(object)cboImprintSize, ref y);
		AddRow("贴底微调", (Control)(object)numImprintBottomOffset, "文字上下边距", (Control)(object)numImprintCellPadding, ref y);
		((TextBoxBase)txtImprintSend).Multiline = true;
		((Control)txtImprintSend).Height = 50;
		AddRow("抄送", (Control)(object)txtImprintSend, null, null, ref y);
		AddRow("制发机关", (Control)(object)txtImprintOffice, "印发日期", (Control)(object)txtImprintDate, ref y);
		AddRow("日期模式", (Control)(object)cboImprintDateMode, null, null, ref y);
		AddButton("新增模板", 16, 575, AddTemplate);
		AddButton("复制模板", 105, 575, CopyTemplate);
		AddButton("删除模板", 194, 575, DeleteTemplate);
		AddButton("设为当前", 283, 575, SetActiveTemplate);
		AddButton("确定", 700, 575, SaveAndClose, primary: true);
		AddButton("取消", 790, 575, delegate
		{
			((Form)this).DialogResult = (DialogResult)2;
		});
		ConfigureTooltips();
		chkCopyNumber.CheckedChanged += delegate
		{
			UpdateConditionalControls();
		};
		cboSecurityLevel.SelectedIndexChanged += delegate
		{
			UpdateConditionalControls();
		};
		cboHeaderFitMode.SelectedIndexChanged += delegate
		{
			UpdateConditionalControls();
		};
		cboImprintDateMode.SelectedIndexChanged += delegate
		{
			UpdateConditionalControls();
		};
		UpdateConditionalControls();
		toolTip.SetToolTip((Control)(object)lstTemplates, "选择要查看或编辑的红头模板；圆点标记表示当前执行模板。");
		AppleFormStyler.Apply((Form)(object)this, toolTip);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void ConfigureTooltips()
	{
		ApplyFieldTooltip((Control)(object)txtName, "RedHeader.TemplateName");
		ApplyFieldTooltip((Control)(object)txtHeaderText, "RedHeader.HeaderText");
		ApplyFieldTooltip((Control)(object)txtDocumentNumber, "RedHeader.DocumentNumberText");
		ApplyFieldTooltip((Control)(object)chkUseTimesNewRomanForNumbers, "RedHeader.Numbers.UseTimesNewRoman");
		ApplyFieldTooltip((Control)(object)chkCopyNumber, "RedHeader.TopMark.CopyNumberEnabled");
		ApplyFieldTooltip((Control)(object)txtCopyNumber, "RedHeader.TopMark.CopyNumber");
		ApplyFieldTooltip((Control)(object)cboSecurityLevel, "RedHeader.TopMark.SecurityLevel");
		ApplyFieldTooltip((Control)(object)cboConfidentialityPeriod, "RedHeader.TopMark.ConfidentialityPeriod");
		ApplyFieldTooltip((Control)(object)cboUrgencyLevel, "RedHeader.TopMark.UrgencyLevel");
		ApplyFieldTooltip((Control)(object)txtTopMarkFont, "RedHeader.TopMark.Font");
		ApplyFieldTooltip((Control)(object)numTopMarkSize, "RedHeader.TopMark.Size");
		ApplyFieldTooltip((Control)(object)numTopMarkLineSpacing, "RedHeader.TopMark.LineSpacing");
		ApplyFieldTooltip((Control)(object)numTopMarkIndent, "RedHeader.TopMark.Indent");
		ApplyFieldTooltip((Control)(object)cboTopMarkColor, "RedHeader.TopMark.Color");
		ApplyFieldTooltip((Control)(object)txtHeaderFont, "RedHeader.Header.Font");
		ApplyFieldTooltip((Control)(object)numHeaderSize, "RedHeader.Header.Size");
		ApplyFieldTooltip((Control)(object)cboHeaderBold, "RedHeader.Header.Bold");
		ApplyFieldTooltip((Control)(object)cboHeaderColor, "RedHeader.Header.Color");
		ApplyFieldTooltip((Control)(object)cboHeaderAlignment, "RedHeader.Header.Alignment");
		ApplyFieldTooltip((Control)(object)numHeaderLineSpacing, "RedHeader.Header.LineSpacing");
		ApplyFieldTooltip((Control)(object)numHeaderBefore, "RedHeader.Header.Before");
		ApplyFieldTooltip((Control)(object)numHeaderAfter, "RedHeader.Header.After");
		ApplyFieldTooltip((Control)(object)numHeaderIndent, "RedHeader.Header.Indent");
		ApplyFieldTooltip((Control)(object)numHeaderWidth, "RedHeader.Header.Width");
		ApplyFieldTooltip((Control)(object)numHeaderScale, "RedHeader.Header.Scale");
		ApplyFieldTooltip((Control)(object)cboHeaderFitMode, "RedHeader.Header.FitMode");
		ApplyFieldTooltip((Control)(object)numHeaderMinimumScale, "RedHeader.Header.MinimumScale");
		ApplyFieldTooltip((Control)(object)numHeaderSpacing, "RedHeader.Header.CharSpacing");
		ApplyFieldTooltip((Control)(object)txtDocNumberFont, "RedHeader.Number.Font");
		ApplyFieldTooltip((Control)(object)numDocNumberSize, "RedHeader.Number.Size");
		ApplyFieldTooltip((Control)(object)numDocNumberLineSpacing, "RedHeader.Number.LineSpacing");
		ApplyFieldTooltip((Control)(object)numDocNumberBefore, "RedHeader.Number.Before");
		ApplyFieldTooltip((Control)(object)numDocNumberAfter, "RedHeader.Number.After");
		ApplyFieldTooltip((Control)(object)cboRedLineStyle, "RedHeader.Line.Style");
		ApplyFieldTooltip((Control)(object)numRedLineWidth, "RedHeader.Line.Width");
		ApplyFieldTooltip((Control)(object)numRedLineThickness, "RedHeader.Line.Thickness");
		ApplyFieldTooltip((Control)(object)cboRedLineColor, "RedHeader.Line.Color");
		ApplyFieldTooltip((Control)(object)numRedLineBefore, "RedHeader.Line.Before");
		ApplyFieldTooltip((Control)(object)numRedLineAfter, "RedHeader.Line.After");
		ApplyFieldTooltip((Control)(object)numTitleGapLines, "RedHeader.TitleGap.Lines");
		ApplyFieldTooltip((Control)(object)numTitleGapLineSpacing, "RedHeader.TitleGap.LineSpacing");
		ApplyFieldTooltip((Control)(object)chkImprintEnabled, "RedHeader.Imprint.Enabled");
		ApplyFieldTooltip((Control)(object)chkImprintEvenPage, "RedHeader.Imprint.EvenPage");
		ApplyFieldTooltip((Control)(object)txtImprintFont, "RedHeader.Imprint.Font");
		ApplyFieldTooltip((Control)(object)cboImprintSize, "RedHeader.Imprint.Size");
		ApplyFieldTooltip((Control)(object)numImprintBottomOffset, "RedHeader.Imprint.BottomOffset");
		ApplyFieldTooltip((Control)(object)numImprintCellPadding, "RedHeader.Imprint.CellPadding");
		ApplyFieldTooltip((Control)(object)txtImprintSend, "RedHeader.Imprint.Send");
		ApplyFieldTooltip((Control)(object)txtImprintOffice, "RedHeader.Imprint.Office");
		ApplyFieldTooltip((Control)(object)txtImprintDate, "RedHeader.Imprint.Date");
	}

	private void ApplyFieldTooltip(Control control, string key)
	{
		UiTextApplier.ApplyTooltip(toolTip, control, key);
		if (control != null && fieldLabels.TryGetValue(control, out var value))
		{
			UiTextApplier.ApplyTooltip(toolTip, (Control)(object)value, key);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void ConfigureConfidentialityPeriod()
	{
		cboConfidentialityPeriod.DropDownStyle = (ComboBoxStyle)1;
		cboConfidentialityPeriod.Items.AddRange(new object[4] { "10年", "20年", "30年", "长期" });
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void UpdateConditionalControls()
	{
		((Control)txtCopyNumber).Enabled = chkCopyNumber.Checked;
		bool enabled = !string.Equals(Convert.ToString(cboSecurityLevel.SelectedItem), "无", StringComparison.Ordinal);
		((Control)cboConfidentialityPeriod).Enabled = enabled;
		bool enabled2 = string.Equals(Convert.ToString(cboHeaderFitMode.SelectedItem), "自动适应一行", StringComparison.Ordinal);
		((Control)numHeaderMinimumScale).Enabled = enabled2;
		((Control)txtImprintDate).Enabled = ((ListControl)cboImprintDateMode).SelectedIndex == 0;
	}

	private void AddSection(string text, ref int y)
	{
		Label val = new Label
		{
			Text = text,
			Location = new Point(0, y),
			Size = new Size(620, 26),
			Font = new Font(((Control)this).Font, (FontStyle)1),
			ForeColor = Color.FromArgb(0, 82, 155)
		};
		((Control)panel).Controls.Add((Control)(object)val);
		y += 30;
	}

	private void AddRow(string label1, Control control1, string label2, Control control2, ref int y)
	{
		fieldLabels[control1] = AddLabel(label1, 0, y);
		control1.Location = new Point(110, y);
		control1.Size = new Size(205, (control1.Height > 28) ? control1.Height : 28);
		((Control)panel).Controls.Add(control1);
		if (!string.IsNullOrWhiteSpace(label2) && control2 != null)
		{
			fieldLabels[control2] = AddLabel(label2, 340, y);
			control2.Location = new Point(450, y);
			control2.Size = new Size(190, (control2.Height > 28) ? control2.Height : 28);
			((Control)panel).Controls.Add(control2);
		}
		y += Math.Max(control1.Height, (control2 == null) ? 28 : control2.Height) + 10;
	}

	private void AddFullWidthRow(string label, Control control, ref int y)
	{
		fieldLabels[control] = AddLabel(label, 0, y);
		control.Location = new Point(110, y);
		control.Size = new Size(530, (control.Height > 28) ? control.Height : 28);
		((Control)panel).Controls.Add(control);
		y += control.Height + 10;
	}

	private Label AddLabel(string text, int x, int y)
	{
		Label val = new Label
		{
			Text = text,
			Location = new Point(x, y + 4),
			Size = new Size(104, 22),
			TextAlign = (ContentAlignment)16
		};
		((Control)panel).Controls.Add((Control)(object)val);
		return val;
	}

	private void AddButton(string text, int x, int y, Action action, bool primary = false)
	{
		Button val = new Button
		{
			Text = text,
			Location = new Point(x, y),
			Size = new Size(82, 32)
		};
		if (primary)
		{
			((Control)val).BackColor = Color.FromArgb(32, 111, 203);
			((Control)val).ForeColor = Color.White;
			((ButtonBase)val).FlatStyle = (FlatStyle)0;
			((ButtonBase)val).FlatAppearance.BorderSize = 0;
		}
		((Control)val).Click += delegate
		{
			action();
		};
		((Control)this).Controls.Add((Control)(object)val);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void LoadTemplateList()
	{
		lstTemplates.Items.Clear();
		for (int i = 0; i < templateSet.Templates.Count; i++)
		{
			RedHeaderTemplate redHeaderTemplate = templateSet.Templates[i];
			lstTemplates.Items.Add((object)(((redHeaderTemplate.Id == templateSet.ActiveTemplateId) ? "● " : "") + redHeaderTemplate.Name));
		}
		int selectedIndex = 0;
		for (int j = 0; j < templateSet.Templates.Count; j++)
		{
			if (templateSet.Templates[j].Id == templateSet.ActiveTemplateId)
			{
				selectedIndex = j;
				break;
			}
		}
		if (lstTemplates.Items.Count > 0)
		{
			((ListControl)lstTemplates).SelectedIndex = selectedIndex;
		}
	}

	private void LoadCurrentTemplate()
	{
		RedHeaderTemplate currentTemplate = CurrentTemplate;
		if (currentTemplate != null)
		{
			((Control)txtName).Text = currentTemplate.Name;
			((Control)txtHeaderText).Text = currentTemplate.HeaderText;
			((Control)txtDocumentNumber).Text = currentTemplate.DocumentNumberText ?? "";
			RedHeaderTopMarkOptions redHeaderTopMarkOptions = currentTemplate.TopMarks ?? new RedHeaderTopMarkOptions();
			chkCopyNumber.Checked = redHeaderTopMarkOptions.CopyNumberEnabled;
			((Control)txtCopyNumber).Text = redHeaderTopMarkOptions.CopyNumber ?? "";
			SelectCombo(cboSecurityLevel, redHeaderTopMarkOptions.SecurityLevel);
			((Control)cboConfidentialityPeriod).Text = redHeaderTopMarkOptions.ConfidentialityPeriod ?? "";
			SelectCombo(cboUrgencyLevel, redHeaderTopMarkOptions.UrgencyLevel);
			((Control)txtTopMarkFont).Text = redHeaderTopMarkOptions.FontName;
			SetNumber(numTopMarkSize, redHeaderTopMarkOptions.FontSize);
			SetNumber(numTopMarkLineSpacing, redHeaderTopMarkOptions.LineSpacing);
			SetNumber(numTopMarkIndent, redHeaderTopMarkOptions.LeftIndentChars);
			((ListControl)cboTopMarkColor).SelectedIndex = ((redHeaderTopMarkOptions.Color == 255) ? 1 : 0);
			((Control)txtHeaderFont).Text = currentTemplate.HeaderFont;
			SetNumber(numHeaderSize, currentTemplate.HeaderSize);
			((ListControl)cboHeaderBold).SelectedIndex = (currentTemplate.HeaderBold ? 1 : 0);
			((ListControl)cboHeaderColor).SelectedIndex = HeaderColorToSelectedIndex(currentTemplate.HeaderColor);
			SelectCombo(cboHeaderAlignment, currentTemplate.HeaderAlignment);
			SetNumber(numHeaderLineSpacing, currentTemplate.HeaderLineSpacing);
			SetNumber(numHeaderBefore, currentTemplate.HeaderSpaceBefore);
			SetNumber(numHeaderAfter, currentTemplate.HeaderSpaceAfter);
			SetNumber(numHeaderIndent, currentTemplate.HeaderIndentChars);
			SetNumber(numHeaderWidth, currentTemplate.HeaderLayoutWidthPercent);
			SetNumber(numHeaderScale, currentTemplate.HeaderCharacterScalePercent);
			SelectCombo(cboHeaderFitMode, FitModeToUi(currentTemplate.HeaderFitMode));
			SetNumber(numHeaderMinimumScale, currentTemplate.HeaderMinimumScalePercent);
			SetNumber(numHeaderSpacing, currentTemplate.HeaderCharacterSpacing);
			((Control)txtDocNumberFont).Text = currentTemplate.DocumentNumberFont;
			SetNumber(numDocNumberSize, currentTemplate.DocumentNumberSize);
			SetNumber(numDocNumberLineSpacing, currentTemplate.DocumentNumberLineSpacing);
			SetNumber(numDocNumberBefore, currentTemplate.DocumentNumberSpaceBefore);
			SetNumber(numDocNumberAfter, currentTemplate.DocumentNumberSpaceAfter);
			SelectCombo(cboRedLineStyle, NormalizeRedLineStyleForUi(currentTemplate.RedLineStyle));
			SetNumber(numRedLineWidth, currentTemplate.RedLineWidthPercent);
			SetNumber(numRedLineThickness, currentTemplate.RedLineThickness);
			((ListControl)cboRedLineColor).SelectedIndex = RedLineColorToSelectedIndex(currentTemplate.RedLineColor);
			SetNumber(numRedLineBefore, currentTemplate.RedLineSpaceBefore);
			SetNumber(numRedLineAfter, currentTemplate.RedLineSpaceAfter);
			SetNumber(numTitleGapLines, currentTemplate.TitleGapLines);
			SetNumber(numTitleGapLineSpacing, currentTemplate.TitleGapLineSpacing);
			chkImprintEnabled.Checked = currentTemplate.ImprintEnabled;
			chkImprintEvenPage.Checked = currentTemplate.ImprintOnEvenPage;
			((Control)txtImprintFont).Text = currentTemplate.ImprintFont;
			SelectCombo(cboImprintSize, currentTemplate.ImprintSize);
			SetNumber(numImprintBottomOffset, currentTemplate.ImprintBottomOffset);
			SetNumber(numImprintCellPadding, currentTemplate.ImprintCellPaddingCm);
			((Control)txtImprintSend).Text = currentTemplate.ImprintSend ?? "";
			((Control)txtImprintOffice).Text = currentTemplate.ImprintOffice ?? "";
			((Control)txtImprintDate).Text = currentTemplate.ImprintDate ?? "";
			((ListControl)cboImprintDateMode).SelectedIndex = ((currentTemplate.ImprintDateMode == RedHeaderImprintDateMode.AutoToday) ? 1 : 0);
			chkUseTimesNewRomanForNumbers.Checked = currentTemplate.UseTimesNewRomanForNumbers;
			UpdateConditionalControls();
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private bool SaveCurrentTemplate()
	{
		int selectedIndex = ((ListControl)lstTemplates).SelectedIndex;
		RedHeaderTemplate currentTemplate = CurrentTemplate;
		if (currentTemplate == null || selectedIndex < 0)
		{
			return false;
		}
		if (string.IsNullOrWhiteSpace(((Control)txtName).Text))
		{
			AppleMessageDialog.Show((IWin32Window)(object)this, "请输入模板名称。", "提示", (MessageBoxButtons)0, (MessageBoxIcon)64);
			return false;
		}
		RedHeaderTemplate redHeaderTemplate = RedHeaderTemplateService.Clone(currentTemplate);
		redHeaderTemplate.Name = ((Control)txtName).Text.Trim();
		redHeaderTemplate.HeaderText = ((Control)txtHeaderText).Text.Trim();
		redHeaderTemplate.DocumentNumberText = ((Control)txtDocumentNumber).Text;
		redHeaderTemplate.TopMarks = new RedHeaderTopMarkOptions
		{
			CopyNumberEnabled = chkCopyNumber.Checked,
			CopyNumber = ((Control)txtCopyNumber).Text.Trim(),
			SecurityLevel = Convert.ToString(cboSecurityLevel.SelectedItem),
			ConfidentialityPeriod = ((((ListControl)cboSecurityLevel).SelectedIndex == 0) ? "" : ((Control)cboConfidentialityPeriod).Text.Trim()),
			UrgencyLevel = Convert.ToString(cboUrgencyLevel.SelectedItem),
			FontName = ((Control)txtTopMarkFont).Text.Trim(),
			FontSize = ToFloat(numTopMarkSize),
			LineSpacing = ToFloat(numTopMarkLineSpacing),
			LeftIndentChars = ToFloat(numTopMarkIndent),
			Color = ((((ListControl)cboTopMarkColor).SelectedIndex == 1) ? 255 : 0)
		};
		redHeaderTemplate.HeaderFont = ((Control)txtHeaderFont).Text.Trim();
		redHeaderTemplate.HeaderSize = ToFloat(numHeaderSize);
		redHeaderTemplate.HeaderBold = ((ListControl)cboHeaderBold).SelectedIndex == 1;
		redHeaderTemplate.HeaderColor = HeaderColorFromSelectedIndex(((ListControl)cboHeaderColor).SelectedIndex);
		redHeaderTemplate.HeaderAlignment = Convert.ToString(cboHeaderAlignment.SelectedItem);
		redHeaderTemplate.HeaderLineSpacing = ToFloat(numHeaderLineSpacing);
		redHeaderTemplate.HeaderSpaceBefore = ToFloat(numHeaderBefore);
		redHeaderTemplate.HeaderSpaceAfter = ToFloat(numHeaderAfter);
		redHeaderTemplate.HeaderIndentChars = ToFloat(numHeaderIndent);
		redHeaderTemplate.HeaderLayoutWidthPercent = ToFloat(numHeaderWidth);
		redHeaderTemplate.HeaderCharacterScalePercent = ToFloat(numHeaderScale);
		redHeaderTemplate.HeaderFitMode = FitModeFromUi(Convert.ToString(cboHeaderFitMode.SelectedItem));
		redHeaderTemplate.HeaderMinimumScalePercent = ToFloat(numHeaderMinimumScale);
		redHeaderTemplate.HeaderCharacterSpacing = ToFloat(numHeaderSpacing);
		redHeaderTemplate.DocumentNumberFont = ((Control)txtDocNumberFont).Text.Trim();
		redHeaderTemplate.DocumentNumberSize = ToFloat(numDocNumberSize);
		redHeaderTemplate.DocumentNumberLineSpacing = ToFloat(numDocNumberLineSpacing);
		redHeaderTemplate.DocumentNumberSpaceBefore = ToFloat(numDocNumberBefore);
		redHeaderTemplate.DocumentNumberSpaceAfter = ToFloat(numDocNumberAfter);
		redHeaderTemplate.RedLineStyle = NormalizeRedLineStyleForUi(Convert.ToString(cboRedLineStyle.SelectedItem));
		redHeaderTemplate.RedLineWidthPercent = ToFloat(numRedLineWidth);
		redHeaderTemplate.RedLineThickness = ToFloat(numRedLineThickness);
		redHeaderTemplate.RedLineColor = RedLineColorFromSelectedIndex(((ListControl)cboRedLineColor).SelectedIndex);
		redHeaderTemplate.RedLineSpaceBefore = ToFloat(numRedLineBefore);
		redHeaderTemplate.RedLineSpaceAfter = ToFloat(numRedLineAfter);
		redHeaderTemplate.TitleGapLines = Convert.ToInt32(numTitleGapLines.Value);
		redHeaderTemplate.TitleGapLineSpacing = ToFloat(numTitleGapLineSpacing);
		redHeaderTemplate.ImprintEnabled = chkImprintEnabled.Checked;
		redHeaderTemplate.ImprintOnEvenPage = chkImprintEvenPage.Checked;
		redHeaderTemplate.ImprintFont = ((Control)txtImprintFont).Text.Trim();
		redHeaderTemplate.ImprintSize = Convert.ToString(cboImprintSize.SelectedItem);
		redHeaderTemplate.ImprintBottomOffset = ToFloat(numImprintBottomOffset);
		redHeaderTemplate.ImprintCellPaddingCm = ToFloat(numImprintCellPadding);
		redHeaderTemplate.ImprintSend = ((Control)txtImprintSend).Text;
		redHeaderTemplate.ImprintOffice = ((Control)txtImprintOffice).Text.Trim();
		redHeaderTemplate.ImprintDate = ((Control)txtImprintDate).Text.Trim();
		redHeaderTemplate.ImprintDateMode = ((((ListControl)cboImprintDateMode).SelectedIndex == 1) ? RedHeaderImprintDateMode.AutoToday : RedHeaderImprintDateMode.Manual);
		redHeaderTemplate.UseTimesNewRomanForNumbers = chkUseTimesNewRomanForNumbers.Checked;
		redHeaderTemplate.ImprintDateUseTimesNewRoman = false;
		try
		{
			RedHeaderTemplateService.ValidateTemplate(redHeaderTemplate);
		}
		catch (Exception ex)
		{
			AppleMessageDialog.Show((IWin32Window)(object)this, ex.Message, "参数检查", (MessageBoxButtons)0, (MessageBoxIcon)64);
			return false;
		}
		templateSet.Templates[selectedIndex] = redHeaderTemplate;
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void AddTemplate()
	{
		SaveCurrentTemplate();
		RedHeaderTemplate item = new RedHeaderTemplate
		{
			Name = "新红头模板"
		};
		templateSet.Templates.Add(item);
		LoadTemplateList();
		((ListControl)lstTemplates).SelectedIndex = templateSet.Templates.Count - 1;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void CopyTemplate()
	{
		RedHeaderTemplate currentTemplate = CurrentTemplate;
		if (currentTemplate != null)
		{
			SaveCurrentTemplate();
			RedHeaderTemplate redHeaderTemplate = RedHeaderTemplateService.Clone(currentTemplate);
			redHeaderTemplate.Id = Guid.NewGuid().ToString("N");
			redHeaderTemplate.Name = currentTemplate.Name + " 副本";
			templateSet.Templates.Add(redHeaderTemplate);
			LoadTemplateList();
			((ListControl)lstTemplates).SelectedIndex = templateSet.Templates.Count - 1;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void DeleteTemplate()
	{
		if (CurrentTemplate == null)
		{
			return;
		}
		if (templateSet.Templates.Count <= 1)
		{
			AppleMessageDialog.Show((IWin32Window)(object)this, "至少保留一个红头模板。", "提示", (MessageBoxButtons)0, (MessageBoxIcon)64);
			return;
		}
		int selectedIndex = ((ListControl)lstTemplates).SelectedIndex;
		if ((int)AppleMessageDialog.Show((IWin32Window)(object)this, "确定删除当前红头模板吗？", "提示", (MessageBoxButtons)1, (MessageBoxIcon)32) == 1)
		{
			RedHeaderTemplate redHeaderTemplate = templateSet.Templates[selectedIndex];
			templateSet.Templates.RemoveAt(selectedIndex);
			if (templateSet.ActiveTemplateId == redHeaderTemplate.Id)
			{
				templateSet.ActiveTemplateId = templateSet.Templates[0].Id;
			}
			LoadTemplateList();
			((ListControl)lstTemplates).SelectedIndex = Math.Min(selectedIndex, templateSet.Templates.Count - 1);
		}
	}

	private void SetActiveTemplate()
	{
		if (SaveCurrentTemplate() && CurrentTemplate != null)
		{
			templateSet.ActiveTemplateId = CurrentTemplate.Id;
			LoadTemplateList();
		}
	}

	private void SaveAndClose()
	{
		if (SaveCurrentTemplate())
		{
			RedHeaderTemplateService.Save(templateSet);
			((Form)this).DialogResult = (DialogResult)1;
		}
	}

	private static NumericUpDown CreateNumber(decimal min, decimal max, decimal increment)
	{
		ScrollFriendlyNumericUpDown scrollFriendlyNumericUpDown = new ScrollFriendlyNumericUpDown();
		((NumericUpDown)scrollFriendlyNumericUpDown).Minimum = min;
		((NumericUpDown)scrollFriendlyNumericUpDown).Maximum = max;
		((NumericUpDown)scrollFriendlyNumericUpDown).Increment = increment;
		((NumericUpDown)scrollFriendlyNumericUpDown).DecimalPlaces = GetDecimalPlaces(increment);
		((NumericUpDown)scrollFriendlyNumericUpDown).ThousandsSeparator = false;
		return (NumericUpDown)(object)scrollFriendlyNumericUpDown;
	}

	private static int GetDecimalPlaces(decimal increment)
	{
		int val = (decimal.GetBits(increment)[3] >> 16) & 0x7F;
		return Math.Max(0, Math.Min(4, val));
	}

	private static ComboBox CreateCombo(params string[] items)
	{
		ScrollFriendlyComboBox scrollFriendlyComboBox = new ScrollFriendlyComboBox();
		((ComboBox)scrollFriendlyComboBox).DropDownStyle = (ComboBoxStyle)2;
		ScrollFriendlyComboBox scrollFriendlyComboBox2 = scrollFriendlyComboBox;
		((ComboBox)scrollFriendlyComboBox2).Items.AddRange((object[])items);
		if (((ComboBox)scrollFriendlyComboBox2).Items.Count > 0)
		{
			((ListControl)scrollFriendlyComboBox2).SelectedIndex = 0;
		}
		return (ComboBox)(object)scrollFriendlyComboBox2;
	}

	private static void RouteMouseWheelToScrollableParent(Control source, MouseEventArgs e)
	{
		for (Control val = ((source != null) ? source.Parent : null); val != null; val = val.Parent)
		{
			ScrollableControl val2 = (ScrollableControl)(object)((val is ScrollableControl) ? val : null);
			if (val2 != null && val2.AutoScroll)
			{
				int x = -val2.AutoScrollPosition.X;
				int num = -val2.AutoScrollPosition.Y;
				int num2 = SystemInformation.MouseWheelScrollLines;
				if (num2 <= 0)
				{
					num2 = 3;
				}
				int num3 = Math.Max(16, ((ScrollProperties)val2.VerticalScroll).SmallChange);
				int y = Math.Max(0, num - Math.Sign(e.Delta) * num3 * num2);
				val2.AutoScrollPosition = new Point(x, y);
				break;
			}
		}
	}

	private static void SetNumber(NumericUpDown number, float value)
	{
		decimal num = Convert.ToDecimal(value);
		if (num < number.Minimum)
		{
			num = number.Minimum;
		}
		if (num > number.Maximum)
		{
			num = number.Maximum;
		}
		number.Value = num;
	}

	private static float ToFloat(NumericUpDown number)
	{
		return Convert.ToSingle(number.Value);
	}

	private static void SelectCombo(ComboBox combo, string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			if (combo.Items.Count > 0)
			{
				((ListControl)combo).SelectedIndex = 0;
			}
			return;
		}
		int num = 0;
		while (true)
		{
			if (num >= combo.Items.Count)
			{
				if (combo.Items.Count > 0)
				{
					((ListControl)combo).SelectedIndex = 0;
				}
				return;
			}
			if (string.Equals(Convert.ToString(combo.Items[num]), value, StringComparison.OrdinalIgnoreCase))
			{
				break;
			}
			num++;
		}
		((ListControl)combo).SelectedIndex = num;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string FitModeToUi(string value)
	{
		if (!(value == "autoSingleLine"))
		{
			if (value == "allowWrap")
			{
				return "允许换行";
			}
			return "手动";
		}
		return "自动适应一行";
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string FitModeFromUi(string value)
	{
		if (value == "自动适应一行")
		{
			return "autoSingleLine";
		}
		if (value == "允许换行")
		{
			return "allowWrap";
		}
		return "manual";
	}

	private static int HeaderColorToSelectedIndex(int color)
	{
		return color switch
		{
			0 => 1, 
			16777215 => 2, 
			_ => 0, 
		};
	}

	private static int HeaderColorFromSelectedIndex(int selectedIndex)
	{
		return selectedIndex switch
		{
			1 => 0, 
			2 => 16777215, 
			_ => 255, 
		};
	}

	private static int RedLineColorToSelectedIndex(int color)
	{
		return color switch
		{
			16777215 => 2, 
			0 => 1, 
			_ => 0, 
		};
	}

	private static int RedLineColorFromSelectedIndex(int selectedIndex)
	{
		return selectedIndex switch
		{
			1 => 0, 
			2 => 16777215, 
			_ => 255, 
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void ConfigureRedLineStyleSelector()
	{
		cboRedLineStyle.DropDownStyle = (ComboBoxStyle)2;
		cboRedLineStyle.DrawMode = (DrawMode)1;
		cboRedLineStyle.ItemHeight = 34;
		cboRedLineStyle.DropDownWidth = 320;
		cboRedLineStyle.Items.AddRange(new object[6] { "normal", "star", "upperThickLowerThin", "lowerThickUpperThin", "singleTop", "singleBottom" });
		cboRedLineStyle.DrawItem += new DrawItemEventHandler(DrawRedLineStyleItem);
		cboRedLineStyle.SelectedIndexChanged += delegate
		{
			((Control)cboRedLineStyle).AccessibleDescription = RedLineStyleAccessibleName(Convert.ToString(cboRedLineStyle.SelectedItem));
			((Control)cboRedLineStyle).Invalidate();
		};
		cboRedLineColor.SelectedIndexChanged += delegate
		{
			((Control)cboRedLineStyle).Invalidate();
		};
		numRedLineThickness.ValueChanged += delegate
		{
			((Control)cboRedLineStyle).Invalidate();
		};
		((Control)cboRedLineStyle).AccessibleName = "红线样式";
		((ListControl)cboRedLineStyle).SelectedIndex = 0;
	}

	private void DrawRedLineStyleItem(object sender, DrawItemEventArgs e)
	{
		e.DrawBackground();
		if (e.Index >= 0 && e.Index < cboRedLineStyle.Items.Count)
		{
			string style = Convert.ToString(cboRedLineStyle.Items[e.Index]);
			Rectangle bounds = new Rectangle(e.Bounds.Left + 14, e.Bounds.Top + 5, Math.Max(40, e.Bounds.Width - 28), Math.Max(18, e.Bounds.Height - 10));
			DrawRedLinePreview(e.Graphics, bounds, style);
		}
		e.DrawFocusRectangle();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void DrawRedLinePreview(Graphics graphics, Rectangle bounds, string style)
	{
		graphics.SmoothingMode = (SmoothingMode)4;
		Color color = ((((ListControl)cboRedLineColor).SelectedIndex == 1) ? Color.Black : ((((ListControl)cboRedLineColor).SelectedIndex == 2) ? Color.White : Color.Red));
		float num = Math.Max(1f, Math.Min(4f, ToFloat(numRedLineThickness)));
		float num2 = (float)bounds.Top + (float)bounds.Height / 2f;
		float num3 = (float)bounds.Left + 4f;
		float num4 = (float)bounds.Right - 4f;
		switch (style)
		{
		case "star":
		{
			float num6 = Math.Min(8f, (float)bounds.Height / 2f - 1f);
			float num7 = (float)bounds.Left + (float)bounds.Width / 2f;
			Pen val4 = new Pen(color, Math.Max(1f, num * 0.65f));
			try
			{
				SolidBrush val5 = new SolidBrush(color);
				try
				{
					graphics.DrawLine(val4, num3, num2, num7 - num6 - 5f, num2);
					graphics.DrawLine(val4, num7 + num6 + 5f, num2, num4, num2);
					graphics.FillPolygon((Brush)(object)val5, CreateStarPoints(num7, num2, num6));
					break;
				}
				finally
				{
					((IDisposable)val5)?.Dispose();
				}
			}
			finally
			{
				((IDisposable)val4)?.Dispose();
			}
		}
		case "upperThickLowerThin":
		case "lowerThickUpperThin":
		{
			bool flag = style == "upperThickLowerThin";
			Pen val2 = new Pen(color, flag ? Math.Max(2f, num) : 1f);
			try
			{
				Pen val3 = new Pen(color, flag ? 1f : Math.Max(2f, num));
				try
				{
					graphics.DrawLine(val2, num3, num2 - 3f, num4, num2 - 3f);
					graphics.DrawLine(val3, num3, num2 + 3f, num4, num2 + 3f);
					break;
				}
				finally
				{
					((IDisposable)val3)?.Dispose();
				}
			}
			finally
			{
				((IDisposable)val2)?.Dispose();
			}
		}
		default:
		{
			float num5 = num2;
			if (style == "singleTop")
			{
				num5 = (float)bounds.Top + 4f;
			}
			if (style == "singleBottom")
			{
				num5 = (float)bounds.Bottom - 4f;
			}
			Pen val = new Pen(color, num);
			try
			{
				graphics.DrawLine(val, num3, num5, num4, num5);
				break;
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		}
		}
	}

	private static PointF[] CreateStarPoints(float centerX, float centerY, float outerRadius)
	{
		PointF[] array = new PointF[10];
		double num = -Math.PI / 2.0;
		for (int i = 0; i < array.Length; i++)
		{
			float num2 = ((i % 2 == 0) ? outerRadius : (outerRadius * 0.42f));
			array[i] = new PointF(centerX + (float)Math.Cos(num) * num2, centerY + (float)Math.Sin(num) * num2);
			num += Math.PI / 5.0;
		}
		return array;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string NormalizeRedLineStyleForUi(string style)
	{
		switch (style)
		{
		case "double":
			return "upperThickLowerThin";
		case "thick":
			return "lowerThickUpperThin";
		case "star":
		case "upperThickLowerThin":
		case "lowerThickUpperThin":
		case "singleTop":
		case "singleBottom":
			return style;
		default:
			return "normal";
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string RedLineStyleAccessibleName(string style)
	{
		return style switch
		{
			"star" => "带五角星分隔线", 
			"singleBottom" => "单横线，下方", 
			"upperThickLowerThin" => "上粗下细文武线", 
			"singleTop" => "单横线，上方", 
			"lowerThickUpperThin" => "下粗上细文武线", 
			_ => "普通公文分隔线", 
		};
	}
}
