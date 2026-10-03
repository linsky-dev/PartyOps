using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Conversion;
using DocumentRepository.Services.FileSafety;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Conversion;

public static class ConvertExecutor
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static List<string> Execute(Document document, ConvertExecutionPlan plan, ConvertRequest request)
	{
		if (document == null)
		{
			throw new InvalidOperationException("当前文档无效。");
		}
		if (plan == null || plan.Options == null)
		{
			throw new InvalidOperationException("转换方案无效。");
		}
		if (!string.IsNullOrWhiteSpace(plan.OutputFolder))
		{
			bool existedBefore = Directory.Exists(plan.OutputFolder);
			request.Task.Artifacts.RegisterDirectory(plan.OutputFolder, existedBefore);
			Directory.CreateDirectory(plan.OutputFolder);
		}
		request.Task.Cancellation.ThrowIfCancellationRequested("执行转换前");
		if (plan.Options.SelectedFormat != ConvertFormat.Image && !string.IsNullOrWhiteSpace(plan.TargetPath))
		{
			request.Task.Artifacts.RegisterFile(plan.TargetPath, File.Exists(plan.TargetPath));
		}
		List<string> list = new List<string>();
		switch (plan.Options.SelectedFormat)
		{
		default:
			throw new InvalidOperationException("未知转换格式。");
		case ConvertFormat.Image:
		{
			list.AddRange(ImageConversionService.ExportImages(document, plan.SourcePath, plan.ImageFolder, plan.Options, plan.ImagePages, request.Task, request?.ConflictResolver, out var usedCompatibilityFallback, out var longImageResolutionAdjusted));
			if (!longImageResolutionAdjusted)
			{
				if (usedCompatibilityFallback)
				{
					plan.HasWarnings = true;
					plan.SuccessMessage = "图片已生成；当前宿主使用兼容渲染，建议抽查页面效果。";
				}
			}
			else
			{
				plan.HasWarnings = true;
				plan.SuccessMessage = (usedCompatibilityFallback ? "图片已生成；为适配当前宿主和内存，长图清晰度已自动调整，建议抽查页面效果。" : "图片已生成；为保证转换稳定，长图清晰度已自动调整，建议抽查页面效果。");
			}
			break;
		}
		case ConvertFormat.Pdf:
			ExportDocument(plan, list, delegate(string p)
			{
				DocumentExportService.ExportPdf(document, p);
			});
			break;
		case ConvertFormat.Docx:
			ExecuteDocx(document, plan, list);
			break;
		case ConvertFormat.Txt:
			ExportDocument(plan, list, delegate(string p)
			{
				DocumentExportService.ExportTxt(document, p, plan.Options);
			});
			break;
		}
		return list;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ExecuteDocx(Document document, ConvertExecutionPlan plan, List<string> files)
	{
		if (plan.Options.DocxMode == DocxConvertMode.ReplaceCurrentDocument)
		{
			if (plan.SourceAlreadyDocx)
			{
				document.Save();
				files.Add(plan.SourcePath);
				return;
			}
			DocxReplaceCurrentResult docxReplaceCurrentResult = DocumentExportService.ExportDocxReplacingCurrent(document, plan.SourcePath, plan.TargetPath);
			files.Add(docxReplaceCurrentResult.TargetPath);
			if (docxReplaceCurrentResult.HasCleanupWarning)
			{
				plan.HasWarnings = true;
				plan.SuccessMessage = "已将当前文档转换为 DOCX，但原 DOC/WPS 文件未能删除。\r\n原因：" + docxReplaceCurrentResult.SourceDeleteError;
			}
		}
		else
		{
			ExportDocument(plan, files, delegate(string p)
			{
				DocumentExportService.ExportDocx(document, p);
			});
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ExportDocument(ConvertExecutionPlan plan, List<string> files, Action<string> exporter)
	{
		if (string.IsNullOrWhiteSpace(plan.TargetPath))
		{
			throw new InvalidOperationException("输出文件路径无效。");
		}
		SafeOutputTransaction.ProduceAndCommit(plan.TargetPath, exporter);
		files.Add(plan.TargetPath);
	}
}
