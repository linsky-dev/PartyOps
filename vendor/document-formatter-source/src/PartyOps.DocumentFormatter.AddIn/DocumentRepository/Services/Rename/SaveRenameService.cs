using System;
using System.IO;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Rename;
using DocumentRepository.Services.FileSafety;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Rename;

public static class SaveRenameService
{
	internal static Action BeforeRollbackForTesting;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static SaveRenameResult Execute(Document doc, string originalPath, string targetPath, bool copyMode)
	{
		if (doc == null)
		{
			throw RenameOperationException.Create(RenameFailureReasonCode.DocumentMissing, RenameFailureStage.Save, new ArgumentNullException("doc"));
		}
		if (string.IsNullOrWhiteSpace(originalPath))
		{
			throw RenameOperationException.Create(RenameFailureReasonCode.StablePathUnavailable, RenameFailureStage.Save);
		}
		if (string.IsNullOrWhiteSpace(targetPath))
		{
			throw RenameOperationException.Create(RenameFailureReasonCode.FilenameInvalid, RenameFailureStage.Save);
		}
		SaveDocument(doc);
		if (!string.Equals(originalPath, targetPath, StringComparison.OrdinalIgnoreCase))
		{
			if (copyMode)
			{
				OutputIntegrityReceipt integrityReceipt = CopyAtomically(originalPath, targetPath);
				return CreatePreparedResult(doc, "已另存一份副本：\r\n" + Path.GetFileName(targetPath) + "\r\n\r\n当前打开的文档名称未修改。", integrityReceipt, originalPath, targetPath, copyMode: true, pathSwitched: false);
			}
			Application value = null;
			try
			{
				value = doc.Application;
				OutputIntegrityReceipt integrityReceipt2 = SwitchCurrentDocumentPath(value, doc, originalPath, targetPath);
				string message = "文档已重命名为：\r\n" + Path.GetFileName(targetPath);
				return CreatePreparedResult(doc, message, integrityReceipt2, originalPath, targetPath, copyMode: false, pathSwitched: true);
			}
			finally
			{
				ComObjectRelease.Release(ref value, "SaveRenameService.Application");
			}
		}
		if (!copyMode)
		{
			return CreatePreparedResult(doc, "文档已重命名为：\r\n" + Path.GetFileName(targetPath), null, originalPath, targetPath, copyMode: false, pathSwitched: false);
		}
		string text = BuildSameNameCopyPath(originalPath);
		OutputIntegrityReceipt integrityReceipt3 = CopyAtomically(originalPath, text);
		return CreatePreparedResult(doc, "已另存一份副本：\r\n" + Path.GetFileName(text) + "\r\n\r\n当前打开的文档名称未修改。", integrityReceipt3, originalPath, text, copyMode: true, pathSwitched: false);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static OutputIntegrityReceipt SwitchCurrentDocumentPath(Application application, Document document, string originalPath, string targetPath)
	{
		bool flag = false;
		try
		{
			LogService.Info("Rename path switch start original=" + originalPath + ", target=" + targetPath);
			SaveDocument(document);
			SaveCurrentDocumentAs(document, targetPath);
			flag = true;
			LogService.Info("Rename path switch saved target=" + targetPath);
			OutputIntegrityReceipt result = ValidateOpenDocumentSnapshot(targetPath);
			LogService.Info("Rename path switch snapshot validated target=" + targetPath);
			EnsureVisibleActiveDocument(application, document, targetPath);
			LogService.Info("Rename path switch active document verified target=" + targetPath);
			return result;
		}
		catch (Exception ex)
		{
			if (flag)
			{
				try
				{
					RestoreOriginalDocument(application, document, originalPath, targetPath);
				}
				catch (Exception rollbackError)
				{
					throw RenameOperationException.RecoveryRequired(ex, rollbackError);
				}
				throw RenameOperationException.RollbackVerified(ex);
			}
			throw RenameFailureClassifier.ClassifyOutputFailure(ex, RenameFailureStage.Save);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Commit(SaveRenameResult result)
	{
		if (result != null)
		{
			if (!result.IsCommitted)
			{
				if (result.PathSwitched && !TryDeleteOldFile(result.OriginalPath, result.TargetPath))
				{
					result.Message += "\r\n\r\n提示：新文件已保存并通过校验，但旧文件可能仍保留在原目录。";
				}
				result.IsCommitted = true;
				LogService.Info("Rename transaction committed. Target=" + result.TargetPath);
			}
			return;
		}
		throw new ArgumentNullException("result");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static Document Rollback(SaveRenameResult result)
	{
		if (result != null)
		{
			if (result.IsCommitted)
			{
				throw new InvalidOperationException("已经提交的一键命名事务不能回滚。");
			}
			if (BeforeRollbackForTesting != null)
			{
				BeforeRollbackForTesting();
			}
			Document activeDocument = result.ActiveDocument;
			if (activeDocument == null)
			{
				return null;
			}
			if (result.PathSwitched)
			{
				Application value = null;
				try
				{
					value = activeDocument.Application;
					RestoreOriginalDocument(value, activeDocument, result.OriginalPath, result.TargetPath);
				}
				finally
				{
					ComObjectRelease.Release(ref value, "SaveRenameService.Rollback.Application");
				}
			}
			else if (result.OutputCreated && !string.IsNullOrWhiteSpace(result.TargetPath) && !string.Equals(result.OriginalPath, result.TargetPath, StringComparison.OrdinalIgnoreCase) && File.Exists(result.TargetPath))
			{
				File.Delete(result.TargetPath);
			}
			LogService.Info("Rename transaction rolled back. Original=" + result.OriginalPath);
			return activeDocument;
		}
		throw new ArgumentNullException("result");
	}

	private static void SaveCurrentDocumentAs(Document document, string targetPath)
	{
		try
		{
			object FileName = targetPath;
			object FileFormat = document.SaveFormat;
			object LockComments = Type.Missing;
			object Password = Type.Missing;
			object AddToRecentFiles = Type.Missing;
			object WritePassword = Type.Missing;
			object ReadOnlyRecommended = Type.Missing;
			object EmbedTrueTypeFonts = Type.Missing;
			object SaveNativePictureFormat = Type.Missing;
			object SaveFormsData = Type.Missing;
			object SaveAsAOCELetter = Type.Missing;
			object Encoding = Type.Missing;
			object InsertLineBreaks = Type.Missing;
			object AllowSubstitutions = Type.Missing;
			object LineEnding = Type.Missing;
			object AddBiDiMarks = Type.Missing;
			object CompatibilityMode = Type.Missing;
			document.SaveAs2(ref FileName, ref FileFormat, ref LockComments, ref Password, ref AddToRecentFiles, ref WritePassword, ref ReadOnlyRecommended, ref EmbedTrueTypeFonts, ref SaveNativePictureFormat, ref SaveFormsData, ref SaveAsAOCELetter, ref Encoding, ref InsertLineBreaks, ref AllowSubstitutions, ref LineEnding, ref AddBiDiMarks, ref CompatibilityMode);
		}
		catch (Exception error)
		{
			throw RenameFailureClassifier.ClassifyOutputFailure(error, RenameFailureStage.Save);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static OutputIntegrityReceipt ValidateOpenDocumentSnapshot(string targetPath)
	{
		string text = AtomicFileService.CreateSiblingTempPath(targetPath);
		try
		{
			File.Copy(targetPath, text, overwrite: false);
			OutputFileIntegrityValidator.Validate(text, Path.GetExtension(targetPath));
			long length = new FileInfo(targetPath).Length;
			if (length <= 0)
			{
				throw new InvalidDataException("rename-output-length-invalid");
			}
			return OutputIntegrityReceipt.Create(new AtomicFileWriteResult
			{
				TargetPath = targetPath,
				Length = length,
				ReplacedExistingFile = false
			}, Path.GetExtension(targetPath));
		}
		catch (Exception innerException)
		{
			throw RenameOperationException.Create(RenameFailureReasonCode.OutputValidationFailed, RenameFailureStage.Verification, innerException);
		}
		finally
		{
			TryDeleteTemporaryFile(text);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void EnsureVisibleActiveDocument(Application application, Document document, string expectedPath)
	{
		Document value = null;
		Window value2 = null;
		try
		{
			application.Visible = true;
			document.Activate();
			value = application.ActiveDocument;
			if (value != null && string.Equals(Path.GetFullPath(value.FullName ?? string.Empty), Path.GetFullPath(expectedPath), StringComparison.OrdinalIgnoreCase))
			{
				value2 = document.ActiveWindow;
				if (value2 != null)
				{
					value2.Visible = true;
					return;
				}
				throw new InvalidOperationException("重命名后的文档没有可见窗口。");
			}
			throw new InvalidOperationException("重命名后的文档未成为当前活动文档。");
		}
		finally
		{
			ComObjectRelease.Release(ref value2, "SaveRenameService.ActiveWindow");
			ComObjectRelease.Release(ref value, "SaveRenameService.ActiveDocument");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void RestoreOriginalDocument(Application application, Document document, string originalPath, string failedTargetPath)
	{
		WdAlertLevel displayAlerts = WdAlertLevel.wdAlertsAll;
		bool flag = false;
		try
		{
			displayAlerts = application.DisplayAlerts;
			flag = true;
			application.DisplayAlerts = WdAlertLevel.wdAlertsNone;
			SaveCurrentDocumentAs(document, originalPath);
			document.Activate();
			LogService.Info("Rename path switch rolled back to original=" + originalPath);
			if (File.Exists(failedTargetPath) && !string.Equals(failedTargetPath, originalPath, StringComparison.OrdinalIgnoreCase))
			{
				File.Delete(failedTargetPath);
			}
		}
		finally
		{
			if (flag)
			{
				try
				{
					application.DisplayAlerts = displayAlerts;
				}
				catch (Exception ex)
				{
					LogService.Warn("SaveRenameService.RestoreDisplayAlerts", ex);
				}
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void TryDeleteTemporaryFile(string path)
	{
		try
		{
			if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch (Exception ex)
		{
			LogService.Warn("SaveRenameService.DeleteValidationSnapshot", ex);
		}
	}

	private static SaveRenameResult CreatePreparedResult(Document document, string message, OutputIntegrityReceipt integrityReceipt, string originalPath, string targetPath, bool copyMode, bool pathSwitched)
	{
		return new SaveRenameResult
		{
			ActiveDocument = document,
			Message = message,
			IntegrityReceipt = integrityReceipt,
			ValidatedOutputPath = integrityReceipt?.TargetPath,
			OriginalPath = originalPath,
			TargetPath = targetPath,
			CopyMode = copyMode,
			PathSwitched = pathSwitched,
			OutputCreated = (integrityReceipt != null),
			IsCommitted = false
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string BuildSameNameCopyPath(string originalPath)
	{
		string directoryName = Path.GetDirectoryName(originalPath);
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(originalPath);
		string extension = Path.GetExtension(originalPath);
		for (int i = 1; i < 1000; i++)
		{
			string text = Path.Combine(directoryName, $"{fileNameWithoutExtension}({i}){extension}");
			if (!File.Exists(text))
			{
				return text;
			}
		}
		return Path.Combine(directoryName, string.Format("{0}_{1}{2}", fileNameWithoutExtension, DateTime.Now.ToString("HHmmss"), extension));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool TryDeleteOldFile(string originalPath, string targetPath)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(originalPath))
			{
				return true;
			}
			if (!string.Equals(originalPath, targetPath, StringComparison.OrdinalIgnoreCase))
			{
				if (!File.Exists(originalPath))
				{
					return true;
				}
				File.Delete(originalPath);
				return true;
			}
			return true;
		}
		catch (Exception ex)
		{
			LogService.Warn("SaveRenameService.TryDeleteOldFile", ex);
			return false;
		}
	}

	private static OutputIntegrityReceipt CopyAtomically(string sourcePath, string targetPath)
	{
		try
		{
			return OutputIntegrityReceipt.Create(SafeOutputTransaction.ProduceAndCommit(targetPath, delegate(string temp)
			{
				File.Copy(sourcePath, temp, overwrite: false);
			}), Path.GetExtension(targetPath));
		}
		catch (Exception error)
		{
			throw RenameFailureClassifier.ClassifyOutputFailure(error, RenameFailureStage.OutputPreparation);
		}
	}

	private static void SaveDocument(Document document)
	{
		try
		{
			document.Save();
		}
		catch (Exception error)
		{
			throw RenameFailureClassifier.ClassifyOutputFailure(error, RenameFailureStage.Save);
		}
	}
}
