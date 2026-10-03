using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using DocumentRepository;
using DocumentRepository.Models;
using DocumentRepository.Models.Conversion;
using DocumentRepository.Models.RedHeader;
using DocumentRepository.Services.Conversion;
using DocumentRepository.Services.RedHeader;
using DocumentRepository.Services.Replace;

namespace PartyOps.DocumentFormatter.Host;

/// <summary>
/// 将 PartyOps 页面参数临时映射到原排版源码的配置服务。
/// 业务规则仍由 FeatureTaskExecutor 执行；本类只完成与原设置窗口相同的配置写入，
/// 并在任务结束后恢复用户原有配置。命名互斥锁防止两个 PartyOps 进程串用设置。
/// </summary>
internal sealed class SourceOptionScope : IDisposable
{
    private const string MutexName = @"Local\PartyOps.DocumentFormatter.SourceOptionScope.v1";
    private readonly Mutex mutex;
    private readonly List<Action> restoreActions = new List<Action>();
    private readonly List<ConfigurationFileSnapshot> configurationSnapshots = new List<ConfigurationFileSnapshot>();
    private bool ownsMutex;
    private bool disposed;

    private SourceOptionScope()
    {
        mutex = new Mutex(false, MutexName);
    }

    public static SourceOptionScope Apply(string featureId, IDictionary<string, object> options)
    {
        SourceOptionScope scope = new SourceOptionScope();
        try
        {
            scope.Acquire();
            scope.CaptureConfigurationFiles();
            scope.ApplyFeature((featureId ?? string.Empty).Trim().ToLowerInvariant(), options ?? new Dictionary<string, object>());
            return scope;
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    private void CaptureConfigurationFiles()
    {
        foreach (string path in new[]
        {
            ConfigManager.GetStoreDescriptor().StorePath,
            ReplacePlanService.GetStoreDescriptor().StorePath,
            RedHeaderTemplateService.GetStoreDescriptor().StorePath,
            RenameRuleManager.GetStoreDescriptor().StorePath,
            ConvertSettingsService.GetStoreDescriptor().StorePath
        }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            configurationSnapshots.Add(ConfigurationFileSnapshot.Capture(path));
        }
    }

    private void Acquire()
    {
        try
        {
            ownsMutex = mutex.WaitOne(TimeSpan.FromSeconds(30));
        }
        catch (AbandonedMutexException)
        {
            ownsMutex = true;
        }
        if (!ownsMutex)
        {
            throw new InvalidOperationException("另一个 PartyOps 公文任务仍在使用原排版设置，请稍后重试。");
        }
    }

    private void ApplyFeature(string featureId, IDictionary<string, object> options)
    {
        switch (featureId)
        {
            case "format":
                ApplyFormat(options);
                return;
            case "replace":
                ApplyReplace(options);
                return;
            case "redheader":
                ApplyRedHeader(options);
                return;
            case "rename":
                ApplyRename(options);
                return;
            case "convert":
            case "pdf-to-word":
                ApplyConvert(options, featureId == "pdf-to-word");
                return;
            default:
                throw new InvalidOperationException("不支持的功能标识。");
        }
    }

    private void ApplyFormat(IDictionary<string, object> options)
    {
        int previousIndex = ConfigManager.CurrentTemplateIndex;
        restoreActions.Add(() => ConfigManager.SwitchTemplate(previousIndex));
        string requested = Text(options, "template", "系统默认");
        string[] names = ConfigManager.TemplateNames;
        int index = IsSystemDefaultAlias(requested)
            ? 0
            : Array.FindIndex(names, name => string.Equals(name, requested, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            throw new InvalidOperationException("原排版源码中不存在排版模板：" + requested + "。");
        }
        ConfigManager.SwitchTemplate(index);
    }

    private static bool IsSystemDefaultAlias(string value)
    {
        string normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0
            || string.Equals(normalized, "系统默认", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "GB/T 9704-2012", StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyReplace(IDictionary<string, object> options)
    {
        ReplacePlanSet previous = ReplacePlanService.Load();
        restoreActions.Add(() => ReplacePlanService.Save(previous));
        List<IDictionary<string, object>> incoming = ObjectList(options, "rules");
        if (incoming.Count == 0 || incoming.Count > 100)
        {
            throw new InvalidOperationException("替换方案必须包含 1—100 条原工具规则。");
        }

        ReplacePlan plan = new ReplacePlan
        {
            Id = "partyops_embedded_request",
            Name = Text(options, "plan_name", "PartyOps 内嵌方案"),
            Rules = incoming.Select((rule, index) => BuildReplaceRule(rule, index)).ToList()
        };
        ReplacePlanService.Save(new ReplacePlanSet
        {
            SchemaVersion = 4,
            ActivePlanId = plan.Id,
            Plans = new List<ReplacePlan> { plan }
        });
    }

    private static ReplaceRule BuildReplaceRule(IDictionary<string, object> source, int index)
    {
        string mode = Text(source, "mode", "text").ToLowerInvariant();
        string find = Text(source, "find", string.Empty);
        ReplaceRule rule = new ReplaceRule
        {
            Name = "PartyOps 规则 " + (index + 1).ToString(CultureInfo.InvariantCulture),
            Enabled = true,
            FindText = mode == "format" ? string.Empty : find,
            ReplaceText = Text(source, "replace", string.Empty),
            UseRegex = mode == "regex",
            UseWildcard = mode == "wildcard",
            FormatOnly = mode == "format",
            FindFormat = new ReplaceFormatCondition(),
            ReplaceFormat = new ReplaceFormatTarget()
        };
        if (mode != "text" && mode != "regex" && mode != "wildcard" && mode != "format")
        {
            throw new InvalidOperationException("替换规则模式无效：" + mode + "。");
        }
        if (mode == "format")
        {
            if (string.IsNullOrWhiteSpace(find))
            {
                throw new InvalidOperationException("格式替换必须填写要匹配的原字体名称。");
            }
            rule.FindFormat.FontName = find.Trim();
            rule.ReplaceFormat.FontName = Text(source, "font_name", string.Empty);
            object size = Value(source, "font_size");
            rule.ReplaceFormat.SizeText = size == null ? string.Empty : Convert.ToString(size, CultureInfo.InvariantCulture);
            rule.ReplaceFormat.Alignment = MapAlignment(Text(source, "alignment", string.Empty));
            if (rule.ReplaceFormat.IsEmpty)
            {
                throw new InvalidOperationException("格式替换必须至少指定一项目标格式。");
            }
        }
        else if (string.IsNullOrWhiteSpace(find))
        {
            throw new InvalidOperationException("文字、正则或通配符替换的查找内容不能为空。");
        }
        return rule;
    }

    private static string MapAlignment(string value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "left" => "左对齐",
            "center" => "居中",
            "right" => "右对齐",
            "justify" => "两端对齐",
            _ => string.Empty
        };
    }

    private void ApplyRedHeader(IDictionary<string, object> options)
    {
        RedHeaderTemplateSet previous = RedHeaderTemplateService.Load();
        restoreActions.Add(() => RedHeaderTemplateService.Save(previous));
        string type = Text(options, "document_type", "down").ToLowerInvariant();
        RedHeaderTemplateSet defaults = RedHeaderTemplateService.CreateDefaults();
        RedHeaderTemplate template = defaults.Templates.FirstOrDefault(item => item.Id == type);
        if (template == null)
        {
            throw new InvalidOperationException("公文类型只能选择下行文、上行文或便函。");
        }
        template = RedHeaderTemplateService.Clone(template);
        template.HeaderText = Text(options, "agency", template.HeaderText);
        template.DocumentNumberText = Text(options, "document_number", template.DocumentNumberText);
        string signatory = Text(options, "signatory", string.Empty);
        if (type == "up" && !string.IsNullOrWhiteSpace(signatory))
        {
            template.DocumentNumberText = template.DocumentNumberText.Trim() + "                 签发人：" + signatory.Trim();
        }
        string imprint = Text(options, "imprint", template.ImprintOffice);
        template.ImprintOffice = imprint;
        template.ImprintEnabled = type != "letter" && !string.IsNullOrWhiteSpace(imprint);
        ApplyTopMarks(template.TopMarks, options);
        RedHeaderTemplateService.Save(new RedHeaderTemplateSet
        {
            ActiveTemplateId = template.Id,
            Templates = new List<RedHeaderTemplate> { template }
        });
    }

    private static void ApplyTopMarks(RedHeaderTopMarkOptions marks, IDictionary<string, object> options)
    {
        string copyNumber = Text(options, "copy_number", string.Empty).Trim();
        marks.CopyNumberEnabled = copyNumber.Length > 0;
        marks.CopyNumber = copyNumber;
        string security = Text(options, "security", string.Empty).Trim();
        marks.SecurityLevel = "无";
        marks.ConfidentialityPeriod = string.Empty;
        foreach (string level in new[] { "绝密", "机密", "秘密" })
        {
            if (!security.StartsWith(level, StringComparison.Ordinal))
            {
                continue;
            }
            marks.SecurityLevel = level;
            marks.ConfidentialityPeriod = security.Substring(level.Length).Trim(' ', '★', '*');
            break;
        }
        if (security.Length > 0 && marks.SecurityLevel == "无")
        {
            throw new InvalidOperationException("密级必须使用秘密、机密或绝密，可在其后填写保密期限。");
        }
        string urgency = Text(options, "urgency", string.Empty).Trim();
        if (urgency.Length == 0)
        {
            urgency = "无";
        }
        if (urgency != "无" && urgency != "加急" && urgency != "特急")
        {
            throw new InvalidOperationException("紧急程度必须是加急或特急。");
        }
        marks.UrgencyLevel = urgency;
    }

    private void ApplyRename(IDictionary<string, object> options)
    {
        RenameRuleSet previous = RenameRuleManager.Load();
        restoreActions.Add(() => RenameRuleManager.Save(previous));
        List<string> parts = StringList(options, "parts");
        if (parts.Count == 0)
        {
            throw new InvalidOperationException("命名规则至少需要一个内容部件。");
        }
        string separator = Text(options, "separator", string.Empty);
        if (separator.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidOperationException("命名分隔符包含文件名禁用字符。");
        }
        List<RenameRulePart> mapped = new List<RenameRulePart>();
        for (int index = 0; index < parts.Count; index++)
        {
            if (index > 0 && separator.Length > 0)
            {
                mapped.Add(new RenameRulePart("custom", separator));
            }
            mapped.Add(MapRenamePart(parts[index], options));
        }
        RenameRule rule = new RenameRule
        {
            Id = "partyops_embedded_request",
            Name = "PartyOps 内嵌规则",
            RenameMode = "online",
            SavePathMode = "source",
            Parts = mapped,
            RotateWords = NormalizeRotateWords(Text(options, "rotation_words", string.Empty))
        };
        if (!RenameRuleManager.ValidateRule(rule, out string validationError))
        {
            throw new InvalidOperationException(validationError);
        }
        RenameRuleManager.Save(new RenameRuleSet
        {
            ActiveRuleId = rule.Id,
            Rules = new List<RenameRule> { rule }
        });
    }

    private static RenameRulePart MapRenamePart(string part, IDictionary<string, object> options)
    {
        return (part ?? string.Empty).Trim() switch
        {
            "title" => new RenameRulePart("mainTitle"),
            "mainTitle" => new RenameRulePart("mainTitle"),
            "document_number" => new RenameRulePart("docNumber"),
            "docNumber" => new RenameRulePart("docNumber"),
            "subtitle" => new RenameRulePart("subtitle"),
            "date" => new RenameRulePart("date"),
            "rotation" => new RenameRulePart("rotate"),
            "rotate" => new RenameRulePart("rotate"),
            "custom" => new RenameRulePart("custom", Text(options, "custom_text", string.Empty)),
            _ => throw new InvalidOperationException("未知命名部件：" + part + "。")
        };
    }

    private static string NormalizeRotateWords(string value)
    {
        string[] words = (value ?? string.Empty)
            .Split(new[] { '|', '、', '，', ',', '；', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => item.Length > 0)
            .ToArray();
        return string.Join("、", words);
    }

    private void ApplyConvert(IDictionary<string, object> options, bool pdfToWord)
    {
        ConvertOptions previous = ConvertSettingsService.Load();
        restoreActions.Add(() => ConvertSettingsService.Save(previous));
        ConvertOptions runtime = Clone(previous);
        runtime.OpenFolderAfterConvert = false;
        runtime.DocxMode = DocxConvertMode.SaveAsNewFile;
        runtime.PdfToWordEngine = PdfToWordEngine.Local;
        runtime.PdfNormalizeChinesePunctuation = Boolean(options, "normalize_punctuation", runtime.PdfNormalizeChinesePunctuation);
        if (!pdfToWord)
        {
            string target = Text(options, "target_format", "pdf").ToLowerInvariant();
            runtime.SelectedFormat = target switch
            {
                "docx" => ConvertFormat.Docx,
                "pdf" => ConvertFormat.Pdf,
                "txt" => ConvertFormat.Txt,
                "png" => ConvertFormat.Image,
                "jpg" => ConvertFormat.Image,
                _ => throw new InvalidOperationException("转换格式只能选择 DOCX、PDF、TXT、PNG 或 JPG。")
            };
            runtime.ImageFormat = target == "jpg" ? ImageFileFormat.Jpg : ImageFileFormat.Png;
            runtime.ImageExportMode = Text(options, "image_mode", "pages") == "long"
                ? ImageExportMode.LongImage
                : ImageExportMode.SingleImages;
            ApplyPageSelection(runtime, Text(options, "page_selection", "all"));
            runtime.ImageDpi = Integer(options, "dpi", runtime.ImageDpi);
            runtime.SameNamePolicy = Text(options, "same_name_policy", "auto-rename") switch
            {
                "overwrite" => ConvertSameNamePolicy.Overwrite,
                "skip" => ConvertSameNamePolicy.Cancel,
                _ => ConvertSameNamePolicy.AutoRename
            };
        }
        ConvertSettingsService.Save(runtime);
    }

    private static void ApplyPageSelection(ConvertOptions options, string selection)
    {
        string value = (selection ?? "all").Trim();
        if (value.Length == 0 || string.Equals(value, "all", StringComparison.OrdinalIgnoreCase) || value == "全部")
        {
            options.ImagePageSelectionMode = PageSelectionMode.All;
            options.ImagePageRange = string.Empty;
            options.ImageSelectedPages = string.Empty;
            return;
        }
        if (value.Contains(",") || value.Contains("，"))
        {
            options.ImagePageSelectionMode = PageSelectionMode.Selected;
            options.ImageSelectedPages = value.Replace('，', ',');
            options.ImagePageRange = string.Empty;
            return;
        }
        options.ImagePageSelectionMode = PageSelectionMode.Range;
        options.ImagePageRange = value.Replace('至', '-');
        options.ImageSelectedPages = string.Empty;
    }

    private static ConvertOptions Clone(ConvertOptions source)
    {
        return new ConvertOptions
        {
            SelectedFormat = source.SelectedFormat,
            SaveLocation = source.SaveLocation,
            CustomOutputFolder = source.CustomOutputFolder,
            SameNamePolicy = source.SameNamePolicy,
            OpenFolderAfterConvert = source.OpenFolderAfterConvert,
            ImageExportMode = source.ImageExportMode,
            ImagePageSelectionMode = source.ImagePageSelectionMode,
            ImagePageRange = source.ImagePageRange,
            ImageSelectedPages = source.ImageSelectedPages,
            ImageFormat = source.ImageFormat,
            ImageDpi = source.ImageDpi,
            DocxMode = source.DocxMode,
            PdfToWordEngine = source.PdfToWordEngine,
            PdfNormalizeChinesePunctuation = source.PdfNormalizeChinesePunctuation,
            TxtRemoveExtraBlankLines = source.TxtRemoveExtraBlankLines
        };
    }

    private static object Value(IDictionary<string, object> values, string key)
    {
        return values.TryGetValue(key, out object value) ? value : null;
    }

    private static string Text(IDictionary<string, object> values, string key, string fallback)
    {
        object value = Value(values, key);
        return value == null ? fallback : Convert.ToString(value, CultureInfo.InvariantCulture) ?? fallback;
    }

    private static bool Boolean(IDictionary<string, object> values, string key, bool fallback)
    {
        object value = Value(values, key);
        if (value == null)
        {
            return fallback;
        }
        if (value is bool flag)
        {
            return flag;
        }
        return bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out bool parsed) ? parsed : fallback;
    }

    private static int Integer(IDictionary<string, object> values, string key, int fallback)
    {
        object value = Value(values, key);
        if (value == null)
        {
            return fallback;
        }
        return int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : fallback;
    }

    private static List<IDictionary<string, object>> ObjectList(IDictionary<string, object> values, string key)
    {
        object value = Value(values, key);
        if (!(value is IEnumerable enumerable) || value is string)
        {
            return new List<IDictionary<string, object>>();
        }
        List<IDictionary<string, object>> result = new List<IDictionary<string, object>>();
        foreach (object item in enumerable)
        {
            if (item is IDictionary<string, object> dictionary)
            {
                result.Add(dictionary);
            }
        }
        return result;
    }

    private static List<string> StringList(IDictionary<string, object> values, string key)
    {
        object value = Value(values, key);
        if (!(value is IEnumerable enumerable) || value is string)
        {
            return new List<string>();
        }
        List<string> result = new List<string>();
        foreach (object item in enumerable)
        {
            string text = Convert.ToString(item, CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(text))
            {
                result.Add(text.Trim());
            }
        }
        return result;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        Exception restoreError = null;
        for (int index = restoreActions.Count - 1; index >= 0; index--)
        {
            try
            {
                restoreActions[index]();
            }
            catch (Exception error)
            {
                restoreError = restoreError ?? error;
            }
        }
        for (int index = configurationSnapshots.Count - 1; index >= 0; index--)
        {
            try
            {
                configurationSnapshots[index].Restore();
            }
            catch (Exception error)
            {
                restoreError = restoreError ?? error;
            }
        }
        if (ownsMutex)
        {
            mutex.ReleaseMutex();
            ownsMutex = false;
        }
        mutex.Dispose();
        if (restoreError != null)
        {
            throw new InvalidOperationException("原排版设置恢复失败；为避免后续任务串用配置，本次结果不予交付。", restoreError);
        }
    }

    private sealed class ConfigurationFileSnapshot
    {
        private readonly string path;
        private readonly bool existed;
        private readonly byte[] content;

        private ConfigurationFileSnapshot(string path, bool existed, byte[] content)
        {
            this.path = path;
            this.existed = existed;
            this.content = content;
        }

        public static ConfigurationFileSnapshot Capture(string path)
        {
            string fullPath = Path.GetFullPath(path);
            return File.Exists(fullPath)
                ? new ConfigurationFileSnapshot(fullPath, true, File.ReadAllBytes(fullPath))
                : new ConfigurationFileSnapshot(fullPath, false, Array.Empty<byte>());
        }

        public void Restore()
        {
            if (!existed)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                return;
            }
            string directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException("原排版设置路径无效。");
            }
            Directory.CreateDirectory(directory);
            string temporary = path + ".partyops-restore-" + Guid.NewGuid().ToString("N");
            File.WriteAllBytes(temporary, content);
            try
            {
                if (File.Exists(path))
                {
                    File.Replace(temporary, path, null, true);
                }
                else
                {
                    File.Move(temporary, path);
                }
            }
            catch
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }
    }
}
