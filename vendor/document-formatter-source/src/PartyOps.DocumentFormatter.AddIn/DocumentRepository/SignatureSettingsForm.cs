using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using DocumentRepository.Services.UiText;

namespace DocumentRepository;

public sealed class SignatureSettingsForm : Form
{
	private readonly FormatConfig config;

	private RadioButton rbWithSeal;

	private RadioButton rbWithoutSeal;

	private NumericUpDown nudBlankLines;

	private readonly ToolTip toolTip = UiTextApplier.CreateToolTip();

	[MethodImpl(MethodImplOptions.NoInlining)]
	public SignatureSettingsForm(FormatConfig config)
	{
		this.config = config ?? throw new ArgumentNullException("config");
		if (this.config.SignatureOptions == null)
		{
			this.config.SignatureOptions = new SignatureFormatOptions();
		}
		InitializeComponent();
		LoadValues();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void InitializeComponent()
	{
		((Control)this).Text = "落款参数";
		((Form)this).StartPosition = (FormStartPosition)4;
		((Form)this).FormBorderStyle = (FormBorderStyle)3;
		((Form)this).MaximizeBox = false;
		((Form)this).MinimizeBox = false;
		((Form)this).ClientSize = new Size(420, 230);
		((Control)this).BackColor = AppleUiColors.Window;
		((Control)this).Controls.Add((Control)new Label
		{
			Text = "落款模式",
			Location = new Point(32, 30),
			Size = new Size(90, 24),
			Font = UiFonts.Body,
			ForeColor = UiColors.TextBody
		});
		rbWithSeal = new RadioButton
		{
			Text = "加盖公章（印章居中）",
			Location = new Point(132, 29),
			Size = new Size(210, 25),
			Font = UiFonts.Body,
			ForeColor = UiColors.TextBody
		};
		rbWithoutSeal = new RadioButton
		{
			Text = "不加盖公章",
			Location = new Point(132, 62),
			Size = new Size(180, 25),
			Font = UiFonts.Body,
			ForeColor = UiColors.TextBody
		};
		((Control)this).Controls.Add((Control)(object)rbWithSeal);
		((Control)this).Controls.Add((Control)(object)rbWithoutSeal);
		((Control)this).Controls.Add((Control)new Label
		{
			Text = "正文前空行",
			Location = new Point(32, 108),
			Size = new Size(90, 24),
			Font = UiFonts.Body,
			ForeColor = UiColors.TextBody
		});
		nudBlankLines = new NumericUpDown
		{
			Location = new Point(132, 104),
			Size = new Size(86, 26),
			Font = UiFonts.Body,
			Minimum = 0m,
			Maximum = 10m,
			Increment = 1m,
			TextAlign = (HorizontalAlignment)1
		};
		((Control)this).Controls.Add((Control)(object)nudBlankLines);
		((Control)this).Controls.Add((Control)new Label
		{
			Text = "行",
			Location = new Point(230, 108),
			Size = new Size(35, 24),
			Font = UiFonts.Body,
			ForeColor = UiColors.TextMuted
		});
		((Control)this).Controls.Add((Control)new Label
		{
			Text = "空行数对盖章和不盖章模式统一生效。",
			Location = new Point(32, 143),
			Size = new Size(340, 24),
			Font = UiFonts.Caption,
			ForeColor = UiColors.TextMuted
		});
		Button val = CreateButton("确定", 230, 180, primary: true);
		((Control)val).Click += delegate
		{
			SaveValues();
			((Form)this).DialogResult = (DialogResult)1;
			((Form)this).Close();
		};
		((Control)this).Controls.Add((Control)(object)val);
		Button val2 = CreateButton("取消", 316, 180, primary: false);
		((Control)val2).Click += delegate
		{
			((Form)this).DialogResult = (DialogResult)2;
			((Form)this).Close();
		};
		((Control)this).Controls.Add((Control)(object)val2);
		((Form)this).AcceptButton = (IButtonControl)(object)val;
		((Form)this).CancelButton = (IButtonControl)(object)val2;
		toolTip.SetToolTip((Control)(object)rbWithSeal, "按加盖公章的公文落款规则处理署名和日期位置。");
		toolTip.SetToolTip((Control)(object)rbWithoutSeal, "按不加盖公章的公文落款规则处理署名和日期位置。");
		toolTip.SetToolTip((Control)(object)nudBlankLines, "设置落款前保留的空行数，对两种落款模式均生效。");
		toolTip.SetToolTip((Control)(object)val, "保存落款参数并返回一键排版设置。");
		toolTip.SetToolTip((Control)(object)val2, "放弃本次落款参数修改。");
		AppleFormStyler.Apply((Form)(object)this, toolTip);
	}

	private void LoadValues()
	{
		rbWithSeal.Checked = config.SignatureOptions.WithSeal;
		rbWithoutSeal.Checked = !config.SignatureOptions.WithSeal;
		int blankLinesBefore = config.SignatureOptions.BlankLinesBefore;
		nudBlankLines.Value = ((blankLinesBefore >= 0 && blankLinesBefore <= 10) ? blankLinesBefore : ((!config.SignatureOptions.WithSeal) ? 1 : 2));
	}

	private void SaveValues()
	{
		config.SignatureOptions.OptionsVersion = 1;
		config.SignatureOptions.WithSeal = rbWithSeal.Checked;
		config.SignatureOptions.BlankLinesBefore = (int)nudBlankLines.Value;
		config.EnableSignatureWithSeal = config.SignatureOptions.WithSeal;
	}

	private static Button CreateButton(string text, int x, int y, bool primary)
	{
		Button val = new Button
		{
			Text = text,
			Location = new Point(x, y),
			Size = new Size(72, 32),
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
