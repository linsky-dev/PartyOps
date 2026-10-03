using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Services.Detection;
using DocumentRepository.Services.Detection.Tables;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Interop;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.Snapshots;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Analysis;

public static class DocumentAnalysisService
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static DocumentAnalysisResult AnalyzeStructuralFlags(Document doc)
	{
		DocumentAnalysisResult documentAnalysisResult = new DocumentAnalysisResult();
		if (doc != null)
		{
			InlineShapes value = null;
			Shapes value2 = null;
			Hyperlinks value3 = null;
			Paragraphs value4 = null;
			try
			{
				documentAnalysisResult.TableAnalysis = TableDetector.AnalyzeDocument(doc);
				documentAnalysisResult.HasTables = documentAnalysisResult.TableAnalysis.HasTables;
				try
				{
					value = doc.InlineShapes;
					documentAnalysisResult.HasImages = value != null && value.Count > 0;
				}
				catch
				{
					documentAnalysisResult.HasImages = true;
				}
				if (!documentAnalysisResult.HasImages)
				{
					try
					{
						value2 = doc.Shapes;
						documentAnalysisResult.HasImages = value2 != null && value2.Count > 0;
					}
					catch
					{
						documentAnalysisResult.HasImages = true;
					}
				}
				try
				{
					value3 = doc.Hyperlinks;
					documentAnalysisResult.HasHyperlinks = value3 != null && value3.Count > 0;
				}
				catch
				{
					documentAnalysisResult.HasHyperlinks = false;
				}
				try
				{
					value4 = doc.Paragraphs;
					documentAnalysisResult.ParagraphCount = value4?.Count ?? 0;
				}
				catch
				{
					documentAnalysisResult.ParagraphCount = 0;
				}
				documentAnalysisResult.Elements = new DocumentElementList
				{
					ParagraphCount = documentAnalysisResult.ParagraphCount,
					HasTables = documentAnalysisResult.HasTables,
					HasImages = documentAnalysisResult.HasImages,
					HasHyperlinks = documentAnalysisResult.HasHyperlinks,
					HasEnglishNumbers = documentAnalysisResult.HasEnglishNumbers,
					HasKeywordCandidates = documentAnalysisResult.HasKeywordCandidates,
					HasSemicolonCandidates = documentAnalysisResult.HasSemicolonCandidates,
					HasTabs = documentAnalysisResult.HasTabs,
					HasOrphanCandidates = documentAnalysisResult.HasOrphanCandidates
				};
				return documentAnalysisResult;
			}
			finally
			{
				if (value != null)
				{
					ComObjectRelease.Release(ref value, "DocumentAnalysisService.inlineShapes");
				}
				if (value2 != null)
				{
					ComObjectRelease.Release(ref value2, "DocumentAnalysisService.shapes");
				}
				if (value3 != null)
				{
					ComObjectRelease.Release(ref value3, "DocumentAnalysisService.hyperlinks");
				}
				if (value4 != null)
				{
					ComObjectRelease.Release(ref value4, "DocumentAnalysisService.paragraphs");
				}
			}
		}
		return documentAnalysisResult;
	}

	public static DocumentAnalysisResult AnalyzeElements(Document doc, FormatConfig cfg)
	{
		return AnalyzeElementsCore(doc, cfg, AnalyzeStructuralFlags(doc), refreshPositionDependentFacts: false);
	}

	public static DocumentAnalysisResult AnalyzeElements(Document doc, FormatConfig cfg, DocumentAnalysisResult structuralFacts)
	{
		return AnalyzeElementsCore(doc, cfg, structuralFacts, refreshPositionDependentFacts: true);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static DocumentAnalysisResult AnalyzeElementsCore(Document doc, FormatConfig cfg, DocumentAnalysisResult structuralFacts, bool refreshPositionDependentFacts)
	{
		DocumentAnalysisResult documentAnalysisResult = structuralFacts ?? throw new ArgumentNullException("structuralFacts");
		DocumentElementList documentElementList = (documentAnalysisResult.Elements = documentAnalysisResult.Elements ?? new DocumentElementList());
		if (doc == null)
		{
			return documentAnalysisResult;
		}
		if (refreshPositionDependentFacts)
		{
			RefreshPositionDependentFacts(doc, documentAnalysisResult, documentElementList);
		}
		Paragraphs value = null;
		try
		{
			value = doc.Paragraphs;
			int num = (documentElementList.ParagraphCount = (documentAnalysisResult.ParagraphCount = value?.Count ?? 0));
			bool forceFirstParagraphAsTitle = ParagraphElementClassifier.IsFirstParagraphTitleMode(cfg);
			ParagraphTextSnapshot paragraphTextSnapshot = ParagraphTextSnapshot.Capture(doc);
			bool isReliable = paragraphTextSnapshot.IsReliable;
			if (!isReliable && !string.IsNullOrWhiteSpace(paragraphTextSnapshot.FailureReason))
			{
				LogService.Warn("DocumentAnalysisService.AnalyzeElements(doc) text snapshot unavailable, fallback to per-paragraph reads: " + paragraphTextSnapshot.FailureReason);
			}
			ParagraphClassificationState paragraphClassificationState = new ParagraphClassificationState();
			if (isReliable)
			{
				for (int i = 1; i <= num; i++)
				{
					WordParagraphTextMap.Entry entry = paragraphTextSnapshot.Paragraphs[i - 1];
					try
					{
						string text = ParagraphTextAnalysisService.CleanParagraphText(entry.Text);
						bool flag = string.IsNullOrWhiteSpace(text);
						ElementType type = ElementType.Unknown;
						if (flag)
						{
							paragraphClassificationState.InAttachmentList = false;
						}
						else
						{
							type = ParagraphElementClassifier.Classify(text, cfg, paragraphClassificationState, new ParagraphClassificationOptions
							{
								ForceFirstParagraphAsTitle = forceFirstParagraphAsTitle
							});
						}
						bool flag2 = false;
						bool flag3 = false;
						bool flag4 = false;
						if (documentAnalysisResult.HasTables || documentAnalysisResult.HasImages)
						{
							Microsoft.Office.Interop.Word.Range value2 = null;
							try
							{
								object Start = entry.RangeStart;
								object End = entry.RangeEnd;
								value2 = doc.Range(ref Start, ref End);
								flag2 = documentAnalysisResult.HasTables && WordRangeInspector.IsInTable(value2);
								flag3 = documentAnalysisResult.HasImages && WordRangeInspector.HasInlineShape(value2);
								flag4 = documentAnalysisResult.HasImages && WordRangeInspector.HasShape(value2);
							}
							finally
							{
								if (value2 != null)
								{
									ComObjectRelease.Release(ref value2, "DocumentAnalysisService.probeRange");
								}
							}
						}
						if (flag2)
						{
							type = ElementType.Table;
						}
						else if (flag3 || flag4)
						{
							type = ElementType.Image;
						}
						DocumentElement documentElement = new DocumentElement
						{
							ParagraphIndex = i,
							ScopeParagraphIndex = i,
							RangeStart = entry.RangeStart,
							RangeEnd = entry.RangeEnd,
							Text = text,
							Type = type,
							IsEmpty = flag,
							IsInTable = flag2,
							HasInlineShape = flag3,
							HasShape = flag4
						};
						ParagraphTextAnalysisService.SetPostProcessFacts(documentElement);
						ParagraphTextAnalysisService.UpdateResultFlags(documentAnalysisResult, documentElement);
						documentElementList.Items.Add(documentElement);
					}
					catch
					{
						documentElementList.Items.Add(new DocumentElement
						{
							ParagraphIndex = i,
							ScopeParagraphIndex = i,
							Text = string.Empty,
							Type = ElementType.Unknown,
							IsEmpty = true
						});
					}
				}
			}
			else
			{
				for (int j = 1; j <= num; j++)
				{
					Paragraph value3 = null;
					Microsoft.Office.Interop.Word.Range value4 = null;
					try
					{
						value3 = value[j];
						value4 = value3.Range;
						string text2 = ParagraphTextAnalysisService.CleanParagraphText(value4.Text);
						bool flag5 = string.IsNullOrWhiteSpace(text2);
						ElementType type2 = ElementType.Unknown;
						if (!flag5)
						{
							type2 = ParagraphElementClassifier.Classify(text2, cfg, paragraphClassificationState, new ParagraphClassificationOptions
							{
								ForceFirstParagraphAsTitle = forceFirstParagraphAsTitle
							});
						}
						else
						{
							paragraphClassificationState.InAttachmentList = false;
						}
						bool flag6 = documentAnalysisResult.HasTables && WordRangeInspector.IsInTable(value3);
						bool flag7 = documentAnalysisResult.HasImages && WordRangeInspector.HasInlineShape(value3);
						bool flag8 = documentAnalysisResult.HasImages && WordRangeInspector.HasShape(value3);
						if (flag6)
						{
							type2 = ElementType.Table;
						}
						else if (flag7 || flag8)
						{
							type2 = ElementType.Image;
						}
						DocumentElement documentElement2 = new DocumentElement
						{
							ParagraphIndex = j,
							ScopeParagraphIndex = j,
							RangeStart = value4.Start,
							RangeEnd = value4.End,
							Text = text2,
							Type = type2,
							IsEmpty = flag5,
							IsInTable = flag6,
							HasInlineShape = flag7,
							HasShape = flag8
						};
						ParagraphTextAnalysisService.SetPostProcessFacts(documentElement2);
						ParagraphTextAnalysisService.UpdateResultFlags(documentAnalysisResult, documentElement2);
						documentElementList.Items.Add(documentElement2);
					}
					catch
					{
						documentElementList.Items.Add(new DocumentElement
						{
							ParagraphIndex = j,
							ScopeParagraphIndex = j,
							Text = string.Empty,
							Type = ElementType.Unknown,
							IsEmpty = true
						});
					}
					finally
					{
						if (value4 != null)
						{
							ComObjectRelease.Release(ref value4, "DocumentAnalysisService.range");
						}
						if (value3 != null)
						{
							ComObjectRelease.Release(ref value3, "DocumentAnalysisService.para");
						}
					}
				}
			}
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "DocumentAnalysisService.paragraphs");
			}
		}
		documentElementList.HasTables = documentAnalysisResult.HasTables;
		documentElementList.HasImages = documentAnalysisResult.HasImages;
		documentElementList.HasAttachments = documentAnalysisResult.HasAttachments;
		documentElementList.HasHyperlinks = documentAnalysisResult.HasHyperlinks;
		documentElementList.HasEnglishNumbers = documentAnalysisResult.HasEnglishNumbers;
		documentElementList.HasKeywordCandidates = documentAnalysisResult.HasKeywordCandidates;
		documentElementList.HasSemicolonCandidates = documentAnalysisResult.HasSemicolonCandidates;
		documentElementList.HasTabs = documentAnalysisResult.HasTabs;
		documentElementList.HasOrphanCandidates = documentAnalysisResult.HasOrphanCandidates;
		return documentAnalysisResult;
	}

	private static void RefreshPositionDependentFacts(Document doc, DocumentAnalysisResult result, DocumentElementList elements)
	{
		result.TableAnalysis = TableDetector.AnalyzeDocument(doc);
		result.HasTables = result.TableAnalysis.HasTables;
		elements.HasTables = result.HasTables;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static DocumentAnalysisResult AnalyzeElements(Microsoft.Office.Interop.Word.Range scopeRange, FormatConfig cfg, bool allowFirstParagraphAsTitle)
	{
		DocumentAnalysisResult documentAnalysisResult = new DocumentAnalysisResult();
		DocumentElementList documentElementList = (documentAnalysisResult.Elements = new DocumentElementList());
		if (scopeRange == null)
		{
			return documentAnalysisResult;
		}
		InlineShapes value = null;
		ShapeRange value2 = null;
		Hyperlinks value3 = null;
		Paragraphs value4 = null;
		try
		{
			documentAnalysisResult.TableAnalysis = TableDetector.AnalyzeRange(scopeRange);
			documentAnalysisResult.HasTables = documentAnalysisResult.TableAnalysis.HasTables;
			try
			{
				value = scopeRange.InlineShapes;
				documentAnalysisResult.HasImages = value != null && value.Count > 0;
			}
			catch
			{
				documentAnalysisResult.HasImages = true;
			}
			if (!documentAnalysisResult.HasImages)
			{
				try
				{
					value2 = scopeRange.ShapeRange;
					documentAnalysisResult.HasImages = value2 != null && value2.Count > 0;
				}
				catch
				{
					documentAnalysisResult.HasImages = true;
				}
			}
			try
			{
				value3 = scopeRange.Hyperlinks;
				documentAnalysisResult.HasHyperlinks = value3 != null && value3.Count > 0;
			}
			catch
			{
				documentAnalysisResult.HasHyperlinks = false;
			}
			value4 = scopeRange.Paragraphs;
			int num = (documentElementList.ParagraphCount = (documentAnalysisResult.ParagraphCount = value4?.Count ?? 0));
			bool forceFirstParagraphAsTitle = allowFirstParagraphAsTitle && ParagraphElementClassifier.IsFirstParagraphTitleMode(cfg);
			ParagraphClassificationState paragraphClassificationState = new ParagraphClassificationState();
			for (int i = 1; i <= num; i++)
			{
				Paragraph value5 = null;
				Microsoft.Office.Interop.Word.Range value6 = null;
				try
				{
					value5 = value4[i];
					value6 = value5.Range;
					string text = ParagraphTextAnalysisService.CleanParagraphText(value6.Text);
					bool flag = string.IsNullOrWhiteSpace(text);
					ElementType type = ElementType.Unknown;
					if (flag)
					{
						paragraphClassificationState.InAttachmentList = false;
					}
					else
					{
						type = ParagraphElementClassifier.Classify(text, cfg, paragraphClassificationState, new ParagraphClassificationOptions
						{
							ForceFirstParagraphAsTitle = forceFirstParagraphAsTitle,
							PreferSignaturePair = true,
							NextParagraphIsDateLine = IsNextSelectedDateLine(value4, i, num),
							PreviousParagraphIsSignatureLine = IsPreviousSelectedSignatureLine(value4, i, cfg)
						});
					}
					bool flag2 = documentAnalysisResult.HasTables && WordRangeInspector.IsInTable(value5);
					bool flag3 = documentAnalysisResult.HasImages && WordRangeInspector.HasInlineShape(value5);
					bool flag4 = documentAnalysisResult.HasImages && WordRangeInspector.HasShape(value5);
					if (flag2)
					{
						type = ElementType.Table;
					}
					else if (flag3 || flag4)
					{
						type = ElementType.Image;
					}
					DocumentElement documentElement = new DocumentElement
					{
						ParagraphIndex = i,
						ScopeParagraphIndex = i,
						RangeStart = value6.Start,
						RangeEnd = value6.End,
						Text = text,
						Type = type,
						IsEmpty = flag,
						IsInTable = flag2,
						HasInlineShape = flag3,
						HasShape = flag4
					};
					ParagraphTextAnalysisService.SetPostProcessFacts(documentElement);
					ParagraphTextAnalysisService.UpdateResultFlags(documentAnalysisResult, documentElement);
					documentElementList.Items.Add(documentElement);
				}
				catch
				{
					documentElementList.Items.Add(new DocumentElement
					{
						ParagraphIndex = i,
						ScopeParagraphIndex = i,
						Text = string.Empty,
						Type = ElementType.Unknown,
						IsEmpty = true
					});
				}
				finally
				{
					if (value6 != null)
					{
						ComObjectRelease.Release(ref value6, "DocumentAnalysisService.range");
					}
					if (value5 != null)
					{
						ComObjectRelease.Release(ref value5, "DocumentAnalysisService.para");
					}
				}
			}
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "DocumentAnalysisService.inlineShapes");
			}
			if (value2 != null)
			{
				ComObjectRelease.Release(ref value2, "DocumentAnalysisService.shapes");
			}
			if (value3 != null)
			{
				ComObjectRelease.Release(ref value3, "DocumentAnalysisService.hyperlinks");
			}
			if (value4 != null)
			{
				ComObjectRelease.Release(ref value4, "DocumentAnalysisService.paragraphs");
			}
		}
		documentElementList.HasTables = documentAnalysisResult.HasTables;
		documentElementList.HasImages = documentAnalysisResult.HasImages;
		documentElementList.HasAttachments = documentAnalysisResult.HasAttachments;
		documentElementList.HasHyperlinks = documentAnalysisResult.HasHyperlinks;
		documentElementList.HasEnglishNumbers = documentAnalysisResult.HasEnglishNumbers;
		documentElementList.HasKeywordCandidates = documentAnalysisResult.HasKeywordCandidates;
		documentElementList.HasSemicolonCandidates = documentAnalysisResult.HasSemicolonCandidates;
		documentElementList.HasTabs = documentAnalysisResult.HasTabs;
		documentElementList.HasOrphanCandidates = documentAnalysisResult.HasOrphanCandidates;
		return documentAnalysisResult;
	}

	private static bool IsSelectedDateLine(string text)
	{
		return SignatureDetector.IsDateLine(text);
	}

	private static bool IsNextSelectedDateLine(Paragraphs paragraphs, int index, int count)
	{
		if (paragraphs == null || index >= count)
		{
			return false;
		}
		return IsSelectedDateLine(GetCleanParagraphText(paragraphs, index + 1));
	}

	private static bool IsPreviousSelectedSignatureLine(Paragraphs paragraphs, int index, FormatConfig cfg)
	{
		if (paragraphs == null || index <= 1)
		{
			return false;
		}
		return ParagraphElementClassifier.IsSelectedSignatureLine(GetCleanParagraphText(paragraphs, index - 1), cfg);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string GetCleanParagraphText(Paragraphs paragraphs, int index)
	{
		Paragraph value = null;
		Microsoft.Office.Interop.Word.Range value2 = null;
		try
		{
			value = paragraphs[index];
			value2 = value.Range;
			return ParagraphTextAnalysisService.CleanParagraphText(value2.Text);
		}
		catch
		{
			return string.Empty;
		}
		finally
		{
			if (value2 != null)
			{
				ComObjectRelease.Release(ref value2, "DocumentAnalysisService.range");
			}
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "DocumentAnalysisService.para");
			}
		}
	}
}
