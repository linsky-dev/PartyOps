using System;
using System.IO;
using System.Runtime.CompilerServices;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.FileSafety;

public static class OfficeDocumentOutputTransaction
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static OfficeDocumentOutputResult SaveCloseValidateCommit(Application application, Document document, string targetPath, WdSaveFormat saveFormat, bool reopenCommittedDocument)
	{
		if (application == null)
		{
			throw new ArgumentNullException("application");
		}
		if (document != null)
		{
			if (string.IsNullOrWhiteSpace(targetPath))
			{
				throw new InvalidOperationException("Office 输出目标路径为空。");
			}
			string text = AtomicFileService.CreateSiblingTempPath(targetPath);
			bool flag = false;
			bool flag2 = false;
			try
			{
				SaveAs(document, text, saveFormat);
				CloseDocument(document);
				flag = true;
				OutputFileIntegrityValidator.Validate(text, Path.GetExtension(targetPath));
				AtomicFileWriteResult fileResult = AtomicFileService.CommitTempFile(text, targetPath);
				flag2 = true;
				return new OfficeDocumentOutputResult
				{
					FileResult = fileResult,
					IntegrityReceipt = OutputIntegrityReceipt.Create(fileResult, Path.GetExtension(targetPath)),
					ReopenedDocument = (reopenCommittedDocument ? OpenDocument(application, targetPath) : null)
				};
			}
			finally
			{
				if (!flag)
				{
					TryCloseDocument(document, "OfficeDocumentOutputTransaction.SaveCloseValidateCommit.Close");
				}
				if (!flag2)
				{
					TryDeleteTemp(text);
				}
			}
		}
		throw new ArgumentNullException("document");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static OfficeDocumentOutputResult CopyCloseValidateCommit(Application application, Document document, string sourcePath, string targetPath, bool reopenCommittedDocument)
	{
		if (application == null)
		{
			throw new ArgumentNullException("application");
		}
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
		{
			throw new FileNotFoundException("待重命名的原始文档不存在。", sourcePath);
		}
		if (!string.IsNullOrWhiteSpace(targetPath))
		{
			if (!string.Equals(Path.GetExtension(sourcePath), Path.GetExtension(targetPath), StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException("一键命名只能修改文件名，不能同时改变文件格式。");
			}
			string text = AtomicFileService.CreateSiblingTempPath(targetPath);
			bool flag = false;
			bool flag2 = false;
			try
			{
				document.Save();
				File.Copy(sourcePath, text, overwrite: false);
				CloseDocument(document);
				flag = true;
				OutputFileIntegrityValidator.Validate(text, Path.GetExtension(targetPath));
				AtomicFileWriteResult fileResult = AtomicFileService.CommitTempFile(text, targetPath);
				flag2 = true;
				return new OfficeDocumentOutputResult
				{
					FileResult = fileResult,
					IntegrityReceipt = OutputIntegrityReceipt.Create(fileResult, Path.GetExtension(targetPath)),
					ReopenedDocument = (reopenCommittedDocument ? OpenDocument(application, targetPath) : null)
				};
			}
			catch
			{
				if (flag && reopenCommittedDocument && !flag2)
				{
					TryOpenRecoveryDocument(application, sourcePath);
				}
				throw;
			}
			finally
			{
				if (!flag2)
				{
					TryDeleteTemp(text);
				}
			}
		}
		throw new InvalidOperationException("重命名目标路径为空。");
	}

	private static void SaveAs(Document document, string path, WdSaveFormat saveFormat)
	{
		object FileName = path;
		object FileFormat = saveFormat;
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

	private static void CloseDocument(Document document)
	{
		object SaveChanges = WdSaveOptions.wdDoNotSaveChanges;
		object OriginalFormat = Type.Missing;
		object RouteDocument = Type.Missing;
		document.Close(ref SaveChanges, ref OriginalFormat, ref RouteDocument);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static Document OpenDocument(Application application, string path)
	{
		Documents value = null;
		try
		{
			value = application.Documents;
			object FileName = path;
			object ReadOnly = false;
			object Visible = true;
			Documents documents = value;
			object ConfirmConversions = Type.Missing;
			object AddToRecentFiles = Type.Missing;
			object PasswordDocument = Type.Missing;
			object PasswordTemplate = Type.Missing;
			object Revert = Type.Missing;
			object WritePasswordDocument = Type.Missing;
			object WritePasswordTemplate = Type.Missing;
			object Format = Type.Missing;
			object Encoding = Type.Missing;
			object OpenAndRepair = Type.Missing;
			object DocumentDirection = Type.Missing;
			object NoEncodingDialog = Type.Missing;
			object XMLTransform = Type.Missing;
			return documents.Open(ref FileName, ref ConfirmConversions, ref ReadOnly, ref AddToRecentFiles, ref PasswordDocument, ref PasswordTemplate, ref Revert, ref WritePasswordDocument, ref WritePasswordTemplate, ref Format, ref Encoding, ref Visible, ref OpenAndRepair, ref DocumentDirection, ref NoEncodingDialog, ref XMLTransform);
		}
		catch (Exception innerException)
		{
			throw new InvalidOperationException("输出文件已经生成并通过校验，但 WPS 未能重新打开该文件：" + path, innerException);
		}
		finally
		{
			ComObjectRelease.Release(ref value, "OfficeDocumentOutputTransaction.OpenDocument.Documents");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void TryOpenRecoveryDocument(Application application, string sourcePath)
	{
		Document value = null;
		try
		{
			if (File.Exists(sourcePath))
			{
				value = OpenDocument(application, sourcePath);
			}
		}
		catch (Exception ex)
		{
			LogService.Error("OfficeDocumentOutputTransaction.RecoverSource", ex);
		}
		finally
		{
			ComObjectRelease.Release(ref value, "OfficeDocumentOutputTransaction.RecoveredDocument");
		}
	}

	private static void TryCloseDocument(Document document, string context)
	{
		try
		{
			CloseDocument(document);
		}
		catch (Exception ex)
		{
			LogService.Warn(context, ex);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void TryDeleteTemp(string path)
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
			LogService.Warn("OfficeDocumentOutputTransaction.DeleteTemp", ex);
		}
	}
}
