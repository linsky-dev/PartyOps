using System;
using System.IO;
using System.Runtime.CompilerServices;
using DocumentRepository.Services.Logging;

namespace DocumentRepository.Services.FileSafety;

public static class AtomicFileService
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static AtomicFileWriteResult WriteFileAtomically(string targetPath, Action<string> writer, Action<string> validator = null)
	{
		if (!string.IsNullOrWhiteSpace(targetPath))
		{
			if (writer == null)
			{
				throw new ArgumentNullException("writer");
			}
			string directoryName = Path.GetDirectoryName(targetPath);
			if (!string.IsNullOrWhiteSpace(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			string text = CreateSiblingTempPath(targetPath);
			bool flag = false;
			try
			{
				writer(text);
				ValidateProducedFile(text);
				validator?.Invoke(text);
				AtomicFileWriteResult result = CommitTempFile(text, targetPath);
				flag = true;
				return result;
			}
			finally
			{
				if (!flag)
				{
					TryDeleteTemp(text);
				}
			}
		}
		throw new InvalidOperationException("Target path is empty.");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string CreateSiblingTempPath(string targetPath)
	{
		string text = Path.GetDirectoryName(targetPath);
		if (string.IsNullOrWhiteSpace(text))
		{
			text = Directory.GetCurrentDirectory();
		}
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(targetPath);
		string extension = Path.GetExtension(targetPath);
		return Path.Combine(text, "." + fileNameWithoutExtension + "." + Guid.NewGuid().ToString("N") + ".tmp" + extension);
	}

	public static AtomicFileWriteResult CommitTempFile(string tempPath, string targetPath)
	{
		ValidateProducedFile(tempPath);
		bool flag = File.Exists(targetPath);
		long length = new FileInfo(tempPath).Length;
		if (!flag)
		{
			File.Move(tempPath, targetPath);
		}
		else
		{
			File.Replace(tempPath, targetPath, null, ignoreMetadataErrors: true);
		}
		return new AtomicFileWriteResult
		{
			TargetPath = targetPath,
			Length = length,
			ReplacedExistingFile = flag
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ValidateProducedFile(string path)
	{
		if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
		{
			if (new FileInfo(path).Length <= 0)
			{
				throw new InvalidOperationException("Output file is empty.");
			}
			return;
		}
		throw new InvalidOperationException("Output file was not produced.");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void TryDeleteTemp(string tempPath)
	{
		try
		{
			if (!string.IsNullOrWhiteSpace(tempPath) && File.Exists(tempPath))
			{
				File.Delete(tempPath);
			}
		}
		catch (Exception ex)
		{
			LogService.Warn("AtomicFileService.TryDeleteTemp, exception=" + ex.GetType().Name);
		}
	}
}
