using System.Collections.Generic;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using DocumentRepository.Models;
using DocumentRepository.Services.Replace;
using DocumentRepository.Services.UiText;

namespace DocumentRepository;

public class ReplaceRegexBuilderForm : Form
{
	private readonly ComboBox cboPreset = new ComboBox();

	private readonly Label lblDescription = new Label();

	private readonly TextBox txtSample = new TextBox();

	private readonly TextBox txtResult = new TextBox();

	private readonly TextBox txtPattern = new TextBox();

	private readonly TextBox txtReplacement = new TextBox();

	private readonly Label lblStatus = new Label();

	private readonly IList<ReplaceRegexPreset> presets;

	private readonly ToolTip toolTip = UiTextApplier.CreateToolTip();

	public string RuleName { get; private set; }

	public string Pattern { get; private set; }

	public string Replacement { get; private set; }

	public string SampleText { get; private set; }

	private ReplaceRegexPreset CurrentPreset
	{
		get
		{
			if (((ListControl)cboPreset).SelectedIndex < 0 || ((ListControl)cboPreset).SelectedIndex >= presets.Count)
			{
				return null;
			}
			return presets[((ListControl)cboPreset).SelectedIndex];
		}
	}

	public ReplaceRegexBuilderForm()
	{
		presets = ReplaceRegexPresetService.GetPresets();
		InitializeComponent();
		if (cboPreset.Items.Count > 0)
		{
			((ListControl)cboPreset).SelectedIndex = 0;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void InitializeComponent()
	{
		((Control)this).Text = "帮我创建正则";
		((Form)this).StartPosition = (FormStartPosition)4;
		((Form)this).ClientSize = new Size(744, 545);
		((Control)this).MinimumSize = new Size(760, 584);
		((Form)this).MaximizeBox = false;
		((Form)this).MinimizeBox = false;
		((Form)this).FormBorderStyle = (FormBorderStyle)3;
		((Control)this).Font = new Font("Microsoft YaHei UI", 9f);
		((Control)this).BackColor = AppleUiColors.Window;
		((Control)this).Controls.Add((Control)new Label
		{
			Text = "正则创建助手",
			Left = 22,
			Top = 18,
			AutoSize = true,
			Font = new Font(((Control)this).Font, (FontStyle)1),
			ForeColor = AppleUiColors.TextPrimary
		});
		((Control)this).Controls.Add((Control)new Label
		{
			Text = "选择常见用途",
			Left = 22,
			Top = 58,
			Width = 100
		});
		((Control)cboPreset).Left = 125;
		((Control)cboPreset).Top = 54;
		((Control)cboPreset).Width = 580;
		cboPreset.DropDownStyle = (ComboBoxStyle)2;
		foreach (ReplaceRegexPreset preset in presets)
		{
			cboPreset.Items.Add((object)preset.Name);
		}
		cboPreset.SelectedIndexChanged += delegate
		{
			LoadPreset();
		};
		((Control)this).Controls.Add((Control)(object)cboPreset);
		((Control)lblDescription).Left = 125;
		((Control)lblDescription).Top = 86;
		((Control)lblDescription).Width = 580;
		((Control)lblDescription).Height = 42;
		((Control)lblDescription).ForeColor = Color.DimGray;
		((Control)this).Controls.Add((Control)(object)lblDescription);
		((Control)this).Controls.Add((Control)new Label
		{
			Text = "原文示例",
			Left = 22,
			Top = 142,
			Width = 90
		});
		((Control)txtSample).Left = 125;
		((Control)txtSample).Top = 138;
		((Control)txtSample).Width = 580;
		((Control)txtSample).Height = 78;
		((TextBoxBase)txtSample).Multiline = true;
		txtSample.ScrollBars = (ScrollBars)2;
		((Control)txtSample).TextChanged += delegate
		{
			RefreshPreview();
		};
		((Control)this).Controls.Add((Control)(object)txtSample);
		((Control)this).Controls.Add((Control)new Label
		{
			Text = "转换结果",
			Left = 22,
			Top = 232,
			Width = 90
		});
		((Control)txtResult).Left = 125;
		((Control)txtResult).Top = 228;
		((Control)txtResult).Width = 580;
		((Control)txtResult).Height = 78;
		((TextBoxBase)txtResult).Multiline = true;
		txtResult.ScrollBars = (ScrollBars)2;
		((TextBoxBase)txtResult).ReadOnly = true;
		((Control)txtResult).BackColor = Color.White;
		((Control)this).Controls.Add((Control)(object)txtResult);
		AppleGroupBox appleGroupBox = new AppleGroupBox();
		((Control)appleGroupBox).Text = "生成的规则";
		((Control)appleGroupBox).Left = 22;
		((Control)appleGroupBox).Top = 326;
		((Control)appleGroupBox).Width = 683;
		((Control)appleGroupBox).Height = 130;
		AppleGroupBox appleGroupBox2 = appleGroupBox;
		((Control)this).Controls.Add((Control)(object)appleGroupBox2);
		((Control)appleGroupBox2).Controls.Add((Control)new Label
		{
			Text = "查找表达式",
			Left = 16,
			Top = 29,
			Width = 85
		});
		((Control)txtPattern).Left = 105;
		((Control)txtPattern).Top = 25;
		((Control)txtPattern).Width = 555;
		((TextBoxBase)txtPattern).ReadOnly = true;
		((Control)txtPattern).BackColor = Color.White;
		((Control)appleGroupBox2).Controls.Add((Control)(object)txtPattern);
		((Control)appleGroupBox2).Controls.Add((Control)new Label
		{
			Text = "替换表达式",
			Left = 16,
			Top = 72,
			Width = 85
		});
		((Control)txtReplacement).Left = 105;
		((Control)txtReplacement).Top = 68;
		((Control)txtReplacement).Width = 555;
		((TextBoxBase)txtReplacement).ReadOnly = true;
		((Control)txtReplacement).BackColor = Color.White;
		((Control)appleGroupBox2).Controls.Add((Control)(object)txtReplacement);
		((Control)lblStatus).Left = 22;
		((Control)lblStatus).Top = 474;
		((Control)lblStatus).Width = 445;
		((Control)lblStatus).Height = 46;
		((Control)this).Controls.Add((Control)(object)lblStatus);
		Button val = new Button
		{
			Text = "使用此规则",
			Left = 500,
			Top = 478,
			Width = 100,
			Height = 34,
			BackColor = Color.FromArgb(32, 105, 190),
			ForeColor = Color.White,
			FlatStyle = (FlatStyle)0
		};
		((Control)val).Click += delegate
		{
			AcceptPreset();
		};
		((Control)this).Controls.Add((Control)(object)val);
		Button val2 = new Button
		{
			Text = "取消",
			Left = 615,
			Top = 478,
			Width = 90,
			Height = 34,
			DialogResult = (DialogResult)2
		};
		((Control)this).Controls.Add((Control)(object)val2);
		((Form)this).CancelButton = (IButtonControl)(object)val2;
		toolTip.SetToolTip((Control)(object)cboPreset, "选择常见替换场景，自动生成对应的正则查找和替换表达式。");
		toolTip.SetToolTip((Control)(object)txtSample, "输入或修改示例文字，下方会实时显示转换结果。");
		toolTip.SetToolTip((Control)(object)txtResult, "显示当前规则对示例文字的转换结果，不会修改文档。");
		toolTip.SetToolTip((Control)(object)txtPattern, "显示将写入替换规则的正则查找表达式。");
		toolTip.SetToolTip((Control)(object)txtReplacement, "显示将写入替换规则的正则替换表达式。");
		toolTip.SetToolTip((Control)(object)val, "将生成的表达式带回替换规则编辑页面。");
		toolTip.SetToolTip((Control)(object)val2, "放弃本次正则规则创建。");
		AppleFormStyler.Apply((Form)(object)this, toolTip);
	}

	private void LoadPreset()
	{
		ReplaceRegexPreset currentPreset = CurrentPreset;
		if (currentPreset != null)
		{
			((Control)lblDescription).Text = currentPreset.Description;
			((Control)txtPattern).Text = currentPreset.Pattern;
			((Control)txtReplacement).Text = currentPreset.Replacement;
			((Control)txtSample).Text = currentPreset.SampleText;
			RefreshPreview();
		}
	}

	private void RefreshPreview()
	{
		ReplaceRegexPreset currentPreset = CurrentPreset;
		if (currentPreset != null)
		{
			ReplacePreviewResult replacePreviewResult = ReplaceRegexService.Preview(ReplaceRegexPresetService.CreateRule(currentPreset), ((Control)txtSample).Text);
			((Control)txtResult).Text = replacePreviewResult.AfterText ?? string.Empty;
			((Control)lblStatus).Text = replacePreviewResult.Message;
			((Control)lblStatus).ForeColor = (replacePreviewResult.Success ? Color.FromArgb(0, 128, 70) : Color.Firebrick);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void AcceptPreset()
	{
		ReplaceRegexPreset currentPreset = CurrentPreset;
		if (currentPreset != null)
		{
			ReplacePreviewResult replacePreviewResult = ReplaceRegexService.Preview(ReplaceRegexPresetService.CreateRule(currentPreset), ((Control)txtSample).Text);
			if (replacePreviewResult.Success)
			{
				RuleName = currentPreset.Name;
				Pattern = currentPreset.Pattern;
				Replacement = currentPreset.Replacement;
				SampleText = ((Control)txtSample).Text;
				((Form)this).DialogResult = (DialogResult)1;
				((Form)this).Close();
			}
			else
			{
				AppleMessageDialog.Show((IWin32Window)(object)this, replacePreviewResult.Message, "规则检查", (MessageBoxButtons)0, (MessageBoxIcon)64);
			}
		}
	}
}
