using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using DocumentRepository.Services.Logging;

namespace DocumentRepository.Services.Conversion.PdfToWord;

public sealed class LocalPdfToWordEngine
{
	public const int ScannedCharacterThreshold = 20;

	private readonly LayoutReconstructionOptions _layoutOptions;

	private readonly bool _normalizeChinesePunctuation;

	public LocalPdfToWordEngine()
		: this(new LayoutReconstructionOptions(), normalizeChinesePunctuation: false)
	{
	}

	public LocalPdfToWordEngine(LayoutReconstructionOptions layoutOptions)
		: this(layoutOptions, normalizeChinesePunctuation: false)
	{
	}

	public LocalPdfToWordEngine(LayoutReconstructionOptions layoutOptions, bool normalizeChinesePunctuation)
	{
		_layoutOptions = layoutOptions ?? new LayoutReconstructionOptions();
		_normalizeChinesePunctuation = normalizeChinesePunctuation;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public LocalPdfToWordResult Convert(string pdfPath, Stream docxStream, Action<int, int> perPageProgress = null, Action<int, int> reconstructProgress = null, Func<PdfTypeClassificationResult, bool> mixedContentConfirmer = null)
	{
		if (!string.IsNullOrWhiteSpace(pdfPath))
		{
			if (docxStream == null)
			{
				throw new ArgumentNullException("docxStream");
			}
			PdfReadingAdapter pdfReadingAdapter = null;
			try
			{
				try
				{
					pdfReadingAdapter = PdfReadingAdapter.Open(pdfPath);
				}
				catch (PdfReadingException ex)
				{
					if (LooksEncrypted(ex))
					{
						throw new LocalPdfToWordException(PdfToWordFailureReason.Encrypted, "PDF 已加密或受密码保护，无法读取。", ex);
					}
					throw new LocalPdfToWordException(PdfToWordFailureReason.Unsupported, "PDF 无法解析：" + ex.Message, ex);
				}
				if (pdfReadingAdapter.PageCount > 0)
				{
					int pageCount = pdfReadingAdapter.PageCount;
					List<PdfPageContent> list = new List<PdfPageContent>(pageCount);
					int num = 0;
					for (int i = 0; i < pageCount; i++)
					{
						try
						{
							PdfPageContent pdfPageContent = pdfReadingAdapter.ReadPage(i);
							list.Add(pdfPageContent);
							num += pdfPageContent.MeaningfulCharacterCount;
						}
						catch (Exception ex2)
						{
							throw new LocalPdfToWordException(PdfToWordFailureReason.Internal, "读取 PDF 第 " + (i + 1) + " 页失败：" + ex2.Message, ex2);
						}
						perPageProgress?.Invoke(i + 1, pageCount);
					}
					PdfTypeClassificationResult pdfTypeClassificationResult = PdfPageTypeClassifier.Classify(list);
					if (pdfTypeClassificationResult.DocumentKind == PdfDocumentKind.Scanned || pdfTypeClassificationResult.DocumentKind == PdfDocumentKind.ImageOnly)
					{
						throw new LocalPdfToWordException(PdfToWordFailureReason.NoText, "PDF 中未识别到文本（可能为纯扫描件）。");
					}
					if (pdfTypeClassificationResult.DocumentKind != PdfDocumentKind.Mixed || mixedContentConfirmer == null || mixedContentConfirmer(pdfTypeClassificationResult))
					{
						if (num == 0)
						{
							throw new LocalPdfToWordException(PdfToWordFailureReason.NoText, "PDF 中未识别到文本（可能为纯扫描件）。");
						}
						foreach (PdfPageClassification page in pdfTypeClassificationResult.Pages)
						{
							if (page.Type == PdfPageType.SuspectedGarbledText)
							{
								try
								{
									LogService.Info("PdfPageTypeClassifier.GarbledTextExcluded page=" + (page.PageIndex + 1) + " reason=" + page.ReasonCode + " chars=" + page.CharacterCount);
								}
								catch
								{
								}
								list[page.PageIndex].Elements.Clear();
							}
						}
						ReconstructedDocument document;
						try
						{
							document = LayoutReconstructionService.Reconstruct(list, _layoutOptions, reconstructProgress);
						}
						catch (OperationCanceledException)
						{
							throw;
						}
						catch (Exception ex4)
						{
							throw new LocalPdfToWordException(PdfToWordFailureReason.Internal, "版面重建失败：" + ex4.Message, ex4);
						}
						if (_normalizeChinesePunctuation)
						{
							ChinesePunctuationNormalizer.NormalizeDocument(document);
						}
						PdfImageRecoder.ResolveAllOrThrow(list);
						try
						{
							DocxWriter.Write(docxStream, document);
						}
						catch (Exception ex5)
						{
							throw new LocalPdfToWordException(PdfToWordFailureReason.Internal, "DOCX 写出失败：" + ex5.Message, ex5);
						}
						if (docxStream.CanSeek)
						{
							docxStream.Position = 0L;
						}
						return new LocalPdfToWordResult
						{
							MeaningfulCharacterCount = num,
							LikelyScanned = (num < 20),
							PageCount = pageCount,
							DocumentKind = pdfTypeClassificationResult.DocumentKind,
							PagesNeedingOcr = pdfTypeClassificationResult.PagesNeedingOcr
						};
					}
					throw new OperationCanceledException("用户已取消任务（混合型 PDF 转换确认）。");
				}
				throw new LocalPdfToWordException(PdfToWordFailureReason.Unsupported, "PDF 不包含任何页面。");
			}
			catch (LocalPdfToWordException)
			{
				throw;
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex8)
			{
				throw new LocalPdfToWordException(PdfToWordFailureReason.Internal, "PDF 转 Word 失败：" + ex8.Message, ex8);
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
		throw new ArgumentNullException("pdfPath");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool LooksEncrypted(Exception ex)
	{
		if (ex == null)
		{
			return false;
		}
		if (ex is LocalPdfToWordException)
		{
			return false;
		}
		string text = (ex.Message ?? string.Empty).ToLowerInvariant();
		if (text.IndexOf("encrypt", StringComparison.Ordinal) < 0 && text.IndexOf("password", StringComparison.Ordinal) < 0 && text.IndexOf("owner password", StringComparison.Ordinal) < 0 && text.IndexOf("解密", StringComparison.Ordinal) < 0)
		{
			return text.IndexOf("密码", StringComparison.Ordinal) >= 0;
		}
		return true;
	}
}
