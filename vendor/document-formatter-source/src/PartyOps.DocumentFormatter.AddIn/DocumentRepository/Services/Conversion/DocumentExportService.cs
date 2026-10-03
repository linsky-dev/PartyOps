using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using DocumentRepository.Models.Conversion;
using DocumentRepository.Services.FileSafety;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Conversion;

public static class DocumentExportService
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ExportDocx(Document document, string targetPath)
	{
		if (document == null)
		{
			throw new InvalidOperationException("当前文档无效。");
		}
		Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
		string documentPath = OutputPathService.GetDocumentPath(document);
		if (string.Equals(Path.GetExtension(documentPath), ".docx", StringComparison.OrdinalIgnoreCase) && document.Saved && File.Exists(documentPath))
		{
			File.Copy(documentPath, targetPath, overwrite: true);
			return;
		}
		Document document2 = null;
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			string wordOpenXML = document.WordOpenXML;
			Documents documents = document.Application.Documents;
			object Template = Type.Missing;
			object NewTemplate = Type.Missing;
			object DocumentType = Type.Missing;
			object Visible = false;
			document2 = documents.Add(ref Template, ref NewTemplate, ref DocumentType, ref Visible);
			value = document2.Content;
			Microsoft.Office.Interop.Word.Range range = value;
			Visible = Type.Missing;
			range.InsertXML(wordOpenXML, ref Visible);
			object FileName = targetPath;
			object FileFormat = WdSaveFormat.wdFormatXMLDocument;
			Document document3 = document2;
			Visible = Type.Missing;
			DocumentType = Type.Missing;
			NewTemplate = Type.Missing;
			Template = Type.Missing;
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
			document3.SaveAs2(ref FileName, ref FileFormat, ref Visible, ref DocumentType, ref NewTemplate, ref Template, ref ReadOnlyRecommended, ref EmbedTrueTypeFonts, ref SaveNativePictureFormat, ref SaveFormsData, ref SaveAsAOCELetter, ref Encoding, ref InsertLineBreaks, ref AllowSubstitutions, ref LineEnding, ref AddBiDiMarks, ref CompatibilityMode);
		}
		finally
		{
			if (document2 != null)
			{
				object SaveChanges = WdSaveOptions.wdDoNotSaveChanges;
				try
				{
					Document document4 = document2;
					object CompatibilityMode = Type.Missing;
					object AddBiDiMarks = Type.Missing;
					document4.Close(ref SaveChanges, ref CompatibilityMode, ref AddBiDiMarks);
				}
				catch (Exception ex)
				{
					LogService.Warn("DocumentExportService.ExportDocx.CloseTemp", ex);
				}
			}
			ComObjectRelease.Release(ref value, "DocumentExportService.ExportDocx.TempRange");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static DocxReplaceCurrentResult ExportDocxReplacingCurrent(Document document, string sourcePath, string targetPath)
	{
		if (document == null)
		{
			throw new InvalidOperationException("当前文档无效。");
		}
		if (!string.IsNullOrWhiteSpace(sourcePath) && File.Exists(sourcePath))
		{
			if (!string.IsNullOrWhiteSpace(targetPath))
			{
				string fullPath = Path.GetFullPath(sourcePath);
				string fullPath2 = Path.GetFullPath(targetPath);
				if (!string.Equals(Path.GetExtension(fullPath), ".docx", StringComparison.OrdinalIgnoreCase))
				{
					Directory.CreateDirectory(Path.GetDirectoryName(fullPath2));
					object FileName = fullPath2;
					object FileFormat = WdSaveFormat.wdFormatXMLDocument;
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
					OutputFileIntegrityValidator.Validate(fullPath2, ".docx");
					string text = OutputPathService.GetDocumentPath(document);
					if (!string.IsNullOrWhiteSpace(text))
					{
						text = Path.GetFullPath(text);
					}
					DocxReplaceCurrentResult docxReplaceCurrentResult = new DocxReplaceCurrentResult
					{
						SourcePath = fullPath,
						TargetPath = fullPath2,
						CurrentDocumentPath = text,
						SourceDeleted = true
					};
					try
					{
						if (!string.Equals(fullPath, fullPath2, StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath))
						{
							File.Delete(fullPath);
						}
					}
					catch (Exception ex)
					{
						docxReplaceCurrentResult.SourceDeleted = false;
						docxReplaceCurrentResult.SourceDeleteError = ex.Message;
						LogService.Warn("DocumentExportService.ExportDocxReplacingCurrent.DeleteSource", ex);
					}
					if (!string.Equals(docxReplaceCurrentResult.CurrentDocumentPath, fullPath2, StringComparison.OrdinalIgnoreCase))
					{
						throw new InvalidOperationException("DOCX 转换后当前文档路径不一致。目标：" + fullPath2 + "；当前：" + docxReplaceCurrentResult.CurrentDocumentPath);
					}
					return docxReplaceCurrentResult;
				}
				document.Save();
				return new DocxReplaceCurrentResult
				{
					SourcePath = fullPath,
					TargetPath = fullPath,
					CurrentDocumentPath = fullPath,
					SourceDeleted = false
				};
			}
			throw new InvalidOperationException("DOCX 输出路径无效。");
		}
		throw new InvalidOperationException("请先保存当前文档，再替换为 DOCX。");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ExportPdf(Document document, string targetPath)
	{
		if (document == null)
		{
			throw new InvalidOperationException("当前文档无效。");
		}
		Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
		ExportFixedFormat(document, targetPath, WdExportFormat.wdExportFormatPDF);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ExportXps(Document document, string targetPath)
	{
		if (document == null)
		{
			throw new InvalidOperationException("当前文档无效。");
		}
		Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
		ExportFixedFormat(document, targetPath, WdExportFormat.wdExportFormatXPS);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ExportTxt(Document document, string targetPath, ConvertOptions options)
	{
		if (document == null)
		{
			throw new InvalidOperationException("当前文档无效。");
		}
		Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
		Microsoft.Office.Interop.Word.Range value = null;
		string text;
		try
		{
			value = document.Content;
			text = value.Text ?? string.Empty;
		}
		catch (Exception ex)
		{
			LogService.Error("DocumentExportService.ExportTxt.ReadContent", ex);
			throw new InvalidOperationException("读取文档文字失败，未生成 TXT 文件。", ex);
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "DocumentExportService.ExportTxt");
			}
		}
		text = text.Replace("\r", Environment.NewLine).Replace("\a", "").Replace("\f", "");
		if (options != null && options.TxtRemoveExtraBlankLines)
		{
			text = Regex.Replace(text, "(\\r?\\n)[ \\t\u3000]*(\\r?\\n)+", Environment.NewLine + Environment.NewLine);
		}
		File.WriteAllText(targetPath, text.TrimEnd(Array.Empty<char>()) + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
		if (!File.Exists(targetPath))
		{
			throw new InvalidOperationException("TXT 文件未生成。目标路径：" + targetPath);
		}
	}

	private static void ExportFixedFormat(Document document, string targetPath, WdExportFormat format)
	{
		object FixedFormatExtClassPtr = Type.Missing;
		document.ExportAsFixedFormat(targetPath, format, OpenAfterExport: false, WdExportOptimizeFor.wdExportOptimizeForPrint, WdExportRange.wdExportAllDocument, 1, 1, WdExportItem.wdExportDocumentContent, IncludeDocProps: true, KeepIRM: true, WdExportCreateBookmarks.wdExportCreateNoBookmarks, DocStructureTags: true, BitmapMissingFonts: true, UseISO19005_1: false, ref FixedFormatExtClassPtr);
	}
}
