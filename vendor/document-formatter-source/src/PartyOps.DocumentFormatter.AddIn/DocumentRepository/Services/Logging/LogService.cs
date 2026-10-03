using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace DocumentRepository.Services.Logging;

public static class LogService
{
	private const long MaxLogFileBytes = 2097152L;

	private const int ArchivedLogCount = 5;

	private static readonly object SyncRoot = new object();

	private static string _startupId = "none";

	public static string LogDirectoryPath
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		get
		{
			return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DocumentRepository");
		}
	}

	public static string LogFilePath
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		get
		{
			return Path.Combine(LogDirectoryPath, "plugin.log");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void SetStartupSession(string startupId)
	{
		_startupId = (string.IsNullOrWhiteSpace(startupId) ? "none" : startupId.Trim());
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Error(string context, Exception ex = null)
	{
		Write("ERROR", context, ex);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Warn(string context, Exception ex = null)
	{
		Write("WARN", context, ex);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Info(string context)
	{
		Write("INFO", context, null);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void LogEnvironment(string addInVersion, string hostName, string hostVersion)
	{
		string value = "unknown";
		int num = 0;
		try
		{
			using Process process = Process.GetCurrentProcess();
			value = process.ProcessName;
			num = process.Id;
		}
		catch
		{
		}
		Info("STARTUP_ENV addin=" + Safe(addInVersion) + " host=" + Safe(hostName) + " hostVersion=" + Safe(hostVersion) + " process=" + Safe(value) + " processId=" + num + " processArch=" + (Environment.Is64BitProcess ? "x64" : "x86") + " osArch=" + (Environment.Is64BitOperatingSystem ? "x64" : "x86") + " clr=" + Environment.Version?.ToString() + " os=" + Environment.OSVersion);
	}

	private static void Write(string level, string message, Exception ex)
	{
		try
		{
			Directory.CreateDirectory(LogDirectoryPath);
			string text = BuildEntry(level, message, ex);
			lock (SyncRoot)
			{
				RotateIfNeeded(LogFilePath, Encoding.UTF8.GetByteCount(text));
				using FileStream stream = new FileStream(LogFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
				using StreamWriter streamWriter = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
				streamWriter.Write(text);
			}
		}
		catch
		{
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string BuildEntry(string level, string message, Exception ex)
	{
		int value = 0;
		try
		{
			value = Process.GetCurrentProcess().Id;
		}
		catch
		{
		}
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append("] ");
		stringBuilder.Append('[').Append(level).Append("] ");
		stringBuilder.Append("[startup=").Append(_startupId).Append("] ");
		stringBuilder.Append("[pid=").Append(value).Append("] ");
		stringBuilder.Append("[tid=").Append(Thread.CurrentThread.ManagedThreadId).Append("] ");
		stringBuilder.Append(message ?? string.Empty).AppendLine();
		if (ex != null)
		{
			stringBuilder.Append("exceptionType=").Append(ex.GetType().Name).AppendLine();
			if (string.Equals(Environment.GetEnvironmentVariable("PARTYOPS_FORMATTER_DIAGNOSTIC_STACK"), "1", StringComparison.Ordinal))
			{
				stringBuilder.Append(ex).AppendLine();
			}
		}
		stringBuilder.AppendLine();
		return stringBuilder.ToString();
	}

	private static void RotateIfNeeded(string logFile, int incomingBytes)
	{
		try
		{
			FileInfo fileInfo = new FileInfo(logFile);
			if (!fileInfo.Exists || fileInfo.Length + incomingBytes <= 2097152)
			{
				return;
			}
			string archivePath = GetArchivePath(logFile, 5);
			if (File.Exists(archivePath))
			{
				File.Delete(archivePath);
			}
			for (int num = 4; num >= 1; num--)
			{
				string archivePath2 = GetArchivePath(logFile, num);
				string archivePath3 = GetArchivePath(logFile, num + 1);
				if (File.Exists(archivePath2))
				{
					File.Move(archivePath2, archivePath3);
				}
			}
			if (File.Exists(logFile))
			{
				File.Move(logFile, GetArchivePath(logFile, 1));
			}
		}
		catch
		{
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string GetArchivePath(string logFile, int index)
	{
		string text = Path.GetDirectoryName(logFile);
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(logFile);
		string extension = Path.GetExtension(logFile);
		if (text == null)
		{
			text = string.Empty;
		}
		return Path.Combine(text, fileNameWithoutExtension + "." + index + extension);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string Safe(string value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			return value.Replace("\r", " ").Replace("\n", " ");
		}
		return "unknown";
	}
}
