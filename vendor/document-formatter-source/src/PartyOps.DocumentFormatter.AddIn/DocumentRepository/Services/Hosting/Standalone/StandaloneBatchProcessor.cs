using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using DocumentRepository;
using DocumentRepository.Models;
using DocumentRepository.Models.Conversion;
using DocumentRepository.Models.Features;
using DocumentRepository.Models.Standalone;
using DocumentRepository.Services.Auth;
using DocumentRepository.Services.Conversion;
using DocumentRepository.Services.Conversion.PdfToWord;
using DocumentRepository.Services.Features;
using DocumentRepository.Services.FileSafety;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Hosting.Standalone;

/// <summary>
/// 将独立版任务转换为恢复后的统一功能命令。所有源文件都先复制或转换到输出目录，业务命令只操作副本。
/// </summary>
public sealed class StandaloneBatchProcessor
{
	private static readonly HashSet<string> SupportedDocumentExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		".docx", ".doc", ".wps", ".rtf"
	};

	public StandaloneBatchResult Execute(StandaloneBatchRequest request, Action<ProcessingProgress> report)
	{
		ValidateRequest(request);
		Directory.CreateDirectory(request.OutputDirectory);
		StandaloneBatchResult batchResult = new StandaloneBatchResult();
		OfficeHostSession host = null;
		LogService.Info("Standalone batch start feature=" + request.FeatureId + ", count=" + request.SourcePaths.Count + ", host=" + request.HostPreference);
		try
		{
			for (int index = 0; index < request.SourcePaths.Count; index++)
			{
				if (request.CancellationToken.IsCancellationRequested)
				{
					break;
				}
				string sourcePath = request.SourcePaths[index];
				Report(report, index, request.SourcePaths.Count, 0, sourcePath, "正在准备文件...");
				StandaloneJobResult job;
				try
				{
					job = ProcessOne(request, sourcePath, ref host, delegate(int percent, string message)
					{
						Report(report, index, request.SourcePaths.Count, percent, sourcePath, message);
					});
				}
				catch (OperationCanceledException)
				{
					job = new StandaloneJobResult
					{
						SourcePath = sourcePath,
						Cancelled = true,
						Message = "用户已取消。",
						HostDisplayName = host?.DisplayName
					};
				}
				catch (Exception ex)
				{
					LogService.Error("Standalone batch item failed feature=" + request.FeatureId + ", file=" + Path.GetFileName(sourcePath), ex);
					job = new StandaloneJobResult
					{
						SourcePath = sourcePath,
						Success = false,
						Message = ex.Message,
						HostDisplayName = host?.DisplayName
					};
				}
				batchResult.Jobs.Add(job);
				Report(report, index + 1, request.SourcePaths.Count, 0, sourcePath, job.Success ? "处理完成" : job.Cancelled ? "已取消" : "处理失败：" + job.Message);
				if (job.Cancelled)
				{
					break;
				}
			}
		}
		finally
		{
			host?.Dispose();
			LogService.Info("Standalone batch finish feature=" + request.FeatureId + ", success=" + batchResult.SuccessCount + ", failed=" + batchResult.FailureCount + ", cancelled=" + batchResult.CancelledCount);
		}
		return batchResult;
	}

	/// <summary>
	/// 验证真实 Word/WPS COM 引擎可以完成“只读打开源文件 → 另存 DOCX → 完整性校验 → 关闭”。
	/// 该入口不执行收费业务，也不跳过业务入口中的授权规则。
	/// </summary>
	public StandaloneJobResult ProbeHost(StandaloneHostProbeRequest request, Action<ProcessingProgress> report)
	{
		ValidateProbeRequest(request);
		string sourcePath = Path.GetFullPath(request.SourcePath);
		string outputDirectory = Path.GetFullPath(request.OutputDirectory);
		Directory.CreateDirectory(outputDirectory);
		string outputPath = UniquePath(Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(sourcePath) + "_引擎自检.docx"));
		string sourceHash = ComputeSha256(sourcePath);
		OfficeHostSession host = null;
		Document document = null;
		StandaloneJobResult result = new StandaloneJobResult { SourcePath = sourcePath };
		try
		{
			request.CancellationToken.ThrowIfCancellationRequested();
			Report(report, 0, 1, 10, sourcePath, "正在启动独立文档引擎...");
			host = OfficeHostSession.Start(request.HostPreference);
			result.HostDisplayName = host.DisplayName;
			Report(report, 0, 1, 35, sourcePath, "已启动：" + host.DisplayName);
			document = host.OpenDocument(sourcePath, true);
			Report(report, 0, 1, 60, sourcePath, "源文件已只读打开，正在另存自检副本...");
			host.SaveAsDocx(document, outputPath);
			host.CloseDocument(ref document, false);
			ValidateOutputWithRetry(outputPath, ".docx");
			if (!string.Equals(sourceHash, ComputeSha256(sourcePath), StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException("源文件在文档引擎自检期间发生变化。为保护数据，已将本次自检判定为失败。");
			}
			result.Success = true;
			result.Message = "文档引擎自检通过：源文件未改变，另存 DOCX 已通过完整性校验。";
			result.OutputPaths.Add(outputPath);
			Report(report, 1, 1, 0, sourcePath, "文档引擎自检通过");
			LogService.Info("Standalone host probe success host=" + host.DisplayName + ", file=" + Path.GetFileName(sourcePath));
			return result;
		}
		catch (OperationCanceledException)
		{
			result.Cancelled = true;
			result.Message = "文档引擎自检已取消。";
			return result;
		}
		catch (Exception ex)
		{
			result.Message = ex.Message;
			LogService.Error("Standalone host probe failed file=" + Path.GetFileName(sourcePath), ex);
			return result;
		}
		finally
		{
			host?.CloseDocument(ref document, false);
			host?.Dispose();
			if (!result.Success)
			{
				TryDeleteOwnedOutput(outputPath, sourcePath, outputDirectory);
			}
		}
	}

	private static StandaloneJobResult ProcessOne(StandaloneBatchRequest request, string sourcePath, ref OfficeHostSession host, Action<int, string> report)
	{
		request.CancellationToken.ThrowIfCancellationRequested();
		string fullSourcePath = Path.GetFullPath(sourcePath);
		if (!File.Exists(fullSourcePath))
		{
			throw new FileNotFoundException("源文件不存在。", fullSourcePath);
		}
		string extension = Path.GetExtension(fullSourcePath);
		bool isPdf = string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase);
		if (!isPdf && !SupportedDocumentExtensions.Contains(extension))
		{
			throw new NotSupportedException("不支持该文件类型：" + extension + "。目前支持 DOCX、DOC、WPS、RTF 和 PDF。");
		}
		if (string.Equals(request.FeatureId, "pdf-to-word", StringComparison.OrdinalIgnoreCase) && !isPdf)
		{
			throw new NotSupportedException("“PDF 转 Word”仅接受 PDF 文件。其他文档请使用“一键转换”。");
		}
		string suffix = string.IsNullOrWhiteSpace(request.OutputSuffix) ? "_已处理" : request.OutputSuffix;
		string workingPath = UniquePath(Path.Combine(request.OutputDirectory, Path.GetFileNameWithoutExtension(fullSourcePath) + suffix + ".docx"));
		List<string> ownedPaths = new List<string> { workingPath };
		Document document = null;
		bool keepWorkingDocument = false;
		StandaloneJobResult result = new StandaloneJobResult
		{
			SourcePath = fullSourcePath
		};
		try
		{
			if (isPdf)
			{
				report(8, "正在使用本地引擎解析 PDF...");
				ConvertPdfLocally(fullSourcePath, workingPath, request, delegate(int page, int total, string stage)
				{
					int percent = total <= 0 ? 20 : 8 + Math.Min(45, page * 45 / total);
					report(percent, stage + " " + page + "/" + total);
				});
				if (string.Equals(request.FeatureId, "pdf-to-word", StringComparison.OrdinalIgnoreCase) || string.Equals(request.FeatureId, "convert", StringComparison.OrdinalIgnoreCase))
				{
					result.Success = true;
					result.Message = "PDF 已在本地转换为可编辑 DOCX。";
					result.OutputPaths.Add(workingPath);
					return result;
				}
			}
			else
			{
				report(8, "正在创建不会影响原文件的可编辑副本...");
				if (string.Equals(extension, ".docx", StringComparison.OrdinalIgnoreCase))
				{
					SafeOutputTransaction.ProduceAndCommit(workingPath, tempPath => File.Copy(fullSourcePath, tempPath, true), ".docx");
				}
				else
				{
					host ??= OfficeHostSession.Start(request.HostPreference);
					Document sourceDocument = null;
					try
					{
						sourceDocument = host.OpenDocument(fullSourcePath, true);
						host.SaveAsDocx(sourceDocument, workingPath);
					}
					finally
					{
						host.CloseDocument(ref sourceDocument, false);
					}
				}
			}

			request.CancellationToken.ThrowIfCancellationRequested();
			ValidateOutputWithRetry(workingPath, ".docx");
			host ??= OfficeHostSession.Start(request.HostPreference);
			result.HostDisplayName = host.DisplayName;
			report(18, "已启动后台文档引擎：" + host.DisplayName);
			document = host.OpenDocument(workingPath, false);
			HashSet<string> filesBefore = SnapshotFiles(request.OutputDirectory);
			ConvertOptions previousConvertOptions = null;
			try
			{
				if (string.Equals(request.FeatureId, "convert", StringComparison.OrdinalIgnoreCase))
				{
					previousConvertOptions = ApplyStandaloneConvertOutput(request.OutputDirectory);
				}
				report(28, "正在执行" + request.FeatureDisplayName + "...");
				OperationContext context = OperationContext.FromApplication(host.Application);
				context.Document = document;
				context.UserInterface = NonInteractiveFeatureUiService.Instance;
				CommandResult commandResult = FeatureTaskExecutor.Execute(request.FeatureId, context, FeatureExecutionOptions.NonInteractive("standalone-desktop"));
				if (commandResult == null)
				{
					throw new InvalidOperationException(request.FeatureDisplayName + "没有返回执行结果。");
				}
				if (commandResult.Cancelled)
				{
					result.Cancelled = true;
					result.Message = commandResult.Message ?? "操作已取消。";
					return result;
				}
				if (!commandResult.Success)
				{
					result.Message = commandResult.Message ?? (request.FeatureDisplayName + "执行失败。");
					return result;
				}
				document.Save();
				string currentDocumentPath = SafeDocumentPath(document, workingPath);
				if (!PathsEqual(currentDocumentPath, workingPath))
				{
					ownedPaths.Add(currentDocumentPath);
				}
				report(72, "功能执行完成，正在导出所选格式...");

				if (string.Equals(request.FeatureId, "convert", StringComparison.OrdinalIgnoreCase))
				{
					List<string> convertedOutputs = SnapshotFiles(request.OutputDirectory).Where(path => !filesBefore.Contains(path) && !PathsEqual(path, workingPath)).ToList();
					foreach (string output in convertedOutputs)
					{
						result.OutputPaths.Add(output);
					}
					if (result.OutputPaths.Count == 0)
					{
						result.OutputPaths.Add(currentDocumentPath);
						keepWorkingDocument = true;
					}
				}
				else
				{
					ExportRequestedFormats(request, document, currentDocumentPath, result.OutputPaths, report);
					keepWorkingDocument = request.ExportDocx;
					if (request.ExportDocx)
					{
						result.OutputPaths.Insert(0, currentDocumentPath);
					}
				}

				// WPS 的 Save 返回时压缩包可能仍在后台收尾。PDF/TXT 导出依赖活动文档，
				// 因此先完成导出，再显式带保存关闭；只有释放文档句柄后才校验最终文件。
				report(95, "正在关闭文档并完成写盘校验...");
				host.CloseDocument(ref document, true);
				ValidateOutputWithRetry(currentDocumentPath, Path.GetExtension(currentDocumentPath));
				foreach (string output in result.OutputPaths.Where(path => !PathsEqual(path, currentDocumentPath)))
				{
					ValidateOutputWithRetry(output, Path.GetExtension(output));
				}
				result.Success = true;
				result.Message = string.IsNullOrWhiteSpace(commandResult.Message) ? request.FeatureDisplayName + "完成。" : commandResult.Message;
				return result;
			}
			finally
			{
				if (previousConvertOptions != null)
				{
					ConvertSettingsService.Save(previousConvertOptions);
				}
			}
		}
		finally
		{
			host?.CloseDocument(ref document, false);
			if (!result.Success || (!keepWorkingDocument && !result.OutputPaths.Any(path => PathsEqual(path, workingPath))))
			{
				foreach (string ownedPath in ownedPaths.Distinct(StringComparer.OrdinalIgnoreCase))
				{
					TryDeleteOwnedOutput(ownedPath, fullSourcePath, request.OutputDirectory);
				}
			}
		}
	}

	private static void ExportRequestedFormats(StandaloneBatchRequest request, Document document, string currentDocumentPath, IList<string> outputs, Action<int, string> report)
	{
		string basePath = Path.Combine(request.OutputDirectory, Path.GetFileNameWithoutExtension(currentDocumentPath));
		if (request.ExportPdf)
		{
			request.CancellationToken.ThrowIfCancellationRequested();
			string pdfPath = UniquePath(basePath + ".pdf");
			report(80, "正在导出 PDF...");
			SafeOutputTransaction.ProduceAndCommit(pdfPath, tempPath => DocumentExportService.ExportPdf(document, tempPath), ".pdf");
			outputs.Add(pdfPath);
		}
		if (request.ExportTxt)
		{
			request.CancellationToken.ThrowIfCancellationRequested();
			string txtPath = UniquePath(basePath + ".txt");
			report(90, "正在导出 TXT...");
			ConvertOptions options;
			try
			{
				options = ConvertSettingsService.Load();
			}
			catch
			{
				options = new ConvertOptions();
			}
			SafeOutputTransaction.ProduceAndCommit(txtPath, tempPath => DocumentExportService.ExportTxt(document, tempPath, options), ".txt");
			outputs.Add(txtPath);
		}
	}

	private static ConvertOptions ApplyStandaloneConvertOutput(string outputDirectory)
	{
		ConvertOptions previous = ConvertSettingsService.Load();
		ConvertOptions runtime = Clone(previous);
		runtime.SaveLocation = ConvertSaveLocation.CustomFolder;
		runtime.CustomOutputFolder = outputDirectory;
		runtime.SameNamePolicy = ConvertSameNamePolicy.AutoRename;
		runtime.OpenFolderAfterConvert = false;
		ConvertSettingsService.Save(runtime);
		return previous;
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

	private static void ConvertPdfLocally(string sourcePath, string targetPath, StandaloneBatchRequest request, Action<int, int, string> progress)
	{
		SafeOutputTransaction.ProduceAndCommit(targetPath, delegate(string tempPath)
		{
			using FileStream output = new FileStream(tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
			new LocalPdfToWordEngine().Convert(sourcePath, output, delegate(int page, int total)
			{
				request.CancellationToken.ThrowIfCancellationRequested();
				progress(page, total, "正在解析页面");
			}, delegate(int page, int total)
			{
				request.CancellationToken.ThrowIfCancellationRequested();
				progress(page, total, "正在还原段落");
			});
		}, ".docx");
	}

	private static string ComputeSha256(string path)
	{
		using SHA256 algorithm = SHA256.Create();
		using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1024 * 1024, FileOptions.SequentialScan);
		return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", string.Empty);
	}

	private static void ValidateOutputWithRetry(string path, string extension)
	{
		Exception lastError = null;
		for (int attempt = 1; attempt <= 12; attempt++)
		{
			try
			{
				OutputFileIntegrityValidator.Validate(path, extension);
				return;
			}
			catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException)
			{
				lastError = ex;
				if (attempt < 12)
				{
					Thread.Sleep(100 * attempt);
				}
			}
		}
		throw new InvalidDataException("文档引擎写盘完成后，输出文件仍未通过完整性校验。", lastError);
	}

	private static HashSet<string> SnapshotFiles(string directory)
	{
		if (!Directory.Exists(directory))
		{
			return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		}
		return new HashSet<string>(Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
	}

	private static string SafeDocumentPath(Document document, string fallback)
	{
		try
		{
			string path = document?.FullName;
			return string.IsNullOrWhiteSpace(path) ? fallback : Path.GetFullPath(path);
		}
		catch
		{
			return fallback;
		}
	}

	private static string UniquePath(string path)
	{
		return File.Exists(path) ? OutputPathService.AutoRename(path) : path;
	}

	private static bool PathsEqual(string left, string right)
	{
		if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
		{
			return false;
		}
		return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
	}

	private static void TryDeleteOwnedOutput(string path, string sourcePath, string outputDirectory)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || PathsEqual(path, sourcePath))
			{
				return;
			}
			string fullPath = Path.GetFullPath(path);
			string root = Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
			if (fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
			{
				File.Delete(fullPath);
			}
		}
		catch
		{
		}
	}

	private static void ValidateRequest(StandaloneBatchRequest request)
	{
		if (request == null)
		{
			throw new ArgumentNullException(nameof(request));
		}
		if (request.SourcePaths == null || request.SourcePaths.Count == 0)
		{
			throw new InvalidOperationException("请先添加至少一个待处理文件。");
		}
		if (string.IsNullOrWhiteSpace(request.FeatureId))
		{
			throw new InvalidOperationException("请选择要执行的功能。");
		}
		if (string.IsNullOrWhiteSpace(request.OutputDirectory))
		{
			throw new InvalidOperationException("请选择输出目录。");
		}
		if (!string.Equals(request.FeatureId, "convert", StringComparison.OrdinalIgnoreCase) && !string.Equals(request.FeatureId, "pdf-to-word", StringComparison.OrdinalIgnoreCase) && !request.ExportDocx && !request.ExportPdf && !request.ExportTxt)
		{
			throw new InvalidOperationException("请至少选择一种输出格式。");
		}
	}

	private static void ValidateProbeRequest(StandaloneHostProbeRequest request)
	{
		if (request == null)
		{
			throw new ArgumentNullException(nameof(request));
		}
		if (string.IsNullOrWhiteSpace(request.SourcePath) || !File.Exists(request.SourcePath))
		{
			throw new FileNotFoundException("请选择一个存在的 Word/WPS 文档进行引擎自检。", request.SourcePath);
		}
		string extension = Path.GetExtension(request.SourcePath);
		if (!SupportedDocumentExtensions.Contains(extension))
		{
			throw new NotSupportedException("文档引擎自检仅支持 DOCX、DOC、WPS 和 RTF；PDF 转 Word由本地引擎处理。");
		}
		if (string.IsNullOrWhiteSpace(request.OutputDirectory))
		{
			throw new InvalidOperationException("请选择输出目录。");
		}
	}

	private static void Report(Action<ProcessingProgress> report, int completed, int total, int itemPercent, string sourcePath, string message)
	{
		if (report == null)
		{
			return;
		}
		int overall = total <= 0 ? 0 : Math.Min(100, Math.Max(0, (completed * 100 + itemPercent) / total));
		report(new ProcessingProgress
		{
			Completed = Math.Min(completed, total),
			Total = total,
			Percent = overall,
			SourcePath = sourcePath,
			Message = message
		});
	}
}
