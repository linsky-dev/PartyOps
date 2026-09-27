using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Web.Script.Serialization;
using DocumentRepository.Models.Recovery;
using DocumentRepository.Services.Configuration;
using DocumentRepository.Services.FileSafety;
using DocumentRepository.Services.Logging;

namespace DocumentRepository.Services.Recovery;

public static class RecoveryManifestStore
{
	public const string ManifestFileName = "manifest.json";

	private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string GetManifestPath(string recoveryDirectory)
	{
		return Path.Combine(recoveryDirectory, "manifest.json");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void WriteAtomic(string recoveryDirectory, RecoveryManifest manifest)
	{
		if (!IsControlledDirectory(recoveryDirectory))
		{
			throw new InvalidOperationException("Recovery manifest directory is outside the controlled root.");
		}
		ValidateManifest(recoveryDirectory, manifest);
		AtomicFileService.WriteFileAtomically(GetManifestPath(recoveryDirectory), delegate(string tempPath)
		{
			string contents = Serializer.Serialize((object)manifest);
			File.WriteAllText(tempPath, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		}, delegate(string tempPath)
		{
			RecoveryManifest manifest2 = Serializer.Deserialize<RecoveryManifest>(File.ReadAllText(tempPath));
			ValidateManifest(recoveryDirectory, manifest2);
		});
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static RecoveryManifest TryRead(string recoveryDirectory)
	{
		try
		{
			if (!IsControlledDirectory(recoveryDirectory))
			{
				return null;
			}
			string manifestPath = GetManifestPath(recoveryDirectory);
			if (File.Exists(manifestPath))
			{
				RecoveryManifest recoveryManifest = Serializer.Deserialize<RecoveryManifest>(File.ReadAllText(manifestPath));
				ValidateManifest(recoveryDirectory, recoveryManifest);
				return recoveryManifest;
			}
			return null;
		}
		catch (Exception exception)
		{
			WarnSafe("RecoveryManifestStore.TryRead", exception);
			return null;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool TryUpdateState(string recoveryDirectory, RecoveryCopyState newState, DateTime updatedAtUtc)
	{
		try
		{
			RecoveryManifest recoveryManifest = TryRead(recoveryDirectory);
			if (recoveryManifest == null)
			{
				return false;
			}
			if (!Enum.TryParse<RecoveryCopyState>(recoveryManifest.State, out var result))
			{
				return false;
			}
			if (result == newState)
			{
				return true;
			}
			if (result != RecoveryCopyState.Prepared)
			{
				return false;
			}
			recoveryManifest.State = newState.ToString();
			recoveryManifest.StateUpdatedAtUtc = updatedAtUtc;
			WriteAtomic(recoveryDirectory, recoveryManifest);
			return true;
		}
		catch (Exception exception)
		{
			WarnSafe("RecoveryManifestStore.TryUpdateState", exception);
			return false;
		}
	}

	public static bool IsControlledDirectory(string directory)
	{
		if (!IsControlledDirectory(directory, ApplicationDataPaths.RecoveryRoot))
		{
			return IsControlledDirectory(directory, ApplicationDataPaths.RecoveryFallbackRoot);
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool IsControlledDirectory(string directory, string rootPath)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(directory))
			{
				return false;
			}
			string fullPath = Path.GetFullPath(directory);
			string fullPath2 = Path.GetFullPath(rootPath);
			if (IsControlledRoot(rootPath))
			{
				if (!string.Equals(Path.GetDirectoryName(fullPath.TrimEnd('\\', '/')), fullPath2.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
				{
					return false;
				}
				if (Directory.Exists(fullPath))
				{
					return (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) == 0;
				}
				return false;
			}
			return false;
		}
		catch (Exception exception)
		{
			WarnSafe("RecoveryManifestStore.IsControlledDirectory", exception);
			return false;
		}
	}

	public static bool IsControlledRoot()
	{
		return IsControlledRoot(ApplicationDataPaths.RecoveryRoot);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool IsControlledRoot(string rootPath)
	{
		try
		{
			string fullPath = Path.GetFullPath(rootPath);
			if (!Directory.Exists(fullPath))
			{
				return false;
			}
			return (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) == 0;
		}
		catch (Exception exception)
		{
			WarnSafe("RecoveryManifestStore.IsControlledRoot", exception);
			return false;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ValidateManifest(string recoveryDirectory, RecoveryManifest manifest)
	{
		if (manifest == null || manifest.SchemaVersion != 1)
		{
			throw new InvalidDataException("Recovery manifest schema is invalid.");
		}
		string fileName = Path.GetFileName(Path.GetFullPath(recoveryDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		if (string.IsNullOrWhiteSpace(manifest.RecoveryId) || !string.Equals(fileName, manifest.RecoveryId, StringComparison.Ordinal))
		{
			throw new InvalidDataException("Recovery manifest identity does not match its directory.");
		}
		if (!string.IsNullOrWhiteSpace(manifest.TaskId) && !string.IsNullOrWhiteSpace(manifest.FeatureId) && !string.IsNullOrWhiteSpace(manifest.DocumentLifecycleId) && !string.IsNullOrWhiteSpace(manifest.SourceFullPath) && Path.IsPathRooted(manifest.SourceFullPath) && !string.IsNullOrWhiteSpace(manifest.SourceFileName) && !string.IsNullOrWhiteSpace(manifest.CopyFileName) && string.Equals(Path.GetFileName(manifest.CopyFileName), manifest.CopyFileName, StringComparison.Ordinal) && manifest.SourceLength >= 0 && IsSha256(manifest.SourceSha256) && IsSha256(manifest.CopySha256) && !(manifest.CreatedAtUtc == default(DateTime)) && !(manifest.StateUpdatedAtUtc == default(DateTime)) && !(manifest.StateUpdatedAtUtc < manifest.CreatedAtUtc))
		{
			if (Enum.TryParse<RecoveryCopyState>(manifest.State, out var _) && Enum.TryParse<RecoveryCoverage>(manifest.Coverage, out var _))
			{
				return;
			}
			throw new InvalidDataException("Recovery manifest state or coverage is invalid.");
		}
		throw new InvalidDataException("Recovery manifest required fields are invalid.");
	}

	private static bool IsSha256(string value)
	{
		if (!string.IsNullOrWhiteSpace(value) && value.Length == 64)
		{
			foreach (char c in value)
			{
				if ((c < '0' || c > '9') && (c < 'a' || c > 'f') && (c < 'A' || c > 'F'))
				{
					return false;
				}
			}
			return true;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void WarnSafe(string context, Exception exception)
	{
		LogService.Warn(context + ", exception=" + ((exception == null) ? "unknown" : exception.GetType().Name));
	}
}
