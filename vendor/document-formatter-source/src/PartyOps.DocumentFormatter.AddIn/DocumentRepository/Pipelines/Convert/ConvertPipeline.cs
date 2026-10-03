using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Conversion;
using DocumentRepository.Models.Tasks;
using DocumentRepository.Services.Conversion;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Pipelines.Convert;

public class ConvertPipeline
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public ConvertResult Execute(ConvertRequest request)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		ConvertFailureStage stage = ConvertFailureStage.Entry;
		ConvertOptions options = request?.Options;
		try
		{
			if (request == null || request.Context == null || request.Context.Document == null)
			{
				return ConvertFailurePresentation.ToResult(ConvertOperationException.Create(ConvertFailureReasonCode.NoActiveDocument, ConvertFailureStage.Entry));
			}
			if (request.Task == null)
			{
				return ConvertFailurePresentation.ToResult(ConvertOperationException.Create(ConvertFailureReasonCode.TaskContextUnavailable, ConvertFailureStage.Entry));
			}
			DocumentSessionOptions documentSessionOptions = DocumentSessionOptions.Create("convert", "一键转换");
			documentSessionOptions.SuppressAlerts = true;
			documentSessionOptions.UseUndoRecord = false;
			documentSessionOptions.SelectionRestoreMode = SelectionRestoreMode.OriginalSelection;
			using DocumentSession documentSession = DocumentSession.Begin(request.Context, documentSessionOptions);
			Document document = request.Context.Document;
			stage = ConvertFailureStage.Analyze;
			documentSession.SetStage("analyze");
			request.Task.Cancellation.ThrowIfCancellationRequested("分析当前文档前");
			Report(request, 5, 100, "正在分析当前文档", "分析");
			ConvertDocumentAnalysis analysis = ConvertDocumentAnalyzer.Analyze(document);
			stage = ConvertFailureStage.Plan;
			documentSession.SetStage("plan");
			Report(request, 15, 100, "正在生成转换方案", "规划");
			ConvertExecutionPlan convertExecutionPlan = ConvertPlanBuilder.Build(analysis, request.Options, request.ConflictResolver);
			request.Task.Cancellation.ThrowIfCancellationRequested("转换方案生成后");
			LogService.Info("ConvertPipeline start format=" + convertExecutionPlan.Options.SelectedFormat);
			documentSession.SetStage("execute");
			stage = ConvertFailureStage.Execute;
			Report(request, 30, 100, "正在执行转换", "执行");
			List<string> list = ConvertExecutor.Execute(document, convertExecutionPlan, request);
			stage = ConvertFailureStage.Verify;
			documentSession.SetStage("verify-result");
			request.Task.Cancellation.ThrowIfCancellationRequested("验证转换结果前");
			Report(request, 95, 100, "正在整理转换结果", "结果");
			ConvertVerifier.Verify(convertExecutionPlan, list);
			stopwatch.Stop();
			LogService.Info("ConvertPipeline success ms=" + stopwatch.ElapsedMilliseconds + ", files=" + list.Count);
			documentSession.Complete();
			string message = convertExecutionPlan.SuccessMessage + "\r\n输出位置：" + convertExecutionPlan.OutputFolder;
			return convertExecutionPlan.HasWarnings ? ConvertResult.OkWithWarnings(message, convertExecutionPlan.OutputFolder, list) : ConvertResult.Ok(message, convertExecutionPlan.OutputFolder, list);
		}
		catch (OperationCanceledException ex)
		{
			LogService.Warn("ConvertPipeline canceled", ex);
			request?.Task?.Artifacts?.Cleanup();
			return ConvertResult.Cancel("已取消转换。");
		}
		catch (Exception ex2)
		{
			LogService.Error("ConvertPipeline.Execute", ex2);
			request?.Task?.Artifacts?.Cleanup();
			return ConvertFailurePresentation.ToResult(ConvertFailureClassifier.Classify(ex2, stage, options));
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void Report(ConvertRequest request, int current, int total, string step, string stage)
	{
		if (request != null && request.Task != null)
		{
			request.Task.Progress.Report(TaskProgressInfo.Create("一键转换", current, total, step, stage));
		}
	}
}
