using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using DocumentRepository.Services.Logging;

namespace DocumentRepository;

internal static class SafeProcessLauncher
{
	public static bool OpenHttpsUrl(string url, params string[] allowedHosts)
	{
		if (!TryValidateHttpsUrl(url, out var uri, allowedHosts))
		{
			return false;
		}
		return Start(new ProcessStartInfo(uri.ToString())
		{
			UseShellExecute = true
		});
	}

	public static bool OpenFileOrDirectory(string path)
	{
		return OpenFileOrDirectoryCore(path, redactFailureDetails: false);
	}

	internal static bool OpenSensitiveFileOrDirectory(string path)
	{
		return OpenFileOrDirectoryCore(path, redactFailureDetails: true);
	}

	private static bool OpenFileOrDirectoryCore(string path, bool redactFailureDetails)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return false;
		}
		string fullPath = Path.GetFullPath(path);
		if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
		{
			return false;
		}
		return Start(new ProcessStartInfo(fullPath)
		{
			UseShellExecute = true
		}, redactFailureDetails);
	}

	public static bool RevealFileInExplorer(string path)
	{
		return RevealFileInExplorerCore(path, redactFailureDetails: false);
	}

	internal static bool RevealSensitiveFileInExplorer(string path)
	{
		return RevealFileInExplorerCore(path, redactFailureDetails: true);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool RevealFileInExplorerCore(string path, bool redactFailureDetails)
	{
		if (!string.IsNullOrWhiteSpace(path))
		{
			string fullPath = Path.GetFullPath(path);
			if (File.Exists(fullPath))
			{
				string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
				if (!File.Exists(text))
				{
					return false;
				}
				return Start(new ProcessStartInfo
				{
					FileName = text,
					Arguments = "/select,\"" + fullPath + "\"",
					UseShellExecute = false
				}, redactFailureDetails);
			}
			return false;
		}
		return false;
	}

	public static bool TryValidateHttpsUrl(string url, out Uri uri, params string[] allowedHosts)
	{
		uri = null;
		if (Uri.TryCreate(url, UriKind.Absolute, out uri))
		{
			if (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
			{
				if (allowedHosts == null || allowedHosts.Length == 0)
				{
					return true;
				}
				string a = (uri.Host ?? string.Empty).ToLowerInvariant();
				foreach (string text in allowedHosts)
				{
					if (string.Equals(a, (text ?? string.Empty).ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
					{
						return true;
					}
				}
				return false;
			}
			return false;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool Start(ProcessStartInfo psi, bool redactFailureDetails = false)
	{
		try
		{
			Process.Start(psi);
			return true;
		}
		catch (Exception ex)
		{
			if (redactFailureDetails)
			{
				LogService.Warn("SafeProcessLauncher.Start, exception=" + ex.GetType().Name);
			}
			else
			{
				LogService.Error("SafeProcessLauncher.Start", ex);
			}
			return false;
		}
	}
}
