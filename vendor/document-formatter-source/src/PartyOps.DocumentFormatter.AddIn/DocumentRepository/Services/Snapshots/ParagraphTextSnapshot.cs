using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Snapshots;

internal sealed class ParagraphTextSnapshot
{
	public bool IsReliable { get; private set; }

	public string FailureReason { get; private set; }

	public int WordParagraphCount { get; private set; }

	public int ScopeStart { get; private set; }

	public int ScopeEnd { get; private set; }

	public string SourceText { get; private set; }

	public List<WordParagraphTextMap.Entry> Paragraphs { get; private set; }

	private ParagraphTextSnapshot()
	{
		Paragraphs = new List<WordParagraphTextMap.Entry>();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ParagraphTextSnapshot Capture(Document document, Microsoft.Office.Interop.Word.Range scopeRange = null)
	{
		ParagraphTextSnapshot paragraphTextSnapshot = new ParagraphTextSnapshot();
		if (document == null)
		{
			paragraphTextSnapshot.FailureReason = "document-null";
			return paragraphTextSnapshot;
		}
		if (scopeRange != null)
		{
			paragraphTextSnapshot.FailureReason = "partial-scope-uses-legacy-path";
			return paragraphTextSnapshot;
		}
		Microsoft.Office.Interop.Word.Range value = null;
		Paragraphs value2 = null;
		try
		{
			value = ((scopeRange == null) ? document.Content : scopeRange.Duplicate);
			value2 = value.Paragraphs;
			paragraphTextSnapshot.WordParagraphCount = value2?.Count ?? 0;
			paragraphTextSnapshot.ScopeStart = value.Start;
			paragraphTextSnapshot.ScopeEnd = value.End;
			string text = value.Text;
			WordParagraphTextMap.Result result = WordParagraphTextMap.Build(text, paragraphTextSnapshot.ScopeStart, paragraphTextSnapshot.ScopeEnd, paragraphTextSnapshot.WordParagraphCount);
			paragraphTextSnapshot.IsReliable = result.IsReliable;
			paragraphTextSnapshot.FailureReason = result.FailureReason;
			paragraphTextSnapshot.Paragraphs.AddRange(result.Paragraphs);
			if (paragraphTextSnapshot.IsReliable)
			{
				paragraphTextSnapshot.SourceText = text;
			}
			return paragraphTextSnapshot;
		}
		catch (Exception ex)
		{
			paragraphTextSnapshot.Paragraphs.Clear();
			paragraphTextSnapshot.IsReliable = false;
			paragraphTextSnapshot.FailureReason = ex.Message;
			LogService.Warn("ParagraphTextSnapshot.Capture", ex);
			return paragraphTextSnapshot;
		}
		finally
		{
			ComObjectRelease.Release(ref value2, "ParagraphTextSnapshot.Paragraphs");
			ComObjectRelease.Release(ref value, "ParagraphTextSnapshot.Scope");
		}
	}

	public string TextAt(int index)
	{
		if (index < 1 || index > Paragraphs.Count)
		{
			return null;
		}
		return Paragraphs[index - 1].Text;
	}
}
