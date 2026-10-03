using System;
using System.IO;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Conversion;

namespace DocumentRepository.Services.Conversion;

public static class ConvertPlanBuilder
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ConvertExecutionPlan Build(ConvertDocumentAnalysis analysis, ConvertOptions options, Func<string, ConvertConflictDecision> conflictResolver)
	{
		if (analysis == null || string.IsNullOrWhiteSpace(analysis.SourcePath))
		{
			throw ConvertOperationException.Create(ConvertFailureReasonCode.DocumentAnalysisUnavailable, ConvertFailureStage.Plan);
		}
		options = ConvertSettingsService.Normalize(options);
		string text = OutputPathService.ResolveOutputFolder(analysis.SourcePath, options);
		if (string.IsNullOrWhiteSpace(text))
		{
			throw ConvertOperationException.Create(ConvertFailureReasonCode.OutputFolderInvalid, ConvertFailureStage.Plan);
		}
		ConvertExecutionPlan convertExecutionPlan = new ConvertExecutionPlan
		{
			Options = options,
			SourcePath = analysis.SourcePath,
			OutputFolder = text,
			SourceAlreadyDocx = analysis.IsDocx
		};
		switch (options.SelectedFormat)
		{
		case ConvertFormat.Docx:
			BuildDocxPlan(convertExecutionPlan, options, conflictResolver);
			break;
		default:
			throw ConvertOperationException.Create(ConvertFailureReasonCode.UnsupportedFormat, ConvertFailureStage.Plan);
		case ConvertFormat.Pdf:
			BuildDocumentPlan(convertExecutionPlan, ".pdf", "已转换为 PDF。", options, conflictResolver);
			break;
		case ConvertFormat.Txt:
			BuildDocumentPlan(convertExecutionPlan, ".txt", "已转换为 TXT。", options, conflictResolver);
			break;
		case ConvertFormat.Image:
			BuildImagePlan(convertExecutionPlan, analysis, options, conflictResolver);
			break;
		}
		return convertExecutionPlan;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void BuildDocxPlan(ConvertExecutionPlan plan, ConvertOptions options, Func<string, ConvertConflictDecision> conflictResolver)
	{
		if (options.DocxMode != DocxConvertMode.ReplaceCurrentDocument || !plan.SourceAlreadyDocx)
		{
			BuildDocumentPlan(plan, ".docx", (options.DocxMode == DocxConvertMode.ReplaceCurrentDocument) ? "已将当前文档转换为 DOCX。" : "已转换为 DOCX。", options, conflictResolver);
			return;
		}
		plan.TargetPath = plan.SourcePath;
		plan.SuccessMessage = "当前文档已是 DOCX。";
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void BuildDocumentPlan(ConvertExecutionPlan plan, string extension, string message, ConvertOptions options, Func<string, ConvertConflictDecision> conflictResolver)
	{
		string text = OutputPathService.BuildDocumentOutputPath(plan.SourcePath, plan.OutputFolder, extension, options.SameNamePolicy, conflictResolver);
		if (!string.IsNullOrWhiteSpace(text))
		{
			plan.TargetPath = text;
			plan.SuccessMessage = message;
			return;
		}
		throw new OperationCanceledException("用户取消了转换。");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void BuildImagePlan(ConvertExecutionPlan plan, ConvertDocumentAnalysis analysis, ConvertOptions options, Func<string, ConvertConflictDecision> conflictResolver)
	{
		PageSelectionResult pageSelectionResult = PageSelectionParser.Parse(options.ImagePageSelectionMode, options.ImagePageRange, options.ImageSelectedPages, analysis.PageCount);
		if (!pageSelectionResult.Success)
		{
			throw ConvertOperationException.Create(pageSelectionResult.FailureReasonCode ?? ConvertFailureReasonCode.PageRangeFormatInvalid, ConvertFailureStage.Plan);
		}
		string outputFolder = (plan.ImageFolder = Path.Combine(plan.OutputFolder, Path.GetFileNameWithoutExtension(plan.SourcePath) + "_图片"));
		plan.OutputFolder = outputFolder;
		plan.ImagePages.AddRange(pageSelectionResult.Pages);
		plan.SuccessMessage = "已转换为图片。";
	}
}
