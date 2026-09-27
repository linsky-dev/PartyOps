using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;
using DocumentRepository.Models.Rules;
using DocumentRepository.Services.FileSafety;
using DocumentRepository.Services.Logging;

namespace DocumentRepository.Services.Rules;

public static class XmlRuleStore
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static RuleLoadResult<T> Load<T>(RuleStoreDescriptor descriptor, Func<T> createDefault, Func<T, T> normalize)
	{
		if (descriptor != null)
		{
			descriptor.EnsureValid();
			if (createDefault != null)
			{
				string storePath = descriptor.StorePath;
				if (!File.Exists(storePath))
				{
					try
					{
						T value = Normalize(createDefault(), normalize);
						Save(storePath, value, normalize);
						LogService.Info("XmlRuleStore default store created (missing file). StoreId=" + descriptor.Id);
						return RuleLoadResult<T>.Success(RuleLoadStatus.CreatedDefault, value, descriptor.Id, storePath, "rule-store-created-default");
					}
					catch (Exception ex)
					{
						LogService.Warn("XmlRuleStore could not create default store. StoreId=" + descriptor.Id, ex);
						return RuleLoadResult<T>.Failure(RuleLoadStatus.WriteFailed, descriptor.Id, storePath, "rule-store-create-default-failed", ex);
					}
				}
				T val;
				try
				{
					using FileStream fileStream = File.OpenRead(storePath);
					val = (T)new XmlSerializer(typeof(T)).Deserialize((Stream)fileStream);
					if (val == null)
					{
						throw new InvalidDataException("Rule store is empty.");
					}
				}
				catch (UnauthorizedAccessException ex2)
				{
					LogService.Warn("XmlRuleStore read failed (access). StoreId=" + descriptor.Id, ex2);
					return RuleLoadResult<T>.Failure(RuleLoadStatus.ReadFailed, descriptor.Id, storePath, "rule-store-read-failed", ex2);
				}
				catch (IOException ex3)
				{
					LogService.Warn("XmlRuleStore read failed (io). StoreId=" + descriptor.Id, ex3);
					return RuleLoadResult<T>.Failure(RuleLoadStatus.ReadFailed, descriptor.Id, storePath, "rule-store-read-failed", ex3);
				}
				catch (InvalidOperationException ex4)
				{
					LogService.Warn("XmlRuleStore corrupt store (deserialize). StoreId=" + descriptor.Id, ex4);
					return RuleLoadResult<T>.Failure(RuleLoadStatus.Corrupt, descriptor.Id, storePath, "rule-store-corrupt", ex4);
				}
				catch (InvalidDataException ex5)
				{
					LogService.Warn("XmlRuleStore corrupt store (empty/data). StoreId=" + descriptor.Id, ex5);
					return RuleLoadResult<T>.Failure(RuleLoadStatus.Corrupt, descriptor.Id, storePath, "rule-store-corrupt", ex5);
				}
				try
				{
					val = Normalize(val, normalize);
				}
				catch (Exception ex6)
				{
					LogService.Warn("XmlRuleStore corrupt store (normalize). StoreId=" + descriptor.Id, ex6);
					return RuleLoadResult<T>.Failure(RuleLoadStatus.Corrupt, descriptor.Id, storePath, "rule-store-normalize-failed", ex6);
				}
				return RuleLoadResult<T>.Success(RuleLoadStatus.Loaded, val, descriptor.Id, storePath, "rule-store-loaded");
			}
			throw new ArgumentNullException("createDefault");
		}
		throw new ArgumentNullException("descriptor");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static RuleLoadResult<T> LoadWithSelfHealing<T>(RuleStoreDescriptor descriptor, Func<T> createDefault, Func<T, T> normalize)
	{
		RuleLoadResult<T> ruleLoadResult = Load(descriptor, createDefault, normalize);
		if (ruleLoadResult.Usable)
		{
			return ruleLoadResult;
		}
		T value;
		try
		{
			value = Normalize(createDefault(), normalize);
		}
		catch (Exception ex)
		{
			LogService.Warn("XmlRuleStore self-healing default is invalid. StoreId=" + descriptor.Id, ex);
			return ruleLoadResult;
		}
		if (ruleLoadResult.Status == RuleLoadStatus.Corrupt && File.Exists(descriptor.StorePath))
		{
			string archivedPath = null;
			try
			{
				archivedPath = ArchiveUnreadableStore(descriptor.StorePath, "corrupt");
				Save(descriptor.StorePath, value, normalize);
				LogService.Warn("XmlRuleStore recovered corrupt store with safe default. StoreId=" + descriptor.Id);
				RuleLoadResult<T> ruleLoadResult2 = RuleLoadResult<T>.Success(RuleLoadStatus.CreatedDefault, value, descriptor.Id, descriptor.StorePath, "rule-store-recovered-default");
				ruleLoadResult2.ArchivedPath = archivedPath;
				ruleLoadResult2.Error = ruleLoadResult.Error;
				return ruleLoadResult2;
			}
			catch (Exception ex2)
			{
				LogService.Warn("XmlRuleStore uses in-memory default because corrupt store could not be repaired. StoreId=" + descriptor.Id, ex2);
				return RuleLoadResult<T>.Fallback(value, descriptor.Id, descriptor.StorePath, "rule-store-in-memory-default", ex2, archivedPath);
			}
		}
		LogService.Warn("XmlRuleStore uses in-memory default because persistent store is unavailable. StoreId=" + descriptor.Id, ruleLoadResult.Error);
		return RuleLoadResult<T>.Fallback(value, descriptor.Id, descriptor.StorePath, "rule-store-in-memory-default", ruleLoadResult.Error);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static RuleLoadResult<T> ResetToDefault<T>(RuleStoreDescriptor descriptor, Func<T> createDefault, Func<T, T> normalize)
	{
		if (descriptor == null)
		{
			throw new ArgumentNullException("descriptor");
		}
		descriptor.EnsureValid();
		if (createDefault == null)
		{
			throw new ArgumentNullException("createDefault");
		}
		string storePath = descriptor.StorePath;
		string archivedPath = null;
		if (File.Exists(storePath))
		{
			try
			{
				archivedPath = ArchiveUnreadableStore(storePath, "corrupt");
			}
			catch (Exception ex)
			{
				LogService.Warn("XmlRuleStore reset refused: backup failed. StoreId=" + descriptor.Id, ex);
				return RuleLoadResult<T>.Failure(RuleLoadStatus.ReadFailed, descriptor.Id, storePath, "rule-store-backup-failed", ex);
			}
		}
		try
		{
			T value = Normalize(createDefault(), normalize);
			Save(storePath, value, normalize);
			LogService.Info("XmlRuleStore reset to default after user confirmation. StoreId=" + descriptor.Id);
			RuleLoadResult<T> ruleLoadResult = RuleLoadResult<T>.Success(RuleLoadStatus.CreatedDefault, value, descriptor.Id, storePath, "rule-store-created-default");
			ruleLoadResult.ArchivedPath = archivedPath;
			return ruleLoadResult;
		}
		catch (Exception ex2)
		{
			LogService.Warn("XmlRuleStore reset failed after backup. StoreId=" + descriptor.Id, ex2);
			RuleLoadResult<T> ruleLoadResult2 = RuleLoadResult<T>.Failure(RuleLoadStatus.WriteFailed, descriptor.Id, storePath, "rule-store-reset-failed", ex2);
			ruleLoadResult2.ArchivedPath = archivedPath;
			return ruleLoadResult2;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static RuleSaveResult Save<T>(string path, T value, Func<T, T> normalize = null)
	{
		if (!string.IsNullOrWhiteSpace(path))
		{
			if (!string.IsNullOrWhiteSpace(Path.GetDirectoryName(path)))
			{
				T normalized = Normalize(value, normalize);
				XmlSerializer serializer = new XmlSerializer(typeof(T));
				AtomicFileWriteResult atomicFileWriteResult = AtomicFileService.WriteFileAtomically(path, delegate(string tempPath)
				{
					using FileStream fileStream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
					serializer.Serialize((Stream)fileStream, (object)normalized);
				}, [MethodImpl(MethodImplOptions.NoInlining)] (string tempPath) =>
				{
					using FileStream fileStream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read);
					Normalize(((T)serializer.Deserialize((Stream)fileStream)) ?? throw new InvalidDataException("Serialized rule store cannot be read back: " + path), normalize);
				});
				return new RuleSaveResult
				{
					StorePath = atomicFileWriteResult.TargetPath,
					Length = atomicFileWriteResult.Length,
					ReplacedExistingFile = atomicFileWriteResult.ReplacedExistingFile
				};
			}
			throw new InvalidDataException("Rule store directory cannot be empty: " + path);
		}
		throw new ArgumentException("Rule store path cannot be empty.", "path");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static RuleSaveResult Save<T>(RuleStoreDescriptor descriptor, T value, Func<T, T> normalize = null)
	{
		if (descriptor == null)
		{
			throw new ArgumentNullException("descriptor");
		}
		descriptor.EnsureValid();
		return Save(descriptor.StorePath, value, normalize);
	}

	private static T Normalize<T>(T value, Func<T, T> normalize)
	{
		if (normalize != null)
		{
			return normalize(value);
		}
		return value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static string ArchiveUnreadableStore(string path, string reason)
	{
		if (!string.IsNullOrWhiteSpace(path))
		{
			if (File.Exists(path))
			{
				string? directoryName = Path.GetDirectoryName(path);
				if (string.IsNullOrWhiteSpace(directoryName))
				{
					throw new InvalidDataException("Archive directory cannot be empty: " + path);
				}
				string text = (string.IsNullOrWhiteSpace(reason) ? "invalid" : reason.Trim());
				string text2 = Path.Combine(directoryName, Path.GetFileName(path) + "." + text + "." + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "." + Guid.NewGuid().ToString("N") + ".bak");
				File.Copy(path, text2, overwrite: false);
				return text2;
			}
			return string.Empty;
		}
		throw new ArgumentException("Archive source path cannot be empty.", "path");
	}
}
