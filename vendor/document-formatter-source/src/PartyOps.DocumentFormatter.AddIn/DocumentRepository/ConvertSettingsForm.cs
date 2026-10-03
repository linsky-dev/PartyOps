using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using DocumentRepository.Models.Conversion;
using DocumentRepository.Services.Conversion;
using DocumentRepository.Services.UiText;

namespace DocumentRepository;

public class ConvertSettingsForm : Form
{
	private readonly ComboBox cboSaveLocation = new ComboBox();

	private readonly TextBox txtCustomFolder = new TextBox();

	private readonly Button btnBrowse = new Button();

	private readonly ComboBox cboSameName = new ComboBox();

	private readonly CheckBox chkOpenFolder = new CheckBox();

	private readonly ComboBox cboImageMode = new ComboBox();

	private readonly ComboBox cboPageMode = new ComboBox();

	private readonly TextBox txtPageRange = new TextBox();

	private readonly TextBox txtSelectedPages = new TextBox();

	private readonly ComboBox cboImageFormat = new ComboBox();

	private readonly NumericUpDown numDpi = new NumericUpDown();

	private readonly ComboBox cboDocxMode = new ComboBox();

	private readonly ComboBox cboPdfEngine = new ComboBox();

	private readonly CheckBox chkPdfNormalizePunctuation = new CheckBox();

	private readonly CheckBox chkTxtRemoveBlank = new CheckBox();

	private ConvertOptions options;

	private readonly ToolTip toolTip = UiTextApplier.CreateToolTip();

	[MethodImpl(MethodImplOptions.NoInlining)]
	public ConvertSettingsForm()
	{
		((Control)this).Text = "一键转换规则设置";
		((Form)this).StartPosition = (FormStartPosition)4;
		((Form)this).ClientSize = new Size(760, 610);
		((Control)this).MinimumSize = new Size(760, 610);
		((Control)this).Font = new Font("Microsoft YaHei UI", 9f);
		((Control)this).BackColor = Color.FromArgb(244, 247, 251);
		options = ConvertSettingsService.Load();
		BuildUi();
		LoadOptions();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void BuildUi()
	{
		((ContainerControl)this).AutoScaleMode = (AutoScaleMode)2;
		TableLayoutPanel val = new TableLayoutPanel
		{
			Dock = (DockStyle)5,
			ColumnCount = 1,
			RowCount = 3
		};
		val.ColumnStyles.Add(new ColumnStyle((SizeType)2, 100f));
		val.RowStyles.Add(new RowStyle((SizeType)0));
		val.RowStyles.Add(new RowStyle((SizeType)2, 100f));
		val.RowStyles.Add(new RowStyle((SizeType)0));
		((Control)this).Controls.Add((Control)(object)val);
		val.Controls.Add((Control)new Label
		{
			Text = "一键转换规则设置",
			Font = new Font("Microsoft YaHei UI", 12f, (FontStyle)1),
			ForeColor = Color.FromArgb(0, 82, 155),
			AutoSize = true,
			Margin = new Padding(22, 16, 0, 4)
		}, 0, 0);
		TableLayoutPanel val2 = new TableLayoutPanel
		{
			Dock = (DockStyle)5,
			ColumnCount = 1,
			RowCount = 5
		};
		val2.ColumnStyles.Add(new ColumnStyle((SizeType)2, 100f));
		val2.RowStyles.Add(new RowStyle((SizeType)1, 152f));
		val2.RowStyles.Add(new RowStyle((SizeType)1, 112f));
		val2.RowStyles.Add(new RowStyle((SizeType)1, 180f));
		val2.RowStyles.Add(new RowStyle((SizeType)1, 68f));
		val2.RowStyles.Add(new RowStyle((SizeType)2, 100f));
		val.Controls.Add((Control)(object)val2, 0, 1);
		GroupBox val3 = CreateGroup("基础设置");
		TableLayoutPanel val4 = CreateFieldGrid();
		((Control)val3).Controls.Add((Control)(object)val4);
		val4.RowStyles.Add(new RowStyle((SizeType)0));
		val4.RowStyles.Add(new RowStyle((SizeType)0));
		val4.RowStyles.Add(new RowStyle((SizeType)0));
		val4.Controls.Add((Control)(object)CreateLabel("保存位置"), 0, 0);
		SetupCombo(cboSaveLocation, new object[2] { "原文件夹", "自定义文件夹" });
		cboSaveLocation.SelectedIndexChanged += delegate
		{
			UpdateFieldState();
		};
		val4.Controls.Add((Control)(object)cboSaveLocation, 1, 0);
		val4.Controls.Add((Control)(object)CreateLabel("同名处理"), 2, 0);
		SetupCombo(cboSameName, new object[4] { "每次询问", "自动改名", "覆盖", "取消转换" });
		val4.Controls.Add((Control)(object)cboSameName, 3, 0);
		val4.Controls.Add((Control)(object)CreateLabel("自定义路径"), 0, 1);
		TableLayoutPanel val5 = new TableLayoutPanel
		{
			Anchor = (AnchorStyles)12,
			ColumnCount = 2,
			RowCount = 1,
			AutoSize = true,
			Margin = new Padding(0)
		};
		val5.ColumnStyles.Add(new ColumnStyle((SizeType)2, 100f));
		val5.ColumnStyles.Add(new ColumnStyle((SizeType)0));
		val5.RowStyles.Add(new RowStyle((SizeType)0));
		((Control)txtCustomFolder).Dock = (DockStyle)5;
		((Control)txtCustomFolder).Margin = new Padding(3, 5, 3, 3);
		val5.Controls.Add((Control)(object)txtCustomFolder, 0, 0);
		((Control)btnBrowse).Text = "选择...";
		((Control)btnBrowse).Size = new Size(84, 28);
		((Control)btnBrowse).Margin = new Padding(6, 2, 12, 3);
		((Control)btnBrowse).Click += delegate
		{
			ChooseFolder();
		};
		val5.Controls.Add((Control)(object)btnBrowse, 1, 0);
		val4.Controls.Add((Control)(object)val5, 1, 1);
		val4.SetColumnSpan((Control)(object)val5, 3);
		((Control)chkOpenFolder).Text = "转换完成后打开输出文件夹";
		((Control)chkOpenFolder).AutoSize = true;
		((Control)chkOpenFolder).Anchor = (AnchorStyles)4;
		((Control)chkOpenFolder).Margin = new Padding(3, 8, 3, 3);
		val4.Controls.Add((Control)(object)chkOpenFolder, 1, 2);
		val4.SetColumnSpan((Control)(object)chkOpenFolder, 3);
		val2.Controls.Add((Control)(object)val3, 0, 0);
		GroupBox val6 = CreateGroup("DOCX 设置");
		TableLayoutPanel val7 = CreateFieldGrid();
		((Control)val6).Controls.Add((Control)(object)val7);
		val7.RowStyles.Add(new RowStyle((SizeType)0));
		val7.RowStyles.Add(new RowStyle((SizeType)0));
		val7.Controls.Add((Control)(object)CreateLabel("转换方式"), 0, 0);
		SetupCombo(cboDocxMode, new object[2] { "另存新文件", "替换当前文档" });
		val7.Controls.Add((Control)(object)cboDocxMode, 1, 0);
		val7.Controls.Add((Control)(object)CreateLabel("PDF 转 Word 引擎"), 2, 0);
		SetupCombo(cboPdfEngine, new object[2] { "本地引擎（推荐）", "Word/WPS 内建导入" });
		val7.Controls.Add((Control)(object)cboPdfEngine, 3, 0);
		((Control)chkPdfNormalizePunctuation).Text = "可选：规范中文语境中的半角标点（保护网址、数字、邮箱和文件名）";
		((Control)chkPdfNormalizePunctuation).AutoSize = true;
		((Control)chkPdfNormalizePunctuation).Anchor = (AnchorStyles)4;
		((Control)chkPdfNormalizePunctuation).Margin = new Padding(3, 7, 3, 3);
		val7.Controls.Add((Control)(object)chkPdfNormalizePunctuation, 1, 1);
		val7.SetColumnSpan((Control)(object)chkPdfNormalizePunctuation, 3);
		val2.Controls.Add((Control)(object)val6, 0, 1);
		GroupBox val8 = CreateGroup("图片设置");
		TableLayoutPanel val9 = CreateFieldGrid();
		((Control)val8).Controls.Add((Control)(object)val9);
		val9.RowStyles.Add(new RowStyle((SizeType)0));
		val9.RowStyles.Add(new RowStyle((SizeType)0));
		val9.RowStyles.Add(new RowStyle((SizeType)0));
		val9.Controls.Add((Control)(object)CreateLabel("输出形式"), 0, 0);
		SetupCombo(cboImageMode, new object[2] { "单图", "长图" });
		val9.Controls.Add((Control)(object)cboImageMode, 1, 0);
		val9.Controls.Add((Control)(object)CreateLabel("页码选择"), 2, 0);
		SetupCombo(cboPageMode, new object[3] { "全文", "范围页", "指定页" });
		cboPageMode.SelectedIndexChanged += delegate
		{
			UpdateFieldState();
		};
		val9.Controls.Add((Control)(object)cboPageMode, 3, 0);
		val9.Controls.Add((Control)(object)CreateLabel("范围页"), 0, 1);
		((Control)txtPageRange).Anchor = (AnchorStyles)12;
		((Control)txtPageRange).Margin = new Padding(3, 5, 12, 3);
		val9.Controls.Add((Control)(object)txtPageRange, 1, 1);
		val9.Controls.Add((Control)(object)CreateLabel("指定页"), 2, 1);
		((Control)txtSelectedPages).Anchor = (AnchorStyles)12;
		((Control)txtSelectedPages).Margin = new Padding(3, 5, 12, 3);
		val9.Controls.Add((Control)(object)txtSelectedPages, 3, 1);
		val9.Controls.Add((Control)(object)CreateLabel("图片格式"), 0, 2);
		SetupCombo(cboImageFormat, new object[2] { "PNG", "JPG" });
		val9.Controls.Add((Control)(object)cboImageFormat, 1, 2);
		val9.Controls.Add((Control)(object)CreateLabel("清晰度"), 2, 2);
		FlowLayoutPanel val10 = new FlowLayoutPanel
		{
			Anchor = (AnchorStyles)12,
			FlowDirection = (FlowDirection)0,
			WrapContents = false,
			AutoSize = true,
			Margin = new Padding(0)
		};
		((Control)numDpi).Width = 100;
		numDpi.Minimum = 72m;
		numDpi.Maximum = 600m;
		numDpi.Increment = 50m;
		((Control)numDpi).Margin = new Padding(3, 4, 4, 3);
		((Control)val10).Controls.Add((Control)(object)numDpi);
		((Control)val10).Controls.Add((Control)new Label
		{
			Text = "DPI",
			AutoSize = true,
			ForeColor = Color.Black,
			Margin = new Padding(0, 8, 3, 3)
		});
		val9.Controls.Add((Control)(object)val10, 3, 2);
		val2.Controls.Add((Control)(object)val8, 0, 2);
		GroupBox val11 = CreateGroup("TXT 设置");
		((Control)chkTxtRemoveBlank).Text = "导出 TXT 时清理多余空行";
		((Control)chkTxtRemoveBlank).AutoSize = true;
		((Control)chkTxtRemoveBlank).Location = new Point(22, 26);
		((Control)val11).Controls.Add((Control)(object)chkTxtRemoveBlank);
		val2.Controls.Add((Control)(object)val11, 0, 3);
		FlowLayoutPanel val12 = new FlowLayoutPanel
		{
			Dock = (DockStyle)5,
			FlowDirection = (FlowDirection)2,
			WrapContents = false,
			AutoSize = true,
			Margin = new Padding(0, 2, 12, 8)
		};
		val.Controls.Add((Control)(object)val12, 0, 2);
		Button control = AddButton((Control)(object)val12, "取消", delegate
		{
			((Form)this).DialogResult = (DialogResult)2;
		});
		Button control2 = AddButton((Control)(object)val12, "确定", SaveAndClose, primary: true);
		ConfigureTooltips();
		UiTextApplier.ApplyTooltip(toolTip, (Control)(object)control, "Convert.Settings.Cancel");
		UiTextApplier.ApplyTooltip(toolTip, (Control)(object)control2, "Convert.Settings.Confirm");
		((Component)this).Disposed += delegate
		{
			((Component)(object)toolTip).Dispose();
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void ConfigureTooltips()
	{
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Convert.SaveLocation", "保存位置", (Control)cboSaveLocation);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Convert.CustomFolder", "自定义路径", (Control)txtCustomFolder, (Control)btnBrowse);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Convert.SameName", "同名处理", (Control)cboSameName);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Convert.OpenFolder", "转换完成后打开输出文件夹", (Control)chkOpenFolder);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Convert.DocxMode", "转换方式", (Control)cboDocxMode);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Convert.PdfToWordEngine", "PDF 转 Word 引擎", (Control)cboPdfEngine);
		toolTip.SetToolTip((Control)(object)chkPdfNormalizePunctuation, "默认关闭。只在 PDF 版面和段落结构重建完成后处理中文语境半角标点，不改变段落边界。");
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Convert.ImageMode", "输出形式", (Control)cboImageMode);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Convert.PageMode", "页码选择", (Control)cboPageMode);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Convert.PageRange", "范围页", (Control)txtPageRange);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Convert.SelectedPages", "指定页", (Control)txtSelectedPages);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Convert.ImageFormat", "图片格式", (Control)cboImageFormat);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Convert.ImageDpi", "清晰度", (Control)numDpi);
		UiTextApplier.ApplyParameterTooltip(toolTip, (Control)(object)this, "Convert.TxtRemoveBlank", "导出 TXT 时清理多余空行", (Control)chkTxtRemoveBlank);
	}

	private GroupBox CreateGroup(string text)
	{
		return new GroupBox
		{
			Text = text,
			Dock = (DockStyle)5,
			Margin = new Padding(12, 6, 12, 6),
			ForeColor = Color.FromArgb(0, 82, 155)
		};
	}

	private static TableLayoutPanel CreateFieldGrid()
	{
		TableLayoutPanel val = new TableLayoutPanel
		{
			Dock = (DockStyle)5,
			ColumnCount = 4,
			Padding = new Padding(12, 6, 6, 4)
		};
		val.ColumnStyles.Add(new ColumnStyle((SizeType)0));
		val.ColumnStyles.Add(new ColumnStyle((SizeType)2, 50f));
		val.ColumnStyles.Add(new ColumnStyle((SizeType)0));
		val.ColumnStyles.Add(new ColumnStyle((SizeType)2, 50f));
		return val;
	}

	private static Label CreateLabel(string text)
	{
		return new Label
		{
			Text = text,
			AutoSize = true,
			TextAlign = (ContentAlignment)16,
			Anchor = (AnchorStyles)4,
			Margin = new Padding(3, 7, 8, 3),
			ForeColor = Color.Black
		};
	}

	private void SetupCombo(ComboBox combo, object[] items)
	{
		combo.DropDownStyle = (ComboBoxStyle)2;
		combo.Items.AddRange(items);
		((Control)combo).Anchor = (AnchorStyles)12;
		((Control)combo).Margin = new Padding(3, 3, 12, 3);
		int num = combo.DropDownWidth;
		Graphics val = ((Control)combo).CreateGraphics();
		try
		{
			foreach (object obj in items)
			{
				int val2 = (int)Math.Ceiling(val.MeasureString(obj.ToString(), ((Control)this).Font).Width) + SystemInformation.VerticalScrollBarWidth + 8;
				num = Math.Max(num, val2);
			}
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
		combo.DropDownWidth = num;
	}

	private Button AddButton(Control parent, string text, Action action, bool primary = false)
	{
		Button val = new Button
		{
			Text = text,
			Size = new Size(85, 32),
			Margin = new Padding(10, 6, 0, 6)
		};
		if (primary)
		{
			((Control)val).BackColor = Color.FromArgb(32, 111, 203);
			((Control)val).ForeColor = Color.White;
			((ButtonBase)val).FlatStyle = (FlatStyle)0;
			((ButtonBase)val).FlatAppearance.BorderSize = 0;
		}
		((Control)val).Click += delegate
		{
			action();
		};
		parent.Controls.Add((Control)(object)val);
		return val;
	}

	private void LoadOptions()
	{
		((ListControl)cboSaveLocation).SelectedIndex = ((options.SaveLocation == ConvertSaveLocation.CustomFolder) ? 1 : 0);
		((Control)txtCustomFolder).Text = options.CustomOutputFolder ?? "";
		((ListControl)cboSameName).SelectedIndex = SameNameToIndex(options.SameNamePolicy);
		chkOpenFolder.Checked = options.OpenFolderAfterConvert;
		((ListControl)cboImageMode).SelectedIndex = ((options.ImageExportMode == ImageExportMode.LongImage) ? 1 : 0);
		((ListControl)cboPageMode).SelectedIndex = PageModeToIndex(options.ImagePageSelectionMode);
		((Control)txtPageRange).Text = options.ImagePageRange ?? "";
		((Control)txtSelectedPages).Text = options.ImageSelectedPages ?? "";
		((ListControl)cboImageFormat).SelectedIndex = ((options.ImageFormat == ImageFileFormat.Jpg) ? 1 : 0);
		numDpi.Value = Math.Max(numDpi.Minimum, Math.Min(numDpi.Maximum, options.ImageDpi));
		((ListControl)cboDocxMode).SelectedIndex = ((options.DocxMode == DocxConvertMode.ReplaceCurrentDocument) ? 1 : 0);
		((ListControl)cboPdfEngine).SelectedIndex = ((options.PdfToWordEngine == PdfToWordEngine.HostImport) ? 1 : 0);
		chkPdfNormalizePunctuation.Checked = options.PdfNormalizeChinesePunctuation;
		chkTxtRemoveBlank.Checked = options.TxtRemoveExtraBlankLines;
		UpdateFieldState();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void SaveAndClose()
	{
		options.SaveLocation = ((((ListControl)cboSaveLocation).SelectedIndex == 1) ? ConvertSaveLocation.CustomFolder : ConvertSaveLocation.SourceFolder);
		options.CustomOutputFolder = ((Control)txtCustomFolder).Text.Trim();
		options.SameNamePolicy = IndexToSameName(((ListControl)cboSameName).SelectedIndex);
		options.OpenFolderAfterConvert = chkOpenFolder.Checked;
		options.ImageExportMode = ((((ListControl)cboImageMode).SelectedIndex == 1) ? ImageExportMode.LongImage : ImageExportMode.SingleImages);
		options.ImagePageSelectionMode = IndexToPageMode(((ListControl)cboPageMode).SelectedIndex);
		options.ImagePageRange = ((Control)txtPageRange).Text.Trim();
		options.ImageSelectedPages = ((Control)txtSelectedPages).Text.Trim();
		options.ImageFormat = ((((ListControl)cboImageFormat).SelectedIndex == 1) ? ImageFileFormat.Jpg : ImageFileFormat.Png);
		options.ImageDpi = (int)numDpi.Value;
		options.DocxMode = ((((ListControl)cboDocxMode).SelectedIndex == 1) ? DocxConvertMode.ReplaceCurrentDocument : DocxConvertMode.SaveAsNewFile);
		options.PdfToWordEngine = ((((ListControl)cboPdfEngine).SelectedIndex == 1) ? PdfToWordEngine.HostImport : PdfToWordEngine.Local);
		options.PdfNormalizeChinesePunctuation = chkPdfNormalizePunctuation.Checked;
		options.TxtRemoveExtraBlankLines = chkTxtRemoveBlank.Checked;
		if (options.SaveLocation == ConvertSaveLocation.CustomFolder && (string.IsNullOrWhiteSpace(options.CustomOutputFolder) || !Directory.Exists(options.CustomOutputFolder)))
		{
			MessageBox.Show("自定义保存路径不存在，请重新选择。", "提示", (MessageBoxButtons)0, (MessageBoxIcon)64);
			return;
		}
		if (options.ImagePageSelectionMode == PageSelectionMode.Range && string.IsNullOrWhiteSpace(options.ImagePageRange))
		{
			MessageBox.Show("选择范围页时，请填写页码范围，例如 2-8。", "提示", (MessageBoxButtons)0, (MessageBoxIcon)64);
			return;
		}
		if (options.ImagePageSelectionMode == PageSelectionMode.Selected && string.IsNullOrWhiteSpace(options.ImageSelectedPages))
		{
			MessageBox.Show("选择指定页时，请填写页码，例如 1,3,9。", "提示", (MessageBoxButtons)0, (MessageBoxIcon)64);
			return;
		}
		ConvertSettingsService.Save(options);
		((Form)this).DialogResult = (DialogResult)1;
	}

	private void UpdateFieldState()
	{
		bool enabled = ((ListControl)cboSaveLocation).SelectedIndex == 1;
		((Control)txtCustomFolder).Enabled = enabled;
		((Control)btnBrowse).Enabled = enabled;
		int selectedIndex = ((ListControl)cboPageMode).SelectedIndex;
		((Control)txtPageRange).Enabled = selectedIndex == 1;
		((Control)txtSelectedPages).Enabled = selectedIndex == 2;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void ChooseFolder()
	{
		FolderBrowserDialog val = new FolderBrowserDialog();
		try
		{
			val.Description = "请选择一键转换保存位置";
			if (!string.IsNullOrWhiteSpace(((Control)txtCustomFolder).Text) && Directory.Exists(((Control)txtCustomFolder).Text))
			{
				val.SelectedPath = ((Control)txtCustomFolder).Text;
			}
			if ((int)((CommonDialog)val).ShowDialog((IWin32Window)(object)this) == 1)
			{
				((Control)txtCustomFolder).Text = val.SelectedPath;
			}
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	private static int SameNameToIndex(ConvertSameNamePolicy policy)
	{
		return policy switch
		{
			ConvertSameNamePolicy.AutoRename => 1, 
			ConvertSameNamePolicy.Overwrite => 2, 
			ConvertSameNamePolicy.Cancel => 3, 
			_ => 0, 
		};
	}

	private static ConvertSameNamePolicy IndexToSameName(int index)
	{
		return index switch
		{
			1 => ConvertSameNamePolicy.AutoRename, 
			3 => ConvertSameNamePolicy.Cancel, 
			2 => ConvertSameNamePolicy.Overwrite, 
			_ => ConvertSameNamePolicy.Ask, 
		};
	}

	private static int PageModeToIndex(PageSelectionMode mode)
	{
		return mode switch
		{
			PageSelectionMode.Range => 1, 
			PageSelectionMode.Selected => 2, 
			_ => 0, 
		};
	}

	private static PageSelectionMode IndexToPageMode(int index)
	{
		return index switch
		{
			1 => PageSelectionMode.Range, 
			2 => PageSelectionMode.Selected, 
			_ => PageSelectionMode.All, 
		};
	}
}
