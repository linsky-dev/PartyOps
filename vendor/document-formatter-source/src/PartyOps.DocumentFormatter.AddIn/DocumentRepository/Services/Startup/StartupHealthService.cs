using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using DocumentRepository.Services.Logging;

namespace DocumentRepository.Services.Startup;

internal static class StartupHealthService
{
	private const long MaxHistoryBytes = 1048576L;

	private const int HistoryArchiveCount = 3;

	private static readonly object SyncRoot = new object();

	private static string _startupId = "none";

	private static string _version = "unknown";

	private static string _hostName = "unknown";

	private static string _hostVersion = "unknown";

	private static string _stateFilePath;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Begin(string startupId, string version, string hostName, string hostVersion)
	{
		try
		{
			lock (SyncRoot)
			{
				_startupId = startupId;
				_version = version;
				_hostName = hostName;
				_hostVersion = hostVersion;
				_stateFilePath = BuildStateFilePath();
				WarnIfPreviousStartupWasIncomplete(ReadState(_stateFilePath));
				WriteState("startup-begin", "bootstrap", string.Empty);
			}
		}
		catch (Exception ex)
		{
			LogService.Warn("StartupHealth.Begin", ex);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Mark(string status, string stage, string detail = null)
	{
		try
		{
			lock (SyncRoot)
			{
				if (string.IsNullOrWhiteSpace(_stateFilePath))
				{
					_stateFilePath = BuildStateFilePath();
				}
				WriteState(status, stage, detail);
			}
		}
		catch (Exception ex)
		{
			LogService.Warn("StartupHealth.Mark status=" + status + " stage=" + stage, ex);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string BuildStateFilePath()
	{
		string text = "office-host";
		try
		{
			using Process process = Process.GetCurrentProcess();
			text = process.ProcessName;
		}
		catch
		{
		}
		char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
		foreach (char oldChar in invalidFileNameChars)
		{
			text = text.Replace(oldChar, '_');
		}
		return Path.Combine(LogService.LogDirectoryPath, "startup-health-" + text + ".state");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void WarnIfPreviousStartupWasIncomplete(Dictionary<string, string> previous)
	{
		if (previous != null && previous.Count != 0)
		{
			string value = GetValue(previous, "Status");
			if (!string.Equals(value, "shutdown-complete", StringComparison.OrdinalIgnoreCase) && (!int.TryParse(GetValue(previous, "ProcessId"), out var result) || !IsProcessRunning(result)))
			{
				string text = (string.Equals(value, "startup-complete", StringComparison.OrdinalIgnoreCase) ? "STARTUP_PREVIOUS_UNCLEAN_SHUTDOWN" : "STARTUP_PREVIOUS_INCOMPLETE");
				LogService.Warn(text + " previousStartup=" + GetValue(previous, "StartupId") + " status=" + value + " stage=" + GetValue(previous, "Stage") + " timestamp=" + GetValue(previous, "Timestamp"));
			}
		}
	}

	private static bool IsProcessRunning(int processId)
	{
		if (processId <= 0)
		{
			return false;
		}
		try
		{
			using Process process = Process.GetProcessById(processId);
			return !process.HasExited;
		}
		catch
		{
			return false;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void WriteState(string status, string stage, string detail)
	{
		Directory.CreateDirectory(LogService.LogDirectoryPath);
		string text = _stateFilePath + ".tmp." + Guid.NewGuid().ToString("N");
		int processId = 0;
		try
		{
			processId = Process.GetCurrentProcess().Id;
		}
		catch
		{
		}
		string[] contents = new string[12]
		{
			"StartupId=" + Clean(_startupId),
			"Status=" + Clean(status),
			"Stage=" + Clean(stage),
			"Detail=" + Clean(detail),
			"Timestamp=" + DateTime.Now.ToString("O", CultureInfo.InvariantCulture),
			"Version=" + Clean(_version),
			"Host=" + Clean(_hostName),
			"HostVersion=" + Clean(_hostVersion),
			"ProcessId=" + processId,
			"ThreadId=" + Thread.CurrentThread.ManagedThreadId,
			"ProcessArch=" + (Environment.Is64BitProcess ? "x64" : "x86"),
			"OsArch=" + (Environment.Is64BitOperatingSystem ? "x64" : "x86")
		};
		File.WriteAllLines(text, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		try
		{
			if (File.Exists(_stateFilePath))
			{
				File.Delete(_stateFilePath);
			}
			File.Move(text, _stateFilePath);
			AppendHistory(status, stage, detail, processId);
		}
		finally
		{
			if (File.Exists(text))
			{
				File.Delete(text);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void AppendHistory(string status, string stage, string detail, int processId)
	{
		try
		{
			string path = Path.Combine(LogService.LogDirectoryPath, "startup-health-history.log");
			string text = DateTime.Now.ToString("O", CultureInfo.InvariantCulture) + " startup=" + Clean(_startupId) + " status=" + Clean(status) + " stage=" + Clean(stage) + " detail=" + Clean(detail) + " version=" + Clean(_version) + " host=" + Clean(_hostName) + " hostVersion=" + Clean(_hostVersion) + " pid=" + processId + " tid=" + Thread.CurrentThread.ManagedThreadId + Environment.NewLine;
			RotateHistoryIfNeeded(path, Encoding.UTF8.GetByteCount(text));
			File.AppendAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		}
		catch (Exception ex)
		{
			LogService.Warn("StartupHealth.AppendHistory", ex);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void RotateHistoryIfNeeded(string path, int incomingBytes)
	{
		FileInfo fileInfo = new FileInfo(path);
		if (!fileInfo.Exists || fileInfo.Length + incomingBytes <= 1048576)
		{
			return;
		}
		string path2 = path + "." + 3;
		if (File.Exists(path2))
		{
			File.Delete(path2);
		}
		for (int num = 2; num >= 1; num--)
		{
			string text = path + "." + num;
			string destFileName = path + "." + (num + 1);
			if (File.Exists(text))
			{
				File.Move(text, destFileName);
			}
		}
		File.Move(path, path + ".1");
	}

	private static Dictionary<string, string> ReadState(string path)
	{
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (File.Exists(path))
		{
			string[] array = File.ReadAllLines(path, Encoding.UTF8);
			foreach (string text in array)
			{
				int num = text.IndexOf('=');
				if (num > 0)
				{
					dictionary[text.Substring(0, num)] = text.Substring(num + 1);
				}
			}
			return dictionary;
		}
		return dictionary;
	}

	private static string GetValue(IDictionary<string, string> values, string key)
	{
		if (values == null || !values.TryGetValue(key, out var value))
		{
			return string.Empty;
		}
		return value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string Clean(string value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			return value.Replace("\r", " ").Replace("\n", " ");
		}
		return string.Empty;
	}
}
