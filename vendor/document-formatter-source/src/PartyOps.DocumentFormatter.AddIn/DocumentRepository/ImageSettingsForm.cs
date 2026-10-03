using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using DocumentRepository.Services.UiText;

namespace DocumentRepository;

public sealed class ImageSettingsForm : Form
{
	private readonly FormatConfig config;

	private readonly ImageFormatOptions workingOptions;

	private readonly ComboBox cmbSizeMode = new ComboBox();

	private readonly ComboBox cmbWrapMode = new ComboBox();

	private readonly ComboBox cmbAlignmentMode = new ComboBox();

	private readonly ComboBox cmbBorderMode = new ComboBox();

	private readonly CheckBox chkKeepAspectRatio = new CheckBox();

	private readonly CheckBox chkMainStoryOnly = new CheckBox();

	private readonly CheckBox chkIncludeTableCellImages = new CheckBox();

	private readonly NumericUpDown nudWidth = new NumericUpDown();

	private readonly NumericUpDown nudHeight = new NumericUpDown();

	private readonly NumericUpDown nudMaxWidth = new NumericUpDown();

	private readonly NumericUpDown nudMaxHeight = new NumericUpDown();

	private readonly NumericUpDown nudScalePercent = new NumericUpDown();

	private readonly ToolTip toolTip = UiTextApplier.CreateToolTip();

	[MethodImpl(MethodImplOptions.NoInlining)]
	public ImageSettingsForm(FormatConfig config)
	{
		this.config = config ?? throw new ArgumentNullException("config");
		if (this.config.ImageOptions == null)
		{
			this.config.ImageOptions = new ImageFormatOptions();
		}
		workingOptions = ConfigManager.CloneImageOptions(this.config.ImageOptions);
		InitializeComponent();
		LoadValues();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void InitializeComponent()
	{
		((Control)this).Text = "图片排版参数";
		((Form)this).StartPosition = (FormStartPosition)4;
		((Form)this).FormBorderStyle = (FormBorderStyle)3;
		((Form)this).MaximizeBox = false;
		((Form)this).MinimizeBox = false;
		((Form)this).ShowInTaskbar = false;
		((ContainerControl)this).AutoScaleMode = (AutoScaleMode)2;
		((Form)this).ClientSize = new Size(650, 470);
		((Control)this).BackColor = AppleUiColors.Window;
		((Control)this).Font = UiFonts.Body;
		AddLabel("大小方式", 24, 30);
		SetupCombo(cmbSizeMode, 112, 26, 190, new object[6] { "保持原样", "超出可用区域时缩小", "指定宽度", "指定宽高", "限制最大宽高", "按原始尺寸比例" });
		cmbSizeMode.SelectedIndexChanged += delegate
		{
			UpdateControlStates();
		};
		AddLabel("环绕方式", 332, 30);
		SetupCombo(cmbWrapMode, 420, 26, 200, new object[8] { "保持原样", "嵌入型", "四周型", "紧密型", "穿越型", "上下型", "衬于文字下方", "浮于文字上方" });
		AddLabel("对齐方式", 24, 76);
		SetupCombo(cmbAlignmentMode, 112, 72, 190, new object[4] { "保持原样", "左对齐", "居中", "右对齐" });
		AddLabel("图片边框", 332, 76);
		SetupCombo(cmbBorderMode, 420, 72, 200, new object[3] { "保持原样", "添加黑色单线", "删除已有边框" });
		SetupCheckBox(chkKeepAspectRatio, "保持图片宽高比例", 24, 120, 190);
		SetupCheckBox(chkMainStoryOnly, "安全限制：仅处理正文", 230, 120, 190);
		((Control)chkMainStoryOnly).Enabled = false;
		SetupCheckBox(chkIncludeTableCellImages, "包括表格单元格图片", 436, 120, 190);
		AppleGroupBox appleGroupBox = new AppleGroupBox();
		((Control)appleGroupBox).Text = " 尺寸参数 ";
		((Control)appleGroupBox).Location = new Point(20, 160);
		((Control)appleGroupBox).Size = new Size(610, 178);
		((Control)appleGroupBox).Font = UiFonts.BodyBold;
		((Control)appleGroupBox).ForeColor = UiColors.Primary;
		((Control)appleGroupBox).BackColor = UiColors.BgCard;
		AppleGroupBox appleGroupBox2 = appleGroupBox;
		AddMetric((Control)(object)appleGroupBox2, "指定宽度", nudWidth, 18, 34, "厘米");
		AddMetric((Control)(object)appleGroupBox2, "指定高度", nudHeight, 310, 34, "厘米");
		AddMetric((Control)(object)appleGroupBox2, "最大宽度", nudMaxWidth, 18, 82, "厘米");
		AddMetric((Control)(object)appleGroupBox2, "最大高度", nudMaxHeight, 310, 82, "厘米");
		AddMetric((Control)(object)appleGroupBox2, "原图比例", nudScalePercent, 18, 130, "%");
		((Control)this).Controls.Add((Control)(object)appleGroupBox2);
		Button val = CreateButton("高级设置", 24, 350, UiColors.PrimaryLight, Color.White);
		((Control)val).Size = new Size(110, 32);
		((Control)val).Click += delegate
		{
			ImageAdvancedSettingsForm imageAdvancedSettingsForm = new ImageAdvancedSettingsForm(workingOptions);
			try
			{
				((Form)imageAdvancedSettingsForm).ShowDialog((IWin32Window)(object)this);
			}
			finally
			{
				((IDisposable)(object)imageAdvancedSettingsForm)?.Dispose();
			}
		};
		((Control)this).Controls.Add((Control)(object)val);
		Label val2 = new Label
		{
			Text = "安全规则：只识别真实图片；文本框、线条、图表、公式和 OLE 对象不会作为图片处理。",
			Location = new Point(150, 350),
			Size = new Size(475, 40),
			ForeColor = UiColors.TextMuted,
			Font = UiFonts.Caption
		};
		((Control)this).Controls.Add((Control)(object)val2);
		Button val3 = CreateButton("确定", 450, 415, UiColors.PrimaryLight, Color.White);
		((Control)val3).Click += delegate
		{
			SaveValues();
			config.ImageOptions = workingOptions;
			((Form)this).DialogResult = (DialogResult)1;
			((Form)this).Close();
		};
		Button val4 = CreateButton("取消", 545, 415, UiColors.BgDanger, UiColors.Danger);
		((Control)val4).Click += delegate
		{
			((Form)this).DialogResult = (DialogResult)2;
			((Form)this).Close();
		};
		((Control)this).Controls.Add((Control)(object)val3);
		((Control)this).Controls.Add((Control)(object)val4);
		((Form)this).AcceptButton = (IButtonControl)(object)val3;
		((Form)this).CancelButton = (IButtonControl)(object)val4;
		toolTip.SetToolTip((Control)(object)cmbSizeMode, "选择保持尺寸、按可用版心缩小、指定尺寸或按原始尺寸比例设置。");
		toolTip.SetToolTip((Control)(object)cmbWrapMode, "保持原样最安全；其他选项会在后续执行阶段改变图片环绕类型。");
		toolTip.SetToolTip((Control)(object)cmbAlignmentMode, "设置图片所在段落的水平对齐方式；保持原样时不修改现有对齐。");
		toolTip.SetToolTip((Control)(object)cmbBorderMode, "选择保留、添加黑色单线边框或删除已有图片边框。");
		toolTip.SetToolTip((Control)(object)chkKeepAspectRatio, "调整图片尺寸时锁定原始宽高比例，避免图片变形。");
		toolTip.SetToolTip((Control)(object)chkMainStoryOnly, "开启后排除页眉、页脚、水印等非正文故事范围对象。");
		toolTip.SetToolTip((Control)(object)chkIncludeTableCellImages, "开启后允许处理表格单元格内的真实图片。");
		toolTip.SetToolTip((Control)(object)nudWidth, "在指定宽度或指定宽高模式下设置图片宽度，单位为厘米。");
		toolTip.SetToolTip((Control)(object)nudHeight, "在指定宽高模式下设置图片高度，单位为厘米。");
		toolTip.SetToolTip((Control)(object)nudMaxWidth, "限制最大宽高模式下允许的最大宽度，单位为厘米。");
		toolTip.SetToolTip((Control)(object)nudMaxHeight, "限制最大宽高模式下允许的最大高度，单位为厘米。");
		toolTip.SetToolTip((Control)(object)nudScalePercent, "按图片原始尺寸的百分比统一缩放。");
		toolTip.SetToolTip((Control)(object)val, "打开图片筛选、环绕距离、旋转和已有题注等高级参数。");
		toolTip.SetToolTip((Control)(object)val3, "保存图片排版参数并返回一键排版设置。");
		toolTip.SetToolTip((Control)(object)val4, "放弃本次图片排版参数修改。");
		AppleFormStyler.Apply((Form)(object)this, toolTip);
	}

	private void LoadValues()
	{
		ImageFormatOptions imageFormatOptions = workingOptions;
		((Control)cmbSizeMode).Text = ToSizeText(imageFormatOptions.SizeMode);
		((Control)cmbWrapMode).Text = ToWrapText(imageFormatOptions.WrapMode);
		((Control)cmbAlignmentMode).Text = ToAlignmentText(imageFormatOptions.AlignmentMode);
		((Control)cmbBorderMode).Text = ToBorderText(imageFormatOptions.BorderMode);
		chkKeepAspectRatio.Checked = imageFormatOptions.KeepAspectRatio;
		chkMainStoryOnly.Checked = true;
		chkIncludeTableCellImages.Checked = imageFormatOptions.IncludeTableCellImages;
		nudWidth.Value = Clamp(nudWidth, imageFormatOptions.WidthCm);
		nudHeight.Value = Clamp(nudHeight, imageFormatOptions.HeightCm);
		nudMaxWidth.Value = Clamp(nudMaxWidth, imageFormatOptions.MaxWidthCm);
		nudMaxHeight.Value = Clamp(nudMaxHeight, imageFormatOptions.MaxHeightCm);
		nudScalePercent.Value = Clamp(nudScalePercent, imageFormatOptions.ScalePercent);
		UpdateControlStates();
	}

	private void SaveValues()
	{
		ImageFormatOptions imageFormatOptions = workingOptions;
		imageFormatOptions.OptionsVersion = 2;
		imageFormatOptions.SizeMode = FromSizeText(((Control)cmbSizeMode).Text);
		imageFormatOptions.WrapMode = FromWrapText(((Control)cmbWrapMode).Text);
		imageFormatOptions.AlignmentMode = FromAlignmentText(((Control)cmbAlignmentMode).Text);
		imageFormatOptions.BorderMode = FromBorderText(((Control)cmbBorderMode).Text);
		imageFormatOptions.KeepAspectRatio = chkKeepAspectRatio.Checked;
		imageFormatOptions.MainStoryOnly = true;
		imageFormatOptions.IncludeTableCellImages = chkIncludeTableCellImages.Checked;
		imageFormatOptions.WidthCm = (float)nudWidth.Value;
		imageFormatOptions.HeightCm = (float)nudHeight.Value;
		imageFormatOptions.MaxWidthCm = (float)nudMaxWidth.Value;
		imageFormatOptions.MaxHeightCm = (float)nudMaxHeight.Value;
		imageFormatOptions.ScalePercent = (float)nudScalePercent.Value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void UpdateControlStates()
	{
		string text = FromSizeText(((Control)cmbSizeMode).Text);
		((Control)nudWidth).Enabled = text == "FixedWidth" || text == "FixedSize";
		((Control)nudHeight).Enabled = text == "FixedSize";
		((Control)nudMaxWidth).Enabled = text == "LimitMax";
		((Control)nudMaxHeight).Enabled = text == "LimitMax";
		((Control)nudScalePercent).Enabled = text == "OriginalScalePercent";
	}

	private void SetupCombo(ComboBox combo, int x, int y, int width, object[] items)
	{
		((Control)combo).Location = new Point(x, y);
		((Control)combo).Size = new Size(width, 28);
		combo.DropDownStyle = (ComboBoxStyle)2;
		((Control)combo).Font = UiFonts.Body;
		combo.Items.AddRange(items);
		((Control)this).Controls.Add((Control)(object)combo);
	}

	private void SetupCheckBox(CheckBox checkBox, string text, int x, int y, int width)
	{
		((Control)checkBox).Text = text;
		((Control)checkBox).Location = new Point(x, y);
		((Control)checkBox).Size = new Size(width, 26);
		((Control)checkBox).ForeColor = UiColors.TextBody;
		checkBox.CheckedChanged += delegate
		{
			UpdateControlStates();
		};
		((Control)this).Controls.Add((Control)(object)checkBox);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void AddMetric(Control parent, string text, NumericUpDown input, int x, int y, string unit)
	{
		parent.Controls.Add((Control)new Label
		{
			Text = text,
			Location = new Point(x, y + 4),
			Size = new Size(78, 24),
			ForeColor = UiColors.TextBody
		});
		((Control)input).Location = new Point(x + 82, y);
		((Control)input).Size = new Size(100, 28);
		input.Minimum = ((unit == "%") ? 10m : 0.1m);
		input.Maximum = ((unit == "%") ? 300 : 100);
		input.DecimalPlaces = ((!(unit == "%")) ? 1 : 0);
		input.Increment = ((unit == "%") ? 5m : 0.1m);
		parent.Controls.Add((Control)(object)input);
		parent.Controls.Add((Control)new Label
		{
			Text = unit,
			Location = new Point(x + 188, y + 4),
			Size = new Size(50, 24),
			ForeColor = UiColors.TextMuted
		});
	}

	private void AddLabel(string text, int x, int y)
	{
		((Control)this).Controls.Add((Control)new Label
		{
			Text = text,
			Location = new Point(x, y),
			Size = new Size(82, 24),
			ForeColor = UiColors.TextBody
		});
	}

	private Button CreateButton(string text, int x, int y, Color backColor, Color foreColor)
	{
		Button val = new Button
		{
			Text = text,
			Location = new Point(x, y),
			Size = new Size(80, 32),
			BackColor = backColor,
			ForeColor = foreColor,
			FlatStyle = (FlatStyle)0,
			Cursor = Cursors.Hand
		};
		((ButtonBase)val).FlatAppearance.BorderSize = 0;
		return val;
	}

	private decimal Clamp(NumericUpDown control, float value)
	{
		decimal num = (decimal)value;
		if (num < control.Minimum)
		{
			return control.Minimum;
		}
		if (num > control.Maximum)
		{
			return control.Maximum;
		}
		return num;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string ToSizeText(string value)
	{
		return value switch
		{
			"OriginalScalePercent" => "按原始尺寸比例", 
			"LimitMax" => "限制最大宽高", 
			"FixedWidth" => "指定宽度", 
			"Preserve" => "保持原样", 
			"FixedSize" => "指定宽高", 
			_ => "超出可用区域时缩小", 
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string FromSizeText(string value)
	{
		return value switch
		{
			"指定宽高" => "FixedSize", 
			"按原始尺寸比例" => "OriginalScalePercent", 
			"指定宽度" => "FixedWidth", 
			"保持原样" => "Preserve", 
			"限制最大宽高" => "LimitMax", 
			_ => "ShrinkToFit", 
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string ToWrapText(string value)
	{
		return value switch
		{
			"Behind" => "衬于文字下方", 
			"Tight" => "紧密型", 
			"Inline" => "嵌入型", 
			"TopBottom" => "上下型", 
			"Square" => "四周型", 
			"Front" => "浮于文字上方", 
			"Through" => "穿越型", 
			_ => "保持原样", 
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string FromWrapText(string value)
	{
		return value switch
		{
			"紧密型" => "Tight", 
			"浮于文字上方" => "Front", 
			"四周型" => "Square", 
			"衬于文字下方" => "Behind", 
			"嵌入型" => "Inline", 
			"上下型" => "TopBottom", 
			"穿越型" => "Through", 
			_ => "Preserve", 
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string ToAlignmentText(string value)
	{
		return value switch
		{
			"Right" => "右对齐", 
			"Center" => "居中", 
			"Left" => "左对齐", 
			_ => "保持原样", 
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string FromAlignmentText(string value)
	{
		return value switch
		{
			"右对齐" => "Right", 
			"左对齐" => "Left", 
			"居中" => "Center", 
			_ => "Preserve", 
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string ToBorderText(string value)
	{
		if (value == "Add")
		{
			return "添加黑色单线";
		}
		if (value == "Remove")
		{
			return "删除已有边框";
		}
		return "保持原样";
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string FromBorderText(string value)
	{
		if (!(value == "添加黑色单线"))
		{
			if (value == "删除已有边框")
			{
				return "Remove";
			}
			return "Preserve";
		}
		return "Add";
	}
}
