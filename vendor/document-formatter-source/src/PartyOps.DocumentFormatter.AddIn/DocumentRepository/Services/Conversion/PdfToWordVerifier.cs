using System;
using System.IO;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Conversion;

namespace DocumentRepository.Services.Conversion;

public static class PdfToWordVerifier
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Verify(PdfToWordExecutionPlan plan, PdfToWordConversionOutput output)
	{
		if (plan != null)
		{
			if (output != null)
			{
				if (output.IntegrityReceipt != null)
				{
					output.IntegrityReceipt.AssertMatches(plan.TargetPath, ".docx");
					if (!File.Exists(plan.TargetPath))
					{
						throw new InvalidOperationException("PDF 转 Word 输出文件不存在：" + plan.TargetPath);
					}
					if (output.ReopenedDocument == null)
					{
						throw new InvalidOperationException("DOCX 已生成，但 Word/WPS 未能重新打开输出文档。");
					}
					return;
				}
				throw new InvalidOperationException("PDF 转 Word 缺少 DOCX 完整性校验凭证。");
			}
			throw new InvalidOperationException("PDF 转 Word 未返回转换结果。");
		}
		throw new ArgumentNullException("plan");
	}
}
