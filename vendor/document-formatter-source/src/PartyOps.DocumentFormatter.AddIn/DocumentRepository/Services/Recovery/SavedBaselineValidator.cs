using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Services.Recovery;

public static class SavedBaselineValidator
{
	private static readonly byte[] OleSignature = new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };

	public static bool IsUsable(string path)
	{
		return IsUsable(path, Path.GetExtension(path));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool IsUsable(string path, string expectedExtension)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
			{
				return false;
			}
			string text = (expectedExtension ?? string.Empty).Trim().ToLowerInvariant();
			using FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			if (fileStream.Length > 0)
			{
				switch (text)
				{
				case ".docx":
				case ".docm":
				case ".dotx":
				case ".dotm":
				{
					using ZipArchive zipArchive = new ZipArchive(fileStream, ZipArchiveMode.Read, leaveOpen: false);
					return zipArchive.GetEntry("[Content_Types].xml") != null && zipArchive.GetEntry("word/document.xml") != null;
				}
				case ".doc":
				case ".dot":
				{
					if (fileStream.Length < OleSignature.Length)
					{
						return false;
					}
					for (int i = 0; i < OleSignature.Length; i++)
					{
						if (fileStream.ReadByte() != OleSignature[i])
						{
							return false;
						}
					}
					break;
				}
				}
				return true;
			}
			return false;
		}
		catch
		{
			return false;
		}
	}
}
