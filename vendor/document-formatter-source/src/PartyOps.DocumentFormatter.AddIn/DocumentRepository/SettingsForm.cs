using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using System.Windows.Forms.Layout;
using DocumentRepository.Models.Rules;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.Rules;
using DocumentRepository.Services.Ui;
using DocumentRepository.Services.UiText;
using DocumentRepository.UI.Features;
using Microsoft.Win32;

namespace DocumentRepository;

public class SettingsForm : Form
{
	private static readonly Font fTitle = new Font("微软雅黑", 13f, (FontStyle)1);

	private static readonly Font fGroupBoxTitle = new Font("微软雅黑", 10.5f, (FontStyle)1);

	private static readonly Font fLabel = new Font("微软雅黑", 9.5f);

	private static readonly Font fLabelSmall = new Font("微软雅黑", 9f);

	private static readonly Font fLabelBold = new Font("微软雅黑", 9f, (FontStyle)1);

	private static readonly Font fLabel10 = new Font("微软雅黑", 10f);

	private static readonly Font fButton = new Font("微软雅黑", 11f, (FontStyle)1);

	private static readonly string[] FontNames = SystemFontCatalog.PreferredFonts;

	private const string FontListSeparator = "──────────────";

	private static readonly string[] FontSizeNames = new string[36]
	{
		"初号", "小初", "一号", "小一", "二号", "小二号", "小二", "三号", "小三", "四号",
		"小四", "五号", "小五", "六号", "小六", "七号", "八号", "5", "5.5", "6",
		"6.5", "7.5", "8", "9", "10", "10.5", "11", "12", "14", "16",
		"18", "20", "22", "24", "26", "28"
	};

	private static readonly string[] LineSpacingItems = new string[6] { "28", "29", "30", "31", "32", "单倍行距" };

	private static readonly string[] FirstLineIndentItems = new string[2] { "0", "2" };

	private static readonly string[] ParagraphSpaceItems = new string[7] { "0", "6", "12", "18", "24", "28", "32" };

	private static readonly string[] BoldItems = new string[2] { "加粗", "不加粗" };

	private static readonly string[] KeywordModes = new string[3] { "不加粗", "短语加粗", "整句加粗" };

	private static readonly string[] LeftWingItems = new string[7] { "—", "-", "~", "【", "^", "<", "(" };

	private static readonly string[] RightWingItems = new string[7] { "—", "-", "~", "】", "@", ">", ")" };

	private static readonly string[] PageAlignItems = new string[4] { "奇偶不同", "左对齐", "居中", "右对齐" };

	private static readonly string[] PageNumberModeItems = new string[3] { "开启", "关闭", "首页无页码" };

	private static readonly string[] YesNoItems = new string[2] { "是", "否" };

	private static readonly string[] RecognitionStyleItems = new string[15]
	{
		"一、XX", "（一）XX", "1.XX", "（1）XX", "①XX", "1）XX", "1.1 XX", "1.1.1 XX", "1.1.1.1 XX", "1.1.1.1.1 XX",
		"第一章  XX", "第一部分  XX", "第一篇  XX", "第一节  XX", "第一条  XX"
	};

	private static readonly string[] MainTitleRecognitionModeItems = new string[2] { "规范标题", "首段为标题" };

	private static readonly string[] ParagraphAlignItems = new string[5] { "左对齐", "居中", "右对齐", "两端对齐", "分散对齐" };

	private static readonly string[] OutlineLevelItems = new string[10] { "正文文本", "1级", "2级", "3级", "4级", "5级", "6级", "7级", "8级", "9级" };

	private NumericUpDown nudTop;

	private NumericUpDown nudBottom;

	private NumericUpDown nudLeft;

	private NumericUpDown nudRight;

	private NumericUpDown nudHeader;

	private NumericUpDown nudFooter;

	private ComboBox[] cmbFonts;

	private ComboBox[] cmbSizes;

	private ComboBox[] cmbBold;

	private ComboBox[] cmbLineSpacing;

	private ComboBox[] cmbSpaceBefore;

	private ComboBox[] cmbSpaceAfter;

	private ComboBox[] cmbRecognitionStyles;

	private ComboBox[] cmbAlignments;

	private ComboBox[] cmbOutlineLevels;

	private ToolTip settingsToolTip;

	private ToolTip mainTitleRecognitionToolTip;

	private readonly string[] styleLabels = new string[6] { "主标题", "一级标题", "二级标题", "三级标题", "正文", "页码字体" };

	private ComboBox cmbYiShiMode;

	private ComboBox cmbYiYaoMode;

	private ComboBox cmbDiYiMode;

	private ComboBox[] cmbFirstLineIndent;

	private ComboBox cmbTemplate;

	private Button btnAddTemplate;

	private Button btnRenameTemplate;

	private Button btnDeleteTemplate;

	private Label lblTip;

	private CheckBox chkEnableDocumentGrid;

	private CheckBox chkEnableFixSemicolons;

	private CheckBox chkEnableSignatureFormatting;

	private CheckBox chkEnableOrphanCharFix;

	private CheckBox chkEnableAttachmentFormatting;

	private CheckBox chkEnableTableFormatting;

	private CheckBox chkEnableImageFormatting;

	private CheckBox chkEnableCompilationFormatting;

	private bool loadingValues;

	private ComboBox cmbPageAlign;

	private ComboBox cmbLeftWing;

	private ComboBox cmbRightWing;

	private ComboBox cmbEnablePageNumbers;

	private ComboBox cmbDeleteAi;

	private ComboBox cmbDeleteSpace;

	private ComboBox cmbClearHeadersFooters;

	private ComboBox cmbRemoveHyperlinks;

	private readonly HashSet<ComboBox> fontCombosWithSystemFonts = new HashSet<ComboBox>();

	private readonly Dictionary<ComboBox, string[]> lazyFontPageComboItems = new Dictionary<ComboBox, string[]>();

	private readonly HashSet<ComboBox> populatedLazyFontPageCombos = new HashSet<ComboBox>();

	private FormatConfig config;

	private static string[] GetSystemFontNames()
	{
		return SystemFontCatalog.GetInstalledAdditionalFonts();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public SettingsForm()
	{
		InitializeComponent();
		((Form)this).Load += SettingsForm_Load;
	}

	public void Preload()
	{
		config = ConfigManager.GetCurrentCopy();
		FillComboBoxItems();
		LoadValues();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void SettingsForm_Load(object sender, EventArgs e)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		ConfigManager.Reload();
		RuleLoadResult<TemplateCollection> lastStoreLoadResult = ConfigManager.LastStoreLoadResult;
		if (lastStoreLoadResult != null && !lastStoreLoadResult.Usable)
		{
			if (!RuleStoreResetPrompt.ConfirmAndReset((IWin32Window)(object)this, lastStoreLoadResult, "排版模板库", ConfigManager.ResetStoreToDefault))
			{
				((Form)this).Close();
				return;
			}
			ConfigManager.Reload();
		}
		stopwatch.Restart();
		config = ConfigManager.GetCurrentCopy();
		stopwatch.Restart();
		FillComboBoxItems();
		stopwatch.Restart();
		LoadValues();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void InitializeComponent()
	{
		((Control)this).SuspendLayout();
		((ContainerControl)this).AutoScaleMode = (AutoScaleMode)2;
		((ContainerControl)this).AutoScaleDimensions = new SizeF(96f, 96f);
		((Form)this).ClientSize = new Size(1080, 760);
		((Form)this).FormBorderStyle = (FormBorderStyle)3;
		((Form)this).MaximizeBox = false;
		((Form)this).MinimizeBox = false;
		((Control)this).Name = "SettingsForm";
		((Form)this).StartPosition = (FormStartPosition)1;
		((Control)this).Text = "";
		((Control)this).BackColor = UiColors.BgPage;
		settingsToolTip = UiTextApplier.CreateToolTip();
		Panel val = new Panel
		{
			Location = new Point(0, 0),
			Size = new Size(1080, 40),
			BackColor = UiColors.Primary
		};
		((Control)val).Controls.Add((Control)new Label
		{
			Text = "一键排版自定义参数设置",
			Font = fTitle,
			ForeColor = Color.White,
			Location = new Point(0, 8),
			Size = new Size(1080, 24),
			TextAlign = (ContentAlignment)32,
			BackColor = Color.Transparent
		});
		((Control)this).Controls.Add((Control)(object)val);
		((Control)this).Controls.Add((Control)new Label
		{
			Text = "本插件已启用排版快捷键 Shift+Alt+\"+\"（小键盘加号键）",
			Font = new Font("微软雅黑", 9f, (FontStyle)0),
			ForeColor = UiColors.Danger,
			Location = new Point(20, 45),
			Size = new Size(1040, 20),
			TextAlign = (ContentAlignment)32
		});
		GroupBox val2 = new GroupBox
		{
			Text = " 页边距参数 ",
			Font = fGroupBoxTitle,
			ForeColor = UiColors.Primary,
			Location = new Point(20, 65),
			Size = new Size(220, 242),
			BackColor = UiColors.BgCard
		};
		((Control)val2).SuspendLayout();
		UiTextApplier.ApplyTooltip(settingsToolTip, (Control)(object)val2, "Settings.PageMargins");
		string[] array = new string[6] { "上边距", "下边距", "左边距", "右边距", "页眉距", "页脚距" };
		nudTop = CreateNumericUpDown(92, 26);
		nudBottom = CreateNumericUpDown(92, 60);
		nudLeft = CreateNumericUpDown(92, 94);
		nudRight = CreateNumericUpDown(92, 128);
		nudHeader = CreateNumericUpDown(92, 162);
		nudFooter = CreateNumericUpDown(92, 196);
		NumericUpDown[] array2 = (NumericUpDown[])(object)new NumericUpDown[6] { nudTop, nudBottom, nudLeft, nudRight, nudHeader, nudFooter };
		for (int i = 0; i < 6; i++)
		{
			int num = 28 + i * 34;
			((Control)val2).Controls.Add((Control)new Label
			{
				Text = array[i],
				Location = new Point(12, num + 2),
				Size = new Size(68, 20),
				Font = fLabel,
				ForeColor = UiColors.TextBody,
				TextAlign = (ContentAlignment)64
			});
			((Control)val2).Controls.Add((Control)(object)array2[i]);
			((Control)val2).Controls.Add((Control)new Label
			{
				Text = "厘米",
				Location = new Point(176, num + 2),
				Size = new Size(40, 20),
				Font = fLabelSmall,
				ForeColor = UiColors.TextMuted
			});
		}
		NumericUpDown[] array3 = array2;
		foreach (NumericUpDown control in array3)
		{
			UiTextApplier.ApplyTooltip(settingsToolTip, (Control)(object)control, "Settings.PageMargins");
		}
		((Control)val2).ResumeLayout(false);
		((Control)this).Controls.Add((Control)(object)val2);
		AppleToggleSwitch appleToggleSwitch = new AppleToggleSwitch();
		((Control)appleToggleSwitch).Text = "文档网格";
		((Control)appleToggleSwitch).Location = new Point(20, 312);
		((Control)appleToggleSwitch).Size = new Size(120, 24);
		((Control)appleToggleSwitch).Font = fLabel10;
		((Control)appleToggleSwitch).ForeColor = UiColors.TextBody;
		chkEnableDocumentGrid = (CheckBox)(object)appleToggleSwitch;
		UiTextApplier.ApplyTooltip(settingsToolTip, (Control)(object)chkEnableDocumentGrid, "Settings.DocumentGrid");
		((Control)this).Controls.Add((Control)(object)chkEnableDocumentGrid);
		StyledButton styledButton = new StyledButton();
		((Control)styledButton).Text = "网格参数";
		((Control)styledButton).Location = new Point(145, 311);
		((Control)styledButton).Size = new Size(88, 24);
		((Control)styledButton).Font = fLabelSmall;
		((ButtonBase)styledButton).FlatStyle = (FlatStyle)0;
		((Control)styledButton).BackColor = UiColors.PrimaryLight;
		((Control)styledButton).ForeColor = Color.White;
		((Control)styledButton).Cursor = Cursors.Hand;
		StyledButton styledButton2 = styledButton;
		((ButtonBase)styledButton2).FlatAppearance.BorderSize = 0;
		UiTextApplier.ApplyTooltip(settingsToolTip, (Control)(object)styledButton2, "Settings.DocumentGrid");
		((Control)styledButton2).Click += delegate
		{
			config.TopMargin = (float)nudTop.Value;
			config.BottomMargin = (float)nudBottom.Value;
			DocumentGridSettingsForm documentGridSettingsForm = new DocumentGridSettingsForm(config);
			try
			{
				((Form)documentGridSettingsForm).ShowDialog((IWin32Window)(object)this);
			}
			finally
			{
				((IDisposable)(object)documentGridSettingsForm)?.Dispose();
			}
		};
		((Control)this).Controls.Add((Control)(object)styledButton2);
		AppleToggleSwitch appleToggleSwitch2 = new AppleToggleSwitch();
		((Control)appleToggleSwitch2).Text = "修正分号";
		((Control)appleToggleSwitch2).Location = new Point(20, 438);
		((Control)appleToggleSwitch2).Size = new Size(220, 24);
		((Control)appleToggleSwitch2).Font = fLabel10;
		((Control)appleToggleSwitch2).ForeColor = UiColors.TextBody;
		chkEnableFixSemicolons = (CheckBox)(object)appleToggleSwitch2;
		UiTextApplier.ApplyTooltip(settingsToolTip, (Control)(object)chkEnableFixSemicolons, "Settings.FixSemicolons");
		((Control)this).Controls.Add((Control)(object)chkEnableFixSemicolons);
		AppleToggleSwitch appleToggleSwitch3 = new AppleToggleSwitch();
		((Control)appleToggleSwitch3).Text = "落款排版";
		((Control)appleToggleSwitch3).Location = new Point(20, 338);
		((Control)appleToggleSwitch3).Size = new Size(120, 24);
		((Control)appleToggleSwitch3).Font = fLabel10;
		((Control)appleToggleSwitch3).ForeColor = UiColors.TextBody;
		chkEnableSignatureFormatting = (CheckBox)(object)appleToggleSwitch3;
		UiTextApplier.ApplyTooltip(settingsToolTip, (Control)(object)chkEnableSignatureFormatting, "Settings.SignatureWithSeal");
		((Control)this).Controls.Add((Control)(object)chkEnableSignatureFormatting);
		StyledButton styledButton3 = new StyledButton();
		((Control)styledButton3).Text = "落款参数";
		((Control)styledButton3).Location = new Point(145, 337);
		((Control)styledButton3).Size = new Size(88, 24);
		((Control)styledButton3).Font = fLabelSmall;
		((ButtonBase)styledButton3).FlatStyle = (FlatStyle)0;
		((Control)styledButton3).BackColor = UiColors.PrimaryLight;
		((Control)styledButton3).ForeColor = Color.White;
		((Control)styledButton3).Cursor = Cursors.Hand;
		StyledButton styledButton4 = styledButton3;
		((ButtonBase)styledButton4).FlatAppearance.BorderSize = 0;
		UiTextApplier.ApplyTooltip(settingsToolTip, (Control)(object)styledButton4, "Settings.SignatureWithSeal");
		((Control)styledButton4).Click += delegate
		{
			SignatureSettingsForm signatureSettingsForm = new SignatureSettingsForm(config);
			try
			{
				((Form)signatureSettingsForm).ShowDialog((IWin32Window)(object)this);
			}
			finally
			{
				((IDisposable)(object)signatureSettingsForm)?.Dispose();
			}
		};
		((Control)this).Controls.Add((Control)(object)styledButton4);
		AppleToggleSwitch appleToggleSwitch4 = new AppleToggleSwitch();
		((Control)appleToggleSwitch4).Text = "孤字不成行";
		((Control)appleToggleSwitch4).Location = new Point(20, 464);
		((Control)appleToggleSwitch4).Size = new Size(220, 24);
		((Control)appleToggleSwitch4).Font = fLabel10;
		((Control)appleToggleSwitch4).ForeColor = UiColors.TextBody;
		chkEnableOrphanCharFix = (CheckBox)(object)appleToggleSwitch4;
		UiTextApplier.ApplyTooltip(settingsToolTip, (Control)(object)chkEnableOrphanCharFix, "Settings.OrphanCharFix");
		((Control)this).Controls.Add((Control)(object)chkEnableOrphanCharFix);
		AppleToggleSwitch appleToggleSwitch5 = new AppleToggleSwitch();
		((Control)appleToggleSwitch5).Text = "附件排版";
		((Control)appleToggleSwitch5).Location = new Point(20, 364);
		((Control)appleToggleSwitch5).Size = new Size(120, 24);
		((Control)appleToggleSwitch5).Font = fLabel10;
		((Control)appleToggleSwitch5).ForeColor = UiColors.TextBody;
		chkEnableAttachmentFormatting = (CheckBox)(object)appleToggleSwitch5;
		UiTextApplier.ApplyTooltip(settingsToolTip, (Control)(object)chkEnableAttachmentFormatting, "Settings.AttachmentFormatting");
		((Control)this).Controls.Add((Control)(object)chkEnableAttachmentFormatting);
		StyledButton styledButton5 = new StyledButton();
		((Control)styledButton5).Text = "附件参数";
		((Control)styledButton5).Location = new Point(145, 363);
		((Control)styledButton5).Size = new Size(88, 24);
		((Control)styledButton5).Font = fLabelSmall;
		((ButtonBase)styledButton5).FlatStyle = (FlatStyle)0;
		((Control)styledButton5).BackColor = UiColors.PrimaryLight;
		((Control)styledButton5).ForeColor = Color.White;
		((Control)styledButton5).Cursor = Cursors.Hand;
		StyledButton styledButton6 = styledButton5;
		((ButtonBase)styledButton6).FlatAppearance.BorderSize = 0;
		UiTextApplier.ApplyTooltip(settingsToolTip, (Control)(object)styledButton6, "Settings.AttachmentFormatting");
		((Control)styledButton6).Click += delegate
		{
			AttachmentSettingsForm attachmentSettingsForm = new AttachmentSettingsForm(config);
			try
			{
				if ((int)((Form)attachmentSettingsForm).ShowDialog((IWin32Window)(object)this) == 1)
				{
					ConfigManager.SaveCurrentAttachmentOptions(config.AttachmentOptions);
				}
			}
			finally
			{
				((IDisposable)attachmentSettingsForm)?.Dispose();
			}
		};
		((Control)this).Controls.Add((Control)(object)styledButton6);
		AppleToggleSwitch appleToggleSwitch6 = new AppleToggleSwitch();
		((Control)appleToggleSwitch6).Text = "表格排版";
		((Control)appleToggleSwitch6).Location = new Point(20, 386);
		((Control)appleToggleSwitch6).Size = new Size(120, 24);
		((Control)appleToggleSwitch6).Font = fLabel10;
		((Control)appleToggleSwitch6).ForeColor = UiColors.TextBody;
		chkEnableTableFormatting = (CheckBox)(object)appleToggleSwitch6;
		UiTextApplier.ApplyTooltip(settingsToolTip, (Control)(object)chkEnableTableFormatting, "Settings.TableFormatting");
		((Control)this).Controls.Add((Control)(object)chkEnableTableFormatting);
		StyledButton styledButton7 = new StyledButton();
		((Control)styledButton7).Text = "表格参数";
		((Control)styledButton7).Location = new Point(145, 385);
		((Control)styledButton7).Size = new Size(88, 24);
		((Control)styledButton7).Font = fLabelSmall;
		((ButtonBase)styledButton7).FlatStyle = (FlatStyle)0;
		((Control)styledButton7).BackColor = UiColors.PrimaryLight;
		((Control)styledButton7).ForeColor = Color.White;
		((Control)styledButton7).Cursor = Cursors.Hand;
		StyledButton styledButton8 = styledButton7;
		((ButtonBase)styledButton8).FlatAppearance.BorderSize = 0;
		UiTextApplier.ApplyTooltip(settingsToolTip, (Control)(object)styledButton8, "Settings.TableFormatting");
		((Control)styledButton8).Click += delegate
		{
			TableSettingsForm tableSettingsForm = new TableSettingsForm(config);
			try
			{
				if ((int)((Form)tableSettingsForm).ShowDialog((IWin32Window)(object)this) == 1)
				{
					ConfigManager.SaveCurrentTableOptions(config.TableOptions);
				}
			}
			finally
			{
				((IDisposable)tableSettingsForm)?.Dispose();
			}
		};
		((Control)this).Controls.Add((Control)(object)styledButton8);
		AppleToggleSwitch appleToggleSwitch7 = new AppleToggleSwitch();
		((Control)appleToggleSwitch7).Text = "图片排版";
		((Control)appleToggleSwitch7).Location = new Point(20, 412);
		((Control)appleToggleSwitch7).Size = new Size(120, 24);
		((Control)appleToggleSwitch7).Font = fLabel10;
		((Control)appleToggleSwitch7).ForeColor = UiColors.TextBody;
		chkEnableImageFormatting = (CheckBox)(object)appleToggleSwitch7;
		UiTextApplier.ApplyTooltip(settingsToolTip, (Control)(object)chkEnableImageFormatting, "Settings.ImageFormatting");
		((Control)this).Controls.Add((Control)(object)chkEnableImageFormatting);
		StyledButton styledButton9 = new StyledButton();
		((Control)styledButton9).Text = "图片参数";
		((Control)styledButton9).Location = new Point(145, 411);
		((Control)styledButton9).Size = new Size(88, 24);
		((Control)styledButton9).Font = fLabelSmall;
		((ButtonBase)styledButton9).FlatStyle = (FlatStyle)0;
		((Control)styledButton9).BackColor = UiColors.PrimaryLight;
		((Control)styledButton9).ForeColor = Color.White;
		((Control)styledButton9).Cursor = Cursors.Hand;
		StyledButton styledButton10 = styledButton9;
		((ButtonBase)styledButton10).FlatAppearance.BorderSize = 0;
		UiTextApplier.ApplyTooltip(settingsToolTip, (Control)(object)styledButton10, "Settings.ImageFormatting");
		((Control)styledButton10).Click += delegate
		{
			ImageSettingsForm imageSettingsForm = new ImageSettingsForm(config);
			try
			{
				if ((int)((Form)imageSettingsForm).ShowDialog((IWin32Window)(object)this) == 1)
				{
					ConfigManager.SaveCurrentImageOptions(config.ImageOptions);
				}
			}
			finally
			{
				((IDisposable)(object)imageSettingsForm)?.Dispose();
			}
		};
		((Control)this).Controls.Add((Control)(object)styledButton10);
		AppleToggleSwitch appleToggleSwitch8 = new AppleToggleSwitch();
		((Control)appleToggleSwitch8).Text = "汇编排版";
		((Control)appleToggleSwitch8).Location = new Point(240, 412);
		((Control)appleToggleSwitch8).Size = new Size(120, 24);
		((Control)appleToggleSwitch8).Font = fLabel10;
		((Control)appleToggleSwitch8).ForeColor = UiColors.TextBody;
		chkEnableCompilationFormatting = (CheckBox)(object)appleToggleSwitch8;
		UiTextApplier.ApplyTooltip(settingsToolTip, (Control)(object)chkEnableCompilationFormatting, "Settings.CompilationFormatting");
		chkEnableCompilationFormatting.CheckedChanged += CompilationToggle_CheckedChanged;
		((Control)this).Controls.Add((Control)(object)chkEnableCompilationFormatting);
		StyledButton styledButton11 = new StyledButton();
		((Control)styledButton11).Text = "汇编参数";
		((Control)styledButton11).Location = new Point(365, 411);
		((Control)styledButton11).Size = new Size(88, 24);
		((Control)styledButton11).Font = fLabelSmall;
		((ButtonBase)styledButton11).FlatStyle = (FlatStyle)0;
		((Control)styledButton11).BackColor = UiColors.PrimaryLight;
		((Control)styledButton11).ForeColor = Color.White;
		((Control)styledButton11).Cursor = Cursors.Hand;
		StyledButton styledButton12 = styledButton11;
		((ButtonBase)styledButton12).FlatAppearance.BorderSize = 0;
		UiTextApplier.ApplyTooltip(settingsToolTip, (Control)(object)styledButton12, "Settings.CompilationOptions");
		((Control)styledButton12).Click += delegate
		{
			if (config.CompilationFormatOptions == null)
			{
				config.CompilationFormatOptions = new CompilationFormatOptions();
			}
			CompilationSettingsForm compilationSettingsForm = new CompilationSettingsForm(config.CompilationFormatOptions);
			try
			{
				((Form)compilationSettingsForm).ShowDialog((IWin32Window)(object)this);
			}
			finally
			{
				((IDisposable)compilationSettingsForm)?.Dispose();
			}
		};
		((Control)this).Controls.Add((Control)(object)styledButton12);
		GroupBox val3 = new GroupBox
		{
			Text = " 字体参数 ",
			Font = fGroupBoxTitle,
			ForeColor = UiColors.Primary,
			Location = new Point(240, 65),
			Size = new Size(820, 316),
			BackColor = UiColors.BgCard
		};
		((Control)val3).SuspendLayout();
		string[] array4 = new string[11]
		{
			"类型", "字体", "字号", "加粗", "行距", "首行", "段前", "段后", "对齐", "大纲",
			"识别样式"
		};
		int[] array5 = new int[] { 6, 86, 218, 280, 336, 388, 444, 500, 556, 644, 704 };
		int[] array6 = new int[] { 76, 128, 58, 52, 48, 52, 52, 52, 84, 56, 96 };
		string[] array7 = new string[11]
		{
			null, "Settings.Font", "Settings.FontSize", null, null, null, "Settings.SpaceBefore", "Settings.SpaceAfter", null, null,
			"Settings.RecognitionStyle"
		};
		for (int num2 = 0; num2 < array4.Length; num2++)
		{
			Label val4 = new Label
			{
				Text = array4[num2],
				Location = new Point(array5[num2], 24),
				Size = new Size(array6[num2], 20),
				Font = fLabelBold,
				ForeColor = UiColors.TextBody,
				TextAlign = (ContentAlignment)32
			};
			if (!string.IsNullOrEmpty(array7[num2]))
			{
				UiTextApplier.ApplyTooltip(settingsToolTip, (Control)(object)val4, array7[num2]);
			}
			((Control)val3).Controls.Add((Control)(object)val4);
		}
		((Control)val3).Controls.Add((Control)new Panel
		{
			Location = new Point(8, 46),
			Size = new Size(806, 1),
			BackColor = UiColors.Border
		});
		int[] array8 = new int[] { 2, 82, 214, 276, 332, 384, 440, 496, 552, 640, 700 };
		cmbFonts = (ComboBox[])(object)new ComboBox[7];
		cmbSizes = (ComboBox[])(object)new ComboBox[6];
		cmbBold = (ComboBox[])(object)new ComboBox[6];
		cmbRecognitionStyles = (ComboBox[])(object)new ComboBox[6];
		cmbAlignments = (ComboBox[])(object)new ComboBox[6];
		cmbOutlineLevels = (ComboBox[])(object)new ComboBox[6];
		cmbLineSpacing = (ComboBox[])(object)new ComboBox[5];
		cmbFirstLineIndent = (ComboBox[])(object)new ComboBox[5];
		cmbSpaceBefore = (ComboBox[])(object)new ComboBox[5];
		cmbSpaceAfter = (ComboBox[])(object)new ComboBox[5];
		mainTitleRecognitionToolTip = UiTextApplier.CreateToolTip();
		for (int num3 = 0; num3 < 6; num3++)
		{
			int y = 48 + num3 * 37;
			Color backColor = ((num3 % 2 == 0) ? Color.White : UiColors.BgMuted);
			Panel val5 = new Panel
			{
				Location = new Point(8, y),
				Size = new Size(806, 34),
				BackColor = backColor
			};
			((Control)val5).Controls.Add((Control)new Label
			{
				Text = styleLabels[num3],
				Location = new Point(array8[0], 5),
				Size = new Size(array6[0], 24),
				Font = fLabel,
				ForeColor = UiColors.TextTitle,
				TextAlign = (ContentAlignment)32,
				BackColor = Color.Transparent
			});
			cmbFonts[num3] = CreateComboBox(new Point(array8[1], 6), array6[1] - 4);
			cmbFonts[num3].DropDown += FontCombo_DropDown;
			cmbSizes[num3] = CreateComboBox(new Point(array8[2], 6), array6[2] - 4, editable: true);
			cmbBold[num3] = CreateComboBox(new Point(array8[3], 6), array6[3] - 4);
			UiTextApplier.ApplyTooltip(settingsToolTip, (Control)(object)cmbFonts[num3], "Settings.Font");
			UiTextApplier.ApplyTooltip(settingsToolTip, (Control)(object)cmbSizes[num3], "Settings.FontSize");
			((Control)val5).Controls.Add((Control)(object)cmbFonts[num3]);
			((Control)val5).Controls.Add((Control)(object)cmbSizes[num3]);
			((Control)val5).Controls.Add((Control)(object)cmbBold[num3]);
			if (num3 < 5)
			{
				cmbAlignments[num3] = CreateComboBox(new Point(array8[8], 6), array6[8] - 4);
				((Control)val5).Controls.Add((Control)(object)cmbAlignments[num3]);
				cmbOutlineLevels[num3] = CreateComboBox(new Point(array8[9], 6), array6[9] - 4);
				((Control)val5).Controls.Add((Control)(object)cmbOutlineLevels[num3]);
			}
			switch (num3)
			{
			case 0:
			case 1:
			case 2:
			case 3:
				cmbRecognitionStyles[num3] = CreateComboBox(new Point(array8[10], 6), array6[10] - 4);
				((Control)val5).Controls.Add((Control)(object)cmbRecognitionStyles[num3]);
				if (num3 != 0)
				{
					UiTextApplier.ApplyTooltip(settingsToolTip, (Control)(object)cmbRecognitionStyles[num3], "Settings.RecognitionStyle");
					break;
				}
				UiTextApplier.ApplyTooltipText(mainTitleRecognitionToolTip, (Control)(object)cmbRecognitionStyles[num3], GetMainTitleRecognitionTip(((Control)cmbRecognitionStyles[num3]).Text));
				cmbRecognitionStyles[num3].SelectedIndexChanged += delegate
				{
					UiTextApplier.ApplyTooltipText(mainTitleRecognitionToolTip, (Control)(object)cmbRecognitionStyles[0], GetMainTitleRecognitionTip(((Control)cmbRecognitionStyles[0]).Text));
				};
				((Control)cmbRecognitionStyles[num3]).TextChanged += delegate
				{
					UiTextApplier.ApplyTooltipText(mainTitleRecognitionToolTip, (Control)(object)cmbRecognitionStyles[0], GetMainTitleRecognitionTip(((Control)cmbRecognitionStyles[0]).Text));
				};
				break;
			}
			if (num3 < 5)
			{
				cmbLineSpacing[num3] = CreateComboBox(new Point(array8[4], 6), array6[4] - 4, editable: true);
				((Control)val5).Controls.Add((Control)(object)cmbLineSpacing[num3]);
				cmbFirstLineIndent[num3] = CreateComboBox(new Point(array8[5], 6), array6[5] - 4, editable: true);
				((Control)val5).Controls.Add((Control)(object)cmbFirstLineIndent[num3]);
				cmbSpaceBefore[num3] = CreateComboBox(new Point(array8[6], 6), array6[6] - 4, editable: true);
				((Control)val5).Controls.Add((Control)(object)cmbSpaceBefore[num3]);
				UiTextApplier.ApplyTooltip(settingsToolTip, (Control)(object)cmbSpaceBefore[num3], "Settings.SpaceBefore");
				cmbSpaceAfter[num3] = CreateComboBox(new Point(array8[7], 6), array6[7] - 4, editable: true);
				((Control)val5).Controls.Add((Control)(object)cmbSpaceAfter[num3]);
				UiTextApplier.ApplyTooltip(settingsToolTip, (Control)(object)cmbSpaceAfter[num3], "Settings.SpaceAfter");
			}
			((Control)val3).Controls.Add((Control)(object)val5);
		}
		Panel val6 = new Panel
		{
			Location = new Point(8, 270),
			Size = new Size(806, 34),
			BackColor = UiColors.BgMuted
		};
		Label val7 = new Label
		{
			Text = "英文和数字",
			Location = new Point(array8[0], 5),
			Size = new Size(array6[0], 24),
			Font = fLabel,
			ForeColor = UiColors.TextTitle,
			TextAlign = (ContentAlignment)32,
			BackColor = Color.Transparent
		};
		((Control)val6).Controls.Add((Control)(object)val7);
		UiTextApplier.ApplyTooltip(settingsToolTip, (Control)(object)val7, "Settings.EnglishNumberFont");
		cmbFonts[6] = CreateComboBox(new Point(array8[1], 6), array6[1] - 4);
		cmbFonts[6].DropDown += FontCombo_DropDown;
		UiTextApplier.ApplyTooltip(settingsToolTip, (Control)(object)cmbFonts[6], "Settings.EnglishNumberFont");
		((Control)val6).Controls.Add((Control)(object)cmbFonts[6]);
		((Control)val3).Controls.Add((Control)(object)val6);
		((Control)val3).ResumeLayout(false);
		((Control)this).Controls.Add((Control)(object)val3);
		GroupBox val8 = new GroupBox
		{
			Text = " 关键词加粗 ",
			Font = fGroupBoxTitle,
			ForeColor = UiColors.Primary,
			Location = new Point(240, 394),
			Size = new Size(820, 72),
			BackColor = UiColors.BgCard
		};
		((Control)val8).SuspendLayout();
		UiTextApplier.ApplyTooltip(settingsToolTip, (Control)(object)val8, "Settings.KeywordBold");
		((Control)val8).Controls.Add((Control)new Label
		{
			Text = "▶ 一是至二十是",
			Location = new Point(20, 30),
			Size = new Size(100, 20),
			Font = fLabel,
			ForeColor = UiColors.TextBody
		});
		cmbYiShiMode = CreateComboBox(new Point(125, 26), 120);
		cmbYiShiMode.SelectedIndexChanged += delegate
		{
			ApplyKeywordModeTooltip(cmbYiShiMode);
		};
		((Control)cmbYiShiMode).TextChanged += delegate
		{
			ApplyKeywordModeTooltip(cmbYiShiMode);
		};
		((Control)val8).Controls.Add((Control)(object)cmbYiShiMode);
		((Control)val8).Controls.Add((Control)new Label
		{
			Text = "▶ 一要至二十要",
			Location = new Point(285, 30),
			Size = new Size(100, 20),
			Font = fLabel,
			ForeColor = UiColors.TextBody
		});
		cmbYiYaoMode = CreateComboBox(new Point(390, 26), 120);
		cmbYiYaoMode.SelectedIndexChanged += delegate
		{
			ApplyKeywordModeTooltip(cmbYiYaoMode);
		};
		((Control)cmbYiYaoMode).TextChanged += delegate
		{
			ApplyKeywordModeTooltip(cmbYiYaoMode);
		};
		((Control)val8).Controls.Add((Control)(object)cmbYiYaoMode);
		((Control)val8).Controls.Add((Control)new Label
		{
			Text = "▶ 第一至第二十",
			Location = new Point(550, 30),
			Size = new Size(100, 20),
			Font = fLabel,
			ForeColor = UiColors.TextBody
		});
		cmbDiYiMode = CreateComboBox(new Point(655, 26), 120);
		cmbDiYiMode.SelectedIndexChanged += delegate
		{
			ApplyKeywordModeTooltip(cmbDiYiMode);
		};
		((Control)cmbDiYiMode).TextChanged += delegate
		{
			ApplyKeywordModeTooltip(cmbDiYiMode);
		};
		((Control)val8).Controls.Add((Control)(object)cmbDiYiMode);
		((Control)val8).ResumeLayout(false);
		((Control)this).Controls.Add((Control)(object)val8);
		GroupBox val9 = new GroupBox
		{
			Text = " 页码设置 ",
			Font = fGroupBoxTitle,
			ForeColor = UiColors.Primary,
			Location = new Point(240, 481),
			Size = new Size(820, 65),
			BackColor = UiColors.BgCard
		};
		((Control)val9).SuspendLayout();
		((Control)val9).Controls.Add((Control)new Label
		{
			Text = "页码对齐",
			Location = new Point(20, 28),
			Size = new Size(60, 20),
			Font = fLabel,
			ForeColor = UiColors.TextBody
		});
		cmbPageAlign = CreateComboBox(new Point(90, 24), 110);
		UiTextApplier.ApplyTooltipText(settingsToolTip, (Control)(object)cmbPageAlign, "设置页码在奇偶页或所有页面中的水平对齐方式。");
		((Control)val9).Controls.Add((Control)(object)cmbPageAlign);
		Label val10 = new Label
		{
			Text = "页码开启",
			Location = new Point(245, 28),
			Size = new Size(60, 20),
			Font = fLabel,
			ForeColor = UiColors.TextBody
		};
		UiTextApplier.ApplyTooltip(settingsToolTip, (Control)(object)val10, "Settings.Page.NumberMode");
		((Control)val9).Controls.Add((Control)(object)val10);
		cmbEnablePageNumbers = CreateComboBox(new Point(315, 24), 120);
		UiTextApplier.ApplyTooltip(settingsToolTip, (Control)(object)cmbEnablePageNumbers, "Settings.Page.NumberMode");
		((Control)val9).Controls.Add((Control)(object)cmbEnablePageNumbers);
		Label val11 = new Label
		{
			Text = "左侧符号",
			Location = new Point(470, 28),
			Size = new Size(60, 20),
			Font = fLabel,
			ForeColor = UiColors.TextBody
		};
		UiTextApplier.ApplyTooltip(settingsToolTip, (Control)(object)val11, "Settings.Page.LeftSymbol");
		((Control)val9).Controls.Add((Control)(object)val11);
		cmbLeftWing = CreateComboBox(new Point(540, 24), 70, editable: true);
		UiTextApplier.ApplyTooltip(settingsToolTip, (Control)(object)cmbLeftWing, "Settings.Page.LeftSymbol");
		((Control)val9).Controls.Add((Control)(object)cmbLeftWing);
		Label val12 = new Label
		{
			Text = "右侧符号",
			Location = new Point(660, 28),
			Size = new Size(60, 20),
			Font = fLabel,
			ForeColor = UiColors.TextBody
		};
		UiTextApplier.ApplyTooltip(settingsToolTip, (Control)(object)val12, "Settings.Page.RightSymbol");
		((Control)val9).Controls.Add((Control)(object)val12);
		cmbRightWing = CreateComboBox(new Point(730, 24), 70, editable: true);
		UiTextApplier.ApplyTooltip(settingsToolTip, (Control)(object)cmbRightWing, "Settings.Page.RightSymbol");
		((Control)val9).Controls.Add((Control)(object)cmbRightWing);
		((Control)val9).ResumeLayout(false);
		((Control)this).Controls.Add((Control)(object)val9);
		GroupBox val13 = new GroupBox
		{
			Text = " 清理选项 ",
			Font = fGroupBoxTitle,
			ForeColor = UiColors.Primary,
			Location = new Point(240, 556),
			Size = new Size(820, 72),
			BackColor = UiColors.BgCard
		};
		((Control)val13).SuspendLayout();
		((Control)val13).Controls.Add((Control)new Label
		{
			Text = "▶ 删除 AI 符号",
			Location = new Point(20, 30),
			Size = new Size(100, 20),
			Font = fLabel,
			ForeColor = UiColors.TextBody
		});
		cmbDeleteAi = CreateComboBox(new Point(120, 26), 60);
		UiTextApplier.ApplyTooltipText(settingsToolTip, (Control)(object)cmbDeleteAi, "选择是否清理正文中的 AI 生成痕迹符号，以及网页复制在中文句间留下的不间断空格；不修改普通标点文字。");
		((Control)val13).Controls.Add((Control)(object)cmbDeleteAi);
		((Control)val13).Controls.Add((Control)new Label
		{
			Text = "▶ 删除空格",
			Location = new Point(215, 30),
			Size = new Size(90, 20),
			Font = fLabel,
			ForeColor = UiColors.TextBody
		});
		cmbDeleteSpace = CreateComboBox(new Point(305, 26), 60);
		UiTextApplier.ApplyTooltipText(settingsToolTip, (Control)(object)cmbDeleteSpace, "选择是否清理正文中多余的半角和全角空格。");
		((Control)val13).Controls.Add((Control)(object)cmbDeleteSpace);
		((Control)val13).Controls.Add((Control)new Label
		{
			Text = "▶ 清空页眉页脚",
			Location = new Point(400, 30),
			Size = new Size(120, 20),
			Font = fLabel,
			ForeColor = UiColors.TextBody
		});
		cmbClearHeadersFooters = CreateComboBox(new Point(525, 26), 60);
		UiTextApplier.ApplyTooltipText(settingsToolTip, (Control)(object)cmbClearHeadersFooters, "选择是否在重新生成页码前清空原有页眉和页脚内容。");
		((Control)val13).Controls.Add((Control)(object)cmbClearHeadersFooters);
		((Control)val13).Controls.Add((Control)new Label
		{
			Text = "▶ 清理超链接",
			Location = new Point(620, 30),
			Size = new Size(90, 20),
			Font = fLabel,
			ForeColor = UiColors.TextBody
		});
		cmbRemoveHyperlinks = CreateComboBox(new Point(720, 26), 60);
		UiTextApplier.ApplyTooltipText(settingsToolTip, (Control)(object)cmbRemoveHyperlinks, "选择是否移除正文中的超链接，同时保留显示文字。");
		((Control)val13).Controls.Add((Control)(object)cmbRemoveHyperlinks);
		((Control)val13).ResumeLayout(false);
		((Control)this).Controls.Add((Control)(object)val13);
		Label val16 = new Label
		{
			Text = "当前模板",
			Location = new Point(232, 642),
			Size = new Size(60, 20),
			Font = fLabel10,
			ForeColor = UiColors.TextBody
		};
		cmbTemplate = new ComboBox
		{
			Location = new Point(297, 638),
			Size = new Size(295, 21),
			DropDownStyle = (ComboBoxStyle)2,
			Font = fLabel
		};
		cmbTemplate.SelectedIndexChanged += CmbTemplate_SelectedIndexChanged;
		lblTip = new Label
		{
			Text = "默认模板不能调整参数，请选择其他模板",
			Location = new Point(297, 668),
			Size = new Size(295, 18),
			Font = fLabelSmall,
			ForeColor = UiColors.Danger,
			TextAlign = (ContentAlignment)32,
			Visible = false
		};
		StyledButton styledButton13 = new StyledButton();
		((Control)styledButton13).Text = "设为当前模板固定参数";
		((Control)styledButton13).Location = new Point(610, 638);
		((Control)styledButton13).Size = new Size(450, 46);
		((Control)styledButton13).Font = fButton;
		((ButtonBase)styledButton13).FlatStyle = (FlatStyle)0;
		((Control)styledButton13).BackColor = UiColors.PrimaryLight;
		((Control)styledButton13).ForeColor = Color.White;
		((Control)styledButton13).Cursor = Cursors.Hand;
		StyledButton styledButton14 = styledButton13;
		((ButtonBase)styledButton14).FlatAppearance.BorderSize = 0;
		((Control)styledButton14).Click += BtnSave_Click;
		((Control)this).Controls.Add((Control)(object)val16);
		((Control)this).Controls.Add((Control)(object)cmbTemplate);
		((Control)this).Controls.Add((Control)(object)lblTip);
		((Control)this).Controls.Add((Control)(object)styledButton14);
		ApplyAppleScientificLayout(val2, (Button)(object)styledButton2, (Button)(object)styledButton4, (Button)(object)styledButton6, (Button)(object)styledButton8, (Button)(object)styledButton10, (Button)(object)styledButton12, val3, val8, val9, val13, val16, (Button)(object)styledButton14);
		((Control)this).ResumeLayout(false);
		((Control)this).PerformLayout();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void ApplyAppleScientificLayout(GroupBox legacyMargin, Button btnGridSettings, Button btnSignatureSettings, Button btnAttachmentSettings, Button btnTableSettings, Button btnImageSettings, Button btnCompilationSettings, GroupBox gbFont, GroupBox gbKeyword, GroupBox gbPage, GroupBox gbClean, Label lblCurrentTemplate, Button btnSave)
	{
		((Form)this).ClientSize = new Size(1120, 660);
		((Control)this).Text = "一键排版自定义参数设置";
		((Control)this).BackColor = AppleUiColors.Window;
		((Control)this).Font = UiFonts.Body;
		((Control)this).Controls.Clear();
		TableLayoutPanel val = new TableLayoutPanel
		{
			Dock = (DockStyle)5,
			BackColor = AppleUiColors.Window,
			ColumnCount = 1,
			RowCount = 3,
			Margin = Padding.Empty,
			Padding = Padding.Empty
		};
		val.RowStyles.Add(new RowStyle((SizeType)1, 64f));
		val.RowStyles.Add(new RowStyle((SizeType)2, 100f));
		val.RowStyles.Add(new RowStyle((SizeType)1, 60f));
		((Control)this).Controls.Add((Control)(object)val);
		Panel val2 = BuildAppleSettingsHeader(lblCurrentTemplate);
		val.Controls.Add((Control)(object)val2, 0, 0);
		TableLayoutPanel val3 = new TableLayoutPanel
		{
			Dock = (DockStyle)5,
			BackColor = AppleUiColors.Surface,
			ColumnCount = 2,
			RowCount = 1,
			Margin = Padding.Empty,
			Padding = Padding.Empty
		};
		val3.ColumnStyles.Add(new ColumnStyle((SizeType)1, 170f));
		val3.ColumnStyles.Add(new ColumnStyle((SizeType)2, 100f));
		val.Controls.Add((Control)(object)val3, 0, 1);
		Panel sidebar = new Panel
		{
			Dock = (DockStyle)5,
			BackColor = AppleUiColors.SurfaceMuted,
			Padding = new Padding(10)
		};
		((Control)sidebar).Paint += (PaintEventHandler)delegate(object s, PaintEventArgs e)
		{
			Pen val13 = new Pen(AppleUiColors.Separator);
			try
			{
				e.Graphics.DrawLine(val13, ((Control)sidebar).ClientSize.Width - 1, 0, ((Control)sidebar).ClientSize.Width - 1, ((Control)sidebar).ClientSize.Height);
			}
			finally
			{
				((IDisposable)val13)?.Dispose();
			}
		};
		val3.Controls.Add((Control)(object)sidebar, 0, 0);
		FlowLayoutPanel body;
		Panel val4 = CreateAppleSettingsPage("基础设置", string.Empty, out body);
		FlowLayoutPanel body2;
		Panel val5 = CreateAppleSettingsPage("字体样式", string.Empty, out body2);
		FlowLayoutPanel body3;
		Panel val6 = CreateAppleSettingsPage("关键词", string.Empty, out body3);
		FlowLayoutPanel body4;
		Panel val7 = CreateAppleSettingsPage("页码与清理", string.Empty, out body4);
		Panel val8 = new Panel
		{
			Dock = (DockStyle)5,
			BackColor = AppleUiColors.Surface
		};
		((Control)val8).Controls.Add((Control)(object)val7);
		((Control)val8).Controls.Add((Control)(object)val6);
		((Control)val8).Controls.Add((Control)(object)val5);
		((Control)val8).Controls.Add((Control)(object)val4);
		val3.Controls.Add((Control)(object)val8, 1, 0);
		Panel val9 = BuildAppleMarginSection();
		((Control)body).Controls.Add((Control)(object)val9);
		Panel val10 = BuildAppleModuleSection(new Tuple<CheckBox, Button>[8]
		{
			Tuple.Create<CheckBox, Button>(chkEnableDocumentGrid, btnGridSettings),
			Tuple.Create<CheckBox, Button>(chkEnableSignatureFormatting, btnSignatureSettings),
			Tuple.Create<CheckBox, Button>(chkEnableAttachmentFormatting, btnAttachmentSettings),
			Tuple.Create<CheckBox, Button>(chkEnableTableFormatting, btnTableSettings),
			Tuple.Create<CheckBox, Button>(chkEnableImageFormatting, btnImageSettings),
			Tuple.Create<CheckBox, Button>(chkEnableCompilationFormatting, btnCompilationSettings),
			Tuple.Create<CheckBox, Button>(chkEnableFixSemicolons, null),
			Tuple.Create<CheckBox, Button>(chkEnableOrphanCharFix, null)
		});
		((Control)body).Controls.Add((Control)(object)val10);
		PrepareLegacySettingsGroup(gbFont, 820, 316);
		((Control)body2).Controls.Add((Control)(object)gbFont);
		PrepareLegacySettingsGroup(gbKeyword, 820, 82);
		((Control)body3).Controls.Add((Control)(object)gbKeyword);
		PrepareLegacySettingsGroup(gbPage, 820, 78);
		PrepareLegacySettingsGroup(gbClean, 820, 84);
		((Control)body4).Controls.Add((Control)(object)gbPage);
		((Control)body4).Controls.Add((Control)(object)gbClean);
		((Control)legacyMargin).Visible = false;
		AppleNavigationButton[] navButtons = new AppleNavigationButton[4]
		{
			CreateAppleNavButton("基础设置"),
			CreateAppleNavButton("字体样式"),
			CreateAppleNavButton("关键词"),
			CreateAppleNavButton("页码与清理")
		};
		Panel[] pages = (Panel[])(object)new Panel[4] { val4, val5, val6, val7 };
		FlowLayoutPanel val11 = new FlowLayoutPanel
		{
			Dock = (DockStyle)1,
			Height = 168,
			FlowDirection = (FlowDirection)1,
			WrapContents = false,
			BackColor = Color.Transparent,
			Margin = Padding.Empty,
			Padding = Padding.Empty
		};
		((Control)sidebar).Controls.Add((Control)(object)val11);
		for (int num = 0; num < navButtons.Length; num++)
		{
			int index = num;
			((Control)navButtons[num]).Width = 144;
			((Control)navButtons[num]).Margin = new Padding(0, 0, 0, 4);
			((Control)navButtons[num]).Click += delegate
			{
				ShowAppleSettingsPage(index, navButtons, pages);
			};
			((Control)val11).Controls.Add((Control)(object)navButtons[num]);
		}
		ShowAppleSettingsPage(0, navButtons, pages);
		Panel val12 = BuildAppleSettingsFooter(btnSave);
		val.Controls.Add((Control)(object)val12, 0, 2);
		ApplyAppleTheme((Control)(object)gbFont);
		ApplyAppleTheme((Control)(object)gbKeyword);
		ApplyAppleTheme((Control)(object)gbPage);
		ApplyAppleTheme((Control)(object)gbClean);
		AppleFormStyler.Apply((Form)(object)this, settingsToolTip);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private Panel BuildAppleSettingsHeader(Label lblCurrentTemplate)
	{
		Panel header = new Panel
		{
			Dock = (DockStyle)5,
			BackColor = AppleUiColors.Surface,
			Margin = Padding.Empty
		};
		((Control)header).Paint += (PaintEventHandler)delegate(object s, PaintEventArgs e)
		{
			Pen val = new Pen(AppleUiColors.Separator);
			try
			{
				e.Graphics.DrawLine(val, 0, ((Control)header).ClientSize.Height - 1, ((Control)header).ClientSize.Width, ((Control)header).ClientSize.Height - 1);
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		};
		RoundedPanel roundedPanel = new RoundedPanel();
		((Control)roundedPanel).Location = new Point(20, 14);
		((Control)roundedPanel).Size = new Size(36, 36);
		roundedPanel.Radius = 10;
		((Control)roundedPanel).BackColor = AppleUiColors.Accent;
		roundedPanel.BorderWidth = 0f;
		RoundedPanel roundedPanel2 = roundedPanel;
		((Control)roundedPanel2).Controls.Add((Control)new Label
		{
			Dock = (DockStyle)5,
			Text = "文",
			TextAlign = (ContentAlignment)32,
			BackColor = Color.Transparent,
			ForeColor = Color.White,
			Font = UiFonts.SubHeading
		});
		((Control)header).Controls.Add((Control)(object)roundedPanel2);
		((Control)header).Controls.Add((Control)new Label
		{
			Text = "一键排版自定义参数设置",
			Location = new Point(68, 6),
			Size = new Size(360, 25),
			Font = UiFonts.Title,
			ForeColor = AppleUiColors.TextPrimary,
			BackColor = Color.Transparent
		});
		((Control)header).Controls.Add((Control)new Label
		{
			Text = "快捷键 Shift + Alt + “+”（小键盘加号键）",
			Location = new Point(68, 36),
			Size = new Size(430, 20),
			Font = UiFonts.Caption,
			ForeColor = AppleUiColors.Accent,
			BackColor = Color.Transparent
		});
		((Control)lblCurrentTemplate).Location = new Point(640, 22);
		((Control)lblCurrentTemplate).Size = new Size(64, 22);
		((Control)lblCurrentTemplate).ForeColor = AppleUiColors.TextSecondary;
		((Control)lblCurrentTemplate).Font = UiFonts.Caption;
		lblCurrentTemplate.TextAlign = (ContentAlignment)64;
		((Control)cmbTemplate).Location = new Point(716, 18);
		((Control)cmbTemplate).Size = new Size(180, 28);
		((Control)cmbTemplate).Font = UiFonts.Body;
		((Control)cmbTemplate).BackColor = AppleUiColors.Surface;
		((Control)cmbTemplate).ForeColor = AppleUiColors.TextPrimary;
		((Control)header).Controls.Add((Control)(object)lblCurrentTemplate);
		((Control)header).Controls.Add((Control)(object)cmbTemplate);
		StyledButton styledButton = new StyledButton();
		((Control)styledButton).Text = "改名";
		((Control)styledButton).Location = new Point(972, 14);
		((Control)styledButton).Size = new Size(60, 34);
		btnRenameTemplate = (Button)(object)styledButton;
		((Control)btnRenameTemplate).Click += delegate
		{
			RenameCurrentFormatTemplate();
		};
		((Control)header).Controls.Add((Control)(object)btnRenameTemplate);
		StyledButton styledButton2 = new StyledButton();
		((Control)styledButton2).Text = "删除";
		((Control)styledButton2).Location = new Point(904, 14);
		((Control)styledButton2).Size = new Size(60, 34);
		((Control)styledButton2).ForeColor = UiColors.Danger;
		btnDeleteTemplate = (Button)(object)styledButton2;
		((Control)btnDeleteTemplate).Click += delegate
		{
			DeleteCurrentFormatTemplate();
		};
		((Control)header).Controls.Add((Control)(object)btnDeleteTemplate);
		StyledButton styledButton3 = new StyledButton();
		((Control)styledButton3).Text = "新增";
		((Control)styledButton3).Location = new Point(1040, 14);
		((Control)styledButton3).Size = new Size(60, 34);
		btnAddTemplate = (Button)(object)styledButton3;
		((Control)btnAddTemplate).Click += delegate
		{
			AddFormatTemplate();
		};
		((Control)header).Controls.Add((Control)(object)btnAddTemplate);
		UiTextApplier.ApplyTooltipText(settingsToolTip, (Control)(object)cmbTemplate, "选择排版模板。系统默认模板只读，自定义模板可以修改参数和名称。");
		UiTextApplier.ApplyTooltipText(settingsToolTip, (Control)(object)btnRenameTemplate, "修改当前自定义模板在列表和 Ribbon 下拉菜单中的显示名称。");
		UiTextApplier.ApplyTooltipText(settingsToolTip, (Control)(object)btnDeleteTemplate, "删除当前自定义模板。系统默认模板不能删除。");
		UiTextApplier.ApplyTooltipText(settingsToolTip, (Control)(object)btnAddTemplate, "复制当前模板已保存的参数，创建一个新的自定义模板。最多可保存 9 个自定义模板。");
		((Control)header).Resize += delegate
		{
			((Control)btnAddTemplate).Left = ((Control)header).ClientSize.Width - ((Control)btnAddTemplate).Width - 20;
			((Control)btnDeleteTemplate).Left = ((Control)btnAddTemplate).Left - ((Control)btnDeleteTemplate).Width - 8;
			((Control)btnRenameTemplate).Left = ((Control)btnDeleteTemplate).Left - ((Control)btnRenameTemplate).Width - 8;
			((Control)cmbTemplate).Left = ((Control)btnRenameTemplate).Left - ((Control)cmbTemplate).Width - 8;
			((Control)lblCurrentTemplate).Left = ((Control)cmbTemplate).Left - ((Control)lblCurrentTemplate).Width - 12;
		};
		return header;
	}

	private Panel BuildAppleSettingsFooter(Button btnSave)
	{
		Panel footer = new Panel
		{
			Dock = (DockStyle)5,
			BackColor = AppleUiColors.SurfaceMuted,
			Margin = Padding.Empty
		};
		((Control)footer).Paint += (PaintEventHandler)delegate(object s, PaintEventArgs e)
		{
			Pen val = new Pen(AppleUiColors.Separator);
			try
			{
				e.Graphics.DrawLine(val, 0, 0, ((Control)footer).ClientSize.Width, 0);
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		};
		((Control)lblTip).Location = new Point(648, 19);
		((Control)lblTip).Size = new Size(228, 22);
		lblTip.TextAlign = (ContentAlignment)64;
		((Control)lblTip).Font = UiFonts.Small;
		((Control)lblTip).ForeColor = AppleUiColors.TextSecondary;
		((Control)footer).Controls.Add((Control)(object)lblTip);
		StyleApplePrimaryButton(btnSave);
		((Control)btnSave).Location = new Point(890, 10);
		((Control)btnSave).Size = new Size(210, 40);
		((Control)footer).Controls.Add((Control)(object)btnSave);
		((Control)footer).Resize += delegate
		{
			((Control)btnSave).Left = ((Control)footer).ClientSize.Width - ((Control)btnSave).Width - 20;
			((Control)lblTip).Left = ((Control)btnSave).Left - ((Control)lblTip).Width - 14;
		};
		return footer;
	}

	private Panel CreateAppleSettingsPage(string title, string description, out FlowLayoutPanel body)
	{
		Panel val = new Panel
		{
			Dock = (DockStyle)5,
			BackColor = AppleUiColors.Surface,
			Visible = false
		};
		Panel val2 = new Panel
		{
			Dock = (DockStyle)1,
			Height = (string.IsNullOrWhiteSpace(description) ? 48 : 68),
			BackColor = AppleUiColors.Surface,
			Padding = new Padding(20, 10, 20, 6)
		};
		((Control)val2).Controls.Add((Control)new Label
		{
			Text = title,
			Location = new Point(20, 10),
			Size = new Size(420, 28),
			Font = UiFonts.Heading,
			ForeColor = AppleUiColors.TextPrimary
		});
		if (!string.IsNullOrWhiteSpace(description))
		{
			((Control)val2).Controls.Add((Control)new Label
			{
				Text = description,
				Location = new Point(20, 36),
				Size = new Size(720, 22),
				Font = UiFonts.Caption,
				ForeColor = AppleUiColors.TextSecondary
			});
		}
		body = new FlowLayoutPanel
		{
			Dock = (DockStyle)5,
			AutoScroll = true,
			FlowDirection = (FlowDirection)1,
			WrapContents = false,
			BackColor = AppleUiColors.Surface,
			Padding = new Padding(20, 2, 20, 16)
		};
		((Control)val).Controls.Add((Control)(object)body);
		((Control)val).Controls.Add((Control)(object)val2);
		FlowLayoutPanel bodyForResize = body;
		((Control)body).SizeChanged += delegate
		{
			ResizeApplePageItems(bodyForResize);
		};
		return val;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private Panel BuildAppleMarginSection()
	{
		Panel val = CreateAppleSection("页边距参数", 172);
		TableLayoutPanel val2 = new TableLayoutPanel
		{
			Location = new Point(0, 36),
			Size = new Size(820, 124),
			Anchor = (AnchorStyles)13,
			ColumnCount = 3,
			RowCount = 2,
			BackColor = Color.Transparent,
			Margin = Padding.Empty,
			Padding = Padding.Empty
		};
		for (int i = 0; i < 3; i++)
		{
			val2.ColumnStyles.Add(new ColumnStyle((SizeType)2, 33.333f));
		}
		val2.RowStyles.Add(new RowStyle((SizeType)2, 50f));
		val2.RowStyles.Add(new RowStyle((SizeType)2, 50f));
		((Control)val).Controls.Add((Control)(object)val2);
		NumericUpDown[] array = (NumericUpDown[])(object)new NumericUpDown[6] { nudTop, nudBottom, nudLeft, nudRight, nudHeader, nudFooter };
		string[] array2 = new string[6] { "上边距", "下边距", "左边距", "右边距", "页眉距", "页脚距" };
		for (int j = 0; j < array.Length; j++)
		{
			val2.Controls.Add((Control)(object)CreateAppleMetricField(array2[j], array[j]), j % 3, j / 3);
		}
		return val;
	}

	private void CompilationToggle_CheckedChanged(object sender, EventArgs e)
	{
		if (loadingValues || chkEnableCompilationFormatting == null || !chkEnableCompilationFormatting.Checked)
		{
			return;
		}
		CompilationEnablePromptForm compilationEnablePromptForm = new CompilationEnablePromptForm();
		try
		{
			if ((int)((Form)compilationEnablePromptForm).ShowDialog((IWin32Window)(object)this) != 1)
			{
				chkEnableCompilationFormatting.Checked = false;
			}
		}
		finally
		{
			((IDisposable)compilationEnablePromptForm)?.Dispose();
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private Panel BuildAppleModuleSection(Tuple<CheckBox, Button>[] modules)
	{
		Panel val = CreateAppleSection("排版模块", 242);
		TableLayoutPanel val2 = new TableLayoutPanel
		{
			Location = new Point(0, 36),
			Size = new Size(820, 196),
			Anchor = (AnchorStyles)13,
			ColumnCount = 2,
			RowCount = 4,
			BackColor = Color.Transparent,
			Margin = Padding.Empty,
			Padding = Padding.Empty
		};
		val2.ColumnStyles.Add(new ColumnStyle((SizeType)2, 50f));
		val2.ColumnStyles.Add(new ColumnStyle((SizeType)2, 50f));
		for (int i = 0; i < 4; i++)
		{
			val2.RowStyles.Add(new RowStyle((SizeType)1, 49f));
		}
		((Control)val).Controls.Add((Control)(object)val2);
		for (int j = 0; j < modules.Length; j++)
		{
			val2.Controls.Add((Control)(object)CreateAppleModuleRow(modules[j].Item1, modules[j].Item2), j % 2, j / 2);
		}
		return val;
	}

	private static Panel CreateAppleSection(string title, int height)
	{
		Panel val = new Panel
		{
			Size = new Size(820, height),
			BackColor = AppleUiColors.Surface,
			Margin = new Padding(0, 0, 0, 8)
		};
		((Control)val).Controls.Add((Control)new Label
		{
			Text = title,
			Location = new Point(0, 5),
			Size = new Size(260, 24),
			Font = UiFonts.SubHeading,
			ForeColor = AppleUiColors.TextPrimary
		});
		Panel val2 = new Panel
		{
			Dock = (DockStyle)2,
			Height = 1,
			BackColor = AppleUiColors.Separator
		};
		((Control)val).Controls.Add((Control)(object)val2);
		return val;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static Panel CreateAppleMetricField(string text, NumericUpDown value)
	{
		Panel val = new Panel
		{
			Dock = (DockStyle)5,
			BackColor = AppleUiColors.SurfaceMuted,
			Margin = new Padding(0, 2, 8, 4),
			Padding = new Padding(10, 6, 8, 6)
		};
		((Control)val).Controls.Add((Control)new Label
		{
			Text = text,
			Location = new Point(10, 11),
			Size = new Size(66, 22),
			Font = UiFonts.Caption,
			ForeColor = AppleUiColors.TextSecondary,
			BackColor = Color.Transparent
		});
		((Control)value).Location = new Point(80, 7);
		((Control)value).Size = new Size(112, 24);
		((Control)value).Font = UiFonts.Body;
		((Control)value).BackColor = AppleUiColors.Surface;
		((Control)value).ForeColor = AppleUiColors.TextPrimary;
		((Control)val).Controls.Add((Control)(object)value);
		((Control)val).Controls.Add((Control)new Label
		{
			Text = "厘米",
			Location = new Point(200, 11),
			Size = new Size(40, 22),
			Font = UiFonts.Small,
			ForeColor = AppleUiColors.TextTertiary,
			BackColor = Color.Transparent
		});
		return val;
	}

	private static Panel CreateAppleModuleRow(CheckBox toggle, Button action)
	{
		RoundedPanel roundedPanel = new RoundedPanel();
		((Control)roundedPanel).Dock = (DockStyle)5;
		((Control)roundedPanel).Size = new Size(400, 44);
		((Control)roundedPanel).BackColor = AppleUiColors.SurfaceMuted;
		roundedPanel.BorderColor = AppleUiColors.Separator;
		roundedPanel.BorderWidth = 1f;
		roundedPanel.Radius = 8;
		((Control)roundedPanel).Margin = new Padding(0, 2, 8, 2);
		((Control)roundedPanel).Padding = new Padding(10, 6, 10, 6);
		RoundedPanel row = roundedPanel;
		((Control)toggle).Location = new Point(10, 8);
		((Control)toggle).Size = new Size((action == null) ? 360 : 268, 28);
		((Control)toggle).Anchor = (AnchorStyles)5;
		((Control)toggle).Font = UiFonts.Body;
		((Control)toggle).ForeColor = AppleUiColors.TextPrimary;
		((Control)row).Controls.Add((Control)(object)toggle);
		if (action != null)
		{
			StyleAppleSecondaryButton(action);
			((Control)action).Location = new Point(298, 7);
			((Control)action).Size = new Size(88, 30);
			((Control)action).Anchor = (AnchorStyles)1;
			((Control)row).Controls.Add((Control)(object)action);
		}
		((Control)row).Resize += delegate
		{
			if (action == null)
			{
				((Control)toggle).Width = Math.Max(120, ((Control)row).ClientSize.Width - 24);
			}
			else
			{
				((Control)action).Left = ((Control)row).ClientSize.Width - ((Control)action).Width - 10;
				((Control)toggle).Width = Math.Max(120, ((Control)action).Left - ((Control)toggle).Left - 10);
			}
		};
		return (Panel)(object)row;
	}

	private static void PrepareLegacySettingsGroup(GroupBox group, int width, int height)
	{
		((Control)group).Location = Point.Empty;
		((Control)group).Size = new Size(width, height);
		((Control)group).Margin = new Padding(0, 2, 0, 8);
		((Control)group).BackColor = AppleUiColors.Surface;
		((Control)group).ForeColor = AppleUiColors.TextPrimary;
		((Control)group).Font = UiFonts.SubHeading;
	}

	private static AppleNavigationButton CreateAppleNavButton(string text)
	{
		AppleNavigationButton appleNavigationButton = new AppleNavigationButton();
		((Control)appleNavigationButton).Text = text;
		((Control)appleNavigationButton).Size = new Size(144, 36);
		return appleNavigationButton;
	}

	private static void ShowAppleSettingsPage(int index, AppleNavigationButton[] navButtons, Panel[] pages)
	{
		for (int i = 0; i < pages.Length; i++)
		{
			bool flag = i == index;
			navButtons[i].Selected = flag;
			((Control)pages[i]).Visible = flag;
			if (flag)
			{
				((Control)pages[i]).BringToFront();
			}
		}
	}

	private static void ResizeApplePageItems(FlowLayoutPanel body)
	{
		int width = ((Control)body).ClientSize.Width;
		Padding padding = ((Control)body).Padding;
		int width2 = Math.Max(820, width - padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 2);
		foreach (Control item in (ArrangedElementCollection)((Control)body).Controls)
		{
			item.Width = width2;
		}
	}

	private static void StyleApplePrimaryButton(Button button)
	{
		((ButtonBase)button).FlatStyle = (FlatStyle)0;
		((ButtonBase)button).FlatAppearance.BorderSize = 0;
		((Control)button).BackColor = AppleUiColors.Accent;
		((Control)button).ForeColor = Color.White;
		((Control)button).Font = UiFonts.BodyBold;
		((Control)button).Cursor = Cursors.Hand;
		if (button is StyledButton styledButton)
		{
			styledButton.Radius = 8;
			styledButton.HoverBackColor = AppleUiColors.AccentHover;
			styledButton.PressBackColor = AppleUiColors.AccentPressed;
		}
	}

	private static void StyleAppleSecondaryButton(Button button)
	{
		((ButtonBase)button).FlatStyle = (FlatStyle)0;
		((ButtonBase)button).FlatAppearance.BorderSize = 0;
		((Control)button).BackColor = AppleUiColors.SurfaceHover;
		((Control)button).ForeColor = AppleUiColors.Accent;
		((Control)button).Font = UiFonts.Body;
		((Control)button).Cursor = Cursors.Hand;
		if (button is StyledButton styledButton)
		{
			styledButton.Radius = 8;
			styledButton.HoverBackColor = AppleUiColors.AccentSoft;
			styledButton.PressBackColor = AppleUiColors.Separator;
		}
	}

	private static void ApplyAppleTheme(Control root)
	{
		foreach (Control item in (ArrangedElementCollection)root.Controls)
		{
			Control val = item;
			Label val2 = (Label)(object)((val is Label) ? val : null);
			if (val2 == null)
			{
				if (val is ComboBox || val is NumericUpDown || val is TextBox)
				{
					val.BackColor = AppleUiColors.Surface;
					val.ForeColor = AppleUiColors.TextPrimary;
					val.Font = UiFonts.Caption;
				}
				else if (val is Panel)
				{
					val.BackColor = ((val.BackColor == UiColors.BgMuted) ? AppleUiColors.SurfaceMuted : AppleUiColors.Surface);
				}
			}
			else
			{
				((Control)val2).ForeColor = (((Control)val2).Font.Bold ? AppleUiColors.TextPrimary : AppleUiColors.TextSecondary);
				((Control)val2).BackColor = Color.Transparent;
			}
			ApplyAppleTheme(val);
		}
	}

	private void FillComboBoxItems()
	{
		for (int i = 0; i < 6; i++)
		{
			PopulateInitialFontItems(cmbFonts[i]);
			ConfigureLazyFontPageCombo(cmbSizes[i], FontSizeNames);
			ConfigureLazyFontPageCombo(cmbBold[i], BoldItems);
			if (cmbRecognitionStyles[i] != null)
			{
				ConfigureLazyFontPageCombo(cmbRecognitionStyles[i], (i == 0) ? MainTitleRecognitionModeItems : RecognitionStyleItems);
			}
			if (cmbAlignments[i] != null)
			{
				ConfigureLazyFontPageCombo(cmbAlignments[i], ParagraphAlignItems);
			}
			if (cmbOutlineLevels[i] != null)
			{
				ConfigureLazyFontPageCombo(cmbOutlineLevels[i], OutlineLevelItems);
			}
		}
		PopulateInitialFontItems(cmbFonts[6]);
		for (int j = 0; j < 5; j++)
		{
			ConfigureLazyFontPageCombo(cmbSpaceBefore[j], ParagraphSpaceItems);
			ConfigureLazyFontPageCombo(cmbSpaceAfter[j], ParagraphSpaceItems);
		}
		cmbYiShiMode.Items.Clear();
		ComboBox.ObjectCollection items = cmbYiShiMode.Items;
		object[] keywordModes = KeywordModes;
		items.AddRange(keywordModes);
		cmbYiYaoMode.Items.Clear();
		ComboBox.ObjectCollection items2 = cmbYiYaoMode.Items;
		keywordModes = KeywordModes;
		items2.AddRange(keywordModes);
		cmbDiYiMode.Items.Clear();
		ComboBox.ObjectCollection items3 = cmbDiYiMode.Items;
		keywordModes = KeywordModes;
		items3.AddRange(keywordModes);
		cmbPageAlign.Items.Clear();
		ComboBox.ObjectCollection items4 = cmbPageAlign.Items;
		keywordModes = PageAlignItems;
		items4.AddRange(keywordModes);
		cmbEnablePageNumbers.Items.Clear();
		ComboBox.ObjectCollection items5 = cmbEnablePageNumbers.Items;
		keywordModes = PageNumberModeItems;
		items5.AddRange(keywordModes);
		cmbLeftWing.Items.Clear();
		ComboBox.ObjectCollection items6 = cmbLeftWing.Items;
		keywordModes = LeftWingItems;
		items6.AddRange(keywordModes);
		cmbRightWing.Items.Clear();
		ComboBox.ObjectCollection items7 = cmbRightWing.Items;
		keywordModes = RightWingItems;
		items7.AddRange(keywordModes);
		cmbDeleteAi.Items.Clear();
		ComboBox.ObjectCollection items8 = cmbDeleteAi.Items;
		keywordModes = YesNoItems;
		items8.AddRange(keywordModes);
		cmbDeleteSpace.Items.Clear();
		ComboBox.ObjectCollection items9 = cmbDeleteSpace.Items;
		keywordModes = YesNoItems;
		items9.AddRange(keywordModes);
		cmbClearHeadersFooters.Items.Clear();
		ComboBox.ObjectCollection items10 = cmbClearHeadersFooters.Items;
		keywordModes = YesNoItems;
		items10.AddRange(keywordModes);
		cmbRemoveHyperlinks.Items.Clear();
		ComboBox.ObjectCollection items11 = cmbRemoveHyperlinks.Items;
		keywordModes = YesNoItems;
		items11.AddRange(keywordModes);
		for (int k = 0; k < 5; k++)
		{
			ConfigureLazyFontPageCombo(cmbLineSpacing[k], LineSpacingItems);
			ConfigureLazyFontPageCombo(cmbFirstLineIndent[k], FirstLineIndentItems);
		}
	}

	private void PopulateInitialFontItems(ComboBox comboBox)
	{
		if (comboBox == null)
		{
			return;
		}
		string text = ((Control)comboBox).Text;
		fontCombosWithSystemFonts.Remove(comboBox);
		comboBox.BeginUpdate();
		try
		{
			comboBox.Items.Clear();
			EnsureFontComboValue(comboBox, text);
		}
		finally
		{
			comboBox.EndUpdate();
		}
	}

	private void ConfigureLazyFontPageCombo(ComboBox comboBox, string[] options)
	{
		if (comboBox == null)
		{
			return;
		}
		string text = ((Control)comboBox).Text;
		lazyFontPageComboItems[comboBox] = options ?? new string[0];
		populatedLazyFontPageCombos.Remove(comboBox);
		comboBox.DropDown -= LazyFontPageCombo_DropDown;
		comboBox.DropDown += LazyFontPageCombo_DropDown;
		comboBox.BeginUpdate();
		try
		{
			comboBox.Items.Clear();
			EnsureComboValue(comboBox, text);
		}
		finally
		{
			comboBox.EndUpdate();
		}
	}

	private void LazyFontPageCombo_DropDown(object sender, EventArgs e)
	{
		ComboBox val = (ComboBox)((sender is ComboBox) ? sender : null);
		if (val == null || populatedLazyFontPageCombos.Contains(val) || !lazyFontPageComboItems.TryGetValue(val, out var value))
		{
			return;
		}
		string text = ((Control)val).Text;
		val.BeginUpdate();
		try
		{
			val.Items.Clear();
			ComboBox.ObjectCollection items = val.Items;
			object[] array = value;
			items.AddRange(array);
			EnsureComboValue(val, text);
			populatedLazyFontPageCombos.Add(val);
		}
		finally
		{
			val.EndUpdate();
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void FontCombo_DropDown(object sender, EventArgs e)
	{
		ComboBox val = (ComboBox)((sender is ComboBox) ? sender : null);
		if (val == null || fontCombosWithSystemFonts.Contains(val))
		{
			return;
		}
		string text = ((Control)val).Text;
		string[] systemFontNames = GetSystemFontNames();
		val.BeginUpdate();
		try
		{
			val.Items.Clear();
			ComboBox.ObjectCollection items = val.Items;
			object[] fontNames = FontNames;
			items.AddRange(fontNames);
			if (!ContainsFontName(FontNames, text) && !ContainsFontName(systemFontNames, text) && !string.IsNullOrWhiteSpace(text) && !string.Equals(text, "──────────────", StringComparison.Ordinal))
			{
				val.Items.Add((object)text);
			}
			val.Items.Add((object)"──────────────");
			ComboBox.ObjectCollection items2 = val.Items;
			fontNames = systemFontNames;
			items2.AddRange(fontNames);
			EnsureFontComboValue(val, text);
			fontCombosWithSystemFonts.Add(val);
		}
		finally
		{
			val.EndUpdate();
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void EnsureFontComboValue(ComboBox comboBox, string fontName)
	{
		if (comboBox != null && !string.IsNullOrWhiteSpace(fontName) && !string.Equals(fontName, "──────────────", StringComparison.Ordinal))
		{
			EnsureComboValue(comboBox, fontName);
		}
	}

	private static void EnsureComboValue(ComboBox comboBox, string value)
	{
		if (comboBox != null && !string.IsNullOrWhiteSpace(value))
		{
			int num = FindComboItemIndex(comboBox, value);
			if (num < 0)
			{
				comboBox.Items.Add((object)value);
				num = comboBox.Items.Count - 1;
			}
			((ListControl)comboBox).SelectedIndex = num;
		}
	}

	private static int FindComboItemIndex(ComboBox comboBox, string value)
	{
		for (int i = 0; i < comboBox.Items.Count; i++)
		{
			if (string.Equals(Convert.ToString(comboBox.Items[i]), value, StringComparison.OrdinalIgnoreCase))
			{
				return i;
			}
		}
		return -1;
	}

	private static bool ContainsFontName(string[] values, string fontName)
	{
		if (values == null || string.IsNullOrWhiteSpace(fontName))
		{
			return false;
		}
		for (int i = 0; i < values.Length; i++)
		{
			if (string.Equals(values[i], fontName, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private NumericUpDown CreateNumericUpDown(int x, int y)
	{
		return new NumericUpDown
		{
			Location = new Point(x, y),
			Size = new Size(78, 21),
			DecimalPlaces = 2,
			Increment = 0.1m,
			Minimum = 0m,
			Maximum = 50m,
			Font = fLabelSmall
		};
	}

	private ComboBox CreateComboBox(Point location, int width, bool editable = false)
	{
		return new ComboBox
		{
			Location = location,
			Size = new Size(width, 21),
			DropDownStyle = (ComboBoxStyle)(editable ? 1 : 2),
			Font = fLabelSmall
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string GetDefaultRecognitionStyle(int styleIndex, string value)
	{
		if (styleIndex == 0)
		{
			string mainTitleMode = string.IsNullOrWhiteSpace(value) ? "规范标题" : value.Trim();
			return Array.IndexOf(MainTitleRecognitionModeItems, mainTitleMode) >= 0 ? mainTitleMode : "规范标题";
		}

		string defaultStyle;
		switch (styleIndex)
		{
		case 1:
			defaultStyle = "一、XX";
			break;
		case 2:
			defaultStyle = "（一）XX";
			break;
		case 3:
			defaultStyle = "1.XX";
			break;
		default:
			defaultStyle = "";
			break;
		}

		if (string.IsNullOrWhiteSpace(value) || Array.IndexOf(RecognitionStyleItems, value) < 0)
		{
			return defaultStyle;
		}
		return value;
	}

	private static string GetMainTitleRecognitionTip(string mode)
	{
		return UiTextRegistry.GetMainTitleRecognitionTip(mode);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void ApplyKeywordModeTooltip(ComboBox comboBox)
	{
		if (comboBox != null)
		{
			UiTextApplier.ApplyTooltip(key: (((Control)comboBox).Text ?? "").Trim() switch
			{
				"整句加粗" => "Settings.KeywordBold.Sentence", 
				"短语加粗" => "Settings.KeywordBold.Phrase", 
				"不加粗" => "Settings.KeywordBold.None", 
				_ => "Settings.KeywordBold", 
			}, toolTip: settingsToolTip, control: (Control)(object)comboBox);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void LoadValues()
	{
		Stopwatch.StartNew();
		Stopwatch.StartNew().Restart();
		nudTop.Value = (decimal)config.TopMargin;
		nudBottom.Value = (decimal)config.BottomMargin;
		nudLeft.Value = (decimal)config.LeftMargin;
		nudRight.Value = (decimal)config.RightMargin;
		nudHeader.Value = (decimal)config.HeaderDistance;
		nudFooter.Value = (decimal)config.FooterDistance;
		chkEnableDocumentGrid.Checked = config.EnableDocumentGrid;
		chkEnableFixSemicolons.Checked = config.EnableFixSemicolons;
		chkEnableSignatureFormatting.Checked = config.EnableSignatureFormatting;
		chkEnableOrphanCharFix.Checked = config.EnableOrphanCharFix;
		chkEnableAttachmentFormatting.Checked = config.EnableAttachmentFormatting;
		chkEnableTableFormatting.Checked = config.EnableTableFormatting;
		chkEnableImageFormatting.Checked = config.EnableImageFormatting;
		loadingValues = true;
		try
		{
			chkEnableCompilationFormatting.Checked = config.EnableCompilationFormatting;
		}
		finally
		{
			loadingValues = false;
		}
		TextStyle[] array = new TextStyle[5] { config.MainTitle, config.Level1, config.Level2, config.Level3, config.Body };
		for (int i = 0; i < 5; i++)
		{
			EnsureFontComboValue(cmbFonts[i], array[i].FontName);
			if (cmbRecognitionStyles[i] != null)
			{
				EnsureComboValue(cmbRecognitionStyles[i], GetDefaultRecognitionStyle(i, array[i].RecognitionStyle));
			}
			if (cmbAlignments[i] != null)
			{
				EnsureComboValue(cmbAlignments[i], array[i].Alignment ?? ((i == 0) ? "居中" : "两端对齐"));
			}
			if (cmbOutlineLevels[i] != null)
			{
				EnsureComboValue(cmbOutlineLevels[i], OutlineLevels.Normalize(array[i].OutlineLevel));
			}
			EnsureComboValue(cmbSpaceBefore[i], array[i].SpaceBefore.ToString());
			EnsureComboValue(cmbSpaceAfter[i], array[i].SpaceAfter.ToString());
			EnsureComboValue(cmbSizes[i], array[i].FontSize);
			EnsureComboValue(cmbBold[i], array[i].Bold ? "加粗" : "不加粗");
			EnsureComboValue(cmbLineSpacing[i], array[i].LineSpacing);
			EnsureComboValue(cmbFirstLineIndent[i], array[i].FirstLineIndent ?? "2");
		}
		EnsureFontComboValue(cmbFonts[5], config.PageNumberFontName);
		EnsureComboValue(cmbSizes[5], config.PageFontSize);
		EnsureComboValue(cmbBold[5], config.PageNumberBold ? "加粗" : "不加粗");
		((ListControl)cmbYiShiMode).SelectedIndex = config.YiShiMode;
		((ListControl)cmbYiYaoMode).SelectedIndex = config.YiYaoMode;
		((ListControl)cmbDiYiMode).SelectedIndex = config.DiYiMode;
		switch (config.PageAlign)
		{
		case PageAlignType.Center:
			((ListControl)cmbPageAlign).SelectedIndex = 2;
			break;
		case PageAlignType.Right:
			((ListControl)cmbPageAlign).SelectedIndex = 3;
			break;
		case PageAlignType.Left:
			((ListControl)cmbPageAlign).SelectedIndex = 1;
			break;
		case PageAlignType.OddEvenDifferent:
			((ListControl)cmbPageAlign).SelectedIndex = 0;
			break;
		}
		((Control)cmbLeftWing).Text = config.PageLeftWing;
		((Control)cmbRightWing).Text = config.PageRightWing;
		string value = PageNumberModes.Normalize(config.PageNumberMode, config.EnablePageNumbers);
		((ListControl)cmbEnablePageNumbers).SelectedIndex = Array.IndexOf(PageNumberModeItems, value);
		if (((ListControl)cmbEnablePageNumbers).SelectedIndex < 0)
		{
			((ListControl)cmbEnablePageNumbers).SelectedIndex = 0;
		}
		EnsureFontComboValue(cmbFonts[6], config.EnglishNumberFontName);
		((ListControl)cmbDeleteAi).SelectedIndex = ((!config.DeleteAiSymbols) ? 1 : 0);
		((ListControl)cmbDeleteSpace).SelectedIndex = ((!config.DeleteSpaces) ? 1 : 0);
		((ListControl)cmbClearHeadersFooters).SelectedIndex = ((!config.ClearHeadersFooters) ? 1 : 0);
		((ListControl)cmbRemoveHyperlinks).SelectedIndex = ((!config.RemoveHyperlinks) ? 1 : 0);
		RefreshTemplateSelector();
		UpdateTipText(ConfigManager.CurrentTemplateIndex);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private bool SaveValues()
	{
		config.TopMargin = (float)nudTop.Value;
		config.BottomMargin = (float)nudBottom.Value;
		config.LeftMargin = (float)nudLeft.Value;
		config.RightMargin = (float)nudRight.Value;
		config.HeaderDistance = (float)nudHeader.Value;
		config.FooterDistance = (float)nudFooter.Value;
		config.EnableDocumentGrid = chkEnableDocumentGrid.Checked;
		config.EnableFixSemicolons = chkEnableFixSemicolons.Checked;
		config.EnableSignatureFormatting = chkEnableSignatureFormatting.Checked;
		if (config.SignatureOptions != null)
		{
			config.EnableSignatureWithSeal = config.SignatureOptions.WithSeal;
		}
		config.EnableOrphanCharFix = chkEnableOrphanCharFix.Checked;
		config.EnableAttachmentFormatting = chkEnableAttachmentFormatting.Checked;
		config.EnableTableFormatting = chkEnableTableFormatting.Checked;
		config.EnableImageFormatting = chkEnableImageFormatting.Checked;
		config.EnableCompilationFormatting = chkEnableCompilationFormatting.Checked;
		TextStyle[] array = new TextStyle[5] { config.MainTitle, config.Level1, config.Level2, config.Level3, config.Body };
		for (int i = 0; i < 5; i++)
		{
			string text = ((Control)cmbFonts[i]).Text;
			array[i].FontName = ((text == "──────────────") ? array[i].FontName : text);
			array[i].SpaceBefore = (int.TryParse(((Control)cmbSpaceBefore[i]).Text, out var result) ? result : 0);
			array[i].SpaceAfter = (int.TryParse(((Control)cmbSpaceAfter[i]).Text, out var result2) ? result2 : 0);
			array[i].FontSize = ((Control)cmbSizes[i]).Text;
			array[i].Bold = string.Equals(((Control)cmbBold[i]).Text, "加粗", StringComparison.Ordinal);
			array[i].LineSpacing = ((Control)cmbLineSpacing[i]).Text;
			array[i].FirstLineIndent = ((Control)cmbFirstLineIndent[i]).Text;
			array[i].RecognitionStyle = ((cmbRecognitionStyles[i] != null) ? GetDefaultRecognitionStyle(i, ((Control)cmbRecognitionStyles[i]).Text) : "");
			array[i].Alignment = ((cmbAlignments[i] != null && !string.IsNullOrWhiteSpace(((Control)cmbAlignments[i]).Text)) ? ((Control)cmbAlignments[i]).Text : ((i == 0) ? "居中" : "两端对齐"));
			array[i].OutlineLevel = ((cmbOutlineLevels[i] == null || string.IsNullOrWhiteSpace(((Control)cmbOutlineLevels[i]).Text)) ? "正文文本" : OutlineLevels.Normalize(((Control)cmbOutlineLevels[i]).Text));
		}
		string text2 = ((Control)cmbFonts[5]).Text;
		config.PageNumberFontName = ((text2 == "──────────────") ? config.PageNumberFontName : text2);
		config.PageFontSize = ((Control)cmbSizes[5]).Text;
		config.PageNumberBold = string.Equals(((Control)cmbBold[5]).Text, "加粗", StringComparison.Ordinal);
		config.YiShiMode = ((ListControl)cmbYiShiMode).SelectedIndex;
		config.YiYaoMode = ((ListControl)cmbYiYaoMode).SelectedIndex;
		config.DiYiMode = ((ListControl)cmbDiYiMode).SelectedIndex;
		if (((ListControl)cmbPageAlign).SelectedIndex == 0)
		{
			config.PageAlign = PageAlignType.OddEvenDifferent;
		}
		else if (((ListControl)cmbPageAlign).SelectedIndex == 1)
		{
			config.PageAlign = PageAlignType.Left;
		}
		else if (((ListControl)cmbPageAlign).SelectedIndex == 2)
		{
			config.PageAlign = PageAlignType.Center;
		}
		else if (((ListControl)cmbPageAlign).SelectedIndex == 3)
		{
			config.PageAlign = PageAlignType.Right;
		}
		config.PageLeftWing = ((Control)cmbLeftWing).Text;
		config.PageRightWing = ((Control)cmbRightWing).Text;
		config.PageNumberMode = ((((ListControl)cmbEnablePageNumbers).SelectedIndex >= 0) ? PageNumberModeItems[((ListControl)cmbEnablePageNumbers).SelectedIndex] : "开启");
		config.EnablePageNumbers = PageNumberModes.IsEnabled(config.PageNumberMode, config.EnablePageNumbers);
		string text3 = ((Control)cmbFonts[6]).Text;
		config.EnglishNumberFontName = ((text3 == "──────────────") ? config.EnglishNumberFontName : text3);
		config.DeleteAiSymbols = ((ListControl)cmbDeleteAi).SelectedIndex == 0;
		config.DeleteSpaces = ((ListControl)cmbDeleteSpace).SelectedIndex == 0;
		config.ClearHeadersFooters = ((ListControl)cmbClearHeadersFooters).SelectedIndex == 0;
		config.RemoveHyperlinks = ((ListControl)cmbRemoveHyperlinks).SelectedIndex == 0;
		int num = 0;
		while (true)
		{
			if (num >= 5)
			{
				if (ConfigManager.CurrentTemplateIndex == 0)
				{
					AppleMessageDialog.Show((IWin32Window)(object)this, "系统默认模板不可修改，请切换到其他模板后保存。", "提示", (MessageBoxButtons)0, (MessageBoxIcon)64);
					return false;
				}
				ConfigManager.SaveCurrentTemplate(config);
				TimedMessageForm.ShowMessage("模板设置已保存，下次排版时生效。", "保存成功", 2200);
				return true;
			}
			if (string.IsNullOrWhiteSpace(((Control)cmbLineSpacing[num]).Text))
			{
				AppleMessageDialog.Show((IWin32Window)(object)this, styleLabels[num] + " 的行距不能为空。", "输入错误", (MessageBoxButtons)0, (MessageBoxIcon)48);
				return false;
			}
			if (!(((Control)cmbLineSpacing[num]).Text != "单倍行距") || float.TryParse(((Control)cmbLineSpacing[num]).Text, out var _))
			{
				if (string.IsNullOrWhiteSpace(((Control)cmbFirstLineIndent[num]).Text) || !float.TryParse(((Control)cmbFirstLineIndent[num]).Text, out var _))
				{
					AppleMessageDialog.Show((IWin32Window)(object)this, styleLabels[num] + " 的首行缩进必须是数字。", "输入错误", (MessageBoxButtons)0, (MessageBoxIcon)48);
					return false;
				}
				num++;
				continue;
			}
			break;
		}
		AppleMessageDialog.Show((IWin32Window)(object)this, styleLabels[num] + " 的行距必须是数字或\"单倍行距\"。", "输入错误", (MessageBoxButtons)0, (MessageBoxIcon)48);
		return false;
	}

	private void RefreshTemplateSelector()
	{
		string[] templateNames = ConfigManager.TemplateNames;
		bool flag = cmbTemplate.Items.Count != templateNames.Length;
		if (!flag)
		{
			for (int i = 0; i < templateNames.Length; i++)
			{
				if (!string.Equals(Convert.ToString(cmbTemplate.Items[i]), templateNames[i], StringComparison.Ordinal))
				{
					flag = true;
					break;
				}
			}
		}
		if (flag)
		{
			cmbTemplate.BeginUpdate();
			cmbTemplate.Items.Clear();
			ComboBox.ObjectCollection items = cmbTemplate.Items;
			object[] array = templateNames;
			items.AddRange(array);
			cmbTemplate.EndUpdate();
		}
		int num = Math.Max(0, Math.Min(ConfigManager.CurrentTemplateIndex, cmbTemplate.Items.Count - 1));
		if (((ListControl)cmbTemplate).SelectedIndex != num)
		{
			((ListControl)cmbTemplate).SelectedIndex = num;
		}
		if (btnRenameTemplate != null)
		{
			((Control)btnRenameTemplate).Enabled = num > 0;
		}
		if (btnDeleteTemplate != null)
		{
			((Control)btnDeleteTemplate).Enabled = num > 0;
		}
		if (btnAddTemplate != null)
		{
			((Control)btnAddTemplate).Enabled = templateNames.Length - 1 < 9;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void AddFormatTemplate()
	{
		if (!AppleTextInputDialog.TryShow((IWin32Window)(object)this, "新增排版模板", "新模板会复制当前模板已经保存的参数。", "新模板", out var value))
		{
			return;
		}
		try
		{
			ConfigManager.AddUserTemplate(value, ConfigManager.GetCurrentCopy());
			config = ConfigManager.GetCurrentCopy();
			RefreshTemplateSelector();
			LoadValues();
		}
		catch (Exception ex)
		{
			AppleMessageDialog.Show((IWin32Window)(object)this, ex.Message, "无法新增模板", (MessageBoxButtons)0, (MessageBoxIcon)64);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void RenameCurrentFormatTemplate()
	{
		int currentTemplateIndex = ConfigManager.CurrentTemplateIndex;
		if (currentTemplateIndex <= 0)
		{
			AppleMessageDialog.Show((IWin32Window)(object)this, "系统默认模板不可改名，请先选择自定义模板。", "模板改名", (MessageBoxButtons)0, (MessageBoxIcon)64);
			return;
		}
		string[] templateNames = ConfigManager.TemplateNames;
		if (!AppleTextInputDialog.TryShow((IWin32Window)(object)this, "模板改名", "只修改显示名称，不改变模板参数。", templateNames[currentTemplateIndex], out var value))
		{
			return;
		}
		try
		{
			ConfigManager.RenameUserTemplate(currentTemplateIndex, value);
			RefreshTemplateSelector();
		}
		catch (Exception ex)
		{
			AppleMessageDialog.Show((IWin32Window)(object)this, ex.Message, "无法修改名称", (MessageBoxButtons)0, (MessageBoxIcon)64);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void DeleteCurrentFormatTemplate()
	{
		int currentTemplateIndex = ConfigManager.CurrentTemplateIndex;
		if (currentTemplateIndex > 0)
		{
			string[] templateNames = ConfigManager.TemplateNames;
			string text = ((currentTemplateIndex < templateNames.Length) ? templateNames[currentTemplateIndex] : "当前模板");
			if ((int)AppleMessageDialog.Show((IWin32Window)(object)this, "确定删除模板“" + text + "”吗？删除后不可恢复。", "删除模板", (MessageBoxButtons)1, (MessageBoxIcon)32) == 1)
			{
				try
				{
					ConfigManager.DeleteUserTemplate(currentTemplateIndex);
					config = ConfigManager.GetCurrentCopy();
					RefreshTemplateSelector();
					LoadValues();
					UpdateTipText(ConfigManager.CurrentTemplateIndex);
				}
				catch (Exception ex)
				{
					LogService.Error("Delete format template failed.", ex);
					AppleMessageDialog.Show((IWin32Window)(object)this, "模板暂时无法删除，请稍后重试。", "删除模板", (MessageBoxButtons)0, (MessageBoxIcon)48);
				}
			}
		}
		else
		{
			AppleMessageDialog.Show((IWin32Window)(object)this, "系统默认模板不可删除，请先选择自定义模板。", "删除模板", (MessageBoxButtons)0, (MessageBoxIcon)64);
		}
	}

	private void CmbTemplate_SelectedIndexChanged(object sender, EventArgs e)
	{
		int selectedIndex = ((ListControl)cmbTemplate).SelectedIndex;
		if (selectedIndex >= 0 && selectedIndex != ConfigManager.CurrentTemplateIndex)
		{
			ConfigManager.SwitchTemplate(selectedIndex);
			config = ConfigManager.GetCurrentCopy();
			LoadValues();
			UpdateTipText(ConfigManager.CurrentTemplateIndex);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void UpdateTipText(int templateIndex)
	{
		if (lblTip != null)
		{
			if (templateIndex == 0)
			{
				((Control)lblTip).Text = "默认模板不能调整参数，请选择其他模板";
			}
			else
			{
				((Control)lblTip).Text = "点击右方按钮，保存当前参数到所选模板";
			}
			((Control)lblTip).Visible = true;
		}
	}

	private void BtnSave_Click(object sender, EventArgs e)
	{
		if (SaveValues())
		{
			((Form)this).Close();
		}
	}

}
