using System;
using System.IO;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Conversion;

namespace DocumentRepository.Services.Conversion;

public static class PdfToWordPlanBuilder
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static PdfToWordExecutionPlan Build(string sourcePath, ConvertOptions options, Func<string, ConvertConflictDecision> conflictResolver)
	{
		if (PdfToWordSourceResolver.IsExistingPdf(sourcePath))
		{
			options = ConvertSettingsService.Normalize(options);
			string text = OutputPathService.ResolveOutputFolder(sourcePath, options);
			if (!string.IsNullOrWhiteSpace(text))
			{
				string text2 = OutputPathService.BuildDocumentOutputPath(sourcePath, text, ".docx", options.SameNamePolicy, conflictResolver);
				if (string.IsNullOrWhiteSpace(text2))
				{
					throw new OperationCanceledException("用户取消了 PDF 转 Word。");
				}
				return new PdfToWordExecutionPlan
				{
					Options = options,
					SourcePath = Path.GetFullPath(sourcePath),
					OutputFolder = Path.GetFullPath(text),
					TargetPath = Path.GetFullPath(text2)
				};
			}
			throw new InvalidOperationException("输出目录无效，请在转换规则中重新选择保存位置。");
		}
		throw new InvalidOperationException("PDF 源文件不存在或格式无效。");
	}
}
