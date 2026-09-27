using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using DocumentRepository.Services.Formatting;
using DocumentRepository.Services.UiText;

namespace DocumentRepository;

public sealed class DocumentGridSettingsForm : Form
{
	private readonly FormatConfig config;

	private NumericUpDown nudLines;

	private NumericUpDown nudChars;

	private Label lblSummary;

	private Label lblWarning;

	private readonly ToolTip toolTip = UiTextApplier.CreateToolTip();

	[MethodImpl(MethodImplOptions.NoInlining)]
	public DocumentGridSettingsForm(FormatConfig config)
	{
		this.config = config ?? throw new ArgumentNullException("config");
		if (this.config.DocumentGridOptions == null)
		{
			this.config.DocumentGridOptions = new DocumentGridOptions();
		}
		InitializeComponent();
		LoadValues();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void InitializeComponent()
	{
		((Control)this).Text = "文档网格参数";
		((Form)this).StartPosition = (FormStartPosition)4;
		((Form)this).FormBorderStyle = (FormBorderStyle)3;
		((Form)this).MaximizeBox = false;
		((Form)this).MinimizeBox = false;
		((Form)this).ClientSize = new Size(420, 260);
		((Control)this).BackColor = AppleUiColors.Window;
		((Control)this).Controls.Add((Control)(object)CreateLabel("每页行数", 32, 34));
		nudLines = CreateNumber(142, 30, 1, 50);
		((Control)this).Controls.Add((Control)(object)nudLines);
		((Control)this).Controls.Add((Control)(object)CreateLabel("行", 240, 34));
		((Control)this).Controls.Add((Control)(object)CreateLabel("每行字数", 32, 76));
		nudChars = CreateNumber(142, 72, 1, 50);
		((Control)this).Controls.Add((Control)(object)nudChars);
		((Control)this).Controls.Add((Control)(object)CreateLabel("字", 240, 76));
		lblSummary = new Label
		{
			Location = new Point(32, 116),
			Size = new Size(350, 28),
			Font = UiFonts.Body,
			ForeColor = UiColors.Primary
		};
		((Control)this).Controls.Add((Control)(object)lblSummary);
		lblWarning = new Label
		{
			Location = new Point(32, 146),
			Size = new Size(350, 38),
			Font = UiFonts.Caption,
			ForeColor = UiColors.TextMuted
		};
		((Control)this).Controls.Add((Control)(object)lblWarning);
		Button val = CreateButton("恢复22×28", 32, 205, 105, primary: false);
		((Control)val).Click += delegate
		{
			nudLines.Value = 22m;
			nudChars.Value = 28m;
		};
		((Control)this).Controls.Add((Control)(object)val);
		Button val2 = CreateButton("确定", 230, 205, 72, primary: true);
		((Control)val2).Click += delegate
		{
			SaveValues();
			((Form)this).DialogResult = (DialogResult)1;
			((Form)this).Close();
		};
		((Control)this).Controls.Add((Control)(object)val2);
		Button val3 = CreateButton("取消", 316, 205, 72, primary: false);
		((Control)val3).Click += delegate
		{
			((Form)this).DialogResult = (DialogResult)2;
			((Form)this).Close();
		};
		((Control)this).Controls.Add((Control)(object)val3);
		((Form)this).AcceptButton = (IButtonControl)(object)val2;
		((Form)this).CancelButton = (IButtonControl)(object)val3;
		nudLines.ValueChanged += delegate
		{
			UpdateSummary();
		};
		nudChars.ValueChanged += delegate
		{
			UpdateSummary();
		};
		toolTip.SetToolTip((Control)(object)nudLines, "设置每页正文网格的行数，常规公文推荐 22 行。");
		toolTip.SetToolTip((Control)(object)nudChars, "设置每行正文网格的字数，常规公文推荐 28 字。");
		toolTip.SetToolTip((Control)(object)val, "将每页行数和每行字数恢复为常用的 22×28 网格。");
		toolTip.SetToolTip((Control)(object)val2, "保存文档网格参数并返回一键排版设置。");
		toolTip.SetToolTip((Control)(object)val3, "放弃本次文档网格参数修改。");
		AppleFormStyler.Apply((Form)(object)this, toolTip);
	}

	private void LoadValues()
	{
		nudLines.Value = Clamp(config.DocumentGridOptions.LinesPerPage, 1, 50, 22);
		nudChars.Value = Clamp(config.DocumentGridOptions.CharsPerLine, 1, 50, 28);
		UpdateSummary();
	}

	private void SaveValues()
	{
		config.DocumentGridOptions.OptionsVersion = 1;
		config.DocumentGridOptions.LinesPerPage = (int)nudLines.Value;
		config.DocumentGridOptions.CharsPerLine = (int)nudChars.Value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void UpdateSummary()
	{
		int linesPerPage = (int)nudLines.Value;
		int num = (int)nudChars.Value;
		float num2 = DocumentGridMetrics.CalculateVerticalPitchFromCentimeters(29.7f, config.TopMargin, config.BottomMargin, linesPerPage);
		((Control)lblSummary).Text = linesPerPage + "行 × " + num + "字，预计网格行距 " + num2.ToString("0.00") + " 磅";
		((Control)lblWarning).Text = ((num < 10) ? "提示：每行少于10字会产生非常宽的字符网格，适合特殊版式，不属于常规公文设置。" : "行距随纸张和上下页边距动态计算，不需要单独设置固定磅值。");
	}

	private static decimal Clamp(int value, int min, int max, int fallback)
	{
		return (value < min || value > max) ? fallback : value;
	}

	private static Label CreateLabel(string text, int x, int y)
	{
		return new Label
		{
			Text = text,
			Location = new Point(x, y),
			Size = new Size(100, 24),
			Font = UiFonts.Body,
			ForeColor = UiColors.TextBody
		};
	}

	private static NumericUpDown CreateNumber(int x, int y, int min, int max)
	{
		return new NumericUpDown
		{
			Location = new Point(x, y),
			Size = new Size(86, 26),
			Font = UiFonts.Body,
			Minimum = min,
			Maximum = max,
			Increment = 1m,
			TextAlign = (HorizontalAlignment)1
		};
	}

	private static Button CreateButton(string text, int x, int y, int width, bool primary)
	{
		Button val = new Button
		{
			Text = text,
			Location = new Point(x, y),
			Size = new Size(width, 32),
			Font = UiFonts.Body,
			FlatStyle = (FlatStyle)0,
			BackColor = (primary ? UiColors.PrimaryLight : UiColors.Border),
			ForeColor = (primary ? Color.White : UiColors.TextBody),
			Cursor = Cursors.Hand
		};
		((ButtonBase)val).FlatAppearance.BorderSize = 0;
		return val;
	}
}
