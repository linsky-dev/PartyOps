using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using DocumentRepository.Models.Recovery;
using DocumentRepository.Services.Configuration;
using DocumentRepository.Services.Logging;

namespace DocumentRepository.Services.Recovery;

public static class RecoveryRetentionService
{
	public sealed class CleanupStats
	{
		public int Examined;

		public int Deleted;

		public int Protected;

		public long BytesFreed;

		public long RemainingBytes;

		public bool StoppedEarly;

		public int FallbackExamined;

		public int FallbackDeleted;

		public int ReuseLogExamined;

		public int ReuseLogDeleted;
	}

	public sealed class QuotaEvaluation
	{
		public bool HasCapacity;

		public bool HasDiskSpace;

		public bool UsageReliable;
	}

	private sealed class StorageUsage
	{
		public long Bytes;

		public bool Reliable = true;
	}

	private static int cleanupAttempted;

	private static bool? diskSpaceOverrideForTesting;

	internal static void SetDiskSpaceOverrideForTesting(bool? hasDiskSpace)
	{
		diskSpaceOverrideForTesting = hasDiskSpace;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void CleanupOnce()
	{
		if (Interlocked.Exchange(ref cleanupAttempted, 1) != 0)
		{
			return;
		}
		try
		{
			Cleanup(DateTime.UtcNow, RecoveryRetentionPolicy.CleanupBudget);
		}
		catch (Exception exception)
		{
			WarnSafe("RecoveryRetentionService.CleanupOnce", exception);
		}
	}

	public static QuotaEvaluation EvaluateQuota(long incomingBytes)
	{
		return EvaluateQuota(incomingBytes, ApplicationDataPaths.RecoveryRoot, 2147483648L);
	}

	public static QuotaEvaluation EvaluateFallbackQuota(long incomingBytes)
	{
		return EvaluateQuota(incomingBytes, ApplicationDataPaths.RecoveryFallbackRoot, 1073741824L);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static QuotaEvaluation EvaluateQuota(long incomingBytes, string rootPath, long quotaBytes)
	{
		if (incomingBytes >= 0)
		{
			StorageUsage storageUsage = MeasureUsage(rootPath);
			if (storageUsage.Reliable && WouldExceedQuota(storageUsage.Bytes, incomingBytes, quotaBytes))
			{
				try
				{
					Cleanup(DateTime.UtcNow, RecoveryRetentionPolicy.CleanupBudget);
				}
				catch (Exception exception)
				{
					WarnSafe("RecoveryRetentionService.EvaluateQuota.Cleanup", exception);
				}
				storageUsage = MeasureUsage(rootPath);
				if (storageUsage.Reliable && WouldExceedQuota(storageUsage.Bytes, incomingBytes, quotaBytes))
				{
					try
					{
						Cleanup(DateTime.UtcNow, RecoveryRetentionPolicy.CleanupBudget);
					}
					catch (Exception exception2)
					{
						WarnSafe("RecoveryRetentionService.EvaluateQuota.Cleanup2", exception2);
					}
					storageUsage = MeasureUsage(rootPath);
				}
			}
			bool hasCapacity = storageUsage.Reliable && !WouldExceedQuota(storageUsage.Bytes, incomingBytes, quotaBytes);
			bool flag = true;
			if (diskSpaceOverrideForTesting.HasValue)
			{
				flag = diskSpaceOverrideForTesting.Value;
			}
			else
			{
				try
				{
					DriveInfo driveInfo = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(rootPath)));
					long num = ((incomingBytes > 4611686018427387903L) ? long.MaxValue : (incomingBytes * 2));
					flag = driveInfo.AvailableFreeSpace > num;
				}
				catch (Exception exception3)
				{
					WarnSafe("RecoveryRetentionService.EvaluateQuota.DriveInfo", exception3);
					flag = false;
				}
			}
			return new QuotaEvaluation
			{
				HasCapacity = hasCapacity,
				HasDiskSpace = flag,
				UsageReliable = storageUsage.Reliable
			};
		}
		return new QuotaEvaluation
		{
			HasCapacity = false,
			HasDiskSpace = false,
			UsageReliable = false
		};
	}

	public static CleanupStats Cleanup(DateTime nowUtc, TimeSpan budget)
	{
		CleanupStats cleanupStats = new CleanupStats();
		DateTime utcNow = DateTime.UtcNow;
		string recoveryRoot = ApplicationDataPaths.RecoveryRoot;
		if (Directory.Exists(recoveryRoot))
		{
			if (RecoveryManifestStore.IsControlledRoot())
			{
				List<KeyValuePair<DateTime, string>> list = new List<KeyValuePair<DateTime, string>>();
				foreach (string item in Directory.EnumerateDirectories(recoveryRoot))
				{
					if (cleanupStats.Examined < 64 && !(DateTime.UtcNow - utcNow > budget))
					{
						cleanupStats.Examined++;
						if (!RecoveryManifestStore.IsControlledDirectory(item))
						{
							cleanupStats.Protected++;
							continue;
						}
						RecoveryManifest recoveryManifest = RecoveryManifestStore.TryRead(item);
						if (recoveryManifest != null)
						{
							if (Enum.TryParse<RecoveryCopyState>(recoveryManifest.State, out var result))
							{
								if (RecoveryRetentionPolicy.IsExpired(result, recoveryManifest.StateUpdatedAtUtc, nowUtc))
								{
									list.Add(new KeyValuePair<DateTime, string>(recoveryManifest.StateUpdatedAtUtc, item));
								}
							}
							else
							{
								cleanupStats.Protected++;
							}
						}
						else
						{
							cleanupStats.Protected++;
						}
						continue;
					}
					cleanupStats.StoppedEarly = true;
					break;
				}
				list.Sort((KeyValuePair<DateTime, string> a, KeyValuePair<DateTime, string> b) => a.Key.CompareTo(b.Key));
				foreach (KeyValuePair<DateTime, string> item2 in list)
				{
					if (cleanupStats.Deleted >= 64 || DateTime.UtcNow - utcNow > budget)
					{
						cleanupStats.StoppedEarly = true;
						break;
					}
					long num = DirectorySize(item2.Value);
					if (num < 0)
					{
						cleanupStats.Protected++;
					}
					else if (TryDeleteControlled(item2.Value))
					{
						cleanupStats.Deleted++;
						cleanupStats.BytesFreed += num;
					}
				}
				cleanupStats.RemainingBytes = MeasureUsage(recoveryRoot).Bytes;
				CleanupFallbackRoot(nowUtc, budget, cleanupStats);
				CleanupReuseLog(nowUtc, budget, cleanupStats);
				return cleanupStats;
			}
			cleanupStats.Protected++;
			return cleanupStats;
		}
		return cleanupStats;
	}

	private static void CleanupFallbackRoot(DateTime nowUtc, TimeSpan budget, CleanupStats stats)
	{
		string recoveryFallbackRoot = ApplicationDataPaths.RecoveryFallbackRoot;
		if (!Directory.Exists(recoveryFallbackRoot))
		{
			return;
		}
		if (!RecoveryManifestStore.IsControlledRoot(recoveryFallbackRoot))
		{
			stats.Protected++;
			return;
		}
		DateTime utcNow = DateTime.UtcNow;
		List<KeyValuePair<DateTime, string>> list = new List<KeyValuePair<DateTime, string>>();
		foreach (string item in Directory.EnumerateDirectories(recoveryFallbackRoot))
		{
			if (stats.FallbackExamined >= 64 || DateTime.UtcNow - utcNow > budget)
			{
				stats.StoppedEarly = true;
				break;
			}
			stats.FallbackExamined++;
			if (!RecoveryManifestStore.IsControlledDirectory(item, recoveryFallbackRoot))
			{
				stats.Protected++;
				continue;
			}
			RecoveryManifest recoveryManifest = RecoveryManifestStore.TryRead(item);
			if (recoveryManifest != null)
			{
				if (Enum.TryParse<RecoveryCopyState>(recoveryManifest.State, out var result))
				{
					if (RecoveryRetentionPolicy.IsExpired(result, recoveryManifest.StateUpdatedAtUtc, nowUtc))
					{
						list.Add(new KeyValuePair<DateTime, string>(recoveryManifest.StateUpdatedAtUtc, item));
					}
				}
				else
				{
					stats.Protected++;
				}
			}
			else
			{
				stats.Protected++;
			}
		}
		list.Sort((KeyValuePair<DateTime, string> a, KeyValuePair<DateTime, string> b) => a.Key.CompareTo(b.Key));
		foreach (KeyValuePair<DateTime, string> item2 in list)
		{
			if (stats.FallbackDeleted >= 64 || DateTime.UtcNow - utcNow > budget)
			{
				stats.StoppedEarly = true;
				break;
			}
			long num = DirectorySize(item2.Value);
			if (num < 0)
			{
				stats.Protected++;
			}
			else if (TryDeleteControlled(item2.Value))
			{
				stats.FallbackDeleted++;
				stats.BytesFreed += num;
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void CleanupReuseLog(DateTime nowUtc, TimeSpan budget, CleanupStats stats)
	{
		string reuseLogRoot = ApplicationDataPaths.ReuseLogRoot;
		if (!Directory.Exists(reuseLogRoot))
		{
			return;
		}
		DateTime utcNow = DateTime.UtcNow;
		foreach (string item in Directory.EnumerateDirectories(reuseLogRoot))
		{
			if (stats.ReuseLogExamined >= 64 || DateTime.UtcNow - utcNow > budget)
			{
				stats.StoppedEarly = true;
				break;
			}
			stats.ReuseLogExamined++;
			try
			{
				DirectoryInfo directoryInfo = new DirectoryInfo(item);
				if ((directoryInfo.Attributes & FileAttributes.ReparsePoint) != (FileAttributes)0)
				{
					stats.Protected++;
				}
				else
				{
					if (!((nowUtc - directoryInfo.LastWriteTimeUtc).TotalDays > 3.0))
					{
						continue;
					}
					foreach (string item2 in Directory.EnumerateFiles(item, "*.json", SearchOption.TopDirectoryOnly))
					{
						File.Delete(item2);
					}
					if (Directory.GetFiles(item).Length == 0 && Directory.GetDirectories(item).Length == 0)
					{
						Directory.Delete(item, recursive: false);
						stats.ReuseLogDeleted++;
					}
					continue;
				}
			}
			catch (Exception exception)
			{
				WarnSafe("RecoveryRetentionService.CleanupReuseLog", exception);
				stats.Protected++;
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool TryDeleteControlled(string directory)
	{
		try
		{
			if (RecoveryManifestStore.IsControlledDirectory(directory))
			{
				RecoveryManifest recoveryManifest = RecoveryManifestStore.TryRead(directory);
				if (recoveryManifest == null)
				{
					return false;
				}
				if (Directory.GetDirectories(directory).Length == 0)
				{
					string manifestPath = RecoveryManifestStore.GetManifestPath(directory);
					string text = Path.Combine(directory, recoveryManifest.CopyFileName);
					string[] files = Directory.GetFiles(directory);
					foreach (string text2 in files)
					{
						if ((File.GetAttributes(text2) & FileAttributes.ReparsePoint) != (FileAttributes)0)
						{
							return false;
						}
						if (!string.Equals(text2, manifestPath, StringComparison.OrdinalIgnoreCase) && !string.Equals(text2, text, StringComparison.OrdinalIgnoreCase))
						{
							return false;
						}
					}
					if (File.Exists(text))
					{
						File.Delete(text);
					}
					if (File.Exists(manifestPath))
					{
						File.Delete(manifestPath);
					}
					Directory.Delete(directory, recursive: false);
					return true;
				}
				return false;
			}
			return false;
		}
		catch (Exception exception)
		{
			WarnSafe("RecoveryRetentionService.TryDeleteControlled", exception);
			return false;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static StorageUsage MeasureUsage(string root)
	{
		StorageUsage storageUsage = new StorageUsage();
		checked
		{
			try
			{
				if (!Directory.Exists(root))
				{
					return storageUsage;
				}
				if (!RecoveryManifestStore.IsControlledRoot(root))
				{
					storageUsage.Reliable = false;
					return storageUsage;
				}
				foreach (string item in Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly))
				{
					try
					{
						if ((File.GetAttributes(item) & FileAttributes.ReparsePoint) != (FileAttributes)0)
						{
							storageUsage.Reliable = false;
						}
						else
						{
							storageUsage.Bytes += new FileInfo(item).Length;
						}
					}
					catch
					{
						storageUsage.Reliable = false;
					}
				}
				foreach (string item2 in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly))
				{
					try
					{
						if ((File.GetAttributes(item2) & FileAttributes.ReparsePoint) == 0)
						{
							if (string.Equals(Path.GetFileName(item2), "reuse-log", StringComparison.OrdinalIgnoreCase))
							{
								continue;
							}
							using (IEnumerator<string> enumerator2 = Directory.EnumerateDirectories(item2, "*", SearchOption.TopDirectoryOnly).GetEnumerator())
							{
								if (enumerator2.MoveNext())
								{
									_ = enumerator2.Current;
									storageUsage.Reliable = false;
								}
							}
							foreach (string item3 in Directory.EnumerateFiles(item2, "*", SearchOption.TopDirectoryOnly))
							{
								if ((File.GetAttributes(item3) & FileAttributes.ReparsePoint) == 0)
								{
									storageUsage.Bytes += new FileInfo(item3).Length;
								}
								else
								{
									storageUsage.Reliable = false;
								}
							}
							continue;
						}
						storageUsage.Reliable = false;
					}
					catch
					{
						storageUsage.Reliable = false;
					}
				}
				return storageUsage;
			}
			catch (Exception exception)
			{
				WarnSafe("RecoveryRetentionService.MeasureUsage", exception);
				storageUsage.Reliable = false;
				return storageUsage;
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static long DirectorySize(string dir)
	{
		try
		{
			if (Directory.GetDirectories(dir).Length == 0)
			{
				long num = 0L;
				foreach (string item in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly))
				{
					if ((File.GetAttributes(item) & FileAttributes.ReparsePoint) != (FileAttributes)0)
					{
						return -1L;
					}
					num = checked(num + new FileInfo(item).Length);
				}
				return num;
			}
			return -1L;
		}
		catch
		{
			return -1L;
		}
	}

	private static bool WouldExceedQuota(long currentBytes, long incomingBytes)
	{
		return WouldExceedQuota(currentBytes, incomingBytes, 2147483648L);
	}

	private static bool WouldExceedQuota(long currentBytes, long incomingBytes, long quotaBytes)
	{
		return currentBytes > quotaBytes - incomingBytes;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void WarnSafe(string context, Exception exception)
	{
		LogService.Warn(context + ", exception=" + ((exception == null) ? "unknown" : exception.GetType().Name));
	}
}
