using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Services.FileSafety;

public static class SafeOutputTransaction
{
	public static AtomicFileWriteResult ProduceAndCommit(string targetPath, Action<string> producer, string expectedExtension = null)
	{
		string extension = ResolveExpectedExtension(targetPath, expectedExtension);
		return AtomicFileService.WriteFileAtomically(targetPath, producer, delegate(string temp)
		{
			OutputFileIntegrityValidator.Validate(temp, extension);
		});
	}

	public static AtomicFileWriteResult CommitProducedFile(string producedPath, string targetPath, string expectedExtension = null)
	{
		string expectedExtension2 = ResolveExpectedExtension(targetPath, expectedExtension);
		OutputFileIntegrityValidator.Validate(producedPath, expectedExtension2);
		return AtomicFileService.CommitTempFile(producedPath, targetPath);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string ResolveExpectedExtension(string targetPath, string expectedExtension)
	{
		if (!string.IsNullOrWhiteSpace(expectedExtension))
		{
			return expectedExtension;
		}
		if (string.IsNullOrWhiteSpace(targetPath))
		{
			throw new InvalidOperationException("安全输出目标路径为空。");
		}
		return Path.GetExtension(targetPath);
	}
}
