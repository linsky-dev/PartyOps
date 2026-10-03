using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using DocumentRepository.Models.Recovery;
using DocumentRepository.Models.Safety;
using DocumentRepository.Services.Configuration;
using DocumentRepository.Services.Logging;

namespace DocumentRepository.Services.Recovery;

public static class RecoveryCopyService
{
	private sealed class SourceFingerprint
	{
		public long Length;

		public string Sha256;

		public SourceFingerprint(long length, string sha256)
		{
			Length = length;
			Sha256 = sha256;
		}

		public static SourceFingerprint Read(string path)
		{
			using FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			using SHA256 sHA = SHA256.Create();
			byte[] array = new byte[131072];
			long num = 0L;
			int num2;
			while ((num2 = fileStream.Read(array, 0, array.Length)) > 0)
			{
				sHA.TransformBlock(array, 0, num2, null, 0);
				num += num2;
			}
			sHA.TransformFinalBlock(new byte[0], 0, 0);
			return new SourceFingerprint(num, ToHex(sHA.Hash));
		}

		public bool Equals(SourceFingerprint other)
		{
			if (other != null && Length == other.Length)
			{
				return string.Equals(Sha256, other.Sha256, StringComparison.OrdinalIgnoreCase);
			}
			return false;
		}
	}

	private const int MaxCopyAttempts = 2;

	private const FileShare ActiveDocumentReadShare = FileShare.ReadWrite | FileShare.Delete;

	internal static Action<int, string, string> AfterCopyAttemptForTesting;

	internal static Action BeforeManifestWriteForTesting;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static RecoveryPreparationResult Prepare(RecoveryCopyRequest request)
	{
		if (request == null)
		{
			throw new ArgumentNullException("request");
		}
		if (!string.IsNullOrWhiteSpace(request.TaskId) && !string.IsNullOrWhiteSpace(request.FeatureId) && !string.IsNullOrWhiteSpace(request.DocumentLifecycleId))
		{
			string sourcePath = request.SourcePath;
			if (string.IsNullOrWhiteSpace(sourcePath) || !Path.IsPathRooted(sourcePath))
			{
				return Deny(ExecutionDecisionReasonCode.RecoverySourceMissing, "recovery-source-missing");
			}
			try
			{
				sourcePath = Path.GetFullPath(sourcePath);
			}
			catch
			{
				return Deny(ExecutionDecisionReasonCode.RecoverySourceMissing, "recovery-source-missing");
			}
			if (!File.Exists(sourcePath))
			{
				return Deny(ExecutionDecisionReasonCode.RecoverySourceMissing, "recovery-source-missing");
			}
			string recoveryRoot = ApplicationDataPaths.RecoveryRoot;
			bool flag = false;
			try
			{
				Directory.CreateDirectory(recoveryRoot);
				if (!RecoveryManifestStore.IsControlledRoot(recoveryRoot))
				{
					if (!request.AllowFallback)
					{
						return Deny(ExecutionDecisionReasonCode.RecoveryRootUnavailable, "recovery-root-unavailable");
					}
					flag = true;
				}
			}
			catch (Exception exception)
			{
				WarnSafe("RecoveryCopyService.RootUnavailable", exception);
				if (!request.AllowFallback)
				{
					return Deny(ExecutionDecisionReasonCode.RecoveryRootUnavailable, "recovery-root-unavailable");
				}
				flag = true;
			}
			long length;
			try
			{
				length = new FileInfo(sourcePath).Length;
			}
			catch (Exception exception2)
			{
				WarnSafe("RecoveryCopyService.SourceStatFailed", exception2);
				return Deny(ExecutionDecisionReasonCode.RecoverySourceMissing, "recovery-source-missing");
			}
			if (!flag)
			{
				RecoveryRetentionService.QuotaEvaluation quotaEvaluation = RecoveryRetentionService.EvaluateQuota(length);
				if (!quotaEvaluation.HasCapacity || !quotaEvaluation.HasDiskSpace)
				{
					if (!request.AllowFallback)
					{
						if (quotaEvaluation.HasCapacity)
						{
							return Deny(ExecutionDecisionReasonCode.RecoveryDiskSpaceInsufficient, "recovery-disk-insufficient");
						}
						return Deny(ExecutionDecisionReasonCode.RecoveryQuotaExceeded, "recovery-quota-exceeded");
					}
					flag = true;
				}
			}
			if (flag)
			{
				recoveryRoot = ApplicationDataPaths.RecoveryFallbackRoot;
				try
				{
					Directory.CreateDirectory(recoveryRoot);
					if (!RecoveryManifestStore.IsControlledRoot(recoveryRoot))
					{
						return Deny(ExecutionDecisionReasonCode.RecoveryRootUnavailable, "recovery-root-unavailable");
					}
				}
				catch (Exception exception3)
				{
					WarnSafe("RecoveryCopyService.FallbackRootUnavailable", exception3);
					return Deny(ExecutionDecisionReasonCode.RecoveryRootUnavailable, "recovery-root-unavailable");
				}
				RecoveryRetentionService.QuotaEvaluation quotaEvaluation2 = RecoveryRetentionService.EvaluateFallbackQuota(length);
				if (!quotaEvaluation2.HasCapacity)
				{
					return Deny(ExecutionDecisionReasonCode.RecoveryQuotaExceeded, "recovery-quota-exceeded");
				}
				if (!quotaEvaluation2.HasDiskSpace)
				{
					return Deny(ExecutionDecisionReasonCode.RecoveryDiskSpaceInsufficient, "recovery-disk-insufficient");
				}
			}
			RecoveryRetentionService.CleanupOnce();
			string sourceSha;
			try
			{
				sourceSha = ComputeSha256(sourcePath);
			}
			catch (Exception exception4)
			{
				WarnSafe("RecoveryCopyService.SourceHashFailed", exception4);
				return Deny(ExecutionDecisionReasonCode.RecoveryCopyFailed, "recovery-copy-failed");
			}
			RecoveryManifest recoveryManifest = (flag ? null : FindReusableCopy(sourcePath, sourceSha));
			if (recoveryManifest == null || !TryWriteReuseAudit(recoveryManifest, request))
			{
				string text = Guid.NewGuid().ToString("N");
				string text2 = (flag ? ApplicationDataPaths.RecoveryFallbackDirectory(text) : ApplicationDataPaths.RecoveryDirectory(text));
				string extension = Path.GetExtension(sourcePath);
				string text3 = "source" + (string.IsNullOrWhiteSpace(extension) ? ".bin" : extension);
				string text4 = Path.Combine(text2, text3);
				string text5 = Path.Combine(text2, ".copying-" + Guid.NewGuid().ToString("N") + ".tmp");
				bool flag2 = false;
				try
				{
					Directory.CreateDirectory(text2);
					SourceFingerprint sourceFingerprint = null;
					int num = 1;
					while (true)
					{
						if (num <= 2)
						{
							sourceFingerprint = CopyWithFingerprint(sourcePath, text5);
							AfterCopyAttemptForTesting?.Invoke(num, sourcePath, text5);
							SourceFingerprint other = SourceFingerprint.Read(sourcePath);
							if (!sourceFingerprint.Equals(other))
							{
								if (num == 2)
								{
									break;
								}
								num++;
								continue;
							}
						}
						if ((flag ? RecoveryRetentionService.EvaluateFallbackQuota(0L) : RecoveryRetentionService.EvaluateQuota(0L)).HasCapacity)
						{
							if (VerifyCopy(text5, sourceFingerprint))
							{
								if (SavedBaselineValidator.IsUsable(text5, extension))
								{
									File.Move(text5, text4);
									flag2 = true;
									RecoveryManifest manifest = new RecoveryManifest
									{
										RecoveryId = text,
										TaskId = request.TaskId,
										FeatureId = request.FeatureId,
										DocumentLifecycleId = request.DocumentLifecycleId,
										SourceFullPath = sourcePath,
										SourceFileName = Path.GetFileName(sourcePath),
										SourceExtension = extension,
										SourceLength = sourceFingerprint.Length,
										SourceLastWriteUtc = File.GetLastWriteTimeUtc(sourcePath),
										SourceSha256 = sourceFingerprint.Sha256,
										CopySha256 = sourceFingerprint.Sha256,
										CopyFileName = text3,
										Coverage = ((!request.SourceIsSaved) ? RecoveryCoverage.LastSavedBaseline : RecoveryCoverage.ExactSavedBaseline).ToString(),
										State = RecoveryCopyState.Prepared.ToString(),
										CreatedAtUtc = DateTime.UtcNow,
										StateUpdatedAtUtc = DateTime.UtcNow,
										IsFallback = flag
									};
									try
									{
										BeforeManifestWriteForTesting?.Invoke();
										RecoveryManifestStore.WriteAtomic(text2, manifest);
									}
									catch (Exception exception5)
									{
										WarnSafe("RecoveryCopyService.ManifestWriteFailed", exception5);
										TryDeleteFile(text4);
										TryDeleteDirectory(text2);
										return Deny(ExecutionDecisionReasonCode.RecoveryManifestWriteFailed, "recovery-manifest-write-failed");
									}
									RecoveryCopyReceipt receipt = new RecoveryCopyReceipt(text, request.TaskId, request.DocumentLifecycleId, request.FeatureId, (!request.SourceIsSaved) ? RecoveryCoverage.LastSavedBaseline : RecoveryCoverage.ExactSavedBaseline, sourceFingerprint.Length);
									LogService.Info("RecoveryCopyService prepared id=" + text + ", length=" + sourceFingerprint.Length);
									return RecoveryPreparationResult.Allowed(receipt);
								}
								return CleanupAndDeny(text2, text5, ExecutionDecisionReasonCode.RecoveryCopyVerificationFailed, "recovery-copy-verification-failed");
							}
							return CleanupAndDeny(text2, text5, ExecutionDecisionReasonCode.RecoveryCopyVerificationFailed, "recovery-copy-verification-failed");
						}
						return CleanupAndDeny(text2, text5, ExecutionDecisionReasonCode.RecoveryQuotaExceeded, "recovery-quota-exceeded");
					}
					return CleanupAndDeny(text2, text5, ExecutionDecisionReasonCode.RecoverySourceChangedDuringCopy, "recovery-source-changed");
				}
				catch (Exception exception6)
				{
					WarnSafe("RecoveryCopyService.PrepareFailed", exception6);
					return CleanupAndDeny(text2, flag2 ? null : text5, ExecutionDecisionReasonCode.RecoveryCopyFailed, "recovery-copy-failed");
				}
				finally
				{
					TryDeleteFile(text5);
				}
			}
			LogService.Info("RecoveryCopyService reused id=" + recoveryManifest.RecoveryId + ", length=" + length);
			return RecoveryPreparationResult.Allowed(new RecoveryCopyReceipt(recoveryManifest.RecoveryId, request.TaskId, request.DocumentLifecycleId, request.FeatureId, (!request.SourceIsSaved) ? RecoveryCoverage.LastSavedBaseline : RecoveryCoverage.ExactSavedBaseline, length));
		}
		return Deny(ExecutionDecisionReasonCode.RecoveryCopyFailed, "recovery-copy-failed");
	}

	private static RecoveryPreparationResult Deny(ExecutionDecisionReasonCode reasonCode, string messageKey)
	{
		return RecoveryPreparationResult.Denied(reasonCode, messageKey);
	}

	private static string ComputeSha256(string path)
	{
		using FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
		using SHA256 sHA = SHA256.Create();
		byte[] array = new byte[131072];
		int inputCount;
		while ((inputCount = fileStream.Read(array, 0, array.Length)) > 0)
		{
			sHA.TransformBlock(array, 0, inputCount, null, 0);
		}
		sHA.TransformFinalBlock(new byte[0], 0, 0);
		return ToHex(sHA.Hash);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static RecoveryManifest FindReusableCopy(string sourcePath, string sourceSha256)
	{
		try
		{
			if (!Directory.Exists(ApplicationDataPaths.RecoveryRoot))
			{
				return null;
			}
			foreach (string item in Directory.EnumerateDirectories(ApplicationDataPaths.RecoveryRoot))
			{
				if (RecoveryManifestStore.IsControlledDirectory(item))
				{
					RecoveryManifest recoveryManifest = RecoveryManifestStore.TryRead(item);
					if (recoveryManifest != null && !recoveryManifest.IsFallback && string.Equals(recoveryManifest.SourceFullPath, sourcePath, StringComparison.OrdinalIgnoreCase) && string.Equals(recoveryManifest.SourceSha256, sourceSha256, StringComparison.OrdinalIgnoreCase) && Enum.TryParse<RecoveryCopyState>(recoveryManifest.State, out var result) && (result == RecoveryCopyState.Prepared || result == RecoveryCopyState.Committed) && !((DateTime.UtcNow - recoveryManifest.CreatedAtUtc).TotalMinutes > 10.0))
					{
						return recoveryManifest;
					}
				}
			}
			return null;
		}
		catch (Exception exception)
		{
			WarnSafe("RecoveryCopyService.FindReusableCopy", exception);
			return null;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool TryWriteReuseAudit(RecoveryManifest reused, RecoveryCopyRequest request)
	{
		try
		{
			string text = ApplicationDataPaths.ReuseLogDirectory(reused.RecoveryId);
			Directory.CreateDirectory(text);
			string path = Path.Combine(text, request.TaskId + ".json");
			var anon = new
			{
				TaskId = request.TaskId,
				FeatureId = request.FeatureId,
				DocumentLifecycleId = request.DocumentLifecycleId,
				ReusedRecoveryId = reused.RecoveryId,
				SourceSha256 = reused.SourceSha256,
				ReusedAtUtc = DateTime.UtcNow
			};
			string contents = new JavaScriptSerializer().Serialize((object)anon);
			File.WriteAllText(path, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			return true;
		}
		catch (Exception exception)
		{
			WarnSafe("RecoveryCopyService.TryWriteReuseAudit", exception);
			return false;
		}
	}

	private static RecoveryPreparationResult CleanupAndDeny(string recoveryDir, string tempPath, ExecutionDecisionReasonCode reasonCode, string messageKey)
	{
		TryDeleteFile(tempPath);
		TryDeleteDirectory(recoveryDir);
		return Deny(reasonCode, messageKey);
	}

	private static SourceFingerprint CopyWithFingerprint(string sourcePath, string tempPath)
	{
		using FileStream fileStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
		using FileStream fileStream2 = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
		using SHA256 sHA = SHA256.Create();
		byte[] array = new byte[131072];
		long num = 0L;
		int num2;
		while ((num2 = fileStream.Read(array, 0, array.Length)) > 0)
		{
			fileStream2.Write(array, 0, num2);
			sHA.TransformBlock(array, 0, num2, null, 0);
			num += num2;
		}
		sHA.TransformFinalBlock(new byte[0], 0, 0);
		return new SourceFingerprint(num, ToHex(sHA.Hash));
	}

	private static bool VerifyCopy(string copyPath, SourceFingerprint expected)
	{
		SourceFingerprint other = SourceFingerprint.Read(copyPath);
		return expected.Equals(other);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string ToHex(byte[] hash)
	{
		StringBuilder stringBuilder = new StringBuilder(hash.Length * 2);
		foreach (byte b in hash)
		{
			stringBuilder.Append(b.ToString("x2"));
		}
		return stringBuilder.ToString();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void TryDeleteFile(string path)
	{
		try
		{
			if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch (Exception exception)
		{
			WarnSafe("RecoveryCopyService.TryDeleteFile", exception);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void TryDeleteDirectory(string path)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path) || !RecoveryManifestStore.IsControlledDirectory(path) || Directory.GetDirectories(path).Length != 0)
			{
				return;
			}
			string[] files = Directory.GetFiles(path);
			for (int i = 0; i < files.Length; i++)
			{
				string fileName = Path.GetFileName(files[i]);
				if (!string.Equals(fileName, "manifest.json", StringComparison.OrdinalIgnoreCase) && !fileName.StartsWith("source", StringComparison.OrdinalIgnoreCase) && !fileName.StartsWith(".copying-", StringComparison.OrdinalIgnoreCase) && !fileName.StartsWith(".manifest.", StringComparison.OrdinalIgnoreCase))
				{
					return;
				}
			}
			files = Directory.GetFiles(path);
			for (int i = 0; i < files.Length; i++)
			{
				File.Delete(files[i]);
			}
			Directory.Delete(path, recursive: false);
		}
		catch (Exception exception)
		{
			WarnSafe("RecoveryCopyService.TryDeleteDirectory", exception);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void WarnSafe(string context, Exception exception)
	{
		LogService.Warn(context + ", exception=" + ((exception == null) ? "unknown" : exception.GetType().Name));
	}
}
