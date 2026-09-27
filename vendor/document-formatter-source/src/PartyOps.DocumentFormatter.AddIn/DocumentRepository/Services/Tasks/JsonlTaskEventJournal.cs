using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using DocumentRepository.Models.Tasks;
using DocumentRepository.Services.Logging;

namespace DocumentRepository.Services.Tasks;

public sealed class JsonlTaskEventJournal : ITaskEventJournal
{
	private const int RetentionDays = 30;

	private const int MaxCleanupDeletesPerRun = 64;

	private const int MaxTokenLength = 128;

	private static readonly object SharedLock = new object();

	private static readonly object CleanupRootsLock = new object();

	private static readonly HashSet<string> CleanupRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private static JsonlTaskEventJournal shared;

	private readonly object syncRoot = new object();

	private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();

	private readonly string directoryRoot;

	private readonly Func<DateTime> nowProvider;

	private long sequence;

	public static JsonlTaskEventJournal Shared
	{
		get
		{
			lock (SharedLock)
			{
				if (shared == null)
				{
					shared = new JsonlTaskEventJournal();
				}
				return shared;
			}
		}
	}

	public JsonlTaskEventJournal()
		: this(ResolveDirectoryRoot(), () => DateTime.Now)
	{
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal JsonlTaskEventJournal(string directoryRoot, Func<DateTime> nowProvider)
	{
		if (!string.IsNullOrWhiteSpace(directoryRoot))
		{
			this.directoryRoot = Path.GetFullPath(directoryRoot);
			this.nowProvider = nowProvider ?? throw new ArgumentNullException("nowProvider");
			return;
		}
		throw new ArgumentException("审计日志根目录不能为空。", "directoryRoot");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string ResolveDirectoryRoot()
	{
		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DocumentRepository", "Tasks");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string ResolveFilePath(string taskId)
	{
		return Path.Combine(Path.Combine(ResolveDirectoryRoot(), DateTime.Now.ToString("yyyyMMdd")), SafeTaskIdForFile(taskId) + ".jsonl");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal string ResolveFilePathForTask(string taskId)
	{
		return Path.Combine(Path.Combine(directoryRoot, nowProvider().ToString("yyyyMMdd", CultureInfo.InvariantCulture)), SafeTaskIdForFile(taskId) + ".jsonl");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void Record(TaskEvent taskEvent)
	{
		if (taskEvent == null)
		{
			return;
		}
		try
		{
			CleanupOldDirectoriesOnce();
			lock (syncRoot)
			{
				taskEvent.Sequence = ++sequence;
				string path = ResolveFilePathForTask(taskEvent.TaskId);
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				EnsureTrailingNewline(path);
				string contents = serializer.Serialize((object)ToWhitelistedRecord(taskEvent)) + "\r\n";
				File.AppendAllText(path, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			}
		}
		catch (Exception ex)
		{
			LogService.Warn("JsonlTaskEventJournal.Record", ex);
		}
	}

	void ITaskEventJournal.Record(TaskEvent taskEvent)
	{
		this.Record(taskEvent);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static IDictionary<string, object> ToWhitelistedRecord(TaskEvent e)
	{
		return new Dictionary<string, object>
		{
			{ "SchemaVersion", e.SchemaVersion },
			{ "Sequence", e.Sequence },
			{
				"TimestampUtc",
				e.TimestampUtc.ToString("o", CultureInfo.InvariantCulture)
			},
			{
				"TaskId",
				SafeToken(e.TaskId)
			},
			{
				"FeatureId",
				SafeToken(e.FeatureId)
			},
			{
				"InvocationSource",
				SafeToken(e.InvocationSource)
			},
			{
				"EventType",
				SafeToken(e.EventType)
			},
			{
				"Stage",
				SafeToken(e.Stage)
			},
			{
				"Outcome",
				SafeToken(e.Outcome)
			},
			{
				"ReasonCode",
				SafeToken(e.ReasonCode)
			},
			{
				"DocumentFingerprint",
				Fingerprint(e.DocumentPath)
			},
			{ "ElapsedMilliseconds", e.ElapsedMilliseconds },
			{
				"ExceptionType",
				SafeToken(e.ExceptionType)
			}
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void EnsureTrailingNewline(string path)
	{
		try
		{
			if (!File.Exists(path))
			{
				return;
			}
			using FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
			if (fileStream.Length != 0L)
			{
				fileStream.Seek(-1L, SeekOrigin.End);
				int num = fileStream.ReadByte();
				if (num != 10 && num != -1)
				{
					byte[] array = new byte[2] { 13, 10 };
					fileStream.Write(array, 0, array.Length);
				}
			}
		}
		catch (Exception ex)
		{
			LogService.Warn("JsonlTaskEventJournal.EnsureTrailingNewline", ex);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void CleanupOldDirectoriesOnce()
	{
		lock (CleanupRootsLock)
		{
			if (CleanupRoots.Contains(directoryRoot))
			{
				return;
			}
			CleanupRoots.Add(directoryRoot);
		}
		try
		{
			if (!Directory.Exists(directoryRoot))
			{
				return;
			}
			DateTime dateTime = nowProvider().Date.AddDays(-30.0);
			List<KeyValuePair<DateTime, string>> list = new List<KeyValuePair<DateTime, string>>();
			foreach (string item in Directory.EnumerateDirectories(directoryRoot))
			{
				string fileName = Path.GetFileName(item);
				if (fileName != null && fileName.Length == 8 && DateTime.TryParseExact(fileName, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var result) && result < dateTime)
				{
					list.Add(new KeyValuePair<DateTime, string>(result, item));
				}
			}
			foreach (KeyValuePair<DateTime, string> item2 in list.OrderBy((KeyValuePair<DateTime, string> value) => value.Key).ThenBy<KeyValuePair<DateTime, string>, string>((KeyValuePair<DateTime, string> value) => value.Value, StringComparer.OrdinalIgnoreCase).Take(64))
			{
				try
				{
					Directory.Delete(item2.Value, recursive: true);
				}
				catch (Exception ex)
				{
					LogService.Warn("JsonlTaskEventJournal.Cleanup dir=" + Path.GetFileName(item2.Value), ex);
				}
			}
		}
		catch (Exception ex2)
		{
			LogService.Warn("JsonlTaskEventJournal.Cleanup", ex2);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string SafeTaskIdForFile(string value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			string text = value.Trim();
			if (text.Length <= 80)
			{
				bool flag = true;
				string text2 = text;
				foreach (char c in text2)
				{
					if (!IsAsciiLetterOrDigit(c) && c != '-' && c != '_')
					{
						flag = false;
						break;
					}
				}
				if (flag)
				{
					return text;
				}
			}
			return "task-" + Fingerprint(text);
		}
		return "unknown";
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string SafeToken(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}
		string text = value.Trim();
		if (text.Length <= 128)
		{
			bool flag = true;
			string text2 = text;
			foreach (char c in text2)
			{
				if (!IsAsciiLetterOrDigit(c) && c != '-' && c != '_' && c != '.' && c != ':' && c != '+' && c != '`')
				{
					flag = false;
					break;
				}
			}
			if (flag)
			{
				return text;
			}
		}
		return "redacted-" + Fingerprint(text);
	}

	private static bool IsAsciiLetterOrDigit(char ch)
	{
		if ((ch < 'a' || ch > 'z') && (ch < 'A' || ch > 'Z'))
		{
			if (ch >= '0')
			{
				return ch <= '9';
			}
			return false;
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string Fingerprint(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return string.Empty;
		}
		using SHA256 sHA = SHA256.Create();
		byte[] array = sHA.ComputeHash(Encoding.UTF8.GetBytes(path));
		StringBuilder stringBuilder = new StringBuilder(12);
		for (int i = 0; i < 6; i++)
		{
			stringBuilder.Append(array[i].ToString("x2"));
		}
		return stringBuilder.ToString();
	}
}
