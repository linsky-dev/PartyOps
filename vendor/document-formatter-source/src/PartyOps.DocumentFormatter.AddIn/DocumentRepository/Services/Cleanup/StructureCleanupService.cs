using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Interop;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.Snapshots;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Cleanup;

public static class StructureCleanupService
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void RemoveEmptyParagraphs(Document document, bool hasTables, bool hasImages)
	{
		if (document != null)
		{
			ParagraphTextSnapshot paragraphTextSnapshot = ParagraphTextSnapshot.Capture(document);
			bool isReliable = paragraphTextSnapshot.IsReliable;
			if (!isReliable && !string.IsNullOrWhiteSpace(paragraphTextSnapshot.FailureReason))
			{
				LogService.Warn("StructureCleanupService.RemoveEmptyParagraphs text snapshot unavailable, fallback: " + paragraphTextSnapshot.FailureReason);
			}
			if (isReliable)
			{
				for (int num = paragraphTextSnapshot.Paragraphs.Count; num >= 1; num--)
				{
					WordParagraphTextMap.Entry entry = paragraphTextSnapshot.Paragraphs[num - 1];
					if (TextCleanupService.IsEmptyParagraphText(entry.Text ?? string.Empty))
					{
						Microsoft.Office.Interop.Word.Range value = null;
						try
						{
							object Start = entry.RangeStart;
							object End = entry.RangeEnd;
							value = document.Range(ref Start, ref End);
							if ((!hasTables || !WordRangeInspector.IsInTable(value)) && (!hasImages || !WordRangeInspector.HasInlineOrAnchoredShape(value)))
							{
								Microsoft.Office.Interop.Word.Range range = value;
								End = Type.Missing;
								Start = Type.Missing;
								range.Delete(ref End, ref Start);
							}
						}
						catch (Exception ex)
						{
							LogService.Warn("StructureCleanupService.RemoveEmptyParagraphs.Document", ex);
						}
						finally
						{
							if (value != null)
							{
								ComObjectRelease.Release(ref value, "StructureCleanupService.range");
							}
						}
					}
				}
				return;
			}
			for (int num2 = document.Paragraphs.Count; num2 >= 1; num2--)
			{
				Paragraph value2 = null;
				Microsoft.Office.Interop.Word.Range value3 = null;
				try
				{
					value2 = document.Paragraphs[num2];
					if ((!hasTables || !WordRangeInspector.IsInTable(value2)) && (!hasImages || !WordRangeInspector.HasInlineOrAnchoredShape(value2)))
					{
						value3 = value2.Range;
						if (TextCleanupService.IsEmptyParagraphText(value3.Text ?? string.Empty))
						{
							Microsoft.Office.Interop.Word.Range range2 = value3;
							object Start = Type.Missing;
							object End = Type.Missing;
							range2.Delete(ref Start, ref End);
						}
					}
				}
				catch (Exception ex2)
				{
					LogService.Warn("StructureCleanupService.RemoveEmptyParagraphs.Document", ex2);
				}
				finally
				{
					if (value3 != null)
					{
						ComObjectRelease.Release(ref value3, "StructureCleanupService.range");
					}
					if (value2 != null)
					{
						ComObjectRelease.Release(ref value2, "StructureCleanupService.paragraph");
					}
				}
			}
			return;
		}
		throw new ArgumentNullException("document");
	}

	public static void RemoveEmptyParagraphs(Microsoft.Office.Interop.Word.Range range)
	{
		RemoveEmptyParagraphs(range, preserveSectionBreaks: false);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void RemoveEmptyParagraphs(Microsoft.Office.Interop.Word.Range range, bool preserveSectionBreaks)
	{
		if (range == null)
		{
			throw new ArgumentNullException("range");
		}
		Paragraphs value = null;
		try
		{
			value = range.Paragraphs;
			for (int num = value.Count; num >= 1; num--)
			{
				Paragraph value2 = null;
				Microsoft.Office.Interop.Word.Range value3 = null;
				try
				{
					value2 = value[num];
					if (!WordRangeInspector.IsInTable(value2) && !WordRangeInspector.HasInlineOrAnchoredShape(value2))
					{
						value3 = value2.Range;
						if ((!preserveSectionBreaks || !IsSectionBreakParagraph(value3)) && TextCleanupService.IsEmptyParagraphText(value3.Text ?? string.Empty))
						{
							Microsoft.Office.Interop.Word.Range range2 = value3;
							object Unit = Type.Missing;
							object Count = Type.Missing;
							range2.Delete(ref Unit, ref Count);
						}
					}
				}
				catch (Exception ex)
				{
					LogService.Warn("StructureCleanupService.RemoveEmptyParagraphs.Range", ex);
				}
				finally
				{
					if (value3 != null)
					{
						ComObjectRelease.Release(ref value3, "StructureCleanupService.paragraphRange");
					}
					if (value2 != null)
					{
						ComObjectRelease.Release(ref value2, "StructureCleanupService.paragraph");
					}
				}
			}
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "StructureCleanupService.paragraphs");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void RemoveDocumentHyperlinks(Document document)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		Hyperlinks value = null;
		try
		{
			value = document.Hyperlinks;
			for (int num = value.Count; num >= 1; num--)
			{
				Hyperlink value2 = null;
				try
				{
					Hyperlinks hyperlinks = value;
					object Index = num;
					value2 = hyperlinks.get_Item(ref Index);
					value2.Delete();
				}
				catch (Exception ex)
				{
					LogService.Warn("StructureCleanupService.RemoveDocumentHyperlinks.Document", ex);
				}
				finally
				{
					if (value2 != null)
					{
						ComObjectRelease.Release(ref value2, "StructureCleanupService.link");
					}
				}
			}
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "StructureCleanupService.links");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void RemoveDocumentHyperlinks(Microsoft.Office.Interop.Word.Range range)
	{
		if (range == null)
		{
			throw new ArgumentNullException("range");
		}
		Hyperlinks value = null;
		try
		{
			value = range.Hyperlinks;
			for (int num = value.Count; num >= 1; num--)
			{
				Hyperlink value2 = null;
				try
				{
					Hyperlinks hyperlinks = value;
					object Index = num;
					value2 = hyperlinks.get_Item(ref Index);
					value2.Delete();
				}
				catch (Exception ex)
				{
					LogService.Warn("StructureCleanupService.RemoveDocumentHyperlinks.Range", ex);
				}
				finally
				{
					if (value2 != null)
					{
						ComObjectRelease.Release(ref value2, "StructureCleanupService.link");
					}
				}
			}
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "StructureCleanupService.links");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool IsSectionBreakParagraph(Microsoft.Office.Interop.Word.Range paragraphRange)
	{
		Section value = null;
		try
		{
			value = paragraphRange.Sections[1];
			Microsoft.Office.Interop.Word.Range value2 = value.Range;
			bool result = paragraphRange.End >= value2.End - 1 && value.Index < paragraphRange.Document.Sections.Count;
			ComObjectRelease.Release(ref value2, "StructureCleanupService.SectionRange");
			return result;
		}
		catch
		{
			return false;
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "StructureCleanupService.OwnSection");
			}
		}
	}
}
