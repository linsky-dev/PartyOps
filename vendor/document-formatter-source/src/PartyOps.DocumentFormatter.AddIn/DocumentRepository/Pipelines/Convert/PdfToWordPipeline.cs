using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Conversion;
using DocumentRepository.Models.Tasks;
using DocumentRepository.Services.Conversion;
using DocumentRepository.Services.Logging;

namespace DocumentRepository.Pipelines.Convert;

public sealed class PdfToWordPipeline
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public ConvertResult Execute(PdfToWordRequest request)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		try
		{
			if (request != null && request.Context != null && request.Context.Application != null)
			{
				if (request.Task == null)
				{
					return ConvertResult.Fail("PDF 转 Word 缺少统一任务运行上下文。");
				}
				request.Context.Stage = "analyze";
				Report(request, 10, "正在检查 PDF 文件", "分析");
				request.Task.Cancellation.ThrowIfCancellationRequested("检查 PDF 前");
				request.Context.Stage = "plan";
				PdfToWordExecutionPlan pdfToWordExecutionPlan = PdfToWordPlanBuilder.Build(request.SourcePath, request.Options, request.ConflictResolver);
				Report(request, 25, "正在生成转换方案", "规划");
				LogService.Info("PdfToWordPipeline start");
				request.Context.Stage = "execute";
				Report(request, 40, "正在打开并解析 PDF", "执行");
				PdfToWordConversionOutput pdfToWordConversionOutput = PdfToWordConversionService.Convert(request.Context.Application, request.Context.Document, pdfToWordExecutionPlan.SourcePath, pdfToWordExecutionPlan.TargetPath, request.Task, request.Options, request.Context.UserInterface);
				request.Context.Document = pdfToWordConversionOutput.ReopenedDocument;
				try
				{
					request.Context.Selection = request.Context.Application.Selection;
				}
				catch
				{
					request.Context.Selection = null;
				}
				request.Context.Stage = "verify-result";
				Report(request, 95, "正在验证 DOCX 文件", "验证");
				PdfToWordVerifier.Verify(pdfToWordExecutionPlan, pdfToWordConversionOutput);
				stopwatch.Stop();
				LogService.Info("PdfToWordPipeline success ms=" + stopwatch.ElapsedMilliseconds + ", likelyScanned=" + pdfToWordConversionOutput.LikelyScannedPdf);
				string text = "已将 PDF 转换为 DOCX。\r\n输出位置：" + pdfToWordExecutionPlan.TargetPath;
				if (pdfToWordConversionOutput.PagesNeedingOcr == null || pdfToWordConversionOutput.PagesNeedingOcr.Count <= 0)
				{
					if (!pdfToWordConversionOutput.LikelyScannedPdf)
					{
						return ConvertResult.Ok(text, pdfToWordExecutionPlan.OutputFolder, new string[1] { pdfToWordExecutionPlan.TargetPath });
					}
					text += "\r\n\r\n提示：该文件疑似扫描件，当前本地转换可能只保留页面图片，文字未必可以直接编辑。";
					return ConvertResult.OkWithWarnings(text, pdfToWordExecutionPlan.OutputFolder, new string[1] { pdfToWordExecutionPlan.TargetPath });
				}
				text = text + "\r\n\r\n提示：该文件为混合型 PDF，第 " + string.Join("、", pdfToWordConversionOutput.PagesNeedingOcr) + " 页为扫描或图片页，已保留为页面图片或需 OCR 处理。";
				return ConvertResult.OkWithWarnings(text, pdfToWordExecutionPlan.OutputFolder, new string[1] { pdfToWordExecutionPlan.TargetPath });
			}
			return ConvertResult.Fail("Word/WPS 应用不可用。");
		}
		catch (OperationCanceledException ex)
		{
			LogService.Warn("PdfToWordPipeline canceled type=" + ex.GetType().Name);
			return ConvertResult.Cancel("已取消 PDF 转 Word。");
		}
		catch (Exception ex2)
		{
			string text2 = request.Context?.Stage ?? "unknown";
			LogService.Error("PdfToWordPipeline.Execute stage=" + text2 + " type=" + ex2.GetType().Name);
			return ConvertFailurePresentation.ToResult(ConvertFailureClassifier.Classify(ex2, ResolveFailureStage(request), request?.Options));
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ConvertFailureStage ResolveFailureStage(PdfToWordRequest request)
	{
		string a = ((request == null || request.Context == null) ? null : request.Context.Stage);
		if (string.Equals(a, "analyze", StringComparison.OrdinalIgnoreCase))
		{
			return ConvertFailureStage.Analyze;
		}
		if (!string.Equals(a, "plan", StringComparison.OrdinalIgnoreCase))
		{
			if (string.Equals(a, "execute", StringComparison.OrdinalIgnoreCase))
			{
				return ConvertFailureStage.Execute;
			}
			if (string.Equals(a, "verify-result", StringComparison.OrdinalIgnoreCase))
			{
				return ConvertFailureStage.Verify;
			}
			return ConvertFailureStage.Entry;
		}
		return ConvertFailureStage.Plan;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void Report(PdfToWordRequest request, int current, string step, string stage)
	{
		request.Task.Progress.Report(TaskProgressInfo.Create("PDF 转 Word", current, 100, step, stage));
	}
}
