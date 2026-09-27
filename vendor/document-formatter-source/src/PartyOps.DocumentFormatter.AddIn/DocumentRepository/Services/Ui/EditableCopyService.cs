using System;
using System.IO;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Services.FileSafety;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Ui;

public static class EditableCopyService
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static OperationContext CreateEditableCopyAndContext(OperationContext source, out string failureMessage)
	{
		failureMessage = null;
		if (source == null || source.Application == null || source.Document == null)
		{
			failureMessage = "没有可用的文档上下文。";
			return null;
		}
		Document document = source.Document;
		string text = null;
		try
		{
			string fullName = document.FullName;
			if (!string.IsNullOrWhiteSpace(fullName) && Path.IsPathRooted(fullName))
			{
				text = Path.GetFullPath(fullName);
			}
		}
		catch
		{
		}
		if (text != null && File.Exists(text))
		{
			string? directoryName = Path.GetDirectoryName(text);
			string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(text);
			string extension = Path.GetExtension(text);
			string text2 = Path.Combine(directoryName, fileNameWithoutExtension + "-可编辑副本" + extension);
			if (File.Exists(text2))
			{
				failureMessage = "同名可编辑副本已存在，请先处理已有副本。";
				return null;
			}
			try
			{
				byte[] bytes = File.ReadAllBytes(text);
				AtomicFileService.WriteFileAtomically(text2, delegate(string path)
				{
					File.WriteAllBytes(path, bytes);
				});
			}
			catch (Exception ex)
			{
				LogService.Warn("EditableCopyService.Create", ex);
				failureMessage = "创建可编辑副本失败，请确认原文件所在文件夹可写。";
				return null;
			}
			Document value = null;
			try
			{
				Documents documents = source.Application.Documents;
				object FileName = text2;
				object ConfirmConversions = Type.Missing;
				object ReadOnly = false;
				object AddToRecentFiles = Type.Missing;
				object PasswordDocument = Type.Missing;
				object PasswordTemplate = Type.Missing;
				object Revert = Type.Missing;
				object WritePasswordDocument = Type.Missing;
				object WritePasswordTemplate = Type.Missing;
				object Format = Type.Missing;
				object Encoding = Type.Missing;
				object Visible = Type.Missing;
				object OpenAndRepair = Type.Missing;
				object DocumentDirection = Type.Missing;
				object NoEncodingDialog = Type.Missing;
				object XMLTransform = Type.Missing;
				value = documents.Open(ref FileName, ref ConfirmConversions, ref ReadOnly, ref AddToRecentFiles, ref PasswordDocument, ref PasswordTemplate, ref Revert, ref WritePasswordDocument, ref WritePasswordTemplate, ref Format, ref Encoding, ref Visible, ref OpenAndRepair, ref DocumentDirection, ref NoEncodingDialog, ref XMLTransform);
				if (value.ReadOnly)
				{
					failureMessage = "新创建的副本仍为只读，已取消。";
					try
					{
						Document document2 = value;
						XMLTransform = WdSaveOptions.wdDoNotSaveChanges;
						NoEncodingDialog = Type.Missing;
						DocumentDirection = Type.Missing;
						document2.Close(ref XMLTransform, ref NoEncodingDialog, ref DocumentDirection);
					}
					catch
					{
					}
					ComObjectRelease.Release(ref value, "EditableCopyService.readonly-copy");
					DeleteFailedCopy(text2);
					return null;
				}
			}
			catch (Exception ex2)
			{
				LogService.Warn("EditableCopyService.Open", ex2);
				if (value != null)
				{
					try
					{
						Document document3 = value;
						object DocumentDirection = WdSaveOptions.wdDoNotSaveChanges;
						object NoEncodingDialog = Type.Missing;
						object XMLTransform = Type.Missing;
						document3.Close(ref DocumentDirection, ref NoEncodingDialog, ref XMLTransform);
					}
					catch
					{
					}
					ComObjectRelease.Release(ref value, "EditableCopyService.failed-copy");
				}
				DeleteFailedCopy(text2);
				failureMessage = "无法打开新建的可编辑副本，已取消。";
				return null;
			}
			OperationContext operationContext = OperationContext.FromApplication(source.Application);
			operationContext.IsBatchMode = source.IsBatchMode;
			operationContext.SuppressUserDialogs = source.SuppressUserDialogs;
			operationContext.TaskId = source.TaskId;
			operationContext.FeatureId = source.FeatureId;
			operationContext.InvocationSource = source.InvocationSource;
			operationContext.UserInterface = source.UserInterface;
			operationContext.CurrentConfig = source.CurrentConfig;
			operationContext.Stage = source.Stage;
			operationContext.ObjectLocation = source.ObjectLocation;
			ComObjectRelease.Release(ref value, "EditableCopyService.opened-copy");
			return operationContext;
		}
		failureMessage = "无法取得只读文档的磁盘文件，不能创建可编辑副本。";
		return null;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void DeleteFailedCopy(string path)
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
			LogService.Warn("EditableCopyService.DeleteFailedCopy", ex);
		}
	}
}
