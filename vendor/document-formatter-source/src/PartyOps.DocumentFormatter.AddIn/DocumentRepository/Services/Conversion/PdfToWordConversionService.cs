using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DocumentRepository.Models.Conversion;
using DocumentRepository.Models.Features;
using DocumentRepository.Models.Tasks;
using DocumentRepository.Services.Conversion.PdfToWord;
using DocumentRepository.Services.FileSafety;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Conversion;

public static class PdfToWordConversionService
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static PdfToWordConversionOutput Convert(Application application, Document activeDocument, string sourcePath, string targetPath, TaskRuntimeContext task, ConvertOptions options, IFeatureUiService userInterface = null)
	{
		if (application == null)
		{
			throw new ArgumentNullException("application");
		}
		if (!PdfToWordSourceResolver.IsExistingPdf(sourcePath))
		{
			throw new InvalidOperationException("PDF 源文件不存在或格式无效。");
		}
		if (string.IsNullOrWhiteSpace(targetPath))
		{
			throw new InvalidOperationException("DOCX 输出路径无效。");
		}
		if (!IsSameDocument(activeDocument, sourcePath))
		{
			try
			{
				OutputFileIntegrityValidator.Validate(sourcePath, ".pdf");
			}
			catch (IOException ex)
			{
				LogService.Warn("PdfToWordConversionService.SourceValidateLocked type=" + ex.GetType().Name);
			}
		}
		task?.Cancellation.ThrowIfCancellationRequested("打开 PDF 前");
		if (options == null || options.PdfToWordEngine == PdfToWordEngine.Local)
		{
			try
			{
				return ConvertWithLocalEngine(application, sourcePath, targetPath, task, userInterface, options?.PdfNormalizeChinesePunctuation ?? false);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (LocalPdfToWordException ex3) when (ex3.Reason == PdfToWordFailureReason.NoText)
			{
				throw ConvertOperationException.Create(ConvertFailureReasonCode.ScannedPdfUnsupported, ConvertFailureStage.Execute, ex3);
			}
			catch (LocalPdfToWordException ex4) when (ex4.Reason == PdfToWordFailureReason.Encrypted)
			{
				throw ConvertOperationException.Create(ConvertFailureReasonCode.PdfEncryptedOrProtected, ConvertFailureStage.Execute, ex4);
			}
			catch (LocalPdfToWordException ex5) when (ex5.Reason == PdfToWordFailureReason.Output)
			{
				throw ConvertOperationException.Create(ConvertFailureReasonCode.OutputWriteFailed, ConvertFailureStage.Execute, ex5);
			}
			catch (LocalPdfToWordException ex6)
			{
				LogService.Warn("PdfToWordConversionService.LocalEngineFailed_FallbackToHost reason=" + ex6.Reason);
			}
		}
		else
		{
			ThrowIfHostImportSourceHasNoText(sourcePath, task);
		}
		return ConvertWithHostImport(application, activeDocument, sourcePath, targetPath, task);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static PdfToWordConversionOutput ConvertWithLocalEngine(Application application, string sourcePath, string targetPath, TaskRuntimeContext task, IFeatureUiService userInterface = null, bool normalizeChinesePunctuation = false)
	{
		task?.Cancellation.ThrowIfCancellationRequested("本地转换前");
		bool likelyScanned = false;
		IList<int> pagesNeedingOcr = new List<int>();
		OutputIntegrityReceipt integrityReceipt = OutputIntegrityReceipt.Create(SafeOutputTransaction.ProduceAndCommit(targetPath, delegate(string tempPath)
		{
			using FileStream docxStream = new FileStream(tempPath, FileMode.Create, FileAccess.ReadWrite);
			LocalPdfToWordResult localPdfToWordResult = new LocalPdfToWordEngine(null, normalizeChinesePunctuation).Convert(sourcePath, docxStream, CreatePerPageProgress(task), CreateReconstructProgress(task), CreateMixedContentConfirmer(userInterface));
			likelyScanned = localPdfToWordResult.LikelyScanned;
			if (localPdfToWordResult.PagesNeedingOcr != null)
			{
				pagesNeedingOcr = localPdfToWordResult.PagesNeedingOcr;
			}
		}, ".docx"), ".docx");
		Document reopenedDocument = OpenDocumentForOutput(application, targetPath);
		return new PdfToWordConversionOutput
		{
			TargetPath = targetPath,
			IntegrityReceipt = integrityReceipt,
			ReopenedDocument = reopenedDocument,
			LikelyScannedPdf = likelyScanned,
			PagesNeedingOcr = pagesNeedingOcr
		};
	}

	private static Func<PdfTypeClassificationResult, bool> CreateMixedContentConfirmer(IFeatureUiService userInterface)
	{
		if (userInterface == null)
		{
			return null;
		}
		return [MethodImpl(MethodImplOptions.NoInlining)] (PdfTypeClassificationResult classification) =>
		{
			string text = classification.FormatPagesNeedingOcr();
			string message = "该 PDF 的第 " + text + " 页为扫描、图片或无法识别的文字页，无法转换为可编辑文字（其余 " + (classification.Pages.Count - classification.PagesNeedingOcr.Count) + " 页可正常转换）。\r\n\r\n继续转换时，这些页将保留为页面图片或空白页；如需识别其中文字，请使用带 OCR 的工具处理后再转换。";
			bool result = userInterface.Confirm("PDF 转 Word - 混合型文件确认", message);
			try
			{
				LogService.Info("PdfToWordConversionService.MixedContentConfirm pages=" + classification.Pages.Count + " ocrPages=" + classification.PagesNeedingOcr.Count + " proceed=" + result);
			}
			catch
			{
			}
			return result;
		};
	}

	private static Action<int, int> CreateReconstructProgress(TaskRuntimeContext task)
	{
		if (task == null || task.Progress == null)
		{
			return null;
		}
		return [MethodImpl(MethodImplOptions.NoInlining)] (int page, int total) =>
		{
			if (task.Cancellation != null)
			{
				task.Cancellation.ThrowIfCancellationRequested("识别表格第 " + page + " 页前");
			}
			if (total <= 0)
			{
				total = 1;
			}
			if (page < 1)
			{
				page = 1;
			}
			int num = 90 + (int)Math.Round((double)(page - 1) * 5.0 / (double)total);
			if (num < 90)
			{
				num = 90;
			}
			if (num > 95)
			{
				num = 95;
			}
			task.Progress.Report(TaskProgressInfo.Create("PDF 转 Word", num, 100, "正在识别表格并重建版面（第 " + page + "/" + total + " 页）", "执行"));
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static PdfToWordConversionOutput ConvertWithHostImport(Application application, Document activeDocument, string sourcePath, string targetPath, TaskRuntimeContext task)
	{
		Documents value = null;
		Document value2 = null;
		bool flag = IsSameDocument(activeDocument, sourcePath);
		try
		{
			if (flag)
			{
				value2 = activeDocument;
			}
			else
			{
				value = application.Documents;
				try
				{
					Documents documents = value;
					object FileName = sourcePath;
					object ConfirmConversions = false;
					object ReadOnly = true;
					object AddToRecentFiles = false;
					object PasswordDocument = Type.Missing;
					object PasswordTemplate = Type.Missing;
					object Revert = Type.Missing;
					object WritePasswordDocument = Type.Missing;
					object WritePasswordTemplate = Type.Missing;
					object Format = Type.Missing;
					object Encoding = Type.Missing;
					object Visible = false;
					object OpenAndRepair = Type.Missing;
					object DocumentDirection = Type.Missing;
					object NoEncodingDialog = Type.Missing;
					object XMLTransform = Type.Missing;
					value2 = documents.Open(ref FileName, ref ConfirmConversions, ref ReadOnly, ref AddToRecentFiles, ref PasswordDocument, ref PasswordTemplate, ref Revert, ref WritePasswordDocument, ref WritePasswordTemplate, ref Format, ref Encoding, ref Visible, ref OpenAndRepair, ref DocumentDirection, ref NoEncodingDialog, ref XMLTransform);
				}
				catch (COMException innerException)
				{
					throw new InvalidOperationException("当前 Word/WPS 未能打开该 PDF。文件可能已加密，或当前版本不支持自动 PDF 转 Word。", innerException);
				}
				if (value2 == null)
				{
					throw ConvertOperationException.Create(ConvertFailureReasonCode.HostExportFailed, ConvertFailureStage.Execute);
				}
			}
			task?.Cancellation.ThrowIfCancellationRequested("保存 DOCX 前");
			OfficeDocumentOutputResult officeDocumentOutputResult;
			try
			{
				officeDocumentOutputResult = OfficeDocumentOutputTransaction.SaveCloseValidateCommit(application, value2, targetPath, WdSaveFormat.wdFormatXMLDocument, reopenCommittedDocument: true);
			}
			catch (Exception ex)
			{
				if (flag)
				{
					TryReopenSourcePdf(application, sourcePath);
				}
				if (ex is COMException)
				{
					throw new InvalidOperationException("当前 Word/WPS 无法把该 PDF 保存为 DOCX。复杂版式、受保护文件或宿主版本不兼容都可能导致此问题。", ex);
				}
				throw;
			}
			return new PdfToWordConversionOutput
			{
				TargetPath = targetPath,
				IntegrityReceipt = officeDocumentOutputResult.IntegrityReceipt,
				ReopenedDocument = officeDocumentOutputResult.ReopenedDocument,
				LikelyScannedPdf = IsLikelyScanned(officeDocumentOutputResult.ReopenedDocument)
			};
		}
		finally
		{
			if (!flag)
			{
				ComObjectRelease.Release(ref value2, "PdfToWordConversionService.PdfDocument");
			}
			else
			{
				value2 = null;
			}
			ComObjectRelease.Release(ref value, "PdfToWordConversionService.Documents");
		}
	}

	private static Action<int, int> CreatePerPageProgress(TaskRuntimeContext task)
	{
		if (task == null || task.Progress == null)
		{
			return null;
		}
		return [MethodImpl(MethodImplOptions.NoInlining)] (int page, int total) =>
		{
			if (task.Cancellation != null)
			{
				task.Cancellation.ThrowIfCancellationRequested("转换第 " + page + " 页前");
			}
			if (total <= 0)
			{
				total = 1;
			}
			if (page < 1)
			{
				page = 1;
			}
			int num = 40 + (int)Math.Round((double)(page - 1) * 50.0 / (double)total);
			if (num < 40)
			{
				num = 40;
			}
			if (num > 90)
			{
				num = 90;
			}
			task.Progress.Report(TaskProgressInfo.Create("PDF 转 Word", num, 100, "正在转换第 " + page + "/" + total + " 页", "执行"));
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static Document OpenDocumentForOutput(Application application, string path)
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
			throw new InvalidOperationException("输出文件已经生成并通过校验，但 WPS 未能重新打开该文件。", innerException);
		}
		finally
		{
			ComObjectRelease.Release(ref value, "PdfToWordConversionService.OpenDocumentForOutput.Documents");
		}
	}

	private static bool IsSameDocument(Document document, string sourcePath)
	{
		if (document == null || string.IsNullOrWhiteSpace(sourcePath))
		{
			return false;
		}
		try
		{
			string fullName = document.FullName;
			return !string.IsNullOrWhiteSpace(fullName) && string.Equals(Path.GetFullPath(fullName), Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void TryReopenSourcePdf(Application application, string sourcePath)
	{
		Documents value = null;
		Document value2 = null;
		try
		{
			if (application != null && File.Exists(sourcePath))
			{
				value = application.Documents;
				Documents documents = value;
				object FileName = sourcePath;
				object ConfirmConversions = Type.Missing;
				object ReadOnly = true;
				object AddToRecentFiles = false;
				object PasswordDocument = Type.Missing;
				object PasswordTemplate = Type.Missing;
				object Revert = Type.Missing;
				object WritePasswordDocument = Type.Missing;
				object WritePasswordTemplate = Type.Missing;
				object Format = Type.Missing;
				object Encoding = Type.Missing;
				object Visible = true;
				object OpenAndRepair = Type.Missing;
				object DocumentDirection = Type.Missing;
				object NoEncodingDialog = Type.Missing;
				object XMLTransform = Type.Missing;
				value2 = documents.Open(ref FileName, ref ConfirmConversions, ref ReadOnly, ref AddToRecentFiles, ref PasswordDocument, ref PasswordTemplate, ref Revert, ref WritePasswordDocument, ref WritePasswordTemplate, ref Format, ref Encoding, ref Visible, ref OpenAndRepair, ref DocumentDirection, ref NoEncodingDialog, ref XMLTransform);
			}
		}
		catch (Exception ex)
		{
			LogService.Warn("PdfToWordConversionService.ReopenSourcePdf type=" + ex.GetType().Name);
		}
		finally
		{
			ComObjectRelease.Release(ref value2, "PdfToWordConversionService.ReopenedSourcePdf");
			ComObjectRelease.Release(ref value, "PdfToWordConversionService.ReopenDocuments");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool IsLikelyScanned(Document document)
	{
		if (document == null)
		{
			return false;
		}
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			value = document.Content;
			string obj = ((value == null) ? string.Empty : (value.Text ?? string.Empty));
			int num = 0;
			string text = obj;
			foreach (char c in text)
			{
				if (!char.IsWhiteSpace(c) && c != '\r' && c != '\a' && c != '\f')
				{
					num++;
				}
			}
			return num < 20;
		}
		catch (Exception ex)
		{
			LogService.Warn("PdfToWordConversionService.DetectScannedPdf type=" + ex.GetType().Name);
			return false;
		}
		finally
		{
			ComObjectRelease.Release(ref value, "PdfToWordConversionService.Content");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ThrowIfHostImportSourceHasNoText(string sourcePath, TaskRuntimeContext task)
	{
		PdfReadingAdapter pdfReadingAdapter = null;
		try
		{
			pdfReadingAdapter = PdfReadingAdapter.Open(sourcePath);
			for (int i = 0; i < pdfReadingAdapter.PageCount; i++)
			{
				task?.Cancellation.ThrowIfCancellationRequested("识别 PDF 类型前");
				if (pdfReadingAdapter.ReadPage(i).MeaningfulCharacterCount > 0)
				{
					return;
				}
			}
			throw ConvertOperationException.Create(ConvertFailureReasonCode.ScannedPdfUnsupported, ConvertFailureStage.Execute);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (ConvertOperationException)
		{
			throw;
		}
		catch (Exception ex3)
		{
			LogService.Warn("PdfToWordConversionService.HostImportPreflightUnavailable, exception=" + ex3.GetType().Name);
		}
		finally
		{
			if (pdfReadingAdapter != null)
			{
				pdfReadingAdapter.Dispose();
				pdfReadingAdapter = null;
			}
		}
	}
}
