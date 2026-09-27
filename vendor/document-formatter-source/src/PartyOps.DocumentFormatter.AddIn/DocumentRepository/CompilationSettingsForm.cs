using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using DocumentRepository.Models.CompilationFormatting;
using DocumentRepository.Services.UiText;

namespace DocumentRepository;

public class CompilationSettingsForm : Form
{
	private readonly CompilationFormatOptions options;

	private readonly ToolTip toolTip = UiTextApplier.CreateToolTip();

	private CheckBox chkStartEachArticleOnNewPage;

	private RadioButton radPreserveFrontMatter;

	private RadioButton radRejectFrontMatter;

	private CheckBox chkGenerateToc;

	private RadioButton radTocDocumentStart;

	private RadioButton radTocBeforeFirstArticle;

	private TextBox txtTocTitle;

	private ComboBox cboTocTitleFont;

	private NumericUpDown numTocTitleSize;

	private CheckBox chkTocTitleBold;

	private ComboBox cboTocTitleAlign;

	private NumericUpDown numTocTitleSpaceBefore;

	private NumericUpDown numTocTitleSpaceAfter;

	private ComboBox cboTocEntryFontFarEast;

	private ComboBox cboTocEntryFontAscii;

	private NumericUpDown numTocEntrySize;

	private NumericUpDown numTocEntryLineSpacing;

	private ComboBox cboTocLeader;

	private CheckBox chkTocHyperlink;

	private CheckBox chkTocPageBreak;

	private CheckBox chkTocCountInNumbering;

	private ComboBox cboExistingToc;

	private RadioButton radPageNoChange;

	private RadioButton radPageRemove;

	private RadioButton radPageContinuous;

	private RadioButton radPageRestartPerArticle;

	private static readonly string[] FarEastFonts = new string[8] { "仿宋_GB2312", "仿宋", "黑体", "宋体", "微软雅黑", "方正小标宋简体", "楷体", "楷体_GB2312" };

	private static readonly string[] AsciiFonts = new string[4] { "Times New Roman", "Arial", "Calibri", "Georgia" };

	public CompilationFormatOptions Options => options;

	public CompilationSettingsForm(CompilationFormatOptions options)
	{
		this.options = options ?? new CompilationFormatOptions();
		if (this.options.TocOptions == null)
		{
			this.options.TocOptions = new CompilationTocOptions();
		}
		InitializeComponent();
		LoadValues();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void InitializeComponent()
	{
		((Control)this).Text = "汇编排版参数";
		((Form)this).StartPosition = (FormStartPosition)4;
		((Form)this).FormBorderStyle = (FormBorderStyle)3;
		((Form)this).MaximizeBox = false;
		((Form)this).MinimizeBox = false;
		((Form)this).ClientSize = new Size(620, 640);
		((Control)this).BackColor = UiColors.BgPage;
		Panel val = new Panel
		{
			Location = new Point(0, 0),
			Size = new Size(620, 584),
			AutoScroll = true,
			BackColor = UiColors.BgPage
		};
		int num = 14;
		Label val2 = new Label
		{
			Text = "固定标记：@@汇编@@（只读）",
			Location = new Point(24, num),
			Size = new Size(460, 22),
			Font = UiFonts.BodyBold,
			ForeColor = UiColors.TextTitle
		};
		((Control)val).Controls.Add((Control)(object)val2);
		num += 32;
		GroupBox val3 = new GroupBox
		{
			Text = "文章处理",
			Location = new Point(24, num),
			Size = new Size(572, 62),
			Font = UiFonts.BodyBold,
			ForeColor = UiColors.TextTitle
		};
		chkStartEachArticleOnNewPage = new CheckBox
		{
			Text = "每篇文章另起一页",
			Location = new Point(16, 26),
			Size = new Size(220, 22),
			Font = UiFonts.Body
		};
		((Control)val3).Controls.Add((Control)(object)chkStartEachArticleOnNewPage);
		((Control)val).Controls.Add((Control)(object)val3);
		num += 74;
		GroupBox val4 = new GroupBox
		{
			Text = "前置内容",
			Location = new Point(24, num),
			Size = new Size(572, 84),
			Font = UiFonts.BodyBold,
			ForeColor = UiColors.TextTitle
		};
		radPreserveFrontMatter = new RadioButton
		{
			Text = "保留第一个标记前的内容",
			Location = new Point(16, 26),
			Size = new Size(220, 22),
			Font = UiFonts.Body
		};
		radRejectFrontMatter = new RadioButton
		{
			Text = "发现前置内容时停止执行",
			Location = new Point(16, 52),
			Size = new Size(220, 22),
			Font = UiFonts.Body
		};
		((Control)val4).Controls.Add((Control)(object)radPreserveFrontMatter);
		((Control)val4).Controls.Add((Control)(object)radRejectFrontMatter);
		((Control)val).Controls.Add((Control)(object)val4);
		num += 96;
		GroupBox val5 = new GroupBox
		{
			Text = "目录",
			Location = new Point(24, num),
			Size = new Size(572, 300),
			Font = UiFonts.BodyBold,
			ForeColor = UiColors.TextTitle
		};
		chkGenerateToc = new CheckBox
		{
			Text = "生成目录（重复执行只更新不重复插入）",
			Location = new Point(16, 26),
			Size = new Size(320, 22),
			Font = UiFonts.Body
		};
		((Control)val5).Controls.Add((Control)(object)chkGenerateToc);
		radTocDocumentStart = new RadioButton
		{
			Text = "文档最前",
			Location = new Point(340, 26),
			Size = new Size(90, 22),
			Font = UiFonts.Body
		};
		radTocBeforeFirstArticle = new RadioButton
		{
			Text = "第一篇之前",
			Location = new Point(436, 26),
			Size = new Size(100, 22),
			Font = UiFonts.Body
		};
		((Control)val5).Controls.Add((Control)(object)radTocDocumentStart);
		((Control)val5).Controls.Add((Control)(object)radTocBeforeFirstArticle);
		int num2 = 58;
		AddTocLabel((Control)(object)val5, "目录标题", 16, num2 + 4);
		txtTocTitle = new TextBox
		{
			Location = new Point(88, num2),
			Size = new Size(140, 24),
			Font = UiFonts.Body
		};
		((Control)val5).Controls.Add((Control)(object)txtTocTitle);
		AddTocLabel((Control)(object)val5, "标题字体", 236, num2 + 4);
		cboTocTitleFont = AddFontCombo((Control)(object)val5, FarEastFonts, 308, num2, 120);
		numTocTitleSize = AddNumber((Control)(object)val5, 434, num2, 5m, 72m);
		chkTocTitleBold = new CheckBox
		{
			Text = "加粗",
			Location = new Point(498, num2),
			Size = new Size(56, 22),
			Font = UiFonts.Body
		};
		((Control)val5).Controls.Add((Control)(object)chkTocTitleBold);
		int num3 = 90;
		AddTocLabel((Control)(object)val5, "标题对齐", 16, num3 + 4);
		cboTocTitleAlign = AddCombo((Control)(object)val5, new object[3] { "左对齐", "居中", "右对齐" }, 88, num3, 100);
		AddTocLabel((Control)(object)val5, "标题段前", 200, num3 + 4);
		numTocTitleSpaceBefore = AddNumber((Control)(object)val5, 272, num3, 0m, 200m);
		AddTocLabel((Control)(object)val5, "标题段后", 356, num3 + 4);
		numTocTitleSpaceAfter = AddNumber((Control)(object)val5, 428, num3, 0m, 200m);
		int num4 = 122;
		AddTocLabel((Control)(object)val5, "条目中文字体", 16, num4 + 4);
		cboTocEntryFontFarEast = AddFontCombo((Control)(object)val5, FarEastFonts, 116, num4, 140);
		AddTocLabel((Control)(object)val5, "英文数字字体", 264, num4 + 4);
		cboTocEntryFontAscii = AddCombo((Control)(object)val5, AsciiFonts.Cast<object>().ToArray(), 364, num4, 150, editable: true);
		int num5 = 154;
		AddTocLabel((Control)(object)val5, "条目字号", 16, num5 + 4);
		numTocEntrySize = AddNumber((Control)(object)val5, 88, num5, 5m, 72m);
		AddTocLabel((Control)(object)val5, "条目行距", 172, num5 + 4);
		numTocEntryLineSpacing = AddNumber((Control)(object)val5, 244, num5, 5m, 200m);
		AddTocLabel((Control)(object)val5, "前导符", 328, num5 + 4);
		cboTocLeader = AddCombo((Control)(object)val5, new object[3] { "点线 ……", "短线 ----", "无" }, 388, num5, 110);
		int y = 186;
		chkTocHyperlink = new CheckBox
		{
			Text = "页码带跳转链接",
			Location = new Point(16, y),
			Size = new Size(140, 22),
			Font = UiFonts.Body
		};
		chkTocPageBreak = new CheckBox
		{
			Text = "目录后另起一页",
			Location = new Point(180, y),
			Size = new Size(140, 22),
			Font = UiFonts.Body
		};
		chkTocCountInNumbering = new CheckBox
		{
			Text = "目录页计入连续页码",
			Location = new Point(344, y),
			Size = new Size(160, 22),
			Font = UiFonts.Body
		};
		((Control)val5).Controls.Add((Control)(object)chkTocHyperlink);
		((Control)val5).Controls.Add((Control)(object)chkTocPageBreak);
		((Control)val5).Controls.Add((Control)(object)chkTocCountInNumbering);
		int num6 = 220;
		AddTocLabel((Control)(object)val5, "已有目录处理", 16, num6 + 4);
		cboExistingToc = AddCombo((Control)(object)val5, new object[3] { "保留（照常插入自动目录）", "替换为自动目录", "有用户目录则不生成" }, 116, num6, 220);
		Label val6 = new Label
		{
			Text = "目录样式参数随模板保存，并在生成或更新目录时生效。",
			Location = new Point(16, 254),
			Size = new Size(520, 20),
			Font = UiFonts.Caption,
			ForeColor = UiColors.TextBody
		};
		((Control)val5).Controls.Add((Control)(object)val6);
		((Control)val).Controls.Add((Control)(object)val5);
		num += 312;
		GroupBox val7 = new GroupBox
		{
			Text = "页码",
			Location = new Point(24, num),
			Size = new Size(572, 122),
			Font = UiFonts.BodyBold,
			ForeColor = UiColors.TextTitle
		};
		radPageNoChange = new RadioButton
		{
			Text = "不调整",
			Location = new Point(16, 26),
			Size = new Size(120, 22),
			Font = UiFonts.Body
		};
		radPageRemove = new RadioButton
		{
			Text = "删除页码",
			Location = new Point(16, 52),
			Size = new Size(120, 22),
			Font = UiFonts.Body
		};
		radPageContinuous = new RadioButton
		{
			Text = "全文连续",
			Location = new Point(180, 26),
			Size = new Size(120, 22),
			Font = UiFonts.Body
		};
		radPageRestartPerArticle = new RadioButton
		{
			Text = "每篇重新开始",
			Location = new Point(180, 52),
			Size = new Size(140, 22),
			Font = UiFonts.Body
		};
		((Control)val7).Controls.Add((Control)(object)radPageNoChange);
		((Control)val7).Controls.Add((Control)(object)radPageRemove);
		((Control)val7).Controls.Add((Control)(object)radPageContinuous);
		((Control)val7).Controls.Add((Control)(object)radPageRestartPerArticle);
		Label val8 = new Label
		{
			Text = "“每篇重新开始”按每篇文章的真实开始边界重启页码。",
			Location = new Point(16, 84),
			Size = new Size(520, 20),
			Font = UiFonts.Caption,
			ForeColor = UiColors.TextBody
		};
		((Control)val7).Controls.Add((Control)(object)val8);
		((Control)val).Controls.Add((Control)(object)val7);
		((Control)this).Controls.Add((Control)(object)val);
		Button val9 = new Button
		{
			Text = "确定",
			Location = new Point(360, 596),
			Size = new Size(100, 32),
			BackColor = UiColors.PrimaryLight,
			ForeColor = Color.White,
			FlatStyle = (FlatStyle)0,
			Font = UiFonts.Body
		};
		((ButtonBase)val9).FlatAppearance.BorderSize = 0;
		((Control)val9).Click += [MethodImpl(MethodImplOptions.NoInlining)] (object s, EventArgs e) =>
		{
			CompilationFormatOptions compilationFormatOptions = CloneOptionsForValidation();
			try
			{
				SaveValuesInto(compilationFormatOptions);
				ConfigManager.ValidateCompilationOptionsForEdit(compilationFormatOptions);
				CopyOptions(compilationFormatOptions, options);
				((Form)this).DialogResult = (DialogResult)1;
				((Form)this).Close();
			}
			catch (Exception ex)
			{
				MessageBox.Show((IWin32Window)(object)this, "汇编参数校验未通过：" + ex.Message + "\n请修正后再确定。", "汇编排版参数", (MessageBoxButtons)0, (MessageBoxIcon)48);
			}
		};
		Button val10 = new Button
		{
			Text = "取消",
			Location = new Point(480, 596),
			Size = new Size(100, 32),
			BackColor = UiColors.BgDanger,
			ForeColor = UiColors.Danger,
			FlatStyle = (FlatStyle)0,
			Font = UiFonts.Body,
			DialogResult = (DialogResult)2
		};
		((ButtonBase)val10).FlatAppearance.BorderSize = 0;
		((Control)this).Controls.Add((Control)(object)val9);
		((Control)this).Controls.Add((Control)(object)val10);
		ApplyTooltips((Control)(object)val2, (Control)(object)val9, (Control)(object)val10);
		((Component)this).Disposed += delegate
		{
			((Component)(object)toolTip).Dispose();
		};
		((Form)this).AcceptButton = (IButtonControl)(object)val9;
		((Form)this).CancelButton = (IButtonControl)(object)val10;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void ApplyTooltips(Control markerLabel, Control okButton, Control cancelButton)
	{
		UiTextApplier.ApplyTooltip(toolTip, markerLabel, "Compilation.Marker");
		UiTextApplier.ApplyTooltip(toolTip, (Control)(object)chkStartEachArticleOnNewPage, "Compilation.StartNewPage");
		UiTextApplier.ApplyTooltip(toolTip, (Control)(object)radPreserveFrontMatter, "Compilation.PreserveFrontMatter");
		UiTextApplier.ApplyTooltip(toolTip, (Control)(object)radRejectFrontMatter, "Compilation.RejectFrontMatter");
		UiTextApplier.ApplyTooltip(toolTip, (Control)(object)chkGenerateToc, "Compilation.GenerateToc");
		UiTextApplier.ApplyTooltip(toolTip, (Control)(object)radTocDocumentStart, "Compilation.Toc.DocumentStart");
		UiTextApplier.ApplyTooltip(toolTip, (Control)(object)radTocBeforeFirstArticle, "Compilation.Toc.BeforeFirstArticle");
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Compilation.Toc.Title", "目录标题", (Control)txtTocTitle);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Compilation.Toc.TitleFont", "标题字体", (Control)cboTocTitleFont);
		UiTextApplier.ApplyTooltip(toolTip, (Control)(object)numTocTitleSize, "Compilation.Toc.TitleSize");
		UiTextApplier.ApplyTooltip(toolTip, (Control)(object)chkTocTitleBold, "Compilation.Toc.TitleBold");
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Compilation.Toc.TitleAlign", "标题对齐", (Control)cboTocTitleAlign);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Compilation.Toc.TitleBefore", "标题段前", (Control)numTocTitleSpaceBefore);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Compilation.Toc.TitleAfter", "标题段后", (Control)numTocTitleSpaceAfter);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Compilation.Toc.EntryFontFarEast", "条目中文字体", (Control)cboTocEntryFontFarEast);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Compilation.Toc.EntryFontAscii", "英文数字字体", (Control)cboTocEntryFontAscii);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Compilation.Toc.EntrySize", "条目字号", (Control)numTocEntrySize);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Compilation.Toc.EntryLineSpacing", "条目行距", (Control)numTocEntryLineSpacing);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Compilation.Toc.Leader", "前导符", (Control)cboTocLeader);
		UiTextApplier.ApplyTooltip(toolTip, (Control)(object)chkTocHyperlink, "Compilation.Toc.Hyperlink");
		UiTextApplier.ApplyTooltip(toolTip, (Control)(object)chkTocPageBreak, "Compilation.Toc.PageBreak");
		UiTextApplier.ApplyTooltip(toolTip, (Control)(object)chkTocCountInNumbering, "Compilation.Toc.CountInNumbering");
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Compilation.Toc.ExistingToc", "已有目录处理", (Control)cboExistingToc);
		UiTextApplier.ApplyTooltip(toolTip, (Control)(object)radPageNoChange, "Compilation.Page.NoChange");
		UiTextApplier.ApplyTooltip(toolTip, (Control)(object)radPageRemove, "Compilation.Page.Remove");
		UiTextApplier.ApplyTooltip(toolTip, (Control)(object)radPageContinuous, "Compilation.Page.Continuous");
		UiTextApplier.ApplyTooltip(toolTip, (Control)(object)radPageRestartPerArticle, "Compilation.Page.Restart");
		UiTextApplier.ApplyTooltip(toolTip, okButton, "Compilation.Settings.Confirm");
		UiTextApplier.ApplyTooltip(toolTip, cancelButton, "Compilation.Settings.Cancel");
	}

	private static void AddTocLabel(Control parent, string text, int x, int y)
	{
		parent.Controls.Add((Control)new Label
		{
			Text = text,
			Location = new Point(x, y),
			AutoSize = true,
			Font = UiFonts.Body,
			ForeColor = UiColors.TextBody
		});
	}

	private ComboBox AddFontCombo(Control parent, string[] fonts, int x, int y, int width)
	{
		ComboBox val = new ComboBox
		{
			Location = new Point(x, y),
			Size = new Size(width, 24),
			Font = UiFonts.Body,
			DropDownStyle = (ComboBoxStyle)1
		};
		val.Items.AddRange(fonts.Cast<object>().ToArray());
		parent.Controls.Add((Control)(object)val);
		return val;
	}

	private ComboBox AddCombo(Control parent, object[] items, int x, int y, int width, bool editable = false)
	{
		ComboBox val = new ComboBox
		{
			Location = new Point(x, y),
			Size = new Size(width, 24),
			Font = UiFonts.Body,
			DropDownStyle = (ComboBoxStyle)(editable ? 1 : 2)
		};
		val.Items.AddRange(items);
		parent.Controls.Add((Control)(object)val);
		return val;
	}

	private NumericUpDown AddNumber(Control parent, int x, int y, decimal min, decimal max)
	{
		NumericUpDown val = new NumericUpDown
		{
			Location = new Point(x, y),
			Size = new Size(62, 24),
			Font = UiFonts.Body,
			Minimum = min,
			Maximum = max,
			DecimalPlaces = 1
		};
		parent.Controls.Add((Control)(object)val);
		return val;
	}

	private void LoadValues()
	{
		CompilationTocOptions tocOptions = options.TocOptions;
		chkStartEachArticleOnNewPage.Checked = options.StartEachArticleOnNewPage;
		radPreserveFrontMatter.Checked = options.FrontMatterMode == CompilationFrontMatterMode.Preserve;
		radRejectFrontMatter.Checked = options.FrontMatterMode == CompilationFrontMatterMode.Reject;
		chkGenerateToc.Checked = options.GenerateToc;
		radTocDocumentStart.Checked = options.TocPosition == CompilationTocPosition.DocumentStart;
		radTocBeforeFirstArticle.Checked = options.TocPosition == CompilationTocPosition.BeforeFirstArticle;
		((Control)txtTocTitle).Text = tocOptions.TitleText;
		((Control)cboTocTitleFont).Text = tocOptions.TitleFontName;
		numTocTitleSize.Value = ClampNumber(tocOptions.TitleFontSize, numTocTitleSize);
		chkTocTitleBold.Checked = tocOptions.TitleBold;
		((ListControl)cboTocTitleAlign).SelectedIndex = TocAlignmentToIndex(tocOptions.TitleAlignment);
		numTocTitleSpaceBefore.Value = ClampNumber(tocOptions.TitleSpaceBefore, numTocTitleSpaceBefore);
		numTocTitleSpaceAfter.Value = ClampNumber(tocOptions.TitleSpaceAfter, numTocTitleSpaceAfter);
		((Control)cboTocEntryFontFarEast).Text = tocOptions.EntryFontNameFarEast;
		((Control)cboTocEntryFontAscii).Text = tocOptions.EntryFontNameAscii;
		numTocEntrySize.Value = ClampNumber(tocOptions.EntryFontSize, numTocEntrySize);
		numTocEntryLineSpacing.Value = ClampNumber(tocOptions.EntryLineSpacingPoints, numTocEntryLineSpacing);
		((ListControl)cboTocLeader).SelectedIndex = LeaderToIndex(tocOptions.LeaderStyle);
		chkTocHyperlink.Checked = tocOptions.HyperlinkEntries;
		chkTocPageBreak.Checked = tocOptions.PageBreakAfterToc;
		chkTocCountInNumbering.Checked = tocOptions.IncludeTocPageInContinuousNumbering;
		((ListControl)cboExistingToc).SelectedIndex = ExistingPolicyToIndex(tocOptions.ExistingTocPolicy);
		radPageNoChange.Checked = options.PageNumberMode == CompilationPageNumberMode.NoChange;
		radPageRemove.Checked = options.PageNumberMode == CompilationPageNumberMode.Remove;
		radPageContinuous.Checked = options.PageNumberMode == CompilationPageNumberMode.Continuous;
		radPageRestartPerArticle.Checked = options.PageNumberMode == CompilationPageNumberMode.RestartPerArticle;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void SaveValuesInto(CompilationFormatOptions target)
	{
		CompilationTocOptions tocOptions = target.TocOptions;
		target.StartEachArticleOnNewPage = chkStartEachArticleOnNewPage.Checked;
		target.FrontMatterMode = (radRejectFrontMatter.Checked ? CompilationFrontMatterMode.Reject : CompilationFrontMatterMode.Preserve);
		target.GenerateToc = chkGenerateToc.Checked;
		target.TocPosition = ((!radTocDocumentStart.Checked) ? CompilationTocPosition.BeforeFirstArticle : CompilationTocPosition.DocumentStart);
		tocOptions.TitleText = (string.IsNullOrWhiteSpace(((Control)txtTocTitle).Text) ? "目录" : ((Control)txtTocTitle).Text.Trim());
		tocOptions.TitleFontName = (((Control)cboTocTitleFont).Text ?? "").Trim();
		tocOptions.TitleFontSize = (float)numTocTitleSize.Value;
		tocOptions.TitleBold = chkTocTitleBold.Checked;
		tocOptions.TitleAlignment = IndexToTocAlignment(((ListControl)cboTocTitleAlign).SelectedIndex);
		tocOptions.TitleSpaceBefore = (float)numTocTitleSpaceBefore.Value;
		tocOptions.TitleSpaceAfter = (float)numTocTitleSpaceAfter.Value;
		tocOptions.EntryFontNameFarEast = (((Control)cboTocEntryFontFarEast).Text ?? "").Trim();
		tocOptions.EntryFontNameAscii = (((Control)cboTocEntryFontAscii).Text ?? "").Trim();
		tocOptions.EntryFontSize = (float)numTocEntrySize.Value;
		tocOptions.EntryLineSpacingPoints = (float)numTocEntryLineSpacing.Value;
		tocOptions.LeaderStyle = IndexToLeader(((ListControl)cboTocLeader).SelectedIndex);
		tocOptions.HyperlinkEntries = chkTocHyperlink.Checked;
		tocOptions.PageBreakAfterToc = chkTocPageBreak.Checked;
		tocOptions.IncludeTocPageInContinuousNumbering = chkTocCountInNumbering.Checked;
		tocOptions.ExistingTocPolicy = IndexToExistingPolicy(((ListControl)cboExistingToc).SelectedIndex);
		if (!radPageRemove.Checked)
		{
			if (!radPageContinuous.Checked)
			{
				if (!radPageRestartPerArticle.Checked)
				{
					target.PageNumberMode = CompilationPageNumberMode.NoChange;
				}
				else
				{
					target.PageNumberMode = CompilationPageNumberMode.RestartPerArticle;
				}
			}
			else
			{
				target.PageNumberMode = CompilationPageNumberMode.Continuous;
			}
		}
		else
		{
			target.PageNumberMode = CompilationPageNumberMode.Remove;
		}
	}

	private CompilationFormatOptions CloneOptionsForValidation()
	{
		return new CompilationFormatOptions
		{
			OptionsVersion = options.OptionsVersion,
			GenerateToc = options.GenerateToc,
			StartEachArticleOnNewPage = options.StartEachArticleOnNewPage,
			FrontMatterMode = options.FrontMatterMode,
			TocPosition = options.TocPosition,
			PageNumberMode = options.PageNumberMode,
			TocOptions = ((options.TocOptions == null) ? new CompilationTocOptions() : options.TocOptions.Clone())
		};
	}

	private static void CopyOptions(CompilationFormatOptions source, CompilationFormatOptions target)
	{
		target.OptionsVersion = source.OptionsVersion;
		target.GenerateToc = source.GenerateToc;
		target.StartEachArticleOnNewPage = source.StartEachArticleOnNewPage;
		target.FrontMatterMode = source.FrontMatterMode;
		target.TocPosition = source.TocPosition;
		target.PageNumberMode = source.PageNumberMode;
		target.TocOptions = source.TocOptions;
	}

	private static decimal ClampNumber(float value, NumericUpDown control)
	{
		decimal num = (decimal)value;
		if (!(num < control.Minimum))
		{
			if (num > control.Maximum)
			{
				return control.Maximum;
			}
			return num;
		}
		return control.Minimum;
	}

	private static int TocAlignmentToIndex(CompilationTocTitleAlignment alignment)
	{
		return alignment switch
		{
			CompilationTocTitleAlignment.Right => 2, 
			CompilationTocTitleAlignment.Left => 0, 
			_ => 1, 
		};
	}

	private static CompilationTocTitleAlignment IndexToTocAlignment(int index)
	{
		return index switch
		{
			2 => CompilationTocTitleAlignment.Right, 
			0 => CompilationTocTitleAlignment.Left, 
			_ => CompilationTocTitleAlignment.Center, 
		};
	}

	private static int LeaderToIndex(CompilationTocLeaderStyle leader)
	{
		return leader switch
		{
			CompilationTocLeaderStyle.Dashes => 1, 
			CompilationTocLeaderStyle.None => 2, 
			_ => 0, 
		};
	}

	private static CompilationTocLeaderStyle IndexToLeader(int index)
	{
		return index switch
		{
			1 => CompilationTocLeaderStyle.Dashes, 
			2 => CompilationTocLeaderStyle.None, 
			_ => CompilationTocLeaderStyle.Dots, 
		};
	}

	private static int ExistingPolicyToIndex(CompilationExistingTocPolicy policy)
	{
		return policy switch
		{
			CompilationExistingTocPolicy.Replace => 1, 
			CompilationExistingTocPolicy.Cancel => 2, 
			_ => 0, 
		};
	}

	private static CompilationExistingTocPolicy IndexToExistingPolicy(int index)
	{
		return index switch
		{
			2 => CompilationExistingTocPolicy.Cancel, 
			1 => CompilationExistingTocPolicy.Replace, 
			_ => CompilationExistingTocPolicy.Keep, 
		};
	}
}
