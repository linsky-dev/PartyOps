using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Services.FileSafety;

public sealed class OutputIntegrityReceipt
{
	public string TargetPath { get; private set; }

	public string Extension { get; private set; }

	public long Length { get; private set; }

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static OutputIntegrityReceipt Create(AtomicFileWriteResult fileResult, string extension)
	{
		if (fileResult == null)
		{
			throw new ArgumentNullException("fileResult");
		}
		if (string.IsNullOrWhiteSpace(fileResult.TargetPath))
		{
			throw new InvalidOperationException("输出校验凭证缺少目标路径。");
		}
		return new OutputIntegrityReceipt
		{
			TargetPath = Path.GetFullPath(fileResult.TargetPath),
			Extension = NormalizeExtension(extension),
			Length = fileResult.Length
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void AssertMatches(string expectedPath, string expectedExtension)
	{
		if (!string.IsNullOrWhiteSpace(expectedPath))
		{
			string fullPath = Path.GetFullPath(expectedPath);
			if (string.Equals(TargetPath, fullPath, StringComparison.OrdinalIgnoreCase))
			{
				string text = NormalizeExtension(expectedExtension);
				if (!string.Equals(Extension, text, StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidOperationException("输出校验凭证与目标格式不一致。计划=" + text + "，凭证=" + Extension);
				}
				if (Length <= 0)
				{
					throw new InvalidOperationException("输出校验凭证中的文件长度无效。");
				}
				return;
			}
			throw new InvalidOperationException("输出校验凭证与目标路径不一致。计划=" + fullPath + "，凭证=" + TargetPath);
		}
		throw new InvalidOperationException("输出验证缺少预期路径。");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string NormalizeExtension(string extension)
	{
		if (string.IsNullOrWhiteSpace(extension))
		{
			return string.Empty;
		}
		if (!extension.StartsWith(".", StringComparison.Ordinal))
		{
			return "." + extension;
		}
		return extension;
	}
}
