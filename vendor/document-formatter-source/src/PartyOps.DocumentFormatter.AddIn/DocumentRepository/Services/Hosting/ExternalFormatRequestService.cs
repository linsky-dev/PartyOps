using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Hosting;

internal static class ExternalFormatRequestService
{
	private static readonly TimeSpan RequestLifetime = TimeSpan.FromMinutes(5.0);

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static void Enqueue(string sourcePath)
	{
		string s = NormalizeSupportedPath(sourcePath);
		string queueDirectory = GetQueueDirectory();
		Directory.CreateDirectory(queueDirectory);
		string text = Guid.NewGuid().ToString("N");
		string text2 = Path.Combine(queueDirectory, text + ".request");
		string text3 = text2 + ".tmp";
		string contents = DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture) + Environment.NewLine + Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
		File.WriteAllText(text3, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		File.Move(text3, text2);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static bool HasPendingRequests()
	{
		try
		{
			string queueDirectory = GetQueueDirectory();
			return Directory.Exists(queueDirectory) && Directory.EnumerateFiles(queueDirectory, "*.request").Any();
		}
		catch
		{
			return false;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static bool TryActivateNextRequest(Application application)
	{
		if (application == null)
		{
			return false;
		}
		string queueDirectory = GetQueueDirectory();
		if (!Directory.Exists(queueDirectory))
		{
			return false;
		}
		foreach (string item in Directory.GetFiles(queueDirectory, "*.request").OrderBy<string, string>((string path) => path, StringComparer.OrdinalIgnoreCase))
		{
			if (TryReadRequest(item, out var sourcePath, out var createdUtc) && !(DateTime.UtcNow - createdUtc > RequestLifetime))
			{
				if (TryActivateMatchingDocument(application, sourcePath))
				{
					TryDelete(item);
					return true;
				}
			}
			else
			{
				TryDelete(item);
			}
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool TryActivateMatchingDocument(Application application, string requestedPath)
	{
		Documents value = null;
		try
		{
			value = application.Documents;
			int count = value.Count;
			for (int i = 1; i <= count; i++)
			{
				Document value2 = null;
				try
				{
					Documents documents = value;
					object Index = i;
					value2 = documents.get_Item(ref Index);
					if (!string.Equals(Path.GetFullPath(value2.FullName), requestedPath, StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}
					value2.Activate();
					return true;
				}
				catch
				{
				}
				finally
				{
					ComObjectRelease.Release(ref value2, "ExternalFormatRequestService.Document");
				}
			}
		}
		catch
		{
			return false;
		}
		finally
		{
			ComObjectRelease.Release(ref value, "ExternalFormatRequestService.Documents");
		}
		return false;
	}

	private static bool TryReadRequest(string requestFile, out string sourcePath, out DateTime createdUtc)
	{
		sourcePath = null;
		createdUtc = default(DateTime);
		try
		{
			string[] array = File.ReadAllLines(requestFile, Encoding.UTF8);
			if (array.Length != 2 || !long.TryParse(array[0], NumberStyles.None, CultureInfo.InvariantCulture, out var result))
			{
				return false;
			}
			createdUtc = new DateTime(result, DateTimeKind.Utc);
			sourcePath = NormalizeSupportedPath(Encoding.UTF8.GetString(Convert.FromBase64String(array[1])));
			return File.Exists(sourcePath);
		}
		catch
		{
			return false;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string NormalizeSupportedPath(string sourcePath)
	{
		if (string.IsNullOrWhiteSpace(sourcePath))
		{
			throw new ArgumentException("sourcePath");
		}
		string fullPath = Path.GetFullPath(sourcePath.Trim());
		string extension = Path.GetExtension(fullPath);
		if (!string.Equals(extension, ".docx", StringComparison.OrdinalIgnoreCase) && !string.Equals(extension, ".doc", StringComparison.OrdinalIgnoreCase) && !string.Equals(extension, ".wps", StringComparison.OrdinalIgnoreCase))
		{
			throw new NotSupportedException("unsupported-document-type");
		}
		return fullPath;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string GetQueueDirectory()
	{
		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DocumentRepository", "ExternalFormatRequests");
	}

	private static void TryDelete(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch
		{
		}
	}
}
