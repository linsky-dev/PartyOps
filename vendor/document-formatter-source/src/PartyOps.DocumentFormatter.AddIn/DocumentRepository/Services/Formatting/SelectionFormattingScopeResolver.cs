using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Formatting;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Formatting;

internal static class SelectionFormattingScopeResolver
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static Microsoft.Office.Interop.Word.Range Resolve(Document document, Microsoft.Office.Interop.Word.Range selectionRange)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		if (selectionRange != null)
		{
			int start = selectionRange.Start;
			int end = selectionRange.End;
			if (end > start)
			{
				Microsoft.Office.Interop.Word.Range value = null;
				Microsoft.Office.Interop.Word.Range value2 = null;
				Microsoft.Office.Interop.Word.Range value3 = null;
				Paragraphs value4 = null;
				Paragraphs value5 = null;
				Paragraphs value6 = null;
				Paragraph value7 = null;
				Paragraph value8 = null;
				Microsoft.Office.Interop.Word.Range value9 = null;
				Microsoft.Office.Interop.Word.Range value10 = null;
				Microsoft.Office.Interop.Word.Range value11 = null;
				Paragraphs value12 = null;
				try
				{
					value = document.Content;
					if (start >= value.Start && end <= value.End)
					{
						value4 = selectionRange.Paragraphs;
						int count = value4.Count;
						if (count > 0)
						{
							object Start = start;
							object End = start + 1;
							value2 = document.Range(ref Start, ref End);
							End = end - 1;
							Start = end;
							value3 = document.Range(ref End, ref Start);
							value5 = value2.Paragraphs;
							value6 = value3.Paragraphs;
							if (value5.Count > 0 && value6.Count > 0)
							{
								value7 = value5[1];
								value8 = value6[1];
								value9 = value7.Range;
								value10 = value8.Range;
								int start2 = value9.Start;
								int end2 = value10.End;
								if (start2 > start || end2 < end || end2 <= start2)
								{
									throw BoundaryFailure();
								}
								Start = start2;
								End = end2;
								value11 = document.Range(ref Start, ref End);
								value12 = value11.Paragraphs;
								int count2 = value12.Count;
								if (count2 < count)
								{
									throw BoundaryFailure();
								}
								LogService.Info("Selection scope resolved, originalStart=" + start + ", originalEnd=" + end + ", originalParagraphs=" + count + ", resolvedStart=" + start2 + ", resolvedEnd=" + end2 + ", resolvedParagraphs=" + count2);
								Microsoft.Office.Interop.Word.Range result = value11;
								value11 = null;
								return result;
							}
							throw BoundaryFailure();
						}
						throw BoundaryFailure();
					}
					throw FormatOperationException.Create(FormatFailureReasonCode.SelectionOutsideMainStory, FormatFailureStage.Entry);
				}
				finally
				{
					ComObjectRelease.Release(ref value12, "SelectionScope.ResolvedParagraphs");
					ComObjectRelease.Release(ref value11, "SelectionScope.ResolvedRange");
					ComObjectRelease.Release(ref value10, "SelectionScope.LastRange");
					ComObjectRelease.Release(ref value9, "SelectionScope.FirstRange");
					ComObjectRelease.Release(ref value8, "SelectionScope.LastParagraph");
					ComObjectRelease.Release(ref value7, "SelectionScope.FirstParagraph");
					ComObjectRelease.Release(ref value6, "SelectionScope.EndParagraphs");
					ComObjectRelease.Release(ref value5, "SelectionScope.StartParagraphs");
					ComObjectRelease.Release(ref value4, "SelectionScope.OriginalParagraphs");
					ComObjectRelease.Release(ref value3, "SelectionScope.EndProbe");
					ComObjectRelease.Release(ref value2, "SelectionScope.StartProbe");
					ComObjectRelease.Release(ref value, "SelectionScope.ContentRange");
				}
			}
			throw FormatOperationException.Create(FormatFailureReasonCode.SelectionEmpty, FormatFailureStage.Entry);
		}
		throw new ArgumentNullException("selectionRange");
	}

	private static FormatOperationException BoundaryFailure()
	{
		return FormatOperationException.Create(FormatFailureReasonCode.SelectionBoundaryUnreliable, FormatFailureStage.Plan);
	}
}
