using System;
using System.IO;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Conversion;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Conversion;

public static class OutputPathService
{
	public static string GetDocumentPath(Document document)
	{
		try
		{
			return (document == null) ? "" : (document.FullName ?? "");
		}
		catch
		{
			return "";
		}
	}

	public static string ResolveOutputFolder(string sourcePath, ConvertOptions options)
	{
		string directoryName = Path.GetDirectoryName(sourcePath);
		if (options != null && options.SaveLocation == ConvertSaveLocation.CustomFolder && !string.IsNullOrWhiteSpace(options.CustomOutputFolder))
		{
			return options.CustomOutputFolder;
		}
		return directoryName;
	}

	public static string BuildDocumentOutputPath(string sourcePath, string outputFolder, string extension, ConvertSameNamePolicy policy, Func<string, ConvertConflictDecision> conflictResolver)
	{
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(sourcePath);
		return ResolveConflict(Path.Combine(outputFolder, fileNameWithoutExtension + extension), policy, conflictResolver);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string BuildImageFolder(string sourcePath, string outputFolder)
	{
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(sourcePath);
		string text = Path.Combine(outputFolder, fileNameWithoutExtension + "_图片");
		Directory.CreateDirectory(text);
		return text;
	}

	public static string ResolveConflict(string targetPath, ConvertSameNamePolicy policy, Func<string, ConvertConflictDecision> conflictResolver)
	{
		if (File.Exists(targetPath))
		{
			return policy switch
			{
				ConvertSameNamePolicy.Overwrite => targetPath, 
				ConvertSameNamePolicy.Cancel => null, 
				ConvertSameNamePolicy.AutoRename => AutoRename(targetPath), 
				_ => (conflictResolver?.Invoke(targetPath) ?? ConvertConflictDecision.Cancel) switch
				{
					ConvertConflictDecision.Overwrite => targetPath, 
					ConvertConflictDecision.AutoRename => AutoRename(targetPath), 
					_ => null, 
				}, 
			};
		}
		return targetPath;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string AutoRename(string path)
	{
		string directoryName = Path.GetDirectoryName(path);
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);
		string extension = Path.GetExtension(path);
		for (int i = 1; i < 10000; i++)
		{
			string text = Path.Combine(directoryName, fileNameWithoutExtension + "(" + i + ")" + extension);
			if (!File.Exists(text))
			{
				return text;
			}
		}
		return Path.Combine(directoryName, fileNameWithoutExtension + "_" + DateTime.Now.ToString("yyyyMMddHHmmss") + extension);
	}
}
