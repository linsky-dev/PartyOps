using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Services.Conversion;

public static class PdfToWordSourceResolver
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string Resolve(string activeDocumentPath, Func<string, string> fileSelector)
	{
		if (IsExistingPdf(activeDocumentPath))
		{
			return Path.GetFullPath(activeDocumentPath);
		}
		if (fileSelector == null)
		{
			return null;
		}
		string arg = ResolveInitialDirectory(activeDocumentPath);
		string text = fileSelector(arg);
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		if (!string.Equals(Path.GetExtension(text), ".pdf", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("请选择 PDF 文件。");
		}
		if (!File.Exists(text))
		{
			throw new FileNotFoundException("选择的 PDF 文件不存在。", text);
		}
		return Path.GetFullPath(text);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool IsExistingPdf(string path)
	{
		if (!string.IsNullOrWhiteSpace(path) && string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
		{
			return File.Exists(path);
		}
		return false;
	}

	private static string ResolveInitialDirectory(string activeDocumentPath)
	{
		if (string.IsNullOrWhiteSpace(activeDocumentPath))
		{
			return string.Empty;
		}
		try
		{
			string directoryName = Path.GetDirectoryName(activeDocumentPath);
			return Directory.Exists(directoryName) ? directoryName : string.Empty;
		}
		catch
		{
			return string.Empty;
		}
	}
}
