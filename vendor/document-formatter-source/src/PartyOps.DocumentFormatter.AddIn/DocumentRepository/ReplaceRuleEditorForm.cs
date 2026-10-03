using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using DocumentRepository.Models;
using DocumentRepository.Services.Replace;
using DocumentRepository.Services.Ui;
using DocumentRepository.Services.UiText;

namespace DocumentRepository;

public class ReplaceRuleEditorForm : Form
{
	private readonly TextBox txtRuleName = new TextBox();

	private readonly ComboBox cboRuleType = new ComboBox();

	private readonly TextBox txtFind = new TextBox();

	private readonly TextBox txtReplace = new TextBox();

	private readonly CheckBox chkEnabled = new CheckBox();

	private readonly Button btnRegexBuilder = new Button();

	private readonly Button btnAdvanced = new Button();

	private readonly Panel advancedPanel = new Panel();

	private readonly GroupBox previewGroup = (GroupBox)(object)new AppleGroupBox();

	private readonly TextBox txtSample = new TextBox();

	private readonly TextBox txtPreview = new TextBox();

	private readonly Label lblValidation = new Label();

	private readonly Button btnOk = new Button();

	private readonly Button btnCancel = new Button();

	private readonly Panel contentPanel = new Panel();

	private readonly Panel footerPanel = new Panel();

	private readonly ComboBox cmbFindFont = new ComboBox();

	private readonly ComboBox cmbFindSize = new ComboBox();

	private readonly ComboBox cmbFindBold = new ComboBox();

	private readonly ComboBox cmbFindAlign = new ComboBox();

	private readonly ComboBox cmbFindOutline = new ComboBox();

	private readonly ComboBox cmbFindFirstIndent = new ComboBox();

	private readonly ComboBox cmbFindLineSpacing = new ComboBox();

	private readonly ComboBox cmbTargetFont = new ComboBox();

	private readonly ComboBox cmbTargetSize = new ComboBox();

	private readonly ComboBox cmbTargetBold = new ComboBox();

	private readonly ComboBox cmbTargetAlign = new ComboBox();

	private readonly ComboBox cmbTargetOutline = new ComboBox();

	private readonly ComboBox cmbTargetFirstIndent = new ComboBox();

	private readonly ComboBox cmbTargetLineSpacing = new ComboBox();

	private readonly ToolTip toolTip = UiTextApplier.CreateToolTip();

	public ReplaceRule Rule { get; private set; }

	public ReplaceRuleEditorForm(ReplaceRule source)
	{
		Rule = ReplaceRuleNormalizer.Clone(source ?? new ReplaceRule
		{
			Enabled = true
		});
		InitializeComponent();
		LoadRule();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void InitializeComponent()
	{
		((Control)this).Text = "编辑替换条目";
		((Form)this).StartPosition = (FormStartPosition)4;
		((Form)this).ClientSize = new Size(960, 450);
		((Form)this).MaximizeBox = false;
		((Form)this).MinimizeBox = false;
		((Form)this).FormBorderStyle = (FormBorderStyle)3;
		((Control)this).Font = new Font("Microsoft YaHei UI", 9f);
		((ContainerControl)this).AutoScaleMode = (AutoScaleMode)2;
		((Control)this).BackColor = AppleUiColors.Window;
		((Control)footerPanel).Dock = (DockStyle)2;
		((Control)footerPanel).Height = 56;
		((Control)footerPanel).BackColor = AppleUiColors.SurfaceMuted;
		((Control)contentPanel).Dock = (DockStyle)0;
		((Control)contentPanel).SetBounds(0, 0, ((Form)this).ClientSize.Width, ((Form)this).ClientSize.Height - ((Control)footerPanel).Height);
		((Control)contentPanel).Anchor = (AnchorStyles)15;
		((ScrollableControl)contentPanel).AutoScroll = true;
		((Control)contentPanel).BackColor = AppleUiColors.Window;
		((Control)this).Controls.Add((Control)(object)contentPanel);
		((Control)this).Controls.Add((Control)(object)footerPanel);
		AppleGroupBox appleGroupBox = new AppleGroupBox();
		((Control)appleGroupBox).Text = "基本设置";
		((Control)appleGroupBox).Left = 16;
		((Control)appleGroupBox).Top = 12;
		((Control)appleGroupBox).Width = 928;
		((Control)appleGroupBox).Height = 184;
		AppleGroupBox appleGroupBox2 = appleGroupBox;
		((Control)contentPanel).Controls.Add((Control)(object)appleGroupBox2);
		AddLabel((Control)(object)appleGroupBox2, "条目名称", 18, 28, 78);
		((Control)txtRuleName).SetBounds(98, 24, 390, 25);
		((Control)appleGroupBox2).Controls.Add((Control)(object)txtRuleName);
		AddLabel((Control)(object)appleGroupBox2, "条目类型", 520, 28, 78);
		((Control)cboRuleType).SetBounds(600, 24, 195, 25);
		cboRuleType.DropDownStyle = (ComboBoxStyle)2;
		cboRuleType.Items.AddRange(new object[4] { "普通文字替换", "正则表达式替换", "WPS通配符替换", "仅修改格式" });
		cboRuleType.SelectedIndexChanged += delegate
		{
			UpdateMode();
		};
		((Control)appleGroupBox2).Controls.Add((Control)(object)cboRuleType);
		((Control)chkEnabled).Text = "启用本条目";
		((Control)chkEnabled).SetBounds(815, 25, 100, 24);
		((Control)appleGroupBox2).Controls.Add((Control)(object)chkEnabled);
		AddLabel((Control)(object)appleGroupBox2, "查找内容", 18, 72, 78);
		((Control)txtFind).SetBounds(98, 68, 610, 25);
		((Control)txtFind).TextChanged += delegate
		{
			RefreshPreview();
		};
		((Control)appleGroupBox2).Controls.Add((Control)(object)txtFind);
		((Control)btnRegexBuilder).Text = "帮我创建正则";
		((Control)btnRegexBuilder).SetBounds(724, 66, 150, 30);
		((Control)btnRegexBuilder).Click += delegate
		{
			OpenRegexBuilder();
		};
		((Control)appleGroupBox2).Controls.Add((Control)(object)btnRegexBuilder);
		AddLabel((Control)(object)appleGroupBox2, "替换为", 18, 112, 78);
		((Control)txtReplace).SetBounds(98, 108, 776, 25);
		((Control)txtReplace).TextChanged += delegate
		{
			RefreshPreview();
		};
		((Control)appleGroupBox2).Controls.Add((Control)(object)txtReplace);
		Label val = new Label
		{
			Text = "不填写替换内容表示删除匹配文字。正则使用 $1，WPS 通配符使用 \\1 引用分组。",
			Left = 98,
			Top = 140,
			Width = 776,
			Height = 28,
			ForeColor = Color.DimGray
		};
		((Control)appleGroupBox2).Controls.Add((Control)(object)val);
		((Control)btnAdvanced).Text = "▶ 高级格式设置";
		((Control)btnAdvanced).SetBounds(16, 207, 145, 30);
		((Control)btnAdvanced).Click += delegate
		{
			ToggleAdvanced();
		};
		((Control)contentPanel).Controls.Add((Control)(object)btnAdvanced);
		((Control)advancedPanel).SetBounds(16, 244, 928, 220);
		((Control)advancedPanel).Visible = false;
		((Control)contentPanel).Controls.Add((Control)(object)advancedPanel);
		AppleGroupBox appleGroupBox3 = new AppleGroupBox();
		((Control)appleGroupBox3).Text = "查找格式（不限表示不限制）";
		((Control)appleGroupBox3).Left = 0;
		((Control)appleGroupBox3).Top = 0;
		((Control)appleGroupBox3).Width = 450;
		((Control)appleGroupBox3).Height = 212;
		AppleGroupBox appleGroupBox4 = appleGroupBox3;
		AppleGroupBox appleGroupBox5 = new AppleGroupBox();
		((Control)appleGroupBox5).Text = "替换后的格式（不修改表示保持原样）";
		((Control)appleGroupBox5).Left = 478;
		((Control)appleGroupBox5).Top = 0;
		((Control)appleGroupBox5).Width = 450;
		((Control)appleGroupBox5).Height = 212;
		AppleGroupBox appleGroupBox6 = appleGroupBox5;
		((Control)advancedPanel).Controls.Add((Control)(object)appleGroupBox4);
		((Control)advancedPanel).Controls.Add((Control)(object)appleGroupBox6);
		SetupFontCombo(cmbFindFont, "不限");
		SetupCombo(cmbFindSize, "不限", "二号", "三号", "四号", "28", "32");
		SetupCombo(cmbFindBold, "不限", "加粗", "不加粗");
		SetupCombo(cmbFindAlign, "不限", "居中", "两端对齐", "左对齐", "右对齐");
		SetupCombo(cmbFindOutline, "不限", "正文", "1级", "2级", "3级");
		SetupCombo(cmbFindFirstIndent, "不限", "0", "2");
		SetupCombo(cmbFindLineSpacing, "不限", "28", "32");
		AddFormatRows((GroupBox)(object)appleGroupBox4, new string[7] { "字体", "字号", "加粗", "对齐", "大纲", "首行缩进", "行距" }, (ComboBox[])(object)new ComboBox[7] { cmbFindFont, cmbFindSize, cmbFindBold, cmbFindAlign, cmbFindOutline, cmbFindFirstIndent, cmbFindLineSpacing });
		SetupFontCombo(cmbTargetFont, "不修改");
		SetupCombo(cmbTargetSize, "不修改", "二号", "三号", "四号", "28", "32");
		SetupCombo(cmbTargetBold, "不修改", "加粗", "不加粗");
		SetupCombo(cmbTargetAlign, "不修改", "居中", "两端对齐", "左对齐", "右对齐");
		SetupCombo(cmbTargetOutline, "不修改", "正文", "1级", "2级", "3级");
		SetupCombo(cmbTargetFirstIndent, "不修改", "0", "2");
		SetupCombo(cmbTargetLineSpacing, "不修改", "28", "32");
		AddFormatRows((GroupBox)(object)appleGroupBox6, new string[7] { "字体", "字号", "加粗", "对齐", "大纲", "首行缩进", "行距" }, (ComboBox[])(object)new ComboBox[7] { cmbTargetFont, cmbTargetSize, cmbTargetBold, cmbTargetAlign, cmbTargetOutline, cmbTargetFirstIndent, cmbTargetLineSpacing });
		((Control)previewGroup).Text = "效果预览（只处理示例，不修改文档）";
		((Control)contentPanel).Controls.Add((Control)(object)previewGroup);
		((Control)previewGroup).Controls.Add((Control)new Label
		{
			Text = "原文示例",
			Left = 16,
			Top = 28,
			Width = 70
		});
		((Control)txtSample).SetBounds(88, 24, 350, 60);
		((TextBoxBase)txtSample).Multiline = true;
		txtSample.ScrollBars = (ScrollBars)2;
		((Control)txtSample).TextChanged += delegate
		{
			RefreshPreview();
		};
		((Control)previewGroup).Controls.Add((Control)(object)txtSample);
		((Control)previewGroup).Controls.Add((Control)new Label
		{
			Text = "替换后",
			Left = 458,
			Top = 28,
			Width = 60
		});
		((Control)txtPreview).SetBounds(520, 24, 384, 60);
		((TextBoxBase)txtPreview).Multiline = true;
		txtPreview.ScrollBars = (ScrollBars)2;
		((TextBoxBase)txtPreview).ReadOnly = true;
		((Control)txtPreview).BackColor = Color.White;
		((Control)previewGroup).Controls.Add((Control)(object)txtPreview);
		((Control)lblValidation).SetBounds(88, 91, 816, 24);
		((Control)previewGroup).Controls.Add((Control)(object)lblValidation);
		((Control)btnOk).Text = "确定";
		((Control)btnOk).Size = new Size(100, 34);
		((Control)btnOk).BackColor = Color.FromArgb(32, 105, 190);
		((Control)btnOk).ForeColor = Color.White;
		((ButtonBase)btnOk).FlatStyle = (FlatStyle)0;
		((Control)btnOk).Click += delegate
		{
			SaveRule();
		};
		((Control)footerPanel).Controls.Add((Control)(object)btnOk);
		((Control)btnCancel).Text = "取消";
		((Control)btnCancel).Size = new Size(100, 34);
		btnCancel.DialogResult = (DialogResult)2;
		((Control)footerPanel).Controls.Add((Control)(object)btnCancel);
		((Control)btnOk).SetBounds(724, 10, 100, 34);
		((Control)btnCancel).SetBounds(844, 10, 100, 34);
		((Control)btnOk).Anchor = (AnchorStyles)9;
		((Control)btnCancel).Anchor = (AnchorStyles)9;
		((Control)footerPanel).BringToFront();
		((Form)this).AcceptButton = (IButtonControl)(object)btnOk;
		((Form)this).CancelButton = (IButtonControl)(object)btnCancel;
		LayoutLowerControls();
		ConfigureTooltips((GroupBox)(object)appleGroupBox2, (GroupBox)(object)appleGroupBox4, (GroupBox)(object)appleGroupBox6);
		toolTip.SetToolTip((Control)(object)btnAdvanced, "展开或收起查找格式和替换后格式的高级条件。");
		toolTip.SetToolTip((Control)(object)btnOk, "保存当前替换条目并返回替换方案页面。");
		toolTip.SetToolTip((Control)(object)btnCancel, "放弃本次替换条目修改。");
		AppleFormStyler.Apply((Form)(object)this, toolTip);
		((Control)footerPanel).BringToFront();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void LoadRule()
	{
		ReplaceRuleNormalizer.Normalize(Rule);
		((Control)txtRuleName).Text = Rule.Name;
		chkEnabled.Checked = Rule.Enabled;
		((Control)txtFind).Text = Rule.FindText ?? string.Empty;
		((Control)txtReplace).Text = Rule.ReplaceText ?? string.Empty;
		((ListControl)cboRuleType).SelectedIndex = (Rule.FormatOnly ? 3 : (Rule.UseWildcard ? 2 : (Rule.UseRegex ? 1 : 0)));
		Set(cmbFindFont, Rule.FindFormat.FontName, "不限");
		Set(cmbFindSize, Rule.FindFormat.SizeText, "不限");
		Set(cmbFindBold, Rule.FindFormat.Bold, "不限");
		Set(cmbFindAlign, Rule.FindFormat.Alignment, "不限");
		Set(cmbFindOutline, Rule.FindFormat.OutlineLevel, "不限");
		Set(cmbFindFirstIndent, Rule.FindFormat.FirstIndentChars, "不限");
		Set(cmbFindLineSpacing, Rule.FindFormat.LineSpacing, "不限");
		Set(cmbTargetFont, Rule.ReplaceFormat.FontName, "不修改");
		Set(cmbTargetSize, Rule.ReplaceFormat.SizeText, "不修改");
		Set(cmbTargetBold, Rule.ReplaceFormat.Bold, "不修改");
		Set(cmbTargetAlign, Rule.ReplaceFormat.Alignment, "不修改");
		Set(cmbTargetOutline, Rule.ReplaceFormat.OutlineLevel, "不修改");
		Set(cmbTargetFirstIndent, Rule.ReplaceFormat.FirstIndentChars, "不修改");
		Set(cmbTargetLineSpacing, Rule.ReplaceFormat.LineSpacing, "不修改");
		ReplaceRegexPreset replaceRegexPreset = ReplaceRegexPresetService.GetPresets().FirstOrDefault((ReplaceRegexPreset p) => p.Pattern == Rule.FindText && p.Replacement == Rule.ReplaceText);
		((Control)txtSample).Text = ((replaceRegexPreset != null) ? replaceRegexPreset.SampleText : ((!Rule.UseRegex && !Rule.UseWildcard) ? Rule.FindText : string.Empty));
		UpdateMode();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void UpdateMode()
	{
		bool flag = ((ListControl)cboRuleType).SelectedIndex == 3;
		bool visible = ((ListControl)cboRuleType).SelectedIndex == 1;
		bool flag2 = ((ListControl)cboRuleType).SelectedIndex == 2;
		((Control)txtFind).Enabled = !flag;
		((Control)txtReplace).Enabled = !flag;
		((Control)btnRegexBuilder).Visible = visible;
		((Control)btnAdvanced).Enabled = !flag2;
		if (flag2 && ((Control)advancedPanel).Visible)
		{
			((Control)advancedPanel).Visible = false;
			((Control)btnAdvanced).Text = "▶ 高级格式设置";
			LayoutLowerControls();
		}
		if (flag && !((Control)advancedPanel).Visible)
		{
			ToggleAdvanced();
		}
		RefreshPreview();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void ToggleAdvanced()
	{
		((Control)advancedPanel).Visible = !((Control)advancedPanel).Visible;
		((Control)btnAdvanced).Text = (((Control)advancedPanel).Visible ? "▼ 高级格式设置" : "▶ 高级格式设置");
		LayoutLowerControls();
	}

	private void LayoutLowerControls()
	{
		int num = (((Control)advancedPanel).Visible ? 472 : 244);
		int num2 = (((Control)advancedPanel).Visible ? 92 : 124);
		((Control)previewGroup).SetBounds(16, num, 928, num2);
		((Control)txtSample).Height = (((Control)advancedPanel).Visible ? 35 : 60);
		((Control)txtPreview).Height = (((Control)advancedPanel).Visible ? 35 : 60);
		((Control)lblValidation).Top = (((Control)advancedPanel).Visible ? 63 : 91);
		int num3 = num + num2 + 10;
		((ScrollableControl)contentPanel).AutoScrollMinSize = new Size(0, num3);
		int val = Math.Max(450, Screen.FromControl((Control)(object)this).WorkingArea.Height - 80);
		int val2 = num3 + ((Control)footerPanel).Height;
		((Form)this).ClientSize = new Size(960, Math.Min(val2, val));
		((Control)contentPanel).SetBounds(0, 0, ((Form)this).ClientSize.Width, Math.Max(1, ((Form)this).ClientSize.Height - ((Control)footerPanel).Height));
	}

	private void OpenRegexBuilder()
	{
		ReplaceRegexBuilderForm replaceRegexBuilderForm = new ReplaceRegexBuilderForm();
		try
		{
			if ((int)((Form)replaceRegexBuilderForm).ShowDialog((IWin32Window)(object)this) == 1)
			{
				((Control)txtRuleName).Text = replaceRegexBuilderForm.RuleName;
				((Control)txtFind).Text = replaceRegexBuilderForm.Pattern;
				((Control)txtReplace).Text = replaceRegexBuilderForm.Replacement;
				((Control)txtSample).Text = replaceRegexBuilderForm.SampleText;
				((ListControl)cboRuleType).SelectedIndex = 1;
			}
		}
		finally
		{
			((IDisposable)replaceRegexBuilderForm)?.Dispose();
		}
	}

	private ReplaceRule BuildRuleFromUi()
	{
		bool flag = ((ListControl)cboRuleType).SelectedIndex == 3;
		return new ReplaceRule
		{
			Name = ((Control)txtRuleName).Text.Trim(),
			Enabled = chkEnabled.Checked,
			FindText = (flag ? string.Empty : (((Control)txtFind).Text ?? string.Empty)),
			ReplaceText = (flag ? string.Empty : (((Control)txtReplace).Text ?? string.Empty)),
			FormatOnly = flag,
			UseRegex = (!flag && ((ListControl)cboRuleType).SelectedIndex == 1),
			UseWildcard = (!flag && ((ListControl)cboRuleType).SelectedIndex == 2),
			FindFormat = new ReplaceFormatCondition
			{
				FontName = ((Control)cmbFindFont).Text,
				SizeText = ((Control)cmbFindSize).Text,
				Bold = ((Control)cmbFindBold).Text,
				Alignment = ((Control)cmbFindAlign).Text,
				OutlineLevel = ((Control)cmbFindOutline).Text,
				FirstIndentChars = ((Control)cmbFindFirstIndent).Text,
				LineSpacing = ((Control)cmbFindLineSpacing).Text
			},
			ReplaceFormat = new ReplaceFormatTarget
			{
				FontName = ((Control)cmbTargetFont).Text,
				SizeText = ((Control)cmbTargetSize).Text,
				Bold = ((Control)cmbTargetBold).Text,
				Alignment = ((Control)cmbTargetAlign).Text,
				OutlineLevel = ((Control)cmbTargetOutline).Text,
				FirstIndentChars = ((Control)cmbTargetFirstIndent).Text,
				LineSpacing = ((Control)cmbTargetLineSpacing).Text
			}
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void RefreshPreview()
	{
		if (((ListControl)cboRuleType).SelectedIndex < 0)
		{
			return;
		}
		if (((ListControl)cboRuleType).SelectedIndex == 3)
		{
			((Control)txtPreview).Text = "格式条目不修改文字，将在实际执行时按格式条件核验。";
			((Control)lblValidation).Text = "请在高级格式设置中填写查找格式和替换后的格式。";
			((Control)lblValidation).ForeColor = Color.DimGray;
			return;
		}
		if (((ListControl)cboRuleType).SelectedIndex == 2)
		{
			((Control)txtPreview).Text = "WPS 通配符由 WPS 原生查找引擎执行，请保存条目后在文档中运行。";
			((Control)lblValidation).Text = "通配符分组在替换内容中使用 \\1、\\2 引用。";
			((Control)lblValidation).ForeColor = Color.DimGray;
			return;
		}
		ReplaceRule replaceRule = BuildRuleFromUi();
		if (string.IsNullOrWhiteSpace(replaceRule.FindText))
		{
			((TextBoxBase)txtPreview).Clear();
			((Control)lblValidation).Text = "请填写查找内容。";
			((Control)lblValidation).ForeColor = Color.DimGray;
		}
		else
		{
			ReplacePreviewResult replacePreviewResult = ReplaceRegexService.Preview(replaceRule, ((Control)txtSample).Text);
			((Control)txtPreview).Text = replacePreviewResult.AfterText ?? string.Empty;
			((Control)lblValidation).Text = replacePreviewResult.Message;
			((Control)lblValidation).ForeColor = (replacePreviewResult.Success ? Color.FromArgb(0, 128, 70) : Color.Firebrick);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void SaveRule()
	{
		ReplaceRule rule = BuildRuleFromUi();
		IList<string> list = ReplaceRuleValidationService.Validate(rule);
		if (list.Count <= 0)
		{
			Rule = rule;
			ReplaceRuleNormalizer.Normalize(Rule);
			((Form)this).DialogResult = (DialogResult)1;
			((Form)this).Close();
		}
		else
		{
			AppleMessageDialog.Show((IWin32Window)(object)this, string.Join(Environment.NewLine, list), "条目检查", (MessageBoxButtons)0, (MessageBoxIcon)64);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void ConfigureTooltips(GroupBox basic, GroupBox find, GroupBox target)
	{
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)basic, "Replace.RuleName", "条目名称", (Control)txtRuleName);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)basic, "Replace.RuleType", "条目类型", (Control)cboRuleType);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)basic, "Replace.FindText", "查找内容", (Control)txtFind);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)basic, "Replace.TargetText", "替换为", (Control)txtReplace);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)basic, "Replace.Enabled", "启用本条目", (Control)chkEnabled);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)find, "Replace.FindFormat", ((Control)find).Text, (Control)cmbFindFont, (Control)cmbFindSize, (Control)cmbFindBold, (Control)cmbFindAlign, (Control)cmbFindOutline, (Control)cmbFindFirstIndent, (Control)cmbFindLineSpacing);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)target, "Replace.TargetFormat", ((Control)target).Text, (Control)cmbTargetFont, (Control)cmbTargetSize, (Control)cmbTargetBold, (Control)cmbTargetAlign, (Control)cmbTargetOutline, (Control)cmbTargetFirstIndent, (Control)cmbTargetLineSpacing);
		UiTextApplier.ApplyTooltip(toolTip, (Control)(object)btnRegexBuilder, "Replace.RegexBuilder");
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)previewGroup, "Replace.Preview", ((Control)previewGroup).Text, (Control)txtSample, (Control)txtPreview);
	}

	private static void AddLabel(Control parent, string text, int x, int y, int width)
	{
		parent.Controls.Add((Control)new Label
		{
			Text = text,
			Left = x,
			Top = y + 4,
			Width = width,
			Height = 24
		});
	}

	private static void SetupCombo(ComboBox combo, params string[] values)
	{
		combo.DropDownStyle = (ComboBoxStyle)1;
		combo.Items.AddRange((object[])values);
		((Control)combo).Text = ((values.Length != 0) ? values[0] : string.Empty);
	}

	private static void SetupFontCombo(ComboBox combo, string sentinel)
	{
		combo.DropDownStyle = (ComboBoxStyle)1;
		combo.Items.Add((object)sentinel);
		ComboBox.ObjectCollection items = combo.Items;
		object[] preferredFonts = SystemFontCatalog.PreferredFonts;
		items.AddRange(preferredFonts);
		((Control)combo).Text = sentinel;
		combo.DropDown += delegate
		{
			PopulateAllFonts(combo, sentinel);
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void PopulateAllFonts(ComboBox combo, string sentinel)
	{
		if (combo == null || ((Control)combo).Tag as string == "all-fonts")
		{
			return;
		}
		string text = ((Control)combo).Text;
		combo.BeginUpdate();
		try
		{
			combo.Items.Clear();
			combo.Items.Add((object)sentinel);
			ComboBox.ObjectCollection items = combo.Items;
			object[] preferredFonts = SystemFontCatalog.PreferredFonts;
			items.AddRange(preferredFonts);
			if (!string.IsNullOrWhiteSpace(text) && !string.Equals(text, sentinel, StringComparison.OrdinalIgnoreCase) && !combo.Items.Contains((object)text))
			{
				combo.Items.Add((object)text);
			}
			combo.Items.Add((object)"──────────────");
			ComboBox.ObjectCollection items2 = combo.Items;
			preferredFonts = SystemFontCatalog.GetInstalledAdditionalFonts();
			items2.AddRange(preferredFonts);
			((Control)combo).Text = text;
			((Control)combo).Tag = "all-fonts";
		}
		finally
		{
			combo.EndUpdate();
		}
	}

	private static void AddFormatRows(GroupBox group, string[] labels, ComboBox[] controls)
	{
		for (int i = 0; i < labels.Length; i++)
		{
			((Control)group).Controls.Add((Control)new Label
			{
				Text = labels[i],
				Left = 20,
				Top = 26 + i * 26,
				Width = 80
			});
			((Control)controls[i]).SetBounds(108, 22 + i * 26, 310, 25);
			((Control)group).Controls.Add((Control)(object)controls[i]);
		}
	}

	private static void Set(ComboBox combo, string value, string fallback)
	{
		((Control)combo).Text = (string.IsNullOrWhiteSpace(value) ? fallback : value);
	}
}
