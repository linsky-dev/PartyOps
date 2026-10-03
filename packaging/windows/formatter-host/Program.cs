using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using DocumentRepository.Models.Standalone;
using DocumentRepository.Services.Configuration;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Hosting.Standalone;

namespace PartyOps.DocumentFormatter.Host;

/// <summary>
/// PartyOps 内嵌页面与原排版源码之间的无窗口宿主。
/// 本程序不实现任何排版规则，只把任务交给 StandaloneBatchProcessor，避免规则分叉。
/// </summary>
internal static class Program
{
    private const int ExitInvalidRequest = 2;
    private const int ExitProcessingFailed = 3;
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer
    {
        MaxJsonLength = 1024 * 1024,
        RecursionLimit = 32
    };

    [STAThread]
    private static int Main(string[] args)
    {
        string responsePath = null;
        try
        {
            if (args.Length == 2
                && string.Equals(args[0], "--self-test", StringComparison.OrdinalIgnoreCase))
            {
                responsePath = Path.GetFullPath(args[1]);
                WriteSelfTest(responsePath);
                return 0;
            }
            Dictionary<string, string> arguments = ParseArguments(args);
            string requestPath = RequirePath(arguments, "request");
            responsePath = RequirePath(arguments, "response");
            string progressPath = RequirePath(arguments, "progress");
            string cancelPath = RequirePath(arguments, "cancel");
            AssertPrivateControlFiles(requestPath, responsePath, progressPath, cancelPath);

            HostRequest payload = Json.Deserialize<HostRequest>(File.ReadAllText(requestPath, Encoding.UTF8));
            Validate(payload);

            using CancellationTokenSource cancellation = new CancellationTokenSource();
            using Timer cancelMonitor = new Timer(
                _ =>
                {
                    if (File.Exists(cancelPath))
                    {
                        cancellation.Cancel();
                    }
                },
                null,
                0,
                100);
            using IDisposable messageFilter = InitializeHostRuntime();
            StandaloneBatchResult result;
            using (SourceOptionScope optionScope = SourceOptionScope.Apply(payload.feature_id, payload.options))
            {
                StandaloneBatchRequest request = ToBatchRequest(payload, cancellation.Token);
                result = ExecuteWithWpsPreferredFallback(
                    request,
                    string.Equals(payload.host_preference, "wps-preferred", StringComparison.OrdinalIgnoreCase),
                    progress => AppendProgress(progressPath, progress));
            }
            WriteResponse(responsePath, BuildResponse(result));
            return result.FailureCount == 0 && result.CancelledCount == 0 ? 0 : ExitProcessingFailed;
        }
        catch (Exception error)
        {
            TryWriteFatalResponse(responsePath, error);
            return ExitInvalidRequest;
        }
    }

    private static void WriteSelfTest(string outputPath)
    {
        // 通过真实类型引用强制加载原排版规则程序集；不启动 WPS/Word，也不
        // 读取或修改用户配置。平台安装自检据此发现缺失/错架构程序集。
        Type processor = typeof(StandaloneBatchProcessor);
        string rulesAssembly = processor.Assembly.Location;
        bool nativeBundle = PortableWpsComBridge.IsPortablePlatform
            && string.Equals(
                processor.Assembly.GetName().Name,
                "PartyOps.DocumentFormatter.AddIn",
                StringComparison.Ordinal);
        if ((!nativeBundle && string.IsNullOrWhiteSpace(rulesAssembly))
            || (!nativeBundle && !File.Exists(rulesAssembly)))
        {
            throw new FileNotFoundException("原排版规则程序集未加载。", rulesAssembly);
        }
        string directory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException("宿主自检输出目录不存在。");
        }
        WriteResponse(outputPath, new Dictionary<string, object>
        {
            ["schema"] = 1,
            ["passed"] = true,
            ["engine"] = "source-standalone-batch-processor",
            ["source_project"] = "PartyOps.DocumentFormatter.AddIn",
            ["features"] = new[] { "format", "replace", "redheader", "rename", "convert", "pdf-to-word" },
            ["rules_assembly"] = nativeBundle
                ? "PartyOps.DocumentFormatter.AddIn.dll (native bundle)"
                : Path.GetFileName(rulesAssembly)
        });
    }

    private static IDisposable InitializeHostRuntime()
    {
        HostThreadRuntime.Initialize("PartyOps.EmbeddedFormatterHost");
        ConfigurationMigrationService.Ensure440Migration();
        if (PortableWpsComBridge.IsPortablePlatform)
        {
            PortableWpsComBridge.RegisterIfRequired();
            return NoopDisposable.Instance;
        }
        return ComBusyRetryMessageFilter.Register();
    }

    private sealed class NoopDisposable : IDisposable
    {
        internal static readonly NoopDisposable Instance = new NoopDisposable();

        public void Dispose()
        {
        }
    }

    private static StandaloneBatchResult ExecuteWithWpsPreferredFallback(
        StandaloneBatchRequest request,
        bool allowWordFallback,
        Action<ProcessingProgress> report)
    {
        if (request.HostPreference != OfficeHostPreference.Wps || !allowWordFallback)
        {
            return new StandaloneBatchProcessor().Execute(request, report);
        }

        StandaloneBatchResult first = new StandaloneBatchProcessor().Execute(request, report);
        bool hostUnavailable = first.Jobs.Count > 0
            && first.Jobs.All(job => !job.Success && !job.Cancelled)
            && first.Jobs.All(job => (job.Message ?? string.Empty).Contains("未能启动可用的 Word/WPS 文档引擎"));
        if (!hostUnavailable)
        {
            return first;
        }

        request.HostPreference = OfficeHostPreference.Word;
        return new StandaloneBatchProcessor().Execute(request, report);
    }

    private static StandaloneBatchRequest ToBatchRequest(HostRequest payload, CancellationToken token)
    {
        string featureId = payload.feature_id.Trim().ToLowerInvariant();
        return new StandaloneBatchRequest
        {
            SourcePaths = payload.source_paths.Select(Path.GetFullPath).ToArray(),
            OutputDirectory = Path.GetFullPath(payload.output_directory),
            FeatureId = featureId,
            FeatureDisplayName = string.IsNullOrWhiteSpace(payload.feature_display_name)
                ? FeatureDisplayName(featureId)
                : payload.feature_display_name.Trim(),
            OutputSuffix = string.IsNullOrWhiteSpace(payload.output_suffix)
                ? FeatureOutputSuffix(featureId)
                : payload.output_suffix,
            HostPreference = ParseHostPreference(payload.host_preference),
            ExportDocx = payload.export_docx,
            ExportPdf = payload.export_pdf,
            ExportTxt = payload.export_txt,
            CancellationToken = token
        };
    }

    private static OfficeHostPreference ParseHostPreference(string value)
    {
        switch ((value ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "word":
                return OfficeHostPreference.Word;
            case "auto":
                return OfficeHostPreference.Auto;
            case "wps":
            case "wps-preferred":
            case "":
                // PartyOps 的兼容策略固定为 WPS 优先；仅宿主不可用时才回退 Word。
                return OfficeHostPreference.Wps;
            default:
                throw new InvalidOperationException("不支持的文档宿主偏好。");
        }
    }

    private static string FeatureDisplayName(string featureId)
    {
        return featureId switch
        {
            "format" => "一键排版",
            "replace" => "一键替换",
            "redheader" => "一键套红",
            "rename" => "一键命名",
            "convert" => "一键转换",
            "pdf-to-word" => "PDF 转 Word",
            _ => throw new InvalidOperationException("不支持的功能标识。")
        };
    }

    private static string FeatureOutputSuffix(string featureId)
    {
        return featureId switch
        {
            "format" => "_已排版",
            "replace" => "_已替换",
            "redheader" => "_已套红",
            "rename" => "_已命名",
            "convert" => "_已转换",
            "pdf-to-word" => "_已转换",
            _ => "_已处理"
        };
    }

    private static void Validate(HostRequest payload)
    {
        if (payload == null)
        {
            throw new InvalidOperationException("任务文件不是有效的 JSON 对象。");
        }
        if (payload.source_paths == null || payload.source_paths.Length == 0 || payload.source_paths.Length > 50)
        {
            throw new InvalidOperationException("每次任务必须包含 1—50 个源文件。");
        }
        if (payload.source_paths.Any(path => string.IsNullOrWhiteSpace(path) || !File.Exists(path)))
        {
            throw new FileNotFoundException("任务包含不存在的源文件。");
        }
        if (string.IsNullOrWhiteSpace(payload.output_directory))
        {
            throw new InvalidOperationException("缺少输出目录。");
        }
        if (string.IsNullOrWhiteSpace(payload.feature_id))
        {
            throw new InvalidOperationException("缺少功能标识。");
        }
        FeatureDisplayName(payload.feature_id.Trim().ToLowerInvariant());
    }

    private static Dictionary<string, string> ParseArguments(string[] args)
    {
        Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("宿主参数格式无效。");
            }
            values[args[index].Substring(2)] = args[index + 1];
        }
        return values;
    }

    private static string RequirePath(IReadOnlyDictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out string path) || string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("缺少宿主控制文件：" + key + "。");
        }
        return Path.GetFullPath(path);
    }

    private static void AssertPrivateControlFiles(params string[] paths)
    {
        string root = Path.GetDirectoryName(paths[0]);
        if (string.IsNullOrWhiteSpace(root) || paths.Any(path =>
                !string.Equals(Path.GetDirectoryName(path), root, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("宿主控制文件必须位于同一私有任务目录。");
        }
        if (!File.Exists(paths[0]))
        {
            throw new FileNotFoundException("宿主任务文件不存在。", paths[0]);
        }
    }

    private static void AppendProgress(string path, ProcessingProgress progress)
    {
        Dictionary<string, object> value = new Dictionary<string, object>
        {
            ["type"] = "progress",
            ["completed"] = progress.Completed,
            ["total"] = progress.Total,
            ["percent"] = progress.Percent,
            ["message"] = progress.Message ?? string.Empty
        };
        File.AppendAllText(path, Json.Serialize(value) + Environment.NewLine, new UTF8Encoding(false));
    }

    private static Dictionary<string, object> BuildResponse(StandaloneBatchResult result)
    {
        return new Dictionary<string, object>
        {
            ["schema_version"] = 1,
            ["success_count"] = result.SuccessCount,
            ["failure_count"] = result.FailureCount,
            ["cancelled_count"] = result.CancelledCount,
            ["jobs"] = result.Jobs.Select(job => new Dictionary<string, object>
            {
                ["source_path"] = job.SourcePath ?? string.Empty,
                ["success"] = job.Success,
                ["cancelled"] = job.Cancelled,
                ["message"] = job.Message ?? string.Empty,
                ["host_display_name"] = job.HostDisplayName ?? string.Empty,
                ["output_paths"] = job.OutputPaths.ToArray()
            }).ToArray()
        };
    }

    private static void WriteResponse(string path, object payload)
    {
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, Json.Serialize(payload), new UTF8Encoding(false));
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        File.Move(temporary, path);
    }

    private static void TryWriteFatalResponse(string path, Exception error)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }
        try
        {
            WriteResponse(path, new Dictionary<string, object>
            {
                ["schema_version"] = 1,
                ["fatal"] = true,
                ["error_type"] = error.GetType().Name,
                ["message"] = error.Message
            });
        }
        catch
        {
            // 控制文件不可写时只返回退出码，禁止再弹窗或暴露文档内容。
        }
    }

    private sealed class HostRequest
    {
        public string[] source_paths { get; set; }
        public string output_directory { get; set; }
        public string feature_id { get; set; }
        public string feature_display_name { get; set; }
        public string output_suffix { get; set; }
        public string host_preference { get; set; }
        public bool export_docx { get; set; } = true;
        public bool export_pdf { get; set; }
        public bool export_txt { get; set; }
        public Dictionary<string, object> options { get; set; } = new Dictionary<string, object>();
    }
}
