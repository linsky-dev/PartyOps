using System;
using System.Drawing;
using System.Drawing.Text;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using DocumentRepository.Services.UiText;

namespace DocumentRepository;

public class AttachmentSettingsForm : Form
{
	private CheckBox chkList;

	private CheckBox chkBody;

	private ComboBox cmbMarkerFont;

	private ComboBox cmbMarkerSize;

	private readonly FormatConfig config;

	private readonly ToolTip toolTip = UiTextApplier.CreateToolTip();

	public AttachmentSettingsForm(FormatConfig config)
	{
		this.config = config;
		if (this.config.AttachmentOptions == null)
		{
			this.config.AttachmentOptions = new AttachmentFormatOptions();
		}
		InitializeComponent();
		LoadValues();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void InitializeComponent()
	{
		((Control)this).Text = "附件排版设置";
		((Form)this).StartPosition = (FormStartPosition)4;
		((Form)this).FormBorderStyle = (FormBorderStyle)3;
		((Form)this).MaximizeBox = false;
		((Form)this).MinimizeBox = false;
		((Form)this).ClientSize = new Size(420, 195);
		((Control)this).BackColor = AppleUiColors.Window;
		chkList = new CheckBox
		{
			Text = "排版附件说明（清单）",
			Location = new Point(24, 24),
			Size = new Size(220, 24),
			Font = UiFonts.Body
		};
		chkBody = new CheckBox
		{
			Text = "排版附件正文",
			Location = new Point(24, 52),
			Size = new Size(220, 24),
			Font = UiFonts.Body
		};
		((Control)this).Controls.Add((Control)(object)chkList);
		((Control)this).Controls.Add((Control)(object)chkBody);
		AddLabel("附件正文主标题字体", 24, 94);
		cmbMarkerFont = AddFontCombo(155, 90, 120);
		cmbMarkerSize = AddCombo(295, 90);
		Button val = new Button
		{
			Text = "确定",
			Location = new Point(215, 143),
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
			Location = new Point(310, 143),
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
		toolTip.SetToolTip((Control)(object)val, "保存附件排版参数并返回一键排版设置。");
		toolTip.SetToolTip((Control)(object)val2, "放弃本次附件排版参数修改。");
		AppleFormStyler.Apply((Form)(object)this, toolTip);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void ConfigureTooltips()
	{
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Attachment.List", "排版附件说明（清单）", (Control)chkList);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Attachment.Body", "排版附件正文", (Control)chkBody);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Attachment.BodyTitleStyle", "附件正文主标题字体", (Control)cmbMarkerFont, (Control)cmbMarkerSize);
	}

	private void LoadValues()
	{
		AttachmentFormatOptions attachmentOptions = config.AttachmentOptions;
		chkList.Checked = attachmentOptions.FormatAttachmentList;
		chkBody.Checked = attachmentOptions.FormatAttachmentBody;
		((Control)cmbMarkerFont).Text = attachmentOptions.AttachmentMarkerFontName;
		((Control)cmbMarkerSize).Text = attachmentOptions.AttachmentMarkerFontSize;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void SaveValues()
	{
		AttachmentFormatOptions attachmentOptions = config.AttachmentOptions;
		attachmentOptions.FormatAttachmentList = chkList.Checked;
		attachmentOptions.FormatAttachmentBody = chkBody.Checked;
		attachmentOptions.AttachmentMarkerFontName = (string.IsNullOrWhiteSpace(((Control)cmbMarkerFont).Text) ? "方正小标宋简体" : ((Control)cmbMarkerFont).Text.Trim());
		attachmentOptions.AttachmentMarkerFontSize = (string.IsNullOrWhiteSpace(((Control)cmbMarkerSize).Text) ? "二号" : ((Control)cmbMarkerSize).Text.Trim());
	}

	private void AddLabel(string text, int x, int y)
	{
		((Control)this).Controls.Add((Control)new Label
		{
			Text = text,
			Location = new Point(x, y),
			Size = new Size(130, 24),
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
		val.Items.AddRange(new object[6] { "黑体", "方正小标宋简体", "仿宋_GB2312", "楷体_GB2312", "宋体", "微软雅黑" });
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
	private ComboBox AddCombo(int x, int y)
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
}
