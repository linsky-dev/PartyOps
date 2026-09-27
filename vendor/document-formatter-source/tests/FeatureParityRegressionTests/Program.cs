using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using DocumentRepository;
using DocumentRepository.Models;
using DocumentRepository.Models.Conversion;
using DocumentRepository.Models.FormattingPlan;
using DocumentRepository.Models.RedHeader;
using DocumentRepository.Models.Replace;
using DocumentRepository.Services.Conversion;
using DocumentRepository.Services.Conversion.PdfToWord;
using DocumentRepository.Services.Configuration;
using DocumentRepository.Services.Features;
using DocumentRepository.Services.RedHeader;
using DocumentRepository.Services.Rename;
using DocumentRepository.Services.Replace;
using DocumentRepository.Services.UiText;

namespace FeatureParityRegressionTests;

internal static class Program
{
	private static int failures;
	private static int assertions;

	private static int Main()
	{
		Run("产品能力契约", VerifyCapabilityCatalog);
		Run("一键排版参数与范围", VerifyFormattingCapabilities);
		Run("一键替换模式", VerifyReplaceCapabilities);
		Run("一键套红模板", VerifyRedHeaderCapabilities);
		Run("一键命名规则", VerifyRenameCapabilities);
		Run("一键转换参数", VerifyConversionCapabilities);
		Run("本地 PDF 转 Word", VerifyLocalPdfToWord);
		Run("WPS 原生 COM 加载桥契约", VerifyWpsShimContract);
		Run("跨平台受控恢复目录", VerifyPortableControlledPaths);
		Run("独立桌面版生产契约", VerifyStandaloneDesktopContract);

		Console.WriteLine("功能对等回归：{0} 项断言，{1} 项失败。", assertions, failures);
		return failures == 0 ? 0 : 1;
	}

	private static void VerifyCapabilityCatalog()
	{
		var result = ProductCapabilityCatalog.Validate();
		AssertTrue(result.Success, "产品能力契约应有效：" + string.Join("；", result.Errors));
		var all = ProductCapabilityCatalog.GetAll();
		AssertEqual(25, all.Count, "产品能力数量变化时必须同步更新验收基线");
		AssertEqual(6, all.Select(item => item.FeatureId).Distinct(StringComparer.OrdinalIgnoreCase).Count(), "六个功能入口必须全部有能力映射");
		AssertTrue(all.Any(item => item.CapabilityId == "format.element-recognition"), "排版要素识别能力缺失");
		AssertTrue(all.Any(item => item.CapabilityId == "replace.batch-rules"), "批量替换能力缺失");
		AssertTrue(all.Any(item => item.CapabilityId == "redheader.document-types"), "三类套红模板能力缺失");
		AssertTrue(all.Any(item => item.CapabilityId == "rename.online"), "在线命名能力缺失");
		AssertTrue(all.Any(item => item.CapabilityId == "convert.image-modes"), "分页图片和长图能力缺失");
		AssertTrue(all.Any(item => item.CapabilityId == "pdf-to-word.layout-reconstruction"), "PDF 版面重建能力缺失");
	}

	private static void VerifyPortableControlledPaths()
	{
		string recoveryId = "portable-path-20260901";
		AssertEqual(Path.GetFullPath(Path.Combine(ApplicationDataPaths.RecoveryRoot, recoveryId)), ApplicationDataPaths.RecoveryDirectory(recoveryId), "主恢复目录必须使用当前平台的目录分隔符");
		AssertEqual(Path.GetFullPath(Path.Combine(ApplicationDataPaths.RecoveryFallbackRoot, recoveryId)), ApplicationDataPaths.RecoveryFallbackDirectory(recoveryId), "备用恢复目录必须使用当前平台的目录分隔符");
		AssertEqual(Path.GetFullPath(Path.Combine(ApplicationDataPaths.ReuseLogRoot, recoveryId)), ApplicationDataPaths.ReuseLogDirectory(recoveryId), "复用审计目录必须使用当前平台的目录分隔符");
		AssertThrows<ArgumentException>(() => ApplicationDataPaths.RecoveryDirectory("../escape"), "恢复标识越界必须被拒绝");
	}

	private static void VerifyFormattingCapabilities()
	{
		AssertTrue(UiTextRegistry.Get(UiTextKeys.RibbonSettings).TooltipEnabled, "界面文案注册表必须完成静态初始化并通过全部键校验");
		string settingsSourcePath = Path.Combine(FindProjectRoot(), "src", "PartyOps.DocumentFormatter.AddIn", "DocumentRepository", "SettingsForm.cs");
		string settingsSource = File.ReadAllText(settingsSourcePath, Encoding.UTF8);
		AssertTrue(new[] { "公文范文", "公文交流群", "写稿求助", "用户交流群", "版权所有", "CreateSideActionButton", "ShowImagePopup" }.All(token => !ContainsText(settingsSource, token)), "排版参数窗口不得保留已删除的推广按钮、版权联系方式或其事件内容");
		FormatConfig config = new FormatConfig();
		AssertEqual(3, Enum.GetValues(typeof(FormatExecutionScope)).Length, "排版执行范围数量错误");
		AssertTrue(Enum.IsDefined(typeof(FormatExecutionScope), FormatExecutionScope.FullDocument), "全文排版范围缺失");
		AssertTrue(Enum.IsDefined(typeof(FormatExecutionScope), FormatExecutionScope.NormalSelection), "选中排版范围缺失");
		AssertEqual(9, ConfigManager.MaxUserTemplateCount, "用户排版模板上限错误");
		AssertEqual("规范标题", config.MainTitle.RecognitionStyle, "主标题识别样式默认值错误");
		AssertEqual("一、XX", config.Level1.RecognitionStyle, "一级标题识别样式默认值错误");
		AssertEqual("（一）XX", config.Level2.RecognitionStyle, "二级标题识别样式默认值错误");
		AssertEqual("1.XX", config.Level3.RecognitionStyle, "三级标题识别样式默认值错误");
		AssertEqual("正文文本", config.Body.OutlineLevel, "正文样式默认值错误");
		AssertTrue(config.EnablePageNumbers, "页码默认配置应启用");
		AssertTrue(config.EnableAttachmentFormatting, "附件排版默认配置应启用");
		AssertNotNull(config.ImageOptions, "图片排版参数缺失");
		AssertNotNull(config.TableOptions, "表格排版参数缺失");
	}

	private static void VerifyReplaceCapabilities()
	{
		ReplaceRule normal = new ReplaceRule
		{
			Enabled = true,
			FindText = "旧称",
			ReplaceText = "新称"
		};
		string normalResult = ReplaceRegexService.ApplyToText(normal, "旧称与旧称", out int normalCount);
		AssertEqual("新称与新称", normalResult, "普通文字替换结果错误");
		AssertEqual(2, normalCount, "普通文字替换次数错误");

		ReplaceRule regex = new ReplaceRule
		{
			Enabled = true,
			UseRegex = true,
			FindText = "(\\d{4})年",
			ReplaceText = "$1年度"
		};
		string regexResult = ReplaceRegexService.ApplyToText(regex, "2026年计划", out int regexCount);
		AssertEqual("2026年度计划", regexResult, "正则捕获组替换结果错误");
		AssertEqual(1, regexCount, "正则替换次数错误");

		ReplaceRule wildcard = new ReplaceRule { UseWildcard = true, FindText = "第*条", ReplaceText = "条款" };
		AssertTrue(wildcard.UseWildcard && !wildcard.UseRegex, "通配符模式标志错误");

		ReplaceRule formatOnly = new ReplaceRule
		{
			FormatOnly = true,
			UseRegex = true,
			UseWildcard = true
		};
		ReplaceRuleNormalizer.Normalize(formatOnly);
		AssertFalse(formatOnly.UseRegex, "纯格式替换不应保留正则模式");
		AssertFalse(formatOnly.UseWildcard, "纯格式替换不应保留通配符模式");

		ReplaceRule conflict = new ReplaceRule
		{
			UseRegex = true,
			UseWildcard = true,
			FindText = "冲突"
		};
		AssertThrows<ReplaceOperationException>(() => ReplaceRuleNormalizer.Normalize(conflict), "正则与通配符同时启用必须被拒绝");
	}

	private static void VerifyRedHeaderCapabilities()
	{
		RedHeaderTemplateSet defaults = RedHeaderTemplateService.CreateDefaults();
		AssertEqual(3, defaults.Templates.Count, "内置红头模板数量错误");
		AssertSequenceEqual(new[] { "down", "up", "letter" }, defaults.Templates.Select(item => item.Id), "红头模板类型错误");
		foreach (RedHeaderTemplate template in defaults.Templates)
		{
			RedHeaderTemplateService.ValidateTemplate(template);
		}
		AssertTrue(defaults.Templates.Single(item => item.Id == "up").DocumentNumberText.Contains("签发人"), "上行文缺少签发人区域");
		RedHeaderTemplate letter = defaults.Templates.Single(item => item.Id == "letter");
		AssertFalse(letter.ImprintEnabled, "便函默认不应生成版记");
		AssertEqual("upperThickLowerThin", letter.RedLineStyle, "便函红线样式错误");
		AssertEqual("000008", RedHeaderTemplateValidator.NormalizeCopyNumber("8"), "份号补零错误");

		RedHeaderTemplate down = defaults.Templates.Single(item => item.Id == "down");
		down.TopMarks.CopyNumberEnabled = true;
		down.TopMarks.CopyNumber = "8";
		down.TopMarks.SecurityLevel = "机密";
		down.TopMarks.ConfidentialityPeriod = "10年";
		down.TopMarks.UrgencyLevel = "特急";
		RedHeaderTemplateService.ValidateTemplate(down);
		AssertEqual("机密", down.TopMarks.SecurityLevel, "密级参数未保留");
		AssertEqual("特急", down.TopMarks.UrgencyLevel, "紧急程度参数未保留");
		AssertTrue(down.ImprintEnabled && !string.IsNullOrWhiteSpace(down.ImprintOffice), "下行文版记参数缺失");
	}

	private static void VerifyRenameCapabilities()
	{
		RenameRuleSet defaults = RenameRuleManager.CreateDefaultSet();
		AssertEqual(3, defaults.Rules.Count, "内置命名规则数量错误");
		AssertTrue(defaults.Rules.All(rule => rule.RenameMode == "online"), "内置命名规则必须支持在线重命名");
		AssertTrue(defaults.Rules.Any(rule => rule.Parts.Any(part => part.Type == "docNumber")), "发文字号部件缺失");
		AssertTrue(defaults.Rules.Any(rule => rule.Parts.Any(part => part.Type == "date")), "日期部件缺失");

		RenameRule custom = new RenameRule
		{
			Name = "完整组合",
			RenameMode = "online",
			DateFormat = "yyyyMMdd",
			Parts = new List<RenameRulePart>
			{
				new RenameRulePart("mainTitle"),
				new RenameRulePart("docNumber"),
				new RenameRulePart("subTitle"),
				new RenameRulePart("date"),
				new RenameRulePart("custom", "归档")
			}
		};
		AssertTrue(RenameRuleManager.ValidateRule(custom, out string message), "完整组合命名规则应有效：" + message);
		AssertEqual("公文归档", RenameRuleService.SanitizeFileName(" 公文 / 归档 "), "文件名清洗错误");
		AssertEqual("2026.08.28", RenameRuleService.FormatDate(new DateTime(2026, 8, 28)), "日期格式化错误");
		AssertSequenceEqual(new[] { "甲", "乙", "丙" }, RenameRuleManager.ParseRotateWords("甲、乙、丙"), "轮替词解析错误");
	}

	private static void VerifyConversionCapabilities()
	{
		AssertSequenceEqual(new[] { ConvertFormat.Docx, ConvertFormat.Pdf, ConvertFormat.Image, ConvertFormat.Txt }, Enum.GetValues(typeof(ConvertFormat)).Cast<ConvertFormat>(), "文档转换格式错误");
		AssertSequenceEqual(new[] { ImageExportMode.SingleImages, ImageExportMode.LongImage }, Enum.GetValues(typeof(ImageExportMode)).Cast<ImageExportMode>(), "图片导出模式错误");
		AssertSequenceEqual(new[] { ImageFileFormat.Png, ImageFileFormat.Jpg }, Enum.GetValues(typeof(ImageFileFormat)).Cast<ImageFileFormat>(), "图片文件格式错误");
		AssertEqual(4, Enum.GetValues(typeof(ConvertSameNamePolicy)).Length, "同名文件策略数量错误");
		AssertEqual(2, Enum.GetValues(typeof(ConvertSaveLocation)).Length, "保存位置策略数量错误");

		ConvertOptions defaults = new ConvertOptions();
		AssertEqual(200, defaults.ImageDpi, "默认图片清晰度错误");
		AssertEqual(PdfToWordEngine.Local, defaults.PdfToWordEngine, "PDF 转 Word 默认应使用本地引擎");
		ConvertOptions lowDpi = ConvertSettingsService.Normalize(new ConvertOptions { ImageDpi = 20 });
		ConvertOptions highDpi = ConvertSettingsService.Normalize(new ConvertOptions { ImageDpi = 1200 });
		AssertEqual(72, lowDpi.ImageDpi, "图片清晰度下限错误");
		AssertEqual(600, highDpi.ImageDpi, "图片清晰度上限错误");

		AssertSequenceEqual(Enumerable.Range(1, 5), PageSelectionParser.Parse(PageSelectionMode.All, "", "", 5).Pages, "全部页面解析错误");
		AssertSequenceEqual(new[] { 2, 3, 4 }, PageSelectionParser.Parse(PageSelectionMode.Range, "2至4", "", 5).Pages, "连续页面范围解析错误");
		AssertSequenceEqual(new[] { 1, 3, 5 }, PageSelectionParser.Parse(PageSelectionMode.Selected, "", "1，3、5,3", 5).Pages, "指定页面解析或去重错误");
		AssertFalse(PageSelectionParser.Parse(PageSelectionMode.Range, "5-2", "", 5).Success, "逆序页面范围必须被拒绝");

		ImageConversionBudget single = ImageConversionBudgetService.Evaluate(
			new[] { new ImagePageDimensions { Width = 1200, Height = 1800 } },
			false,
			true);
		AssertTrue(single.Allowed, "常规分页图片预算应允许");
		ImageConversionBudget longImage = ImageConversionBudgetService.Evaluate(
			new[]
			{
				new ImagePageDimensions { Width = 1200, Height = 1800 },
				new ImagePageDimensions { Width = 1200, Height = 1800 }
			},
			true,
			true);
		AssertTrue(longImage.Allowed && longImage.IsLongImage, "常规长图预算应允许");
		AssertEqual(3600L, longImage.OutputHeight, "长图高度合并错误");
	}

	private static void VerifyLocalPdfToWord()
	{
		string testDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-artifacts");
		Directory.CreateDirectory(testDirectory);
		string pdfPath = Path.Combine(testDirectory, "local-pdf-to-word-smoke.pdf");
		try
		{
			File.WriteAllBytes(pdfPath, CreateMinimalTextPdf());
			Stopwatch stopwatch = Stopwatch.StartNew();
			using MemoryStream docx = new MemoryStream();
			LocalPdfToWordResult result = new LocalPdfToWordEngine().Convert(pdfPath, docx);
			stopwatch.Stop();

			AssertEqual(1, result.PageCount, "本地 PDF 页数错误");
			AssertTrue(result.MeaningfulCharacterCount >= LocalPdfToWordEngine.ScannedCharacterThreshold, "文本型 PDF 被误判为扫描件");
			AssertFalse(result.LikelyScanned, "文本型 PDF 不应标记为疑似扫描件");
			AssertTrue(docx.Length > 0, "本地 PDF 转 Word 没有生成 DOCX 数据");
			AssertTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(10), "单页本地转换耗时异常：" + stopwatch.Elapsed);

			using ZipArchive archive = new ZipArchive(docx, ZipArchiveMode.Read, true);
			AssertNotNull(archive.GetEntry("[Content_Types].xml"), "DOCX 内容类型清单缺失");
			AssertNotNull(archive.GetEntry("word/document.xml"), "DOCX 主文档内容缺失");
		}
		finally
		{
			if (File.Exists(pdfPath))
			{
				File.Delete(pdfPath);
			}
			if (Directory.Exists(testDirectory) && Directory.GetFileSystemEntries(testDirectory).Length == 0)
			{
				Directory.Delete(testDirectory);
			}
		}
	}

	private static void VerifyWpsShimContract()
	{
		string projectRoot = FindProjectRoot();
		string shimSourcePath = Path.Combine(projectRoot, "src", "PartyOps.DocumentFormatter.WpsShim", "WpsComAddIn.cs");
		string entryPointPath = Path.Combine(projectRoot, "src", "PartyOps.DocumentFormatter.WpsShim", "WpsComAddInEntryPoint.cs");
		string callbackInterfacePath = Path.Combine(projectRoot, "src", "PartyOps.DocumentFormatter.WpsShim", "Interop", "IRibbonCallbacks.cs");
		string registrationPath = Path.Combine(projectRoot, "tools", "Register-WpsComAddIn.ps1");
		string projectPath = Path.Combine(projectRoot, "src", "PartyOps.DocumentFormatter.WpsShim", "PartyOps.DocumentFormatter.WpsShim.csproj");
		AssertTrue(File.Exists(shimSourcePath), "WPS COM 加载桥源码缺失");
		AssertTrue(File.Exists(entryPointPath), "WPS COM 薄入口源码缺失");
		AssertTrue(File.Exists(callbackInterfacePath), "WPS Ribbon IDispatch 回调契约缺失");
		AssertTrue(File.Exists(registrationPath), "WPS COM 加载桥注册脚本缺失");
		AssertTrue(File.Exists(projectPath), "WPS COM 加载桥项目文件缺失");
		string shimSource = File.ReadAllText(shimSourcePath, Encoding.UTF8);
		string entryPoint = File.ReadAllText(entryPointPath, Encoding.UTF8);
		string callbackInterface = File.ReadAllText(callbackInterfacePath, Encoding.UTF8);
		string registration = File.ReadAllText(registrationPath, Encoding.UTF8);
		string project = File.ReadAllText(projectPath, Encoding.UTF8);
		AssertTrue(ContainsText(entryPoint, "7843E826-447C-484C-BB7E-EAA85A5BCC3F", true), "WPS COM CLSID 发生变化");
		AssertTrue(ContainsText(entryPoint, "PartyOps.DocumentFormatter.WpsAddIn"), "WPS COM ProgID 缺失");
		AssertTrue(ContainsText(entryPoint, "sealed class WpsComAddInEntryPoint : WpsComAddInCore, IDTExtensibility2, IRibbonExtensibility, IRibbonCallbacks"), "WPS COM 入口未在 COM 可见类上显式声明宿主/Ribbon 接口");
		AssertTrue(ContainsText(entryPoint, "ComDefaultInterface(typeof(IRibbonCallbacks))") && ContainsText(callbackInterface, "InterfaceIsIDispatch"), "WPS Ribbon 回调未通过默认 IDispatch 接口暴露");
		AssertTrue(ContainsText(callbackInterface, "OnRibbonLoad") && ContainsText(callbackInterface, "OnFormatSettings") && !ContainsText(callbackInterface, "OnAbout") && !ContainsText(shimSource, "btnAbout"), "WPS Ribbon 回调必须覆盖设置入口且不得再暴露关于入口");
		AssertTrue(ContainsText(shimSource, "[ComVisible(false)]") && ContainsText(shimSource, "class WpsComAddInCore"), "WPS COM 业务核心未与激活入口隔离");
		AssertTrue(ContainsText(shimSource, "IDTExtensibility2") && ContainsText(shimSource, "IRibbonExtensibility"), "WPS COM 宿主/Ribbon 契约缺失");
		AssertTrue(ContainsText(project, "<Reference Include=\"Extensibility\">") && ContainsText(project, "$(VsPublicAssembliesPath)\\extensibility.dll"), "WPS COM 加载桥未使用 VS 2022 官方 Extensibility PIA");
		foreach (string callback in new[] { "OnFormat", "OnReplace", "OnRedHeader", "OnRename", "OnConvert", "OnPdfToWord" })
		{
			AssertTrue(ContainsText(shimSource, callback), "WPS 主功能回调缺失：" + callback);
		}
		AssertTrue(ContainsText(registration, "Registry32") && ContainsText(registration, "Registry64"), "WPS 注册脚本未覆盖双注册表视图");
		AssertTrue(ContainsText(registration, "PartyOps.DocumentFormatter.Wps.WpsComAddInEntryPoint"), "WPS 注册脚本未指向薄 COM 入口");
		AssertTrue(ContainsText(registration, "RegAsm.exe") && ContainsText(registration, "/codebase") && ContainsText(registration, "LoadBehavior"), "WPS CLR/COM 注册关键项缺失");
		AssertTrue(ContainsText(registration, "AddinsWL") && ContainsText(registration, "Uninstall"), "WPS 白名单/卸载契约缺失");
		AssertTrue(ContainsText(registration, "PartyOps.DocumentFormatter.WpsShim") && ContainsText(registration, "Remove-MachineRegistryViewTree"), "WPS 旧 COM 身份机器级清理契约缺失");
		AssertTrue(ContainsText(project, "AssemblyName>PartyOps.DocumentFormatter.WpsShim"), "WPS COM 加载桥程序集名称缺失");
		AssertTrue(File.Exists(Path.Combine(projectRoot, "src", "PartyOps.DocumentFormatter.WpsShim", "WpsComHostBridgeAdapter.cs")), "WPS 延迟业务桥缺失");
		string adapter = File.ReadAllText(Path.Combine(projectRoot, "src", "PartyOps.DocumentFormatter.WpsShim", "WpsComHostBridgeAdapter.cs"), Encoding.UTF8);
		AssertTrue(ContainsText(adapter, "Assembly.LoadFrom") && ContainsText(adapter, "PartyOps.DocumentFormatter.AddIn.dll"), "WPS 加载桥未按宿主目录延迟加载主业务程序集");
	}

	private static void VerifyStandaloneDesktopContract()
	{
		string projectRoot = FindProjectRoot();
		string desktopRoot = Path.Combine(projectRoot, "src", "PartyOps.DocumentFormatter.Desktop");
		string projectPath = Path.Combine(desktopRoot, "PartyOps.DocumentFormatter.Desktop.csproj");
		string programPath = Path.Combine(desktopRoot, "Program.cs");
		string mainFormPath = Path.Combine(desktopRoot, "MainForm.cs");
		string queuePath = Path.Combine(desktopRoot, "Services", "StaWorkQueue.cs");
		string processorPath = Path.Combine(projectRoot, "src", "PartyOps.DocumentFormatter.AddIn", "DocumentRepository", "Services", "Hosting", "Standalone", "StandaloneBatchProcessor.cs");
		string sessionPath = Path.Combine(projectRoot, "src", "PartyOps.DocumentFormatter.AddIn", "DocumentRepository", "Services", "Hosting", "Standalone", "OfficeHostSession.cs");
		string filterPath = Path.Combine(projectRoot, "src", "PartyOps.DocumentFormatter.AddIn", "DocumentRepository", "Services", "Hosting", "Standalone", "ComBusyRetryMessageFilter.cs");
		string publishPath = Path.Combine(projectRoot, "tools", "Publish-Standalone.ps1");
		foreach (string path in new[] { projectPath, programPath, mainFormPath, queuePath, processorPath, sessionPath, filterPath, publishPath })
		{
			AssertTrue(File.Exists(path), "独立桌面版生产文件缺失：" + path);
		}

		string project = File.ReadAllText(projectPath, Encoding.UTF8);
		string program = File.ReadAllText(programPath, Encoding.UTF8);
		string mainForm = File.ReadAllText(mainFormPath, Encoding.UTF8);
		string queue = File.ReadAllText(queuePath, Encoding.UTF8);
		string processor = File.ReadAllText(processorPath, Encoding.UTF8);
		string session = File.ReadAllText(sessionPath, Encoding.UTF8);
		string filter = File.ReadAllText(filterPath, Encoding.UTF8);
		string solution = File.ReadAllText(Path.Combine(projectRoot, "PartyOps.DocumentFormatter.sln"), Encoding.UTF8);

		AssertTrue(ContainsText(project, "<OutputType>WinExe</OutputType>") && ContainsText(project, "<TargetFrameworkVersion>v4.8</TargetFrameworkVersion>"), "独立版必须是 .NET Framework 4.8 Windows 可执行程序");
		AssertTrue(ContainsText(program, "[STAThread]") && ContainsText(program, "Application.Run(new MainForm(args))"), "独立版 STA/主窗口入口缺失");
		foreach (string featureId in new[] { "format", "replace", "redheader", "rename", "convert", "pdf-to-word" })
		{
			AssertTrue(ContainsText(mainForm, "new FeatureChoice(\"" + featureId + "\""), "独立版功能入口缺失：" + featureId);
		}
		AssertTrue(ContainsText(mainForm, "AllowDrop = true") && ContainsText(mainForm, "Multiselect = true"), "独立版拖放/批处理入口缺失");
		AssertTrue(ContainsText(mainForm, "StartHostProbeAsync") && ContainsText(mainForm, "引擎自检"), "独立版真实宿主自检入口缺失");

		AssertTrue(ContainsText(processor, "SafeOutputTransaction.ProduceAndCommit") && ContainsText(processor, "TryDeleteOwnedOutput"), "独立版输出事务或失败清理缺失");
		AssertTrue(!ContainsText(processor, "EnsureFeatureAuthorized") && !ContainsText(processor, "CloudAuthManager") && !ContainsText(processor, "RecordSuccessfulFormatUsage"), "独立版不应再包含登录、会员或云授权入口");
		AssertTrue(ContainsText(processor, "FeatureExecutionOptions.NonInteractive"), "独立版非交互执行契约缺失");
		AssertTrue(!ContainsText(mainForm, "用户中心") && !ContainsText(mainForm, "OpenUserCenter") && !ContainsText(mainForm, "OpenAbout") && !ContainsText(mainForm, "CreateSettingsButton(\"关于\""), "独立版不应显示用户中心、登录或关于入口");
		AssertTrue(ContainsText(processor, "FeatureTaskExecutor.Execute") && ContainsText(processor, "NonInteractiveFeatureUiService.Instance"), "独立版未复用恢复后的统一业务执行器");
		AssertTrue(ContainsText(processor, "ComputeSha256") && ContainsText(processor, "源文件未改变"), "独立版源文件哈希保护契约缺失");
		AssertTrue(ContainsText(processor, "ValidateOutputWithRetry") && ContainsText(processor, "OutputFileIntegrityValidator.Validate"), "独立版 WPS 写盘后完整性校验缺失");
		int saveIndex = processor.IndexOf("document.Save();", StringComparison.Ordinal);
		int exportIndex = processor.IndexOf("ExportRequestedFormats(request, document, currentDocumentPath", saveIndex, StringComparison.Ordinal);
		int closeIndex = processor.IndexOf("host.CloseDocument(ref document, true);", saveIndex, StringComparison.Ordinal);
		int validateIndex = processor.IndexOf("ValidateOutputWithRetry(currentDocumentPath", saveIndex, StringComparison.Ordinal);
		AssertTrue(saveIndex >= 0 && exportIndex > saveIndex && closeIndex > exportIndex, "独立版必须先保存并导出，再带保存关闭 WPS 文档");
		AssertTrue(validateIndex > closeIndex, "独立版必须在 WPS 释放文档句柄后校验 DOCX，禁止对仍打开的压缩包做完整性校验");

		AssertTrue(ContainsText(session, "Activator.CreateInstance") && ContainsText(session, "Application.Documents"), "独立版 Word/WPS COM 启动和文档集合契约缺失");
		AssertTrue(ContainsText(session, "readOnlyValue = readOnly") && ContainsText(session, "addToRecentFiles = false"), "独立版只读打开/不写最近文档契约缺失");
		AssertTrue(ContainsText(session, "SaveAs2") && ContainsText(session, "wdFormatXMLDocument"), "独立版 DOCX 另存契约缺失");
		AssertTrue(ContainsText(session, "QuitOwnedApplication") && ContainsText(session, "wdDoNotSaveChanges"), "独立版宿主释放契约缺失");
		AssertTrue(ContainsText(session, "IsIsolatedApplication") && ContainsText(session, "existingDocumentCount > 0") && ContainsText(session, "拒绝接管"), "独立版防止接管用户已有文档会话的契约缺失");

		AssertTrue(ContainsText(queue, "ApartmentState.STA") && ContainsText(queue, "HostThreadRuntime.Initialize"), "独立版后台 STA 生命周期契约缺失");
		AssertTrue(ContainsText(queue, "ComBusyRetryMessageFilter.Register") && ContainsText(filter, "CoRegisterMessageFilter"), "独立版 COM 忙重试契约缺失");
		AssertTrue(ContainsText(solution, "PartyOps.DocumentFormatter.Desktop") && ContainsText(solution, "{7C306C60-85AF-4BF8-B539-C44CB813A759}"), "独立版项目未纳入恢复解决方案");
	}

	private static bool ContainsText(string text, string value, bool ignoreCase = false)
	{
		return text != null && value != null && text.IndexOf(value, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) >= 0;
	}

	private static string FindProjectRoot()
	{
		DirectoryInfo directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
		while (directory != null)
		{
			if (File.Exists(Path.Combine(directory.FullName, "PartyOps.DocumentFormatter.sln")))
			{
				return directory.FullName;
			}
			directory = directory.Parent;
		}
		throw new DirectoryNotFoundException("无法定位恢复工程根目录");
	}

	private static byte[] CreateMinimalTextPdf()
	{
		const string text = "Recovered source PDF conversion keeps paragraph text and document structure for validation.";
		string content = "BT /F1 12 Tf 72 720 Td (" + text + ") Tj ET";
		List<byte> bytes = new List<byte>();
		List<int> offsets = new List<int> { 0 };
		Append(bytes, "%PDF-1.4\n");
		AddObject(bytes, offsets, 1, "<< /Type /Catalog /Pages 2 0 R >>");
		AddObject(bytes, offsets, 2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
		AddObject(bytes, offsets, 3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>");
		AddObject(bytes, offsets, 4, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
		AddObject(bytes, offsets, 5, "<< /Length " + Encoding.ASCII.GetByteCount(content) + " >>\nstream\n" + content + "\nendstream");
		int xrefOffset = bytes.Count;
		Append(bytes, "xref\n0 6\n0000000000 65535 f \n");
		for (int i = 1; i <= 5; i++)
		{
			Append(bytes, offsets[i].ToString("0000000000") + " 00000 n \n");
		}
		Append(bytes, "trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n" + xrefOffset + "\n%%EOF\n");
		return bytes.ToArray();
	}

	private static void AddObject(List<byte> bytes, IList<int> offsets, int number, string body)
	{
		offsets.Add(bytes.Count);
		Append(bytes, number + " 0 obj\n" + body + "\nendobj\n");
	}

	private static void Append(ICollection<byte> bytes, string text)
	{
		foreach (byte value in Encoding.ASCII.GetBytes(text))
		{
			bytes.Add(value);
		}
	}

	private static void Run(string name, Action test)
	{
		try
		{
			test();
			Console.WriteLine("[通过] " + name);
		}
		catch (Exception exception)
		{
			failures++;
			Console.Error.WriteLine("[失败] {0}：{1}", name, exception);
		}
	}

	private static void AssertTrue(bool value, string message)
	{
		assertions++;
		if (!value)
		{
			throw new InvalidOperationException(message);
		}
	}

	private static void AssertFalse(bool value, string message)
	{
		AssertTrue(!value, message);
	}

	private static void AssertNotNull(object value, string message)
	{
		AssertTrue(value != null, message);
	}

	private static void AssertEqual<T>(T expected, T actual, string message)
	{
		assertions++;
		if (!Equals(expected, actual))
		{
			throw new InvalidOperationException(message + "；期望=" + expected + "，实际=" + actual);
		}
	}

	private static void AssertSequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string message)
	{
		assertions++;
		if (!expected.SequenceEqual(actual))
		{
			throw new InvalidOperationException(message + "；期望=" + string.Join(",", expected) + "，实际=" + string.Join(",", actual));
		}
	}

	private static void AssertThrows<TException>(Action action, string message) where TException : Exception
	{
		assertions++;
		try
		{
			action();
		}
		catch (TException)
		{
			return;
		}
		throw new InvalidOperationException(message);
	}
}
