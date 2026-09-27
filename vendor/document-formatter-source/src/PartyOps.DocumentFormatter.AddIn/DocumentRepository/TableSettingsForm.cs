using System;
using System.Drawing;
using System.Drawing.Text;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using DocumentRepository.Services.UiText;

namespace DocumentRepository;

public class TableSettingsForm : Form
{
	private NumericUpDown nudHeaderRows;

	private NumericUpDown nudRowHeight;

	private NumericUpDown nudColumnWidth;

	private NumericUpDown nudTopPadding;

	private NumericUpDown nudBottomPadding;

	private NumericUpDown nudLeftPadding;

	private NumericUpDown nudRightPadding;

	private CheckBox chkBorders;

	private CheckBox chkShadeHeader;

	private CheckBox chkRepeatHeaderRows;

	private CheckBox chkClearCellIndents;

	private ComboBox cmbHeaderFont;

	private ComboBox cmbHeaderSize;

	private ComboBox cmbBodyFont;

	private ComboBox cmbBodySize;

	private ComboBox cmbHeaderAlignment;

	private ComboBox cmbBodyAlignment;

	private ComboBox cmbTableAlignment;

	private ComboBox cmbTextWrapping;

	private ComboBox cmbRowHeightMode;

	private ComboBox cmbColumnWidthMode;

	private readonly FormatConfig config;

	private readonly ToolTip toolTip = UiTextApplier.CreateToolTip();

	public TableSettingsForm(FormatConfig config)
	{
		this.config = config;
		if (this.config.TableOptions == null)
		{
			this.config.TableOptions = new TableFormatOptions();
		}
		InitializeComponent();
		LoadValues();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void InitializeComponent()
	{
		((Control)this).Text = "表格排版设置";
		((Form)this).StartPosition = (FormStartPosition)4;
		((Form)this).FormBorderStyle = (FormBorderStyle)3;
		((Form)this).MaximizeBox = false;
		((Form)this).MinimizeBox = false;
		((Form)this).ClientSize = new Size(520, 515);
		((Control)this).BackColor = AppleUiColors.Window;
		chkBorders = AddCheckBox("统一设置黑色单线边框", 24, 24, 220);
		chkShadeHeader = AddCheckBox("表头使用浅灰底纹", 270, 24, 200);
		chkRepeatHeaderRows = AddCheckBox("跨页重复表头", 24, 52, 220);
		chkClearCellIndents = AddCheckBox("清除单元格段落缩进", 270, 52, 220);
		AddLabel("表头行数", 24, 94);
		nudHeaderRows = AddNumber(130, 90, 0, 5, 1m, 0, 80);
		AddLabel("行高方式", 270, 94);
		cmbRowHeightMode = AddModeCombo(360, 90, 120);
		cmbRowHeightMode.Items.AddRange(new object[3] { "最小值", "固定值", "自动行高" });
		cmbRowHeightMode.SelectedIndexChanged += delegate
		{
			UpdateControlStates();
		};
		AddLabel("行高值", 24, 132);
		nudRowHeight = AddNumber(130, 128, 0, 100, 30m, 1, 80);
		((Control)this).Controls.Add((Control)new Label
		{
			Text = "磅",
			Location = new Point(214, 132),
			Size = new Size(36, 24),
			Font = UiFonts.Body,
			ForeColor = UiColors.TextMuted
		});
		AddLabel("列宽方式", 270, 132);
		cmbColumnWidthMode = AddModeCombo(360, 128, 120);
		cmbColumnWidthMode.Items.AddRange(new object[4] { "适应页面", "根据内容", "固定列宽", "自动列宽" });
		cmbColumnWidthMode.SelectedIndexChanged += delegate
		{
			UpdateControlStates();
		};
		AddLabel("固定列宽", 24, 170);
		nudColumnWidth = AddNumber(130, 166, 0, 20, 2m, 1, 80);
		((Control)this).Controls.Add((Control)new Label
		{
			Text = "厘米",
			Location = new Point(214, 170),
			Size = new Size(45, 24),
			Font = UiFonts.Body,
			ForeColor = UiColors.TextMuted
		});
		AddLabel("表格对齐", 270, 170);
		cmbTableAlignment = AddModeCombo(360, 166, 120);
		cmbTableAlignment.Items.AddRange(new object[3] { "左对齐", "居中", "右对齐" });
		AddLabel("文字环绕", 24, 208);
		cmbTextWrapping = AddModeCombo(130, 204, 120);
		cmbTextWrapping.Items.AddRange(new object[2] { "无环绕", "环绕" });
		AddLabel("表头字体", 24, 254);
		cmbHeaderFont = AddFontCombo(130, 250, 150);
		cmbHeaderSize = AddSizeCombo(300, 250);
		AddLabel("内容字体", 24, 292);
		cmbBodyFont = AddFontCombo(130, 288, 150);
		cmbBodySize = AddSizeCombo(300, 288);
		AddLabel("表头对齐", 24, 330);
		cmbHeaderAlignment = AddAlignmentCombo(130, 326);
		AddLabel("内容对齐", 270, 330);
		cmbBodyAlignment = AddAlignmentCombo(360, 326);
		AddShortLabel("上边距", 24, 380);
		nudTopPadding = AddNumber(92, 376, 0, 5, 0m, 2, 106);
		AddShortLabel("下边距", 224, 380);
		nudBottomPadding = AddNumber(292, 376, 0, 5, 0m, 2, 106);
		AddShortLabel("左边距", 24, 418);
		nudLeftPadding = AddNumber(92, 414, 0, 5, 0m, 2, 106);
		AddShortLabel("右边距", 224, 418);
		nudRightPadding = AddNumber(292, 414, 0, 5, 0m, 2, 106);
		((Control)this).Controls.Add((Control)new Label
		{
			Text = "单位：厘米",
			Location = new Point(420, 399),
			Size = new Size(90, 24),
			Font = UiFonts.Body,
			ForeColor = UiColors.TextMuted
		});
		Button val = new Button
		{
			Text = "确定",
			Location = new Point(300, 463),
			Size = new Size(80, 32),
			BackColor = UiColors.PrimaryLight,
			ForeColor = Color.White,
			FlatStyle = (FlatStyle)0
		};
		((ButtonBase)val).FlatAppearance.BorderSize = 0;
		((Control)val).Click += delegate
		{
			SaveValues();
			((Form)this).DialogResult = (DialogResult)1;
			((Form)this).Close();
		};
		Button val2 = new Button
		{
			Text = "取消",
			Location = new Point(395, 463),
			Size = new Size(80, 32),
			BackColor = UiColors.BgDanger,
			ForeColor = UiColors.Danger,
			FlatStyle = (FlatStyle)0
		};
		((ButtonBase)val2).FlatAppearance.BorderSize = 0;
		((Control)val2).Click += delegate
		{
			((Form)this).DialogResult = (DialogResult)2;
			((Form)this).Close();
		};
		((Control)this).Controls.Add((Control)(object)val);
		((Control)this).Controls.Add((Control)(object)val2);
		ConfigureTooltips();
		toolTip.SetToolTip((Control)(object)val, "保存表格排版参数并返回一键排版设置。");
		toolTip.SetToolTip((Control)(object)val2, "放弃本次表格排版参数修改。");
		AppleFormStyler.Apply((Form)(object)this, toolTip);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void ConfigureTooltips()
	{
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Table.Borders", "统一设置黑色单线边框", (Control)chkBorders);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Table.ShadeHeader", "表头使用浅灰底纹", (Control)chkShadeHeader);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Table.RepeatHeaderRows", "跨页重复表头", (Control)chkRepeatHeaderRows);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Table.ClearCellIndents", "清除单元格段落缩进", (Control)chkClearCellIndents);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Table.HeaderRows", "表头行数", (Control)nudHeaderRows);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Table.RowHeightMode", "行高方式", (Control)cmbRowHeightMode);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Table.RowHeight", "行高值", (Control)nudRowHeight);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Table.ColumnWidthMode", "列宽方式", (Control)cmbColumnWidthMode);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Table.ColumnWidth", "固定列宽", (Control)nudColumnWidth);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Table.Alignment", "表格对齐", (Control)cmbTableAlignment);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Table.TextWrapping", "文字环绕", (Control)cmbTextWrapping);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Table.HeaderStyle", "表头字体", (Control)cmbHeaderFont, (Control)cmbHeaderSize);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Table.BodyStyle", "内容字体", (Control)cmbBodyFont, (Control)cmbBodySize);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Table.HeaderAlignment", "表头对齐", (Control)cmbHeaderAlignment);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Table.BodyAlignment", "内容对齐", (Control)cmbBodyAlignment);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Table.TopPadding", "上边距", (Control)nudTopPadding);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Table.BottomPadding", "下边距", (Control)nudBottomPadding);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Table.LeftPadding", "左边距", (Control)nudLeftPadding);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Table.RightPadding", "右边距", (Control)nudRightPadding);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void LoadValues()
	{
		TableFormatOptions tableOptions = config.TableOptions;
		chkBorders.Checked = tableOptions.UseBorders;
		chkShadeHeader.Checked = tableOptions.ShadeHeader;
		chkRepeatHeaderRows.Checked = tableOptions.RepeatHeaderRows;
		chkClearCellIndents.Checked = tableOptions.ClearCellIndents;
		nudHeaderRows.Value = Clamp(nudHeaderRows, tableOptions.HeaderRows);
		nudRowHeight.Value = Clamp(nudRowHeight, (decimal)Math.Max(0f, tableOptions.RowHeight));
		nudColumnWidth.Value = Clamp(nudColumnWidth, (decimal)Math.Max(0f, tableOptions.ColumnWidth));
		nudTopPadding.Value = Clamp(nudTopPadding, (decimal)Math.Max(0f, tableOptions.CellTopPadding));
		nudBottomPadding.Value = Clamp(nudBottomPadding, (decimal)Math.Max(0f, tableOptions.CellBottomPadding));
		nudLeftPadding.Value = Clamp(nudLeftPadding, (decimal)Math.Max(0f, tableOptions.CellLeftPadding));
		nudRightPadding.Value = Clamp(nudRightPadding, (decimal)Math.Max(0f, tableOptions.CellRightPadding));
		((Control)cmbHeaderFont).Text = tableOptions.HeaderFontName;
		((Control)cmbHeaderSize).Text = tableOptions.HeaderFontSize;
		((Control)cmbBodyFont).Text = tableOptions.BodyFontName;
		((Control)cmbBodySize).Text = tableOptions.BodyFontSize;
		((Control)cmbHeaderAlignment).Text = (string.IsNullOrWhiteSpace(tableOptions.HeaderAlignment) ? "居中" : tableOptions.HeaderAlignment);
		((Control)cmbBodyAlignment).Text = (string.IsNullOrWhiteSpace(tableOptions.BodyAlignment) ? "两端对齐" : tableOptions.BodyAlignment);
		((Control)cmbTableAlignment).Text = ToTableAlignmentText(tableOptions.TableAlignment);
		((Control)cmbTextWrapping).Text = ToWrappingText(tableOptions.TextWrapping);
		((Control)cmbRowHeightMode).Text = ToRowHeightModeText(tableOptions.RowHeightMode);
		((Control)cmbColumnWidthMode).Text = ToColumnWidthModeText(tableOptions.ColumnWidthMode);
		UpdateControlStates();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void SaveValues()
	{
		TableFormatOptions tableOptions = config.TableOptions;
		tableOptions.OptionsVersion = 1;
		tableOptions.UseBorders = chkBorders.Checked;
		tableOptions.ShadeHeader = chkShadeHeader.Checked;
		tableOptions.RepeatHeaderRows = chkRepeatHeaderRows.Checked;
		tableOptions.ClearCellIndents = chkClearCellIndents.Checked;
		tableOptions.HeaderRows = (int)nudHeaderRows.Value;
		tableOptions.RowHeight = (float)nudRowHeight.Value;
		tableOptions.ColumnWidth = (float)nudColumnWidth.Value;
		tableOptions.CellTopPadding = (float)nudTopPadding.Value;
		tableOptions.CellBottomPadding = (float)nudBottomPadding.Value;
		tableOptions.CellLeftPadding = (float)nudLeftPadding.Value;
		tableOptions.CellRightPadding = (float)nudRightPadding.Value;
		tableOptions.HeaderFontName = (string.IsNullOrWhiteSpace(((Control)cmbHeaderFont).Text) ? "黑体" : ((Control)cmbHeaderFont).Text.Trim());
		tableOptions.HeaderFontSize = (string.IsNullOrWhiteSpace(((Control)cmbHeaderSize).Text) ? "小四" : ((Control)cmbHeaderSize).Text.Trim());
		tableOptions.BodyFontName = (string.IsNullOrWhiteSpace(((Control)cmbBodyFont).Text) ? "仿宋_GB2312" : ((Control)cmbBodyFont).Text.Trim());
		tableOptions.BodyFontSize = (string.IsNullOrWhiteSpace(((Control)cmbBodySize).Text) ? "小四" : ((Control)cmbBodySize).Text.Trim());
		tableOptions.HeaderAlignment = NormalizeAlignment(((Control)cmbHeaderAlignment).Text, "居中");
		tableOptions.BodyAlignment = NormalizeAlignment(((Control)cmbBodyAlignment).Text, "两端对齐");
		tableOptions.TableAlignment = FromTableAlignmentText(((Control)cmbTableAlignment).Text);
		tableOptions.TextWrapping = FromWrappingText(((Control)cmbTextWrapping).Text);
		tableOptions.RowHeightMode = FromRowHeightModeText(((Control)cmbRowHeightMode).Text);
		tableOptions.ColumnWidthMode = FromColumnWidthModeText(((Control)cmbColumnWidthMode).Text);
		tableOptions.FitWindow = tableOptions.ColumnWidthMode == "Window";
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void UpdateControlStates()
	{
		((Control)nudRowHeight).Enabled = ((Control)cmbRowHeightMode).Text != "自动行高";
		((Control)nudColumnWidth).Enabled = ((Control)cmbColumnWidthMode).Text == "固定列宽";
	}

	private CheckBox AddCheckBox(string text, int x, int y, int width)
	{
		CheckBox val = new CheckBox
		{
			Text = text,
			Location = new Point(x, y),
			Size = new Size(width, 24),
			Font = UiFonts.Body
		};
		((Control)this).Controls.Add((Control)(object)val);
		return val;
	}

	private void AddLabel(string text, int x, int y)
	{
		((Control)this).Controls.Add((Control)new Label
		{
			Text = text,
			Location = new Point(x, y),
			Size = new Size(90, 24),
			Font = UiFonts.Body,
			ForeColor = UiColors.TextBody
		});
	}

	private void AddShortLabel(string text, int x, int y)
	{
		((Control)this).Controls.Add((Control)new Label
		{
			Text = text,
			Location = new Point(x, y),
			Size = new Size(60, 24),
			Font = UiFonts.Body,
			ForeColor = UiColors.TextBody
		});
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private ComboBox AddFontCombo(int x, int y, int width)
	{
		ComboBox val = new ComboBox
		{
			Location = new Point(x, y),
			Size = new Size(width, 24),
			Font = UiFonts.Body,
			DropDownStyle = (ComboBoxStyle)1
		};
		val.Items.AddRange(new object[20]
		{
			"方正小标宋简体", "方正小标宋_GBK", "小标宋_GBK", "方正粗宋简体", "黑体", "方正黑体简体", "方正黑体_GBK", "华文黑体", "微软雅黑", "宋体",
			"新宋体", "仿宋", "仿宋_GB2312", "方正仿宋简体", "方正仿宋_GBK", "楷体", "楷体_GB2312", "方正楷体简体", "方正楷体_GBK", "Times New Roman"
		});
		try
		{
			InstalledFontCollection val2 = new InstalledFontCollection();
			try
			{
				FontFamily[] families = ((FontCollection)val2).Families;
				foreach (FontFamily val3 in families)
				{
					if (!val.Items.Contains((object)val3.Name))
					{
						val.Items.Add((object)val3.Name);
					}
				}
			}
			finally
			{
				((IDisposable)val2)?.Dispose();
			}
		}
		catch
		{
		}
		((Control)this).Controls.Add((Control)(object)val);
		return val;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private ComboBox AddSizeCombo(int x, int y)
	{
		ComboBox val = new ComboBox
		{
			Location = new Point(x, y),
			Size = new Size(90, 24),
			Font = UiFonts.Body,
			DropDownStyle = (ComboBoxStyle)1
		};
		val.Items.AddRange(new object[35]
		{
			"初号", "小初", "一号", "小一", "二号", "小二", "三号", "小三", "四号", "小四",
			"五号", "小五", "六号", "小六", "七号", "八号", "5", "5.5", "6", "6.5",
			"7.5", "8", "9", "10", "10.5", "11", "12", "14", "16", "18",
			"20", "22", "24", "26", "28"
		});
		((Control)this).Controls.Add((Control)(object)val);
		return val;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private ComboBox AddAlignmentCombo(int x, int y)
	{
		ComboBox val = AddModeCombo(x, y, 110);
		val.Items.AddRange(new object[5] { "居中", "两端对齐", "左对齐", "右对齐", "分散对齐" });
		return val;
	}

	private ComboBox AddModeCombo(int x, int y, int width)
	{
		ComboBox val = new ComboBox
		{
			Location = new Point(x, y),
			Size = new Size(width, 24),
			Font = UiFonts.Body,
			DropDownStyle = (ComboBoxStyle)2
		};
		((Control)this).Controls.Add((Control)(object)val);
		return val;
	}

	private NumericUpDown AddNumber(int x, int y, int min, int max, decimal value, int decimalPlaces, int width)
	{
		NumericUpDown val = new NumericUpDown
		{
			Location = new Point(x, y),
			Size = new Size(width, 24),
			Minimum = min,
			Maximum = max,
			Value = value,
			DecimalPlaces = decimalPlaces,
			Increment = ((decimalPlaces == 0) ? 1m : 0.1m),
			Font = UiFonts.Body
		};
		((Control)this).Controls.Add((Control)(object)val);
		return val;
	}

	private decimal Clamp(NumericUpDown nud, decimal value)
	{
		return Math.Max(nud.Minimum, Math.Min(nud.Maximum, value));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private string NormalizeAlignment(string value, string fallback)
	{
		string text = (value ?? "").Trim();
		switch (text)
		{
		case "居中":
		case "两端对齐":
		case "左对齐":
		case "右对齐":
		case "分散对齐":
			return text;
		default:
			return fallback;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private string ToTableAlignmentText(string value)
	{
		return (value ?? "").Trim() switch
		{
			"Right" => "右对齐", 
			"Left" => "左对齐", 
			_ => "居中", 
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private string FromTableAlignmentText(string value)
	{
		return (value ?? "").Trim() switch
		{
			"左对齐" => "Left", 
			"右对齐" => "Right", 
			_ => "Center", 
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private string ToWrappingText(string value)
	{
		if (!((value ?? "").Trim() == "Around"))
		{
			return "无环绕";
		}
		return "环绕";
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private string FromWrappingText(string value)
	{
		if (!((value ?? "").Trim() == "环绕"))
		{
			return "None";
		}
		return "Around";
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private string ToRowHeightModeText(string value)
	{
		return (value ?? "").Trim() switch
		{
			"Auto" => "自动行高", 
			"Exactly" => "固定值", 
			_ => "最小值", 
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private string FromRowHeightModeText(string value)
	{
		return (value ?? "").Trim() switch
		{
			"自动行高" => "Auto", 
			"固定值" => "Exactly", 
			_ => "AtLeast", 
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private string ToColumnWidthModeText(string value)
	{
		return (value ?? "").Trim() switch
		{
			"Content" => "根据内容", 
			"Fixed" => "固定列宽", 
			"Auto" => "自动列宽", 
			_ => "适应页面", 
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private string FromColumnWidthModeText(string value)
	{
		return (value ?? "").Trim() switch
		{
			"根据内容" => "Content", 
			"固定列宽" => "Fixed", 
			"自动列宽" => "Auto", 
			_ => "Window", 
		};
	}
}
