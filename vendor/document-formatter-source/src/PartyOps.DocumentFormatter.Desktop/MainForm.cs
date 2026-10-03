using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DocumentRepository.Models.Standalone;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Hosting.Standalone;
using PartyOps.DocumentFormatter.Desktop.Services;

namespace PartyOps.DocumentFormatter.Desktop;

internal sealed class MainForm : Form
{
	private static readonly HashSet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		".docx", ".doc", ".wps", ".rtf", ".pdf"
	};

	private readonly StaWorkQueue workQueue = new StaWorkQueue();
	private readonly ListBox fileList = new ListBox();
	private readonly TextBox outputDirectoryText = new TextBox();
	private readonly ComboBox featureCombo = new ComboBox();
	private readonly ComboBox hostCombo = new ComboBox();
	private readonly CheckBox exportDocx = new CheckBox();
	private readonly CheckBox exportPdf = new CheckBox();
	private readonly CheckBox exportTxt = new CheckBox();
	private readonly Button startButton = new Button();
	private readonly Button hostProbeButton = new Button();
	private readonly Button cancelButton = new Button();
	private readonly ProgressBar progressBar = new ProgressBar();
	private readonly Label statusLabel = new Label();
	private readonly TextBox logText = new TextBox();
	private readonly Label conversionHint = new Label();
	private readonly List<Button> settingsButtons = new List<Button>();
	private CancellationTokenSource cancellation;
	private bool busy;

	public MainForm(string[] args)
	{
		Text = "partyops公文排版助手独立版 1.0.0";
		StartPosition = FormStartPosition.CenterScreen;
		MinimumSize = new Size(1040, 720);
		Size = new Size(1120, 790);
		Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
		BackColor = Color.FromArgb(246, 248, 251);
		AllowDrop = true;
		BuildInterface();
		BindChoices();
		WireEvents();
		outputDirectoryText.Text = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "输出");
		AddFiles(args ?? Array.Empty<string>());
		AppendLog("独立版已就绪。源文件始终保持不变，所有操作均在输出副本上完成。");
	}

	private void BuildInterface()
	{
		TableLayoutPanel root = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 5,
			Padding = new Padding(22, 18, 22, 18),
			BackColor = BackColor
		};
		root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
		root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
		root.RowStyles.Add(new RowStyle(SizeType.Absolute, 142));
		root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
		root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
		Controls.Add(root);

		Panel header = new Panel { Dock = DockStyle.Fill };
		Label title = new Label
		{
			AutoSize = true,
			Text = "partyops公文排版助手",
			Font = new Font("Microsoft YaHei UI", 21F, FontStyle.Bold),
			ForeColor = Color.FromArgb(24, 41, 72),
			Location = new Point(0, 1)
		};
		Label subtitle = new Label
		{
			AutoSize = true,
			Text = "独立软件 · 无需安装 WPS/Word 插件 · 拖入文件即可处理",
			ForeColor = Color.FromArgb(93, 107, 130),
			Location = new Point(4, 45)
		};
		header.Controls.Add(title);
		header.Controls.Add(subtitle);
		root.Controls.Add(header, 0, 0);

		GroupBox filesGroup = CreateGroup("1  添加待处理文件（支持拖放和批量）");
		TableLayoutPanel filesLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(10, 8, 10, 10) };
		filesLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		filesLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126));
		fileList.Dock = DockStyle.Fill;
		fileList.IntegralHeight = false;
		fileList.HorizontalScrollbar = true;
		fileList.AllowDrop = true;
		filesLayout.Controls.Add(fileList, 0, 0);
		FlowLayoutPanel fileButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(8, 0, 0, 0) };
		Button addButton = CreateSecondaryButton("添加文件");
		Button removeButton = CreateSecondaryButton("移除选中");
		Button clearButton = CreateSecondaryButton("清空列表");
		addButton.Click += (_, _) => ChooseFiles();
		removeButton.Click += (_, _) => RemoveSelectedFile();
		clearButton.Click += (_, _) => fileList.Items.Clear();
		fileButtons.Controls.Add(addButton);
		fileButtons.Controls.Add(removeButton);
		fileButtons.Controls.Add(clearButton);
		filesLayout.Controls.Add(fileButtons, 1, 0);
		filesGroup.Controls.Add(filesLayout);
		root.Controls.Add(filesGroup, 0, 1);

		GroupBox optionsGroup = CreateGroup("2  选择功能、文档引擎与输出");
		TableLayoutPanel options = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 3, Padding = new Padding(12, 8, 12, 10) };
		options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
		options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
		options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
		options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
		options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
		options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37));
		options.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
		options.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
		options.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
		options.Controls.Add(CreateFieldLabel("执行功能"), 0, 0);
		featureCombo.Dock = DockStyle.Fill;
		featureCombo.DropDownStyle = ComboBoxStyle.DropDownList;
		options.Controls.Add(featureCombo, 1, 0);
		options.Controls.Add(CreateFieldLabel("文档引擎"), 2, 0);
		hostCombo.Dock = DockStyle.Fill;
		hostCombo.DropDownStyle = ComboBoxStyle.DropDownList;
		options.Controls.Add(hostCombo, 3, 0);
		options.Controls.Add(CreateFieldLabel("快速导出"), 4, 0);
		FlowLayoutPanel exports = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
		exportDocx.Text = "DOCX";
		exportDocx.Checked = true;
		exportPdf.Text = "PDF";
		exportTxt.Text = "TXT";
		exports.Controls.Add(exportDocx);
		exports.Controls.Add(exportPdf);
		exports.Controls.Add(exportTxt);
		options.Controls.Add(exports, 5, 0);
		options.Controls.Add(CreateFieldLabel("输出目录"), 0, 1);
		outputDirectoryText.Dock = DockStyle.Fill;
		options.Controls.Add(outputDirectoryText, 1, 1);
		options.SetColumnSpan(outputDirectoryText, 4);
		Button browseOutput = CreateSecondaryButton("浏览...");
		browseOutput.Dock = DockStyle.Fill;
		browseOutput.Click += (_, _) => ChooseOutputDirectory();
		options.Controls.Add(browseOutput, 5, 1);
		conversionHint.AutoSize = false;
		conversionHint.Dock = DockStyle.Fill;
		conversionHint.TextAlign = ContentAlignment.MiddleLeft;
		conversionHint.ForeColor = Color.FromArgb(98, 108, 126);
		conversionHint.Text = "图片/表格/页码/网格由排版参数控制；分页图片、长图、清晰度和页码范围由“转换设置”控制。";
		options.Controls.Add(conversionHint, 0, 2);
		options.SetColumnSpan(conversionHint, 6);
		optionsGroup.Controls.Add(options);
		root.Controls.Add(optionsGroup, 0, 2);

		Panel actions = new Panel { Dock = DockStyle.Fill };
		FlowLayoutPanel settings = new FlowLayoutPanel { Dock = DockStyle.Left, Width = 690, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Padding = new Padding(0, 4, 0, 0) };
		settings.Controls.Add(CreateSettingsButton("排版参数", WpsComHostBridge.OpenFormatSettings));
		settings.Controls.Add(CreateSettingsButton("替换方案", WpsComHostBridge.OpenReplaceSettings));
		settings.Controls.Add(CreateSettingsButton("红头模板", WpsComHostBridge.OpenRedHeaderSettings));
		settings.Controls.Add(CreateSettingsButton("命名规则", WpsComHostBridge.OpenRenameSettings));
		settings.Controls.Add(CreateSettingsButton("转换设置", WpsComHostBridge.OpenConvertSettings));
		hostProbeButton.Text = "引擎自检";
		hostProbeButton.Width = 80;
		hostProbeButton.Height = 32;
		hostProbeButton.Margin = new Padding(3, 3, 3, 6);
		hostProbeButton.FlatStyle = FlatStyle.Flat;
		hostProbeButton.BackColor = Color.White;
		hostProbeButton.ForeColor = Color.FromArgb(45, 65, 96);
		hostProbeButton.Click += StartHostProbeAsync;
		settingsButtons.Add(hostProbeButton);
		settings.Controls.Add(hostProbeButton);
		actions.Controls.Add(settings);
		startButton.Text = "一键处理并导出";
		startButton.Size = new Size(174, 46);
		startButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
		startButton.Location = new Point(actions.Width - 278, 8);
		startButton.BackColor = Color.FromArgb(31, 99, 235);
		startButton.ForeColor = Color.White;
		startButton.FlatStyle = FlatStyle.Flat;
		startButton.FlatAppearance.BorderSize = 0;
		startButton.Font = new Font(Font, FontStyle.Bold);
		startButton.Click += StartProcessingAsync;
		cancelButton.Text = "取消";
		cancelButton.Size = new Size(82, 46);
		cancelButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
		cancelButton.Location = new Point(actions.Width - 92, 8);
		cancelButton.Enabled = false;
		cancelButton.Click += (_, _) => cancellation?.Cancel();
		actions.Controls.Add(startButton);
		actions.Controls.Add(cancelButton);
		actions.Resize += (_, _) =>
		{
			startButton.Left = actions.ClientSize.Width - 278;
			cancelButton.Left = actions.ClientSize.Width - 92;
		};
		root.Controls.Add(actions, 0, 3);

		GroupBox progressGroup = CreateGroup("3  处理进度与结果");
		TableLayoutPanel progressLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(12, 8, 12, 10) };
		progressLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		progressLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
		progressLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
		progressLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
		progressBar.Dock = DockStyle.Fill;
		progressBar.Style = ProgressBarStyle.Continuous;
		statusLabel.Text = "等待开始";
		statusLabel.Dock = DockStyle.Fill;
		statusLabel.TextAlign = ContentAlignment.MiddleRight;
		Button openOutput = CreateSecondaryButton("打开输出目录");
		openOutput.Click += (_, _) => OpenOutputDirectory();
		progressLayout.Controls.Add(progressBar, 0, 0);
		progressLayout.Controls.Add(statusLabel, 1, 0);
		logText.Dock = DockStyle.Fill;
		logText.Multiline = true;
		logText.ReadOnly = true;
		logText.ScrollBars = ScrollBars.Vertical;
		logText.BackColor = Color.White;
		progressLayout.Controls.Add(logText, 0, 1);
		progressLayout.SetColumnSpan(logText, 2);
		progressGroup.Controls.Add(progressLayout);
		progressGroup.Controls.Add(openOutput);
		openOutput.BringToFront();
		openOutput.Anchor = AnchorStyles.Top | AnchorStyles.Right;
		openOutput.Location = new Point(progressGroup.Width - 138, 16);
		progressGroup.Resize += (_, _) => openOutput.Left = progressGroup.ClientSize.Width - 138;
		root.Controls.Add(progressGroup, 0, 4);
	}

	private void BindChoices()
	{
		featureCombo.Items.AddRange(new object[]
		{
			new FeatureChoice("format", "一键排版", "_已排版"),
			new FeatureChoice("replace", "一键替换", "_已替换"),
			new FeatureChoice("redheader", "一键套红", "_已套红"),
			new FeatureChoice("rename", "一键命名", "_待命名"),
			new FeatureChoice("convert", "一键转换（含分页图片/长图）", "_待转换"),
			new FeatureChoice("pdf-to-word", "PDF 转 Word", "_已转换")
		});
		featureCombo.SelectedIndex = 0;
		hostCombo.Items.AddRange(new object[]
		{
			new HostChoice(OfficeHostPreference.Auto, "自动选择（推荐）"),
			new HostChoice(OfficeHostPreference.Wps, "WPS 文字"),
			new HostChoice(OfficeHostPreference.Word, "Microsoft Word")
		});
		hostCombo.SelectedIndex = 0;
	}

	private void WireEvents()
	{
		DragEnter += HandleDragEnter;
		DragDrop += HandleDragDrop;
		fileList.DragEnter += HandleDragEnter;
		fileList.DragDrop += HandleDragDrop;
		featureCombo.SelectedIndexChanged += (_, _) => UpdateFeatureUi();
		FormClosing += (_, eventArgs) =>
		{
			if (busy && MessageBox.Show("文档仍在处理中。现在关闭会在当前安全保存点后退出，是否继续？", "partyops公文排版助手", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
			{
				eventArgs.Cancel = true;
				return;
			}
			cancellation?.Cancel();
		};
		FormClosed += (_, _) => workQueue.Dispose();
	}

	private async void StartProcessingAsync(object sender, EventArgs eventArgs)
	{
		if (busy)
		{
			return;
		}
		try
		{
			StandaloneBatchRequest request = BuildRequest();
			Directory.CreateDirectory(request.OutputDirectory);
			SetBusy(true);
			progressBar.Value = 0;
			AppendLog("开始：" + request.FeatureDisplayName + "，共 " + request.SourcePaths.Count + " 个文件。输出目录：" + request.OutputDirectory);
			StandaloneBatchResult result = await workQueue.RunAsync(() => new StandaloneBatchProcessor().Execute(request, UpdateProgressFromWorker));
			ShowBatchResult(result);
		}
		catch (TaskCanceledException)
		{
			AppendLog("任务已取消。已完成的输出文件仍保留并已通过完整性校验。");
		}
		catch (Exception ex)
		{
			AppendLog("启动失败：" + ex.Message);
			MessageBox.Show(ex.Message, "无法开始处理", MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
		finally
		{
			SetBusy(false);
		}
	}

	private async void StartHostProbeAsync(object sender, EventArgs eventArgs)
	{
		if (busy)
		{
			return;
		}
		try
		{
			string sourcePath = fileList.Items.Cast<string>().FirstOrDefault(path => !string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase));
			if (string.IsNullOrWhiteSpace(sourcePath))
			{
				throw new InvalidOperationException("请先添加至少一个 DOCX、DOC、WPS 或 RTF 文件进行文档引擎自检。");
			}
			string output = (outputDirectoryText.Text ?? string.Empty).Trim();
			if (string.IsNullOrWhiteSpace(output))
			{
				throw new InvalidOperationException("请选择输出目录。");
			}
			HostChoice host = hostCombo.SelectedItem as HostChoice ?? throw new InvalidOperationException("请选择文档引擎。");
			cancellation?.Dispose();
			cancellation = new CancellationTokenSource();
			StandaloneHostProbeRequest request = new StandaloneHostProbeRequest
			{
				SourcePath = sourcePath,
				OutputDirectory = Path.GetFullPath(output),
				HostPreference = host.Value,
				CancellationToken = cancellation.Token
			};
			SetBusy(true);
			progressBar.Value = 0;
			AppendLog("开始文档引擎自检：只读打开源文件并另存校验副本，不执行业务功能。源文件：" + sourcePath);
			StandaloneJobResult result = await workQueue.RunAsync(() => new StandaloneBatchProcessor().ProbeHost(request, UpdateProgressFromWorker));
			progressBar.Value = result.Success ? 100 : progressBar.Value;
			statusLabel.Text = result.Success ? "自检通过" : result.Cancelled ? "自检取消" : "自检失败";
			AppendLog((result.Success ? "✓ " : result.Cancelled ? "— " : "✕ ") + result.Message);
			if (!string.IsNullOrWhiteSpace(result.HostDisplayName))
			{
				AppendLog("  文档引擎：" + result.HostDisplayName);
			}
			foreach (string outputPath in result.OutputPaths)
			{
				AppendLog("  自检副本：" + outputPath);
			}
			MessageBox.Show(result.Message, "文档引擎自检", MessageBoxButtons.OK, result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
		}
		catch (Exception ex)
		{
			AppendLog("文档引擎自检无法开始：" + ex.Message);
			MessageBox.Show(ex.Message, "文档引擎自检", MessageBoxButtons.OK, MessageBoxIcon.Warning);
		}
		finally
		{
			SetBusy(false);
		}
	}

	private StandaloneBatchRequest BuildRequest()
	{
		List<string> paths = fileList.Items.Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		if (paths.Count == 0)
		{
			throw new InvalidOperationException("请先添加至少一个 DOCX、DOC、WPS、RTF 或 PDF 文件。");
		}
		string output = (outputDirectoryText.Text ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(output))
		{
			throw new InvalidOperationException("请选择输出目录。");
		}
		FeatureChoice feature = featureCombo.SelectedItem as FeatureChoice ?? throw new InvalidOperationException("请选择执行功能。");
		HostChoice host = hostCombo.SelectedItem as HostChoice ?? throw new InvalidOperationException("请选择文档引擎。");
		bool directConvert = feature.Id == "convert" || feature.Id == "pdf-to-word";
		cancellation?.Dispose();
		cancellation = new CancellationTokenSource();
		return new StandaloneBatchRequest
		{
			SourcePaths = paths,
			OutputDirectory = Path.GetFullPath(output),
			FeatureId = feature.Id,
			FeatureDisplayName = feature.DisplayName,
			OutputSuffix = feature.OutputSuffix,
			HostPreference = host.Value,
			ExportDocx = directConvert || exportDocx.Checked,
			ExportPdf = !directConvert && exportPdf.Checked,
			ExportTxt = !directConvert && exportTxt.Checked,
			CancellationToken = cancellation.Token
		};
	}

	private void UpdateProgressFromWorker(ProcessingProgress progress)
	{
		if (IsDisposed || !IsHandleCreated)
		{
			return;
		}
		BeginInvoke((Action)delegate
		{
			progressBar.Value = Math.Max(progressBar.Minimum, Math.Min(progressBar.Maximum, progress.Percent));
			statusLabel.Text = progress.Completed + "/" + progress.Total;
			AppendLog(Path.GetFileName(progress.SourcePath) + "：" + progress.Message);
		});
	}

	private void ShowBatchResult(StandaloneBatchResult result)
	{
		progressBar.Value = result.FailureCount == 0 && result.CancelledCount == 0 ? 100 : progressBar.Value;
		statusLabel.Text = "成功 " + result.SuccessCount + " / 失败 " + result.FailureCount + " / 取消 " + result.CancelledCount;
		foreach (StandaloneJobResult job in result.Jobs)
		{
			AppendLog((job.Success ? "✓ " : job.Cancelled ? "— " : "✕ ") + Path.GetFileName(job.SourcePath) + "：" + job.Message);
			if (!string.IsNullOrWhiteSpace(job.HostDisplayName))
			{
				AppendLog("  文档引擎：" + job.HostDisplayName);
			}
			foreach (string output in job.OutputPaths)
			{
				AppendLog("  输出：" + output);
			}
		}
		MessageBoxIcon icon = result.FailureCount == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning;
		MessageBox.Show("处理完成。\r\n\r\n成功：" + result.SuccessCount + "\r\n失败：" + result.FailureCount + "\r\n取消：" + result.CancelledCount, "partyops公文排版助手独立版", MessageBoxButtons.OK, icon);
	}

	private void UpdateFeatureUi()
	{
		FeatureChoice feature = featureCombo.SelectedItem as FeatureChoice;
		bool directConvert = feature?.Id == "convert" || feature?.Id == "pdf-to-word";
		exportDocx.Enabled = !directConvert;
		exportPdf.Enabled = !directConvert;
		exportTxt.Enabled = !directConvert;
		conversionHint.Text = feature?.Id == "convert"
			? "一键转换将严格使用“转换设置”：DOCX/PDF/TXT、全部/连续/指定页、分页图片/长图、格式和清晰度均可配置。"
			: feature?.Id == "pdf-to-word"
				? "PDF 转 Word 使用本地恢复引擎，无需打开 WPS 插件；转换后生成经过结构校验的 DOCX。"
				: "图片、表格、页码、文档网格和多套模板由“排版参数”控制；可同时快速导出 DOCX、PDF、TXT。";
	}

	private void ChooseFiles()
	{
		using OpenFileDialog dialog = new OpenFileDialog
		{
			Title = "选择待处理文件",
			Filter = "支持的文档|*.docx;*.doc;*.wps;*.rtf;*.pdf|Word/WPS 文档|*.docx;*.doc;*.wps;*.rtf|PDF 文件|*.pdf|所有文件|*.*",
			Multiselect = true,
			CheckFileExists = true
		};
		if (dialog.ShowDialog(this) == DialogResult.OK)
		{
			AddFiles(dialog.FileNames);
		}
	}

	private void ChooseOutputDirectory()
	{
		using FolderBrowserDialog dialog = new FolderBrowserDialog
		{
			Description = "选择处理结果保存目录",
			SelectedPath = Directory.Exists(outputDirectoryText.Text) ? outputDirectoryText.Text : AppDomain.CurrentDomain.BaseDirectory,
			ShowNewFolderButton = true
		};
		if (dialog.ShowDialog(this) == DialogResult.OK)
		{
			outputDirectoryText.Text = dialog.SelectedPath;
		}
	}

	private void AddFiles(IEnumerable<string> paths)
	{
		if (paths == null)
		{
			return;
		}
		HashSet<string> existing = new HashSet<string>(fileList.Items.Cast<string>(), StringComparer.OrdinalIgnoreCase);
		foreach (string path in paths)
		{
			if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || !SupportedExtensions.Contains(Path.GetExtension(path)))
			{
				continue;
			}
			string fullPath = Path.GetFullPath(path);
			if (existing.Add(fullPath))
			{
				fileList.Items.Add(fullPath);
			}
		}
	}

	private void RemoveSelectedFile()
	{
		for (int index = fileList.SelectedIndices.Count - 1; index >= 0; index--)
		{
			fileList.Items.RemoveAt(fileList.SelectedIndices[index]);
		}
	}

	private void HandleDragEnter(object sender, DragEventArgs eventArgs)
	{
		eventArgs.Effect = eventArgs.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
	}

	private void HandleDragDrop(object sender, DragEventArgs eventArgs)
	{
		AddFiles(eventArgs.Data?.GetData(DataFormats.FileDrop) as string[]);
	}

	private void OpenOutputDirectory()
	{
		try
		{
			string path = Path.GetFullPath(outputDirectoryText.Text.Trim());
			Directory.CreateDirectory(path);
			Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
		}
		catch (Exception ex)
		{
			MessageBox.Show(ex.Message, "无法打开输出目录", MessageBoxButtons.OK, MessageBoxIcon.Warning);
		}
	}

	private void SetBusy(bool value)
	{
		busy = value;
		startButton.Enabled = !value;
		cancelButton.Enabled = value;
		featureCombo.Enabled = !value;
		hostCombo.Enabled = !value;
		foreach (Button button in settingsButtons)
		{
			button.Enabled = !value;
		}
		fileList.Enabled = !value;
		outputDirectoryText.Enabled = !value;
		if (!value)
		{
			cancellation?.Dispose();
			cancellation = null;
		}
	}

	private void AppendLog(string message)
	{
		if (string.IsNullOrWhiteSpace(message))
		{
			return;
		}
		logText.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message + Environment.NewLine);
		logText.SelectionStart = logText.TextLength;
		logText.ScrollToCaret();
	}

	private Button CreateSettingsButton(string text, Action action)
	{
		Button button = CreateSecondaryButton(text);
		button.Width = 80;
		button.Click += (_, _) =>
		{
			try
			{
				action();
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message, text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
		};
		settingsButtons.Add(button);
		return button;
	}

	private static GroupBox CreateGroup(string title)
	{
		return new GroupBox
		{
			Text = title,
			Dock = DockStyle.Fill,
			BackColor = Color.White,
			ForeColor = Color.FromArgb(35, 49, 75),
			Padding = new Padding(8)
		};
	}

	private static Label CreateFieldLabel(string text)
	{
		return new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(75, 88, 110) };
	}

	private static Button CreateSecondaryButton(string text)
	{
		return new Button
		{
			Text = text,
			Width = 108,
			Height = 32,
			Margin = new Padding(3, 3, 3, 6),
			FlatStyle = FlatStyle.Flat,
			BackColor = Color.White,
			ForeColor = Color.FromArgb(45, 65, 96)
		};
	}
}
