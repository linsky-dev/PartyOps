using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using DocumentRepository.Services.UiText;

namespace DocumentRepository;

public sealed class ImageAdvancedSettingsForm : Form
{
	private readonly ImageFormatOptions options;

	private readonly CheckBox chkLinked = new CheckBox();

	private readonly CheckBox chkStandaloneOnly = new CheckBox();

	private readonly NumericUpDown nudMinWidth = Metric(0m, 100m, 0.1m);

	private readonly NumericUpDown nudMinHeight = Metric(0m, 100m, 0.1m);

	private readonly CheckBox chkWrapDistances = new CheckBox();

	private readonly NumericUpDown nudTop = Metric(0m, 20m, 0.1m);

	private readonly NumericUpDown nudBottom = Metric(0m, 20m, 0.1m);

	private readonly NumericUpDown nudLeft = Metric(0m, 20m, 0.1m);

	private readonly NumericUpDown nudRight = Metric(0m, 20m, 0.1m);

	private readonly CheckBox chkRotation = new CheckBox();

	private readonly NumericUpDown nudRotation = Metric(-180m, 180m, 1m);

	private readonly CheckBox chkCaption = new CheckBox();

	private readonly TextBox txtCaptionFont = new TextBox();

	private readonly ComboBox cmbCaptionSize = new ComboBox();

	private readonly CheckBox chkCaptionBold = new CheckBox();

	private readonly NumericUpDown nudCaptionBefore = Metric(0m, 100m, 1m, 0);

	private readonly NumericUpDown nudCaptionAfter = Metric(0m, 100m, 1m, 0);

	private readonly ToolTip toolTip = UiTextApplier.CreateToolTip();

	[MethodImpl(MethodImplOptions.NoInlining)]
	public ImageAdvancedSettingsForm(ImageFormatOptions options)
	{
		this.options = options ?? throw new ArgumentNullException("options");
		InitializeComponent();
		LoadValues();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void InitializeComponent()
	{
		((Control)this).Text = "图片高级设置";
		((Form)this).StartPosition = (FormStartPosition)4;
		((Form)this).FormBorderStyle = (FormBorderStyle)3;
		((Form)this).MaximizeBox = false;
		((Form)this).MinimizeBox = false;
		((Form)this).ShowInTaskbar = false;
		((ContainerControl)this).AutoScaleMode = (AutoScaleMode)2;
		((Form)this).ClientSize = new Size(700, 610);
		((Control)this).BackColor = AppleUiColors.Window;
		((Control)this).Font = UiFonts.Body;
		GroupBox parent = Group(" 筛选范围 ", 18, 16, 664, 142);
		AddCheck((Control)(object)parent, chkLinked, "处理链接图片", 18, 30, 180);
		AddCheck((Control)(object)parent, chkStandaloneOnly, "仅处理独立成段图片", 220, 30, 220);
		AddMetric((Control)(object)parent, "最小宽度", nudMinWidth, 18, 78, "厘米（0 表示不限）");
		AddMetric((Control)(object)parent, "最小高度", nudMinHeight, 330, 78, "厘米（0 表示不限）");
		GroupBox obj = Group(" 浮动图片布局 ", 18, 170, 664, 184);
		AddCheck((Control)(object)obj, chkWrapDistances, "设置文字环绕距离", 18, 30, 200);
		AddMetric((Control)(object)obj, "上方距离", nudTop, 18, 72, "厘米");
		AddMetric((Control)(object)obj, "下方距离", nudBottom, 330, 72, "厘米");
		AddMetric((Control)(object)obj, "左侧距离", nudLeft, 18, 112, "厘米");
		AddMetric((Control)(object)obj, "右侧距离", nudRight, 330, 112, "厘米");
		AddCheck((Control)(object)obj, chkRotation, "设置旋转角度", 18, 150, 150);
		((Control)nudRotation).Location = new Point(170, 147);
		((Control)nudRotation).Size = new Size(90, 28);
		((Control)obj).Controls.Add((Control)(object)nudRotation);
		((Control)obj).Controls.Add((Control)new Label
		{
			Text = "度（仅浮动图片）",
			Location = new Point(268, 151),
			Size = new Size(150, 24),
			ForeColor = UiColors.TextMuted
		});
		GroupBox val = Group(" 已有题注 ", 18, 366, 664, 154);
		AddCheck((Control)(object)val, chkCaption, "格式化图片下一段已有题注", 18, 30, 260);
		((Control)val).Controls.Add((Control)new Label
		{
			Text = "字体",
			Location = new Point(18, 76),
			Size = new Size(45, 24),
			ForeColor = UiColors.TextBody
		});
		((Control)txtCaptionFont).Location = new Point(66, 72);
		((Control)txtCaptionFont).Size = new Size(130, 28);
		((Control)val).Controls.Add((Control)(object)txtCaptionFont);
		((Control)val).Controls.Add((Control)new Label
		{
			Text = "字号",
			Location = new Point(214, 76),
			Size = new Size(45, 24),
			ForeColor = UiColors.TextBody
		});
		((Control)cmbCaptionSize).Location = new Point(262, 72);
		((Control)cmbCaptionSize).Size = new Size(90, 28);
		cmbCaptionSize.DropDownStyle = (ComboBoxStyle)2;
		cmbCaptionSize.Items.AddRange(new object[6] { "三号", "小三", "四号", "小四", "五号", "小五" });
		((Control)val).Controls.Add((Control)(object)cmbCaptionSize);
		AddCheck((Control)(object)val, chkCaptionBold, "加粗", 370, 73, 70);
		AddMetric((Control)(object)val, "段前", nudCaptionBefore, 18, 112, "磅");
		AddMetric((Control)(object)val, "段后", nudCaptionAfter, 330, 112, "磅");
		((Control)val).Controls.Add((Control)new Label
		{
			Text = "只识别“图1、图一、照片1”等已有题注，不自动新增文字。",
			Location = new Point(294, 31),
			Size = new Size(350, 28),
			ForeColor = UiColors.TextMuted,
			Font = UiFonts.Caption
		});
		Button val2 = Button("确定", 500, 550, UiColors.PrimaryLight, Color.White);
		((Control)val2).Click += delegate
		{
			SaveValues();
			((Form)this).DialogResult = (DialogResult)1;
			((Form)this).Close();
		};
		Button val3 = Button("取消", 596, 550, UiColors.BgDanger, UiColors.Danger);
		((Control)val3).Click += delegate
		{
			((Form)this).DialogResult = (DialogResult)2;
			((Form)this).Close();
		};
		((Control)this).Controls.Add((Control)(object)val2);
		((Control)this).Controls.Add((Control)(object)val3);
		((Form)this).AcceptButton = (IButtonControl)(object)val2;
		((Form)this).CancelButton = (IButtonControl)(object)val3;
		chkWrapDistances.CheckedChanged += delegate
		{
			UpdateStates();
		};
		chkRotation.CheckedChanged += delegate
		{
			UpdateStates();
		};
		chkCaption.CheckedChanged += delegate
		{
			UpdateStates();
		};
		toolTip.SetToolTip((Control)(object)chkLinked, "开启后允许处理链接到外部文件的图片对象。");
		toolTip.SetToolTip((Control)(object)chkStandaloneOnly, "开启后只处理独立成段的图片，跳过与正文混排的图片。");
		toolTip.SetToolTip((Control)(object)nudMinWidth, "小于此宽度的图片不处理；0 表示不限制。");
		toolTip.SetToolTip((Control)(object)nudMinHeight, "小于此高度的图片不处理；0 表示不限制。");
		toolTip.SetToolTip((Control)(object)chkWrapDistances, "开启后统一设置浮动图片与周围文字之间的距离。");
		toolTip.SetToolTip((Control)(object)nudTop, "设置浮动图片上方与文字的距离，单位为厘米。");
		toolTip.SetToolTip((Control)(object)nudBottom, "设置浮动图片下方与文字的距离，单位为厘米。");
		toolTip.SetToolTip((Control)(object)nudLeft, "设置浮动图片左侧与文字的距离，单位为厘米。");
		toolTip.SetToolTip((Control)(object)nudRight, "设置浮动图片右侧与文字的距离，单位为厘米。");
		toolTip.SetToolTip((Control)(object)chkRotation, "开启后按指定角度旋转浮动图片。");
		toolTip.SetToolTip((Control)(object)nudRotation, "设置浮动图片旋转角度，范围为 -180 至 180 度。");
		toolTip.SetToolTip((Control)(object)chkCaption, "开启后格式化图片下一段已有题注，不会自动新增题注文字。");
		toolTip.SetToolTip((Control)(object)txtCaptionFont, "设置已有图片题注使用的字体。");
		toolTip.SetToolTip((Control)(object)cmbCaptionSize, "设置已有图片题注使用的字号。");
		toolTip.SetToolTip((Control)(object)chkCaptionBold, "设置已有图片题注是否加粗。");
		toolTip.SetToolTip((Control)(object)nudCaptionBefore, "设置已有图片题注的段前距离，单位为磅。");
		toolTip.SetToolTip((Control)(object)nudCaptionAfter, "设置已有图片题注的段后距离，单位为磅。");
		toolTip.SetToolTip((Control)(object)val2, "保存图片高级参数并返回图片排版设置。");
		toolTip.SetToolTip((Control)(object)val3, "放弃本次图片高级参数修改。");
		AppleFormStyler.Apply((Form)(object)this, toolTip);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void LoadValues()
	{
		chkLinked.Checked = options.IncludeLinkedPictures;
		chkStandaloneOnly.Checked = options.ParagraphFilterMode == "StandaloneOnly";
		Set(nudMinWidth, options.MinimumWidthCm);
		Set(nudMinHeight, options.MinimumHeightCm);
		chkWrapDistances.Checked = options.ApplyWrapDistances;
		Set(nudTop, options.DistanceTopCm);
		Set(nudBottom, options.DistanceBottomCm);
		Set(nudLeft, options.DistanceLeftCm);
		Set(nudRight, options.DistanceRightCm);
		chkRotation.Checked = options.ApplyRotation;
		Set(nudRotation, options.RotationDegrees);
		chkCaption.Checked = options.FormatExistingCaptions;
		((Control)txtCaptionFont).Text = options.CaptionFontName ?? "宋体";
		((Control)cmbCaptionSize).Text = (string.IsNullOrWhiteSpace(options.CaptionFontSize) ? "小四" : options.CaptionFontSize);
		chkCaptionBold.Checked = options.CaptionBold;
		Set(nudCaptionBefore, options.CaptionSpaceBefore);
		Set(nudCaptionAfter, options.CaptionSpaceAfter);
		UpdateStates();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void SaveValues()
	{
		options.OptionsVersion = 2;
		options.IncludeLinkedPictures = chkLinked.Checked;
		options.ParagraphFilterMode = (chkStandaloneOnly.Checked ? "StandaloneOnly" : "All");
		options.MinimumWidthCm = (float)nudMinWidth.Value;
		options.MinimumHeightCm = (float)nudMinHeight.Value;
		options.ApplyWrapDistances = chkWrapDistances.Checked;
		options.DistanceTopCm = (float)nudTop.Value;
		options.DistanceBottomCm = (float)nudBottom.Value;
		options.DistanceLeftCm = (float)nudLeft.Value;
		options.DistanceRightCm = (float)nudRight.Value;
		options.ApplyRotation = chkRotation.Checked;
		options.RotationDegrees = (float)nudRotation.Value;
		options.FormatExistingCaptions = chkCaption.Checked;
		options.CaptionFontName = (string.IsNullOrWhiteSpace(((Control)txtCaptionFont).Text) ? "宋体" : ((Control)txtCaptionFont).Text.Trim());
		options.CaptionFontSize = (string.IsNullOrWhiteSpace(((Control)cmbCaptionSize).Text) ? "小四" : ((Control)cmbCaptionSize).Text);
		options.CaptionBold = chkCaptionBold.Checked;
		options.CaptionSpaceBefore = (int)nudCaptionBefore.Value;
		options.CaptionSpaceAfter = (int)nudCaptionAfter.Value;
	}

	private void UpdateStates()
	{
		NumericUpDown[] array = (NumericUpDown[])(object)new NumericUpDown[4] { nudTop, nudBottom, nudLeft, nudRight };
		for (int i = 0; i < array.Length; i++)
		{
			((Control)array[i]).Enabled = chkWrapDistances.Checked;
		}
		((Control)nudRotation).Enabled = chkRotation.Checked;
		Control[] array2 = (Control[])(object)new Control[5]
		{
			(Control)txtCaptionFont,
			(Control)cmbCaptionSize,
			(Control)chkCaptionBold,
			(Control)nudCaptionBefore,
			(Control)nudCaptionAfter
		};
		for (int i = 0; i < array2.Length; i++)
		{
			array2[i].Enabled = chkCaption.Checked;
		}
	}

	private GroupBox Group(string text, int x, int y, int width, int height)
	{
		AppleGroupBox appleGroupBox = new AppleGroupBox();
		((Control)appleGroupBox).Text = text;
		((Control)appleGroupBox).Location = new Point(x, y);
		((Control)appleGroupBox).Size = new Size(width, height);
		AppleGroupBox appleGroupBox2 = appleGroupBox;
		((Control)this).Controls.Add((Control)(object)appleGroupBox2);
		return (GroupBox)(object)appleGroupBox2;
	}

	private static void AddCheck(Control parent, CheckBox box, string text, int x, int y, int width)
	{
		((Control)box).Text = text;
		((Control)box).Location = new Point(x, y);
		((Control)box).Size = new Size(width, 26);
		((Control)box).ForeColor = UiColors.TextBody;
		parent.Controls.Add((Control)(object)box);
	}

	private static void AddMetric(Control parent, string text, NumericUpDown input, int x, int y, string unit)
	{
		parent.Controls.Add((Control)new Label
		{
			Text = text,
			Location = new Point(x, y + 4),
			Size = new Size(78, 24),
			ForeColor = UiColors.TextBody
		});
		((Control)input).Location = new Point(x + 82, y);
		((Control)input).Size = new Size(90, 28);
		parent.Controls.Add((Control)(object)input);
		parent.Controls.Add((Control)new Label
		{
			Text = unit,
			Location = new Point(x + 178, y + 4),
			Size = new Size(130, 24),
			ForeColor = UiColors.TextMuted
		});
	}

	private static NumericUpDown Metric(decimal min, decimal max, decimal increment, int decimals = 1)
	{
		return new NumericUpDown
		{
			Minimum = min,
			Maximum = max,
			Increment = increment,
			DecimalPlaces = decimals
		};
	}

	private static void Set(NumericUpDown input, float value)
	{
		decimal val = (decimal)value;
		input.Value = Math.Max(input.Minimum, Math.Min(input.Maximum, val));
	}

	private static Button Button(string text, int x, int y, Color back, Color fore)
	{
		Button val = new Button
		{
			Text = text,
			Location = new Point(x, y),
			Size = new Size(80, 32),
			BackColor = back,
			ForeColor = fore,
			FlatStyle = (FlatStyle)0,
			Cursor = Cursors.Hand
		};
		((ButtonBase)val).FlatAppearance.BorderSize = 0;
		return val;
	}
}
