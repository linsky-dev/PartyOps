using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using DocumentRepository.Models.Snapshots;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Interop;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Snapshots;

public static class DocumentSnapshotService
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static DocumentSnapshot CaptureSafetyBaseline(Document document)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			value = document.Content;
			return new DocumentSnapshot
			{
				SnapshotId = Guid.NewGuid().ToString("N"),
				DocumentIdentity = GetDocumentIdentity(document),
				WasSaved = ReadSavedState(document),
				ScopeStart = value.Start,
				ScopeEnd = value.End,
				ProtectedObjects = CaptureProtectedObjects(document, value, fullDocument: true)
			};
		}
		finally
		{
			ComObjectRelease.Release(ref value, "DocumentSnapshot.SafetyBaseline.Scope");
		}
	}

	public static DocumentSnapshot Capture(Document document, Microsoft.Office.Interop.Word.Range scopeRange = null)
	{
		return Capture(document, scopeRange, collectStyleNames: false);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static DocumentSnapshot Capture(Document document, Microsoft.Office.Interop.Word.Range scopeRange, bool collectStyleNames)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		Microsoft.Office.Interop.Word.Range value = null;
		Paragraphs value2 = null;
		try
		{
			value = ((scopeRange == null) ? document.Content : scopeRange.Duplicate);
			DocumentSnapshot documentSnapshot = new DocumentSnapshot
			{
				SnapshotId = Guid.NewGuid().ToString("N"),
				DocumentIdentity = GetDocumentIdentity(document),
				WasSaved = ReadSavedState(document),
				ScopeStart = value.Start,
				ScopeEnd = value.End
			};
			ParagraphTextSnapshot paragraphTextSnapshot = ParagraphTextSnapshot.Capture(document, scopeRange);
			bool isReliable = paragraphTextSnapshot.IsReliable;
			if (!isReliable && !string.IsNullOrWhiteSpace(paragraphTextSnapshot.FailureReason))
			{
				LogService.Warn("DocumentSnapshotService.Capture 文本快照不可用，回退逐段读取：" + paragraphTextSnapshot.FailureReason);
			}
			string text = ((isReliable && paragraphTextSnapshot.SourceText != null) ? paragraphTextSnapshot.SourceText : (value.Text ?? string.Empty));
			value2 = value.Paragraphs;
			int num = (documentSnapshot.ParagraphCount = value2?.Count ?? 0);
			StringBuilder stringBuilder = new StringBuilder(text.Length + num * 80);
			stringBuilder.Append(text);
			stringBuilder.Append('|').Append(value.End - value.Start);
			if (!isReliable)
			{
				CaptureParagraphsLegacy(documentSnapshot, value2, stringBuilder, collectStyleNames);
			}
			else
			{
				CaptureParagraphsWithSnapshot(document, documentSnapshot, paragraphTextSnapshot, stringBuilder, collectStyleNames);
			}
			documentSnapshot.ProtectedObjects = CaptureProtectedObjects(document, value, scopeRange == null);
			documentSnapshot.ContentFingerprint = Hash(text + "|paragraphs=" + num);
			documentSnapshot.StateFingerprint = Hash(stringBuilder.ToString());
			if (scopeRange == null)
			{
				documentSnapshot.RecoveryFingerprint = CaptureRecoveryFingerprint(document, documentSnapshot);
			}
			return documentSnapshot;
		}
		finally
		{
			ComObjectRelease.Release(ref value2, "DocumentSnapshot.Paragraphs");
			ComObjectRelease.Release(ref value, "DocumentSnapshot.Scope");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void CaptureParagraphsWithSnapshot(Document document, DocumentSnapshot snapshot, ParagraphTextSnapshot textSnapshot, StringBuilder stateBuilder, bool collectStyleNames)
	{
		for (int i = 1; i <= textSnapshot.Paragraphs.Count; i++)
		{
			WordParagraphTextMap.Entry entry = textSnapshot.Paragraphs[i - 1];
			Microsoft.Office.Interop.Word.Range value = null;
			ParagraphFormat value2 = null;
			Font value3 = null;
			Paragraphs value4 = null;
			Paragraph value5 = null;
			object obj = null;
			object obj2 = null;
			try
			{
				object Start = entry.RangeStart;
				object End = entry.RangeEnd;
				value = document.Range(ref Start, ref End);
				value2 = value.ParagraphFormat;
				value3 = value.Font;
				obj = value.get_Style();
				if (collectStyleNames)
				{
					value4 = value.Paragraphs;
					if (value4 != null && value4.Count > 0)
					{
						value5 = value4[1];
						obj2 = value5.get_Style();
					}
				}
				string text = entry.Text ?? string.Empty;
				string value6 = BuildFormatFacts(obj, value2, value3);
				snapshot.Paragraphs.Add(new ParagraphSnapshot
				{
					Index = i,
					RangeStart = entry.RangeStart,
					RangeEnd = entry.RangeEnd,
					Text = text,
					TextFingerprint = Hash(text),
					FormatFingerprint = Hash(value6),
					StyleName = (collectStyleNames ? ResolveParagraphStructuralStyleName(obj2) : null),
					IsInTable = WordRangeInspector.IsInTable(value)
				});
				stateBuilder.Append('|').Append(entry.RangeStart - snapshot.ScopeStart).Append(':')
					.Append(entry.RangeEnd - snapshot.ScopeStart)
					.Append(':')
					.Append(Hash(text))
					.Append(':')
					.Append(Hash(value6));
			}
			finally
			{
				ComObjectRelease.Release(obj2, "DocumentSnapshot.ParagraphStyle");
				ComObjectRelease.Release(ref value5, "DocumentSnapshot.FirstParagraph");
				ComObjectRelease.Release(ref value4, "DocumentSnapshot.RangeParagraphs");
				ComObjectRelease.Release(obj, "DocumentSnapshot.Style");
				ComObjectRelease.Release(ref value3, "DocumentSnapshot.Font");
				ComObjectRelease.Release(ref value2, "DocumentSnapshot.ParagraphFormat");
				ComObjectRelease.Release(ref value, "DocumentSnapshot.Range");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void CaptureParagraphsLegacy(DocumentSnapshot snapshot, Paragraphs paragraphs, StringBuilder stateBuilder, bool collectStyleNames)
	{
		int num = paragraphs?.Count ?? 0;
		for (int i = 1; i <= num; i++)
		{
			Paragraph value = null;
			Microsoft.Office.Interop.Word.Range value2 = null;
			ParagraphFormat value3 = null;
			Font value4 = null;
			object obj = null;
			try
			{
				value = paragraphs[i];
				value2 = value.Range;
				value3 = value2.ParagraphFormat;
				value4 = value2.Font;
				obj = value.get_Style();
				string text = value2.Text ?? string.Empty;
				string value5 = BuildFormatFacts(obj, value3, value4);
				snapshot.Paragraphs.Add(new ParagraphSnapshot
				{
					Index = i,
					RangeStart = value2.Start,
					RangeEnd = value2.End,
					Text = text,
					TextFingerprint = Hash(text),
					FormatFingerprint = Hash(value5),
					StyleName = (collectStyleNames ? ResolveParagraphStructuralStyleName(obj) : null),
					IsInTable = WordRangeInspector.IsInTable(value)
				});
				stateBuilder.Append('|').Append(value2.Start - snapshot.ScopeStart).Append(':')
					.Append(value2.End - snapshot.ScopeStart)
					.Append(':')
					.Append(Hash(text))
					.Append(':')
					.Append(Hash(value5));
			}
			finally
			{
				ComObjectRelease.Release(obj, "DocumentSnapshot.Style");
				ComObjectRelease.Release(ref value4, "DocumentSnapshot.Font");
				ComObjectRelease.Release(ref value3, "DocumentSnapshot.ParagraphFormat");
				ComObjectRelease.Release(ref value2, "DocumentSnapshot.Range");
				ComObjectRelease.Release(ref value, "DocumentSnapshot.Paragraph");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void AssertSourceUnchanged(DocumentSnapshot expected, Document document, Microsoft.Office.Interop.Word.Range scopeRange = null)
	{
		if (expected != null)
		{
			DocumentSnapshot documentSnapshot = Capture(document, scopeRange);
			if (string.Equals(expected.StateFingerprint, documentSnapshot.StateFingerprint, StringComparison.Ordinal))
			{
				return;
			}
			throw new DocumentSnapshotMismatchException(DocumentSnapshotMismatchKind.SourceState, "文档在生成排版计划后发生了变化，旧计划已拒绝执行。");
		}
		throw new ArgumentNullException("expected");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void AssertRecoveryRestored(DocumentSnapshot expected, Document document)
	{
		if (expected == null)
		{
			throw new ArgumentNullException("expected");
		}
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			bool num = !string.IsNullOrWhiteSpace(expected.RecoveryFingerprint);
			if (!num)
			{
				object Start = expected.ScopeStart;
				object End = expected.ScopeEnd;
				value = document.Range(ref Start, ref End);
			}
			DocumentSnapshot documentSnapshot = Capture(document, value);
			if (!string.Equals(expected.StateFingerprint, documentSnapshot.StateFingerprint, StringComparison.Ordinal))
			{
				throw new DocumentSnapshotMismatchException(DocumentSnapshotMismatchKind.SourceState, "文档回滚后的正文内容或格式与修改前不一致。");
			}
			if (num && (string.IsNullOrWhiteSpace(documentSnapshot.RecoveryFingerprint) || !string.Equals(expected.RecoveryFingerprint, documentSnapshot.RecoveryFingerprint, StringComparison.Ordinal)))
			{
				throw new DocumentSnapshotMismatchException(DocumentSnapshotMismatchKind.SourceState, "文档恢复后的故事区、节设置、书签或自定义属性与修改前不一致。");
			}
		}
		finally
		{
			ComObjectRelease.Release(ref value, "DocumentSnapshot.Recovery.Scope");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void AssertContentMatches(DocumentSnapshot expected, Document document, Microsoft.Office.Interop.Word.Range scopeRange = null)
	{
		if (expected == null)
		{
			throw new ArgumentNullException("expected");
		}
		DocumentSnapshot documentSnapshot = Capture(document, scopeRange);
		if (!string.Equals(expected.ContentFingerprint, documentSnapshot.ContentFingerprint, StringComparison.Ordinal) || expected.ParagraphCount != documentSnapshot.ParagraphCount)
		{
			throw new DocumentSnapshotMismatchException(DocumentSnapshotMismatchKind.NormalizedContent, "活动文档清理结果与规划副本不一致，已停止后续排版。");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static OutsideScopeSnapshot CaptureOutsideScope(Document document, Microsoft.Office.Interop.Word.Range scopeRange)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		if (scopeRange == null)
		{
			throw new ArgumentNullException("scopeRange");
		}
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			value = document.Content;
			int start = value.Start;
			int num = Math.Max(value.Start, scopeRange.Start);
			int num2 = Math.Min(scopeRange.End, value.End);
			int end = value.End;
			return new OutsideScopeSnapshot
			{
				PrefixStart = start,
				PrefixEnd = num,
				SuffixStart = num2,
				SuffixEnd = end,
				PrefixStateFingerprint = CaptureStrictSegmentState(document, start, num),
				SuffixStateFingerprint = CaptureStrictSegmentState(document, num2, end)
			};
		}
		finally
		{
			ComObjectRelease.Release(ref value, "DocumentSnapshot.OutsideContent");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void AssertOutsideScopeUnchanged(OutsideScopeSnapshot expected, Document document, Microsoft.Office.Interop.Word.Range currentScopeRange)
	{
		if (expected != null)
		{
			OutsideScopeSnapshot outsideScopeSnapshot = CaptureOutsideScope(document, currentScopeRange);
			bool flag = !string.Equals(expected.PrefixStateFingerprint, outsideScopeSnapshot.PrefixStateFingerprint, StringComparison.Ordinal);
			bool flag2 = !string.Equals(expected.SuffixStateFingerprint, outsideScopeSnapshot.SuffixStateFingerprint, StringComparison.Ordinal);
			if (flag || flag2)
			{
				LogService.Error("DocumentSnapshotService.AssertOutsideScopeUnchanged | prefixChanged=" + flag + ", suffixChanged=" + flag2 + ", expectedPrefix=" + expected.PrefixStart + "-" + expected.PrefixEnd + ", actualPrefix=" + outsideScopeSnapshot.PrefixStart + "-" + outsideScopeSnapshot.PrefixEnd + ", expectedSuffix=" + expected.SuffixStart + "-" + expected.SuffixEnd + ", actualSuffix=" + outsideScopeSnapshot.SuffixStart + "-" + outsideScopeSnapshot.SuffixEnd);
				throw new DocumentSnapshotMismatchException(DocumentSnapshotMismatchKind.OutsideScope, "选区外内容或格式发生变化，排版结果已判定为无效。");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string CaptureStrictSegmentState(Document document, int start, int end)
	{
		if (end <= start)
		{
			return Hash(string.Empty);
		}
		Microsoft.Office.Interop.Word.Range value = null;
		Paragraphs value2 = null;
		try
		{
			object Start = start;
			object End = end;
			value = document.Range(ref Start, ref End);
			StringBuilder stringBuilder = new StringBuilder();
			string value3 = value.Text ?? string.Empty;
			stringBuilder.Append(value3).Append('|').Append(end - start);
			value2 = value.Paragraphs;
			int num = value2?.Count ?? 0;
			for (int i = 1; i <= num; i++)
			{
				Paragraph value4 = null;
				Microsoft.Office.Interop.Word.Range value5 = null;
				ParagraphFormat value6 = null;
				Font value7 = null;
				object obj = null;
				try
				{
					value4 = value2[i];
					value5 = value4.Range;
					if (value5.Start >= start && value5.End <= end)
					{
						value6 = value5.ParagraphFormat;
						value7 = value5.Font;
						obj = value4.get_Style();
						stringBuilder.Append('|').Append(value5.Start - start).Append(':')
							.Append(value5.End - start)
							.Append(':')
							.Append(Hash(value5.Text ?? string.Empty))
							.Append(':')
							.Append(Hash(BuildFormatFacts(obj, value6, value7)));
					}
				}
				finally
				{
					ComObjectRelease.Release(obj, "DocumentSnapshot.StrictOutside.Style");
					ComObjectRelease.Release(ref value7, "DocumentSnapshot.StrictOutside.Font");
					ComObjectRelease.Release(ref value6, "DocumentSnapshot.StrictOutside.ParagraphFormat");
					ComObjectRelease.Release(ref value5, "DocumentSnapshot.StrictOutside.Range");
					ComObjectRelease.Release(ref value4, "DocumentSnapshot.StrictOutside.Paragraph");
				}
			}
			return Hash(stringBuilder.ToString());
		}
		finally
		{
			ComObjectRelease.Release(ref value2, "DocumentSnapshot.StrictOutside.Paragraphs");
			ComObjectRelease.Release(ref value, "DocumentSnapshot.StrictOutside.Range");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ProtectedObjectSnapshot CaptureProtectedObjects(Document document, Microsoft.Office.Interop.Word.Range scope, bool fullDocument)
	{
		ProtectedObjectSnapshot protectedObjectSnapshot = new ProtectedObjectSnapshot();
		Fields value = null;
		Hyperlinks value2 = null;
		Bookmarks value3 = null;
		Comments value4 = null;
		ContentControls value5 = null;
		InlineShapes value6 = null;
		Tables value7 = null;
		Shapes value8 = null;
		try
		{
			value = scope.Fields;
			value2 = scope.Hyperlinks;
			value3 = scope.Bookmarks;
			value4 = scope.Comments;
			value5 = scope.ContentControls;
			value6 = scope.InlineShapes;
			value7 = scope.Tables;
			protectedObjectSnapshot.Fields = value?.Count ?? 0;
			protectedObjectSnapshot.Hyperlinks = value2?.Count ?? 0;
			protectedObjectSnapshot.Bookmarks = value3?.Count ?? 0;
			protectedObjectSnapshot.Comments = value4?.Count ?? 0;
			protectedObjectSnapshot.ContentControls = value5?.Count ?? 0;
			protectedObjectSnapshot.InlineShapes = value6?.Count ?? 0;
			protectedObjectSnapshot.Tables = value7?.Count ?? 0;
			if (fullDocument)
			{
				value8 = document.Shapes;
				protectedObjectSnapshot.Shapes = value8?.Count ?? 0;
			}
			return protectedObjectSnapshot;
		}
		finally
		{
			ComObjectRelease.Release(ref value8, "DocumentSnapshot.Shapes");
			ComObjectRelease.Release(ref value7, "DocumentSnapshot.Tables");
			ComObjectRelease.Release(ref value6, "DocumentSnapshot.InlineShapes");
			ComObjectRelease.Release(ref value5, "DocumentSnapshot.ContentControls");
			ComObjectRelease.Release(ref value4, "DocumentSnapshot.Comments");
			ComObjectRelease.Release(ref value3, "DocumentSnapshot.Bookmarks");
			ComObjectRelease.Release(ref value2, "DocumentSnapshot.Hyperlinks");
			ComObjectRelease.Release(ref value, "DocumentSnapshot.Fields");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string CaptureRecoveryFingerprint(Document document, DocumentSnapshot snapshot)
	{
		StringBuilder stringBuilder = new StringBuilder(4096);
		stringBuilder.Append("state=").Append(snapshot.StateFingerprint).Append("|saved=")
			.Append(snapshot.WasSaved);
		ProtectedObjectSnapshot protectedObjectSnapshot = snapshot.ProtectedObjects ?? new ProtectedObjectSnapshot();
		stringBuilder.Append("|objects=").Append(protectedObjectSnapshot.Fields).Append(',')
			.Append(protectedObjectSnapshot.Hyperlinks)
			.Append(',')
			.Append(protectedObjectSnapshot.Bookmarks)
			.Append(',')
			.Append(protectedObjectSnapshot.Comments)
			.Append(',')
			.Append(protectedObjectSnapshot.ContentControls)
			.Append(',')
			.Append(protectedObjectSnapshot.InlineShapes)
			.Append(',')
			.Append(protectedObjectSnapshot.Shapes)
			.Append(',')
			.Append(protectedObjectSnapshot.Tables);
		AppendSectionFacts(document, stringBuilder);
		AppendStoryFacts(document, stringBuilder);
		AppendBookmarkFacts(document, stringBuilder);
		AppendCustomPropertyFacts(document, snapshot, stringBuilder);
		return Hash(stringBuilder.ToString());
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void AppendSectionFacts(Document document, StringBuilder builder)
	{
		Sections value = null;
		try
		{
			value = document.Sections;
			int num = value?.Count ?? 0;
			builder.Append("|sections=").Append(num);
			for (int i = 1; i <= num; i++)
			{
				Section value2 = null;
				PageSetup value3 = null;
				try
				{
					value2 = value[i];
					value3 = value2.PageSetup;
					builder.Append('|').Append(i).Append(':')
						.Append((int)value3.Orientation)
						.Append(':')
						.Append(value3.PageWidth.ToString(CultureInfo.InvariantCulture))
						.Append(':')
						.Append(value3.PageHeight.ToString(CultureInfo.InvariantCulture))
						.Append(':')
						.Append(value3.TopMargin.ToString(CultureInfo.InvariantCulture))
						.Append(':')
						.Append(value3.BottomMargin.ToString(CultureInfo.InvariantCulture))
						.Append(':')
						.Append(value3.LeftMargin.ToString(CultureInfo.InvariantCulture))
						.Append(':')
						.Append(value3.RightMargin.ToString(CultureInfo.InvariantCulture))
						.Append(':')
						.Append(value3.HeaderDistance.ToString(CultureInfo.InvariantCulture))
						.Append(':')
						.Append(value3.FooterDistance.ToString(CultureInfo.InvariantCulture));
				}
				finally
				{
					ComObjectRelease.Release(ref value3, "DocumentSnapshot.Recovery.PageSetup");
					ComObjectRelease.Release(ref value2, "DocumentSnapshot.Recovery.Section");
				}
			}
		}
		finally
		{
			ComObjectRelease.Release(ref value, "DocumentSnapshot.Recovery.Sections");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void AppendStoryFacts(Document document, StringBuilder builder)
	{
		WdStoryType[] array = new WdStoryType[11]
		{
			WdStoryType.wdMainTextStory,
			WdStoryType.wdFootnotesStory,
			WdStoryType.wdEndnotesStory,
			WdStoryType.wdCommentsStory,
			WdStoryType.wdTextFrameStory,
			WdStoryType.wdPrimaryHeaderStory,
			WdStoryType.wdEvenPagesHeaderStory,
			WdStoryType.wdFirstPageHeaderStory,
			WdStoryType.wdPrimaryFooterStory,
			WdStoryType.wdEvenPagesFooterStory,
			WdStoryType.wdFirstPageFooterStory
		};
		foreach (WdStoryType wdStoryType in array)
		{
			Microsoft.Office.Interop.Word.Range value = null;
			try
			{
				try
				{
					value = document.StoryRanges[wdStoryType];
				}
				catch
				{
					value = null;
				}
				int num = 0;
				while (value != null)
				{
					num++;
					if (num <= 10000)
					{
						Fields value2 = null;
						InlineShapes value3 = null;
						Tables value4 = null;
						Microsoft.Office.Interop.Word.Range range = null;
						try
						{
							value2 = value.Fields;
							value3 = value.InlineShapes;
							value4 = value.Tables;
							builder.Append("|story=").Append((int)wdStoryType).Append(':')
								.Append(num)
								.Append(':')
								.Append(value.Start)
								.Append(':')
								.Append(value.End)
								.Append(':')
								.Append(Hash(value.Text ?? string.Empty))
								.Append(':')
								.Append(value2?.Count ?? 0)
								.Append(':')
								.Append(value3?.Count ?? 0)
								.Append(':')
								.Append(value4?.Count ?? 0);
							range = value.NextStoryRange;
						}
						finally
						{
							ComObjectRelease.Release(ref value4, "DocumentSnapshot.Recovery.StoryTables");
							ComObjectRelease.Release(ref value3, "DocumentSnapshot.Recovery.StoryInlineShapes");
							ComObjectRelease.Release(ref value2, "DocumentSnapshot.Recovery.StoryFields");
							ComObjectRelease.Release(ref value, "DocumentSnapshot.Recovery.Story");
						}
						value = range;
						continue;
					}
					throw new InvalidOperationException("文档故事区链异常，无法建立恢复快照。");
				}
			}
			finally
			{
				ComObjectRelease.Release(ref value, "DocumentSnapshot.Recovery.StoryTail");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void AppendBookmarkFacts(Document document, StringBuilder builder)
	{
		Bookmarks value = null;
		List<string> list = new List<string>();
		try
		{
			value = document.Bookmarks;
			int num = value?.Count ?? 0;
			for (int i = 1; i <= num; i++)
			{
				Bookmark value2 = null;
				Microsoft.Office.Interop.Word.Range value3 = null;
				try
				{
					Bookmarks bookmarks = value;
					object Index = i;
					value2 = bookmarks.get_Item(ref Index);
					value3 = value2.Range;
					list.Add((value2.Name ?? string.Empty) + ":" + value3.Start + ":" + value3.End);
				}
				finally
				{
					ComObjectRelease.Release(ref value3, "DocumentSnapshot.Recovery.BookmarkRange");
					ComObjectRelease.Release(ref value2, "DocumentSnapshot.Recovery.Bookmark");
				}
			}
		}
		finally
		{
			ComObjectRelease.Release(ref value, "DocumentSnapshot.Recovery.Bookmarks");
		}
		list.Sort(StringComparer.Ordinal);
		builder.Append("|bookmark-facts=").Append(string.Join(";", list));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void AppendCustomPropertyFacts(Document document, DocumentSnapshot snapshot, StringBuilder builder)
	{
		object value = null;
		List<string> list = new List<string>();
		try
		{
			dynamic customDocumentProperties = document.CustomDocumentProperties;
			value = customDocumentProperties;
			int num = customDocumentProperties.Count;
			for (int i = 1; i <= num; i++)
			{
				object value2 = null;
				try
				{
					dynamic val = customDocumentProperties.Item(i);
					value2 = val;
					string text = Convert.ToString(val.Name, CultureInfo.InvariantCulture);
					object obj = val.Value;
					int type = Convert.ToInt32(val.Type, CultureInfo.InvariantCulture);
					bool flag = false;
					string linkSource = null;
					try
					{
						flag = Convert.ToBoolean(val.LinkToContent, CultureInfo.InvariantCulture);
					}
					catch
					{
					}
					if (flag)
					{
						try
						{
							linkSource = Convert.ToString(val.LinkSource, CultureInfo.InvariantCulture);
						}
						catch
						{
						}
					}
					snapshot.CustomDocumentProperties.Add(new CustomDocumentPropertySnapshot
					{
						Name = text,
						Type = type,
						Value = obj,
						LinkToContent = flag,
						LinkSource = linkSource
					});
					list.Add(text + ":" + type + ":" + flag + "=" + Hash(Convert.ToString(obj, CultureInfo.InvariantCulture)));
				}
				finally
				{
					ComObjectRelease.Release(value2, "DocumentSnapshot.Recovery.CustomProperty");
				}
			}
		}
		finally
		{
			ComObjectRelease.Release(value, "DocumentSnapshot.Recovery.CustomProperties");
		}
		list.Sort(StringComparer.Ordinal);
		builder.Append("|custom-properties=").Append(string.Join(";", list));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void RestoreCustomDocumentProperties(DocumentSnapshot expected, Document document)
	{
		if (expected != null)
		{
			if (document == null)
			{
				throw new ArgumentNullException("document");
			}
			DocumentSnapshot documentSnapshot = new DocumentSnapshot();
			StringBuilder builder = new StringBuilder();
			AppendCustomPropertyFacts(document, documentSnapshot, builder);
			if (CustomPropertiesEqual(expected.CustomDocumentProperties, documentSnapshot.CustomDocumentProperties))
			{
				document.Saved = expected.WasSaved;
				return;
			}
			object value = null;
			try
			{
				dynamic customDocumentProperties = document.CustomDocumentProperties;
				value = customDocumentProperties;
				for (int num = customDocumentProperties.Count; num >= 1; num--)
				{
					object value2 = null;
					try
					{
						dynamic val = customDocumentProperties.Item(num);
						value2 = val;
						val.Delete();
					}
					finally
					{
						ComObjectRelease.Release(value2, "DocumentSnapshot.Restore.CustomPropertyDelete");
					}
				}
				foreach (CustomDocumentPropertySnapshot customDocumentProperty in expected.CustomDocumentProperties)
				{
					if (customDocumentProperty != null && !string.IsNullOrWhiteSpace(customDocumentProperty.Name))
					{
						object obj = ((customDocumentProperty.LinkToContent && !string.IsNullOrWhiteSpace(customDocumentProperty.LinkSource)) ? customDocumentProperty.LinkSource : Type.Missing);
						customDocumentProperties.Add(customDocumentProperty.Name, customDocumentProperty.LinkToContent, customDocumentProperty.Type, customDocumentProperty.Value, obj);
					}
				}
				document.Saved = expected.WasSaved;
				return;
			}
			finally
			{
				ComObjectRelease.Release(value, "DocumentSnapshot.Restore.CustomProperties");
			}
		}
		throw new ArgumentNullException("expected");
	}

	private static bool CustomPropertiesEqual(IList<CustomDocumentPropertySnapshot> expected, IList<CustomDocumentPropertySnapshot> actual)
	{
		if (expected == null || actual == null || expected.Count != actual.Count)
		{
			return false;
		}
		List<string> list = new List<string>(expected.Count);
		List<string> list2 = new List<string>(actual.Count);
		foreach (CustomDocumentPropertySnapshot item in expected)
		{
			list.Add(CustomPropertyFact(item));
		}
		foreach (CustomDocumentPropertySnapshot item2 in actual)
		{
			list2.Add(CustomPropertyFact(item2));
		}
		list.Sort(StringComparer.Ordinal);
		list2.Sort(StringComparer.Ordinal);
		for (int i = 0; i < list.Count; i++)
		{
			if (!string.Equals(list[i], list2[i], StringComparison.Ordinal))
			{
				return false;
			}
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string CustomPropertyFact(CustomDocumentPropertySnapshot item)
	{
		if (item == null)
		{
			return "<null>";
		}
		return (item.Name ?? string.Empty) + ":" + item.Type + ":" + item.LinkToContent + ":" + (item.LinkSource ?? string.Empty) + "=" + Hash(Convert.ToString(item.Value, CultureInfo.InvariantCulture));
	}

	private static string ResolveParagraphStructuralStyleName(object style)
	{
		if (!(style is Style style2))
		{
			return Convert.ToString(style, CultureInfo.InvariantCulture);
		}
		return style2.NameLocal;
	}

	private static string BuildFormatFacts(object style, ParagraphFormat paragraphFormat, Font font)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append(Convert.ToString(style, CultureInfo.InvariantCulture));
		if (paragraphFormat != null)
		{
			stringBuilder.Append('|').Append((int)paragraphFormat.Alignment).Append('|')
				.Append(paragraphFormat.LeftIndent.ToString(CultureInfo.InvariantCulture))
				.Append('|')
				.Append(paragraphFormat.RightIndent.ToString(CultureInfo.InvariantCulture))
				.Append('|')
				.Append(paragraphFormat.FirstLineIndent.ToString(CultureInfo.InvariantCulture))
				.Append('|')
				.Append(paragraphFormat.SpaceBefore.ToString(CultureInfo.InvariantCulture))
				.Append('|')
				.Append(paragraphFormat.SpaceAfter.ToString(CultureInfo.InvariantCulture))
				.Append('|')
				.Append(paragraphFormat.LineSpacing.ToString(CultureInfo.InvariantCulture));
		}
		if (font != null)
		{
			stringBuilder.Append('|').Append(font.Name).Append('|')
				.Append(font.Size.ToString(CultureInfo.InvariantCulture))
				.Append('|')
				.Append(font.Bold)
				.Append('|')
				.Append(font.Italic);
		}
		return stringBuilder.ToString();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string Hash(string value)
	{
		using SHA256 sHA = SHA256.Create();
		byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
		byte[] array = sHA.ComputeHash(bytes);
		StringBuilder stringBuilder = new StringBuilder(array.Length * 2);
		byte[] array2 = array;
		foreach (byte b in array2)
		{
			stringBuilder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
		}
		return stringBuilder.ToString();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string GetDocumentIdentity(Document document)
	{
		try
		{
			return string.IsNullOrWhiteSpace(document.FullName) ? document.Name : document.FullName;
		}
		catch
		{
			return "unavailable";
		}
	}

	private static bool ReadSavedState(Document document)
	{
		try
		{
			return document.Saved;
		}
		catch
		{
			return false;
		}
	}
}
