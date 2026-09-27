using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Interop;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.Snapshots;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Cleanup;

public static class TextCleanupService
{
	private sealed class ExactTextCandidate
	{
		public int Start { get; private set; }

		public int End { get; private set; }

		public ExactTextCandidate(int start, int end)
		{
			Start = start;
			End = end;
		}
	}

	private sealed class PunctuationCleanupMetrics
	{
		public int ConvertedCount { get; set; }

		public int PreservedStraightQuoteCount { get; set; }

		public int ProtectedTokenCount { get; set; }

		public int ConservativeSkipCount { get; set; }

		public int ProtectedParagraphCount { get; set; }

		public void Add(ChinesePunctuationNormalizationResult result)
		{
			if (result != null)
			{
				ConvertedCount += result.ConvertedCount;
				PreservedStraightQuoteCount += result.PreservedStraightQuoteCount;
				ProtectedTokenCount += result.ProtectedTokenCount;
				ConservativeSkipCount += result.ConservativeSkipCount;
			}
		}
	}

	private static readonly string[] ConfiguredSpaceFindTexts = new string[20]
	{
		" ", "\u00a0", "\u1680", "\u180e", "\u2000", "\u2001", "\u2002", "\u2003", "\u2004", "\u2005",
		"\u2006", "\u2007", "\u2008", "\u2009", "\u200a", "\u200b", "\u202f", "\u205f", "\u3000", "\ufeff"
	};

	private static readonly HashSet<char> ConfiguredSpaceCharacters = new HashSet<char>(string.Concat(ConfiguredSpaceFindTexts).ToCharArray());

	private static readonly string FullWidthChars = "０１２３４５６７８９ａｂｃｄｅｆｇｈｉｊｋｌｍｎｏｐｑｒｓｔｕｖｗｘｙｚＡＢＣＤＥＦＧＨＩＪＫＬＭＮＯＰＱＲＳＴＵＶＷＸＹＺ";

	private static readonly string HalfWidthChars = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool TryReadWholeTextForStagePrecheck(Document document, out string text)
	{
		text = null;
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			value = document.Content;
			text = value.Text ?? string.Empty;
			return true;
		}
		catch (Exception ex)
		{
			LogService.Warn("TextCleanupService.stagePrecheck, exception=" + ex.GetType().Name);
			return false;
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "TextCleanupService.stagePrecheck");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool TryReadRangeTextForStagePrecheck(Microsoft.Office.Interop.Word.Range range, out string text)
	{
		text = null;
		try
		{
			text = range.Text ?? string.Empty;
			return true;
		}
		catch (Exception ex)
		{
			LogService.Warn("TextCleanupService.stagePrecheckRange, exception=" + ex.GetType().Name);
			return false;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void RemoveConfiguredSpaces(Document document)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		RemoveConfiguredSpacesSkipTables(document, hasTables: false, hasImages: false);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void RemoveConfiguredSpaces(Microsoft.Office.Interop.Word.Range range)
	{
		if (range == null)
		{
			throw new ArgumentNullException("range");
		}
		RemoveConfiguredSpacesInParagraphs(range, skipTables: false, skipImages: false);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void RemoveConfiguredSpacesSkipTables(Document document, bool hasTables, bool hasImages)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			value = document.Content;
			string text;
			if (!hasTables && !hasImages)
			{
				RemoveConfiguredSpacesInParagraphs(value, hasTables, hasImages);
			}
			else if (!TryReadRangeTextForStagePrecheck(value, out text) || CleanupStagePrecheck.HasAnyConfiguredSpace(text, ConfiguredSpaceCharacters))
			{
				RemoveConfiguredSpacesInParagraphs(value, hasTables, hasImages);
			}
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "TextCleanupService.content");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void RemoveConfiguredSpacesInParagraphs(Microsoft.Office.Interop.Word.Range scope, bool skipTables, bool skipImages)
	{
		if (scope != null)
		{
			if (!skipTables && !skipImages)
			{
				string original = scope.Text ?? string.Empty;
				DeleteCharactersWithoutProtection(scope, original, (char ch) => ConfiguredSpaceCharacters.Contains(ch), "删除空格");
				return;
			}
			int start = scope.Start;
			int end = scope.End;
			Paragraphs value = null;
			try
			{
				value = scope.Paragraphs;
				for (int num = value.Count; num >= 1; num--)
				{
					Paragraph value2 = null;
					Microsoft.Office.Interop.Word.Range value3 = null;
					Microsoft.Office.Interop.Word.Range value4 = null;
					try
					{
						value2 = value[num];
						if ((!skipTables || !WordRangeInspector.IsInTable(value2)) && (!skipImages || !WordRangeInspector.HasInlineOrAnchoredShape(value2)))
						{
							value3 = value2.Range;
							int num2 = Math.Max(start, value3.Start);
							int num3 = Math.Min(end, value3.End);
							if (num2 < num3)
							{
								value4 = value3.Duplicate;
								value4.SetRange(num2, num3);
								string text = value4.Text ?? string.Empty;
								if (ContainsConfiguredSpace(text))
								{
									if (!SafeTextMutationService.HasProtectedContent(value4))
									{
										for (int num4 = text.Length - 1; num4 >= 0; num4--)
										{
											if (ConfiguredSpaceCharacters.Contains(text[num4]))
											{
												int num5 = num4 + 1;
												while (num4 >= 0 && ConfiguredSpaceCharacters.Contains(text[num4]))
												{
													num4--;
												}
												int num6 = num4 + 1;
												SafeTextMutationService.TryReplace(value4, num6, num5 - num6, string.Empty, "删除空格");
											}
										}
									}
									else
									{
										LogService.Warn("TextCleanupService.RemoveConfiguredSpaces skipped protected paragraph start=" + value3.Start);
									}
								}
							}
						}
					}
					finally
					{
						if (value4 != null)
						{
							ComObjectRelease.Release(ref value4, "TextCleanupService.targetRange");
						}
						if (value3 != null)
						{
							ComObjectRelease.Release(ref value3, "TextCleanupService.paragraphRange");
						}
						if (value2 != null)
						{
							ComObjectRelease.Release(ref value2, "TextCleanupService.paragraph");
						}
					}
				}
				return;
			}
			finally
			{
				if (value != null)
				{
					ComObjectRelease.Release(ref value, "TextCleanupService.paragraphs");
				}
			}
		}
		throw new ArgumentNullException("scope");
	}

	private static bool ContainsConfiguredSpace(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return false;
		}
		for (int i = 0; i < text.Length; i++)
		{
			if (ConfiguredSpaceCharacters.Contains(text[i]))
			{
				return true;
			}
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void FindReplace(Document document, string findText, string replaceText, bool useWildcards)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		FindReplaceSkipTables(document, findText, replaceText, useWildcards, hasTables: false, hasImages: false);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void FindReplace(Microsoft.Office.Interop.Word.Range range, string findText, string replaceText, bool useWildcards)
	{
		if (range == null)
		{
			throw new ArgumentNullException("range");
		}
		FindReplaceInParagraphs(range, findText, replaceText, useWildcards, skipTables: false, skipImages: false);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void FindReplaceSkipTables(Document document, string findText, string replaceText, bool useWildcards, bool hasTables, bool hasImages)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			value = document.Content;
			FindReplaceInParagraphs(value, findText, replaceText, useWildcards, hasTables, hasImages);
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "TextCleanupService.content");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int NormalizeManualLineBreaks(Document document, bool hasTables, bool hasImages)
	{
		if (document != null)
		{
			if (!hasTables && !hasImages)
			{
				Microsoft.Office.Interop.Word.Range value = null;
				try
				{
					value = document.Content;
					return NormalizeManualLineBreaks(value);
				}
				finally
				{
					if (value != null)
					{
						ComObjectRelease.Release(ref value, "TextCleanupService.content");
					}
				}
			}
			if (TryReadWholeTextForStagePrecheck(document, out var text) && text.IndexOf('\v') < 0)
			{
				return 0;
			}
			int num = 0;
			Paragraphs value2 = null;
			try
			{
				value2 = document.Paragraphs;
				for (int num2 = value2.Count; num2 >= 1; num2--)
				{
					Paragraph value3 = null;
					Microsoft.Office.Interop.Word.Range value4 = null;
					try
					{
						value3 = value2[num2];
						if ((!hasTables || !WordRangeInspector.IsInTable(value3)) && (!hasImages || !WordRangeInspector.HasInlineOrAnchoredShape(value3)))
						{
							value4 = value3.Range;
							num += NormalizeManualLineBreaks(value4);
						}
					}
					finally
					{
						if (value4 != null)
						{
							ComObjectRelease.Release(ref value4, "TextCleanupService.range");
						}
						if (value3 != null)
						{
							ComObjectRelease.Release(ref value3, "TextCleanupService.paragraph");
						}
					}
				}
				return num;
			}
			finally
			{
				if (value2 != null)
				{
					ComObjectRelease.Release(ref value2, "TextCleanupService.paragraphs");
				}
			}
		}
		throw new ArgumentNullException("document");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int NormalizeManualLineBreaks(Microsoft.Office.Interop.Word.Range range)
	{
		if (range != null)
		{
			string text = range.Text ?? string.Empty;
			if (text.IndexOf('\v') >= 0)
			{
				string corrected = text.Replace('\v', '\r');
				return SafeTextMutationService.ApplyEqualLengthCharacterChangesWithoutProtection(range, text, corrected, "手动换行转段落标记");
			}
			return 0;
		}
		throw new ArgumentNullException("range");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void CleanSymbolsAndPunctuation(Document document, FormatConfig config)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		if (config == null)
		{
			throw new ArgumentNullException("config");
		}
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			value = document.Content;
			LogPunctuationCleanupMetrics(CleanSymbolsAndPunctuationByStructure(value, skipTables: false, skipImages: false, config), "document");
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "TextCleanupService.content");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void CleanSymbolsAndPunctuation(Microsoft.Office.Interop.Word.Range range, FormatConfig config)
	{
		if (range == null)
		{
			throw new ArgumentNullException("range");
		}
		if (config == null)
		{
			throw new ArgumentNullException("config");
		}
		LogPunctuationCleanupMetrics(CleanSymbolsAndPunctuationInParagraphs(range, skipTables: false, skipImages: false, config), "range");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void CleanSymbolsAndPunctuationSkipTables(Document document, bool hasTables, bool hasImages, FormatConfig config)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		if (config == null)
		{
			throw new ArgumentNullException("config");
		}
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			value = document.Content;
			string text;
			if (!hasTables && !hasImages)
			{
				LogPunctuationCleanupMetrics(CleanSymbolsAndPunctuationByStructure(value, skipTables: false, skipImages: false, config), "document");
			}
			else if (!TryReadRangeTextForStagePrecheck(value, out text) || CleanupStagePrecheck.HasAnySymbolCleanupWork(text, config.DeleteAiSymbols))
			{
				LogPunctuationCleanupMetrics(CleanSymbolsAndPunctuationInParagraphs(value, hasTables, hasImages, config), "document-complex");
			}
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "TextCleanupService.content");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void NormalizeParagraphHeadNoise(Document document, bool hasTables, bool hasImages)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		ParagraphTextSnapshot paragraphTextSnapshot = ParagraphTextSnapshot.Capture(document);
		bool isReliable = paragraphTextSnapshot.IsReliable;
		if (!isReliable && !string.IsNullOrWhiteSpace(paragraphTextSnapshot.FailureReason))
		{
			LogService.Warn("TextCleanupService.NormalizeParagraphHeadNoise text snapshot unavailable, fallback: " + paragraphTextSnapshot.FailureReason);
		}
		if (isReliable)
		{
			for (int num = paragraphTextSnapshot.Paragraphs.Count; num >= 1; num--)
			{
				WordParagraphTextMap.Entry entry = paragraphTextSnapshot.Paragraphs[num - 1];
				int paragraphHeadNoiseLength = GetParagraphHeadNoiseLength(TrimParagraphMark(entry.Text ?? string.Empty));
				if (paragraphHeadNoiseLength > 0)
				{
					Microsoft.Office.Interop.Word.Range value = null;
					try
					{
						object Start = entry.RangeStart;
						object End = entry.RangeEnd;
						value = document.Range(ref Start, ref End);
						if ((!hasTables || !WordRangeInspector.IsInTable(value)) && (!hasImages || !WordRangeInspector.HasInlineOrAnchoredShape(value)))
						{
							if (SafeTextMutationService.HasProtectedContent(value))
							{
								LogService.Warn("TextCleanupService.NormalizeParagraphHeadNoise skipped protected paragraph start=" + value.Start);
							}
							else
							{
								SafeTextMutationService.TryReplace(value, 0, paragraphHeadNoiseLength, string.Empty, "清理段首噪声");
							}
						}
					}
					finally
					{
						if (value != null)
						{
							ComObjectRelease.Release(ref value, "TextCleanupService.range");
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
					int paragraphHeadNoiseLength2 = GetParagraphHeadNoiseLength(TrimParagraphMark(value3.Text ?? string.Empty));
					if (paragraphHeadNoiseLength2 > 0)
					{
						if (!SafeTextMutationService.HasProtectedContent(value3))
						{
							SafeTextMutationService.TryReplace(value3, 0, paragraphHeadNoiseLength2, string.Empty, "清理段首噪声");
						}
						else
						{
							LogService.Warn("TextCleanupService.NormalizeParagraphHeadNoise skipped protected paragraph start=" + value3.Start);
						}
					}
				}
			}
			finally
			{
				if (value3 != null)
				{
					ComObjectRelease.Release(ref value3, "TextCleanupService.range");
				}
				if (value2 != null)
				{
					ComObjectRelease.Release(ref value2, "TextCleanupService.paragraph");
				}
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ConvertFullWidthLettersAndDigits(Document document, bool hasTables, bool hasImages)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		if (!hasTables && !hasImages)
		{
			Microsoft.Office.Interop.Word.Range value = null;
			try
			{
				value = document.Content;
				ConvertFullWidthLettersAndDigitsInRange(value);
				return;
			}
			finally
			{
				if (value != null)
				{
					ComObjectRelease.Release(ref value, "TextCleanupService.content");
				}
			}
		}
		if (TryReadWholeTextForStagePrecheck(document, out var text) && !CleanupStagePrecheck.ContainsAnyChar(text, FullWidthChars))
		{
			return;
		}
		for (int num = document.Paragraphs.Count; num >= 1; num--)
		{
			Paragraph value2 = null;
			Microsoft.Office.Interop.Word.Range value3 = null;
			try
			{
				value2 = document.Paragraphs[num];
				if ((!hasTables || !WordRangeInspector.IsInTable(value2)) && (!hasImages || !WordRangeInspector.HasInlineOrAnchoredShape(value2)))
				{
					value3 = value2.Range;
					ConvertFullWidthLettersAndDigitsInRange(value3);
				}
			}
			finally
			{
				if (value3 != null)
				{
					ComObjectRelease.Release(ref value3, "TextCleanupService.range");
				}
				if (value2 != null)
				{
					ComObjectRelease.Release(ref value2, "TextCleanupService.paragraph");
				}
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int RemoveTabs(Document document, bool hasTables, bool hasImages)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		if (!hasTables && !hasImages)
		{
			Microsoft.Office.Interop.Word.Range value = null;
			try
			{
				value = document.Content;
				string original = value.Text ?? string.Empty;
				return DeleteCharactersWithoutProtection(value, original, (char ch) => ch == '\t', "删除制表符");
			}
			finally
			{
				if (value != null)
				{
					ComObjectRelease.Release(ref value, "TextCleanupService.content");
				}
			}
		}
		if (TryReadWholeTextForStagePrecheck(document, out var text) && text.IndexOf('\t') < 0)
		{
			return 0;
		}
		int num = 0;
		Paragraphs value2 = null;
		try
		{
			value2 = document.Paragraphs;
			for (int num2 = value2.Count; num2 >= 1; num2--)
			{
				Paragraph value3 = null;
				Microsoft.Office.Interop.Word.Range value4 = null;
				try
				{
					value3 = value2[num2];
					if ((!hasTables || !WordRangeInspector.IsInTable(value3)) && (!hasImages || !WordRangeInspector.HasInlineOrAnchoredShape(value3)))
					{
						value4 = value3.Range;
						if (!SafeTextMutationService.HasProtectedContent(value4))
						{
							string original2 = value4.Text ?? string.Empty;
							num += DeleteCharactersWithoutProtection(value4, original2, (char ch) => ch == '\t', "删除制表符");
						}
						else
						{
							LogService.Warn("TextCleanupService.RemoveTabs skipped protected paragraph start=" + value4.Start);
						}
					}
				}
				finally
				{
					if (value4 != null)
					{
						ComObjectRelease.Release(ref value4, "TextCleanupService.range");
					}
					if (value3 != null)
					{
						ComObjectRelease.Release(ref value3, "TextCleanupService.paragraph");
					}
				}
			}
			return num;
		}
		finally
		{
			if (value2 != null)
			{
				ComObjectRelease.Release(ref value2, "TextCleanupService.paragraphs");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int NormalizeDecimalPoints(Document document, bool hasTables, bool hasImages)
	{
		if (document != null)
		{
			if (!hasTables && !hasImages)
			{
				Microsoft.Office.Interop.Word.Range value = null;
				try
				{
					value = document.Content;
					return NormalizeDecimalPoints(value, inspectProtection: false);
				}
				finally
				{
					if (value != null)
					{
						ComObjectRelease.Release(ref value, "TextCleanupService.content");
					}
				}
			}
			if (!TryReadWholeTextForStagePrecheck(document, out var text) || CleanupStagePrecheck.HasDigitFullStopDigit(text))
			{
				int num = 0;
				Paragraphs value2 = null;
				try
				{
					value2 = document.Paragraphs;
					for (int num2 = value2.Count; num2 >= 1; num2--)
					{
						Paragraph value3 = null;
						Microsoft.Office.Interop.Word.Range value4 = null;
						try
						{
							value3 = value2[num2];
							if ((!hasTables || !WordRangeInspector.IsInTable(value3)) && (!hasImages || !WordRangeInspector.HasInlineOrAnchoredShape(value3)))
							{
								value4 = value3.Range;
								if (!SafeTextMutationService.HasProtectedContent(value4))
								{
									num += NormalizeDecimalPoints(value4, inspectProtection: false);
								}
							}
						}
						finally
						{
							if (value4 != null)
							{
								ComObjectRelease.Release(ref value4, "TextCleanupService.range");
							}
							if (value3 != null)
							{
								ComObjectRelease.Release(ref value3, "TextCleanupService.paragraph");
							}
						}
					}
					return num;
				}
				finally
				{
					if (value2 != null)
					{
						ComObjectRelease.Release(ref value2, "TextCleanupService.paragraphs");
					}
				}
			}
			return 0;
		}
		throw new ArgumentNullException("document");
	}

	public static int NormalizeDecimalPoints(Microsoft.Office.Interop.Word.Range range)
	{
		return NormalizeDecimalPoints(range, inspectProtection: true);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int NormalizeDecimalPoints(Microsoft.Office.Interop.Word.Range range, bool inspectProtection)
	{
		if (range == null)
		{
			throw new ArgumentNullException("range");
		}
		string text = range.Text ?? string.Empty;
		if (text.IndexOf('。') >= 0)
		{
			char[] array = text.ToCharArray();
			for (int i = 1; i < array.Length - 1; i++)
			{
				if (array[i] == '。' && char.IsDigit(array[i - 1]) && char.IsDigit(array[i + 1]))
				{
					array[i] = '.';
				}
			}
			string corrected = new string(array);
			if (!inspectProtection)
			{
				return SafeTextMutationService.ApplyEqualLengthCharacterChangesWithoutProtection(range, text, corrected, "小数点规范化");
			}
			return SafeTextMutationService.ApplyEqualLengthCharacterChanges(range, text, corrected, "小数点规范化");
		}
		return 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ReplaceDocumentNumberBrackets(Document document, bool hasTables, bool hasImages)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			value = document.Content;
			ReplaceDocumentNumberBracketsCore(value, hasTables, hasImages, isWholeDocument: true);
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "TextCleanupService.content");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ReplaceDocumentNumberBrackets(Microsoft.Office.Interop.Word.Range range, bool hasTables, bool hasImages)
	{
		if (range == null)
		{
			throw new ArgumentNullException("range");
		}
		ReplaceDocumentNumberBracketsCore(range, hasTables, hasImages, isWholeDocument: false);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ReplaceDocumentNumberBracketsCore(Microsoft.Office.Interop.Word.Range range, bool hasTables, bool hasImages, bool isWholeDocument)
	{
		string text = range.Text ?? string.Empty;
		if (text.IndexOf('{') < 0 && text.IndexOf('【') < 0 && text.IndexOf('[') < 0 && text.IndexOf('（') < 0 && text.IndexOf('(') < 0)
		{
			return;
		}
		Regex regex = new Regex("([{\\[【（(])\\s*(\\d{4})\\s*([}\\]】）)])\\s*(\\d{1,4})\\s*号");
		ParagraphTextSnapshot paragraphTextSnapshot = (isWholeDocument ? ParagraphTextSnapshot.Capture(range.Document) : ParagraphTextSnapshot.Capture(range.Document, range));
		bool isReliable = paragraphTextSnapshot.IsReliable;
		if (!isReliable && !string.IsNullOrWhiteSpace(paragraphTextSnapshot.FailureReason))
		{
			LogService.Warn("TextCleanupService.ReplaceDocumentNumberBrackets text snapshot unavailable, fallback: " + paragraphTextSnapshot.FailureReason);
		}
		if (isReliable)
		{
			for (int num = paragraphTextSnapshot.Paragraphs.Count; num >= 1; num--)
			{
				WordParagraphTextMap.Entry entry = paragraphTextSnapshot.Paragraphs[num - 1];
				string input = TrimParagraphMark(entry.Text ?? string.Empty);
				MatchCollection matchCollection = regex.Matches(input);
				if (matchCollection.Count != 0)
				{
					Microsoft.Office.Interop.Word.Range value = null;
					try
					{
						Document document = range.Document;
						object Start = entry.RangeStart;
						object End = entry.RangeEnd;
						value = document.Range(ref Start, ref End);
						if ((!hasImages || !WordRangeInspector.HasInlineOrAnchoredShape(value)) && (!hasTables || !WordRangeInspector.IsInTable(value)))
						{
							if (!SafeTextMutationService.HasProtectedContent(value))
							{
								for (int num2 = matchCollection.Count - 1; num2 >= 0; num2--)
								{
									Match match = matchCollection[num2];
									string replacement = "〔" + match.Groups[2].Value + "〕" + match.Groups[4].Value + "号";
									SafeTextMutationService.TryReplace(value, match.Index, match.Length, replacement, "发文字号括号修正");
								}
							}
							else
							{
								LogService.Warn("TextCleanupService.ReplaceDocumentNumberBrackets skipped protected paragraph start=" + value.Start);
							}
						}
					}
					finally
					{
						if (value != null)
						{
							ComObjectRelease.Release(ref value, "TextCleanupService.paragraphRange");
						}
					}
				}
			}
			return;
		}
		for (int num3 = range.Paragraphs.Count; num3 >= 1; num3--)
		{
			Paragraph value2 = null;
			Microsoft.Office.Interop.Word.Range value3 = null;
			try
			{
				value2 = range.Paragraphs[num3];
				if ((!hasImages || !WordRangeInspector.HasInlineOrAnchoredShape(value2)) && (!hasTables || !WordRangeInspector.IsInTable(value2)))
				{
					value3 = value2.Range;
					string input2 = TrimParagraphMark((isReliable ? paragraphTextSnapshot.TextAt(num3) : value3.Text) ?? string.Empty);
					MatchCollection matchCollection2 = regex.Matches(input2);
					if (matchCollection2.Count != 0)
					{
						if (SafeTextMutationService.HasProtectedContent(value3))
						{
							LogService.Warn("TextCleanupService.ReplaceDocumentNumberBrackets skipped protected paragraph start=" + value3.Start);
						}
						else
						{
							for (int num4 = matchCollection2.Count - 1; num4 >= 0; num4--)
							{
								Match match2 = matchCollection2[num4];
								string replacement2 = "〔" + match2.Groups[2].Value + "〕" + match2.Groups[4].Value + "号";
								SafeTextMutationService.TryReplace(value3, match2.Index, match2.Length, replacement2, "发文字号括号修正");
							}
						}
					}
				}
			}
			finally
			{
				if (value3 != null)
				{
					ComObjectRelease.Release(ref value3, "TextCleanupService.paragraphRange");
				}
				if (value2 != null)
				{
					ComObjectRelease.Release(ref value2, "TextCleanupService.paragraph");
				}
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void RestoreInternalPlaceholders(Microsoft.Office.Interop.Word.Range range)
	{
		if (range == null)
		{
			throw new ArgumentNullException("range");
		}
		FindReplace(range, "DOTPLACEHOLDER????????", ".", useWildcards: true);
		FindReplace(range, "URLDOTPLACEHOLDER????????", ".", useWildcards: true);
	}

	internal static bool IsEmptyParagraphText(string text)
	{
		text = text ?? string.Empty;
		return IsEmptyParagraphSlice(text, 0, text.Length);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void FindReplaceInParagraphs(Microsoft.Office.Interop.Word.Range scope, string findText, string replaceText, bool useWildcards, bool skipTables, bool skipImages)
	{
		if (scope == null)
		{
			throw new ArgumentNullException("scope");
		}
		if (skipTables || skipImages)
		{
			int start = scope.Start;
			int end = scope.End;
			Paragraphs value = null;
			try
			{
				value = scope.Paragraphs;
				for (int num = value.Count; num >= 1; num--)
				{
					Paragraph value2 = null;
					Microsoft.Office.Interop.Word.Range value3 = null;
					Microsoft.Office.Interop.Word.Range value4 = null;
					try
					{
						value2 = value[num];
						if ((!skipTables || !WordRangeInspector.IsInTable(value2)) && (!skipImages || !WordRangeInspector.HasInlineOrAnchoredShape(value2)))
						{
							value3 = value2.Range;
							if (SafeTextMutationService.HasProtectedContent(value3))
							{
								LogService.Warn("TextCleanupService.FindReplace skipped protected paragraph start=" + value3.Start);
							}
							else
							{
								int num2 = Math.Max(start, value3.Start);
								int num3 = Math.Min(end, value3.End);
								if (num2 < num3)
								{
									value4 = value3.Duplicate;
									value4.SetRange(num2, num3);
									ExecuteFindReplace(value4, findText, replaceText, useWildcards);
								}
							}
						}
					}
					catch (Exception ex)
					{
						LogService.Warn("TextCleanupService.FindReplaceInParagraphs", ex);
					}
					finally
					{
						if (value4 != null)
						{
							ComObjectRelease.Release(ref value4, "TextCleanupService.targetRange");
						}
						if (value3 != null)
						{
							ComObjectRelease.Release(ref value3, "TextCleanupService.paragraphRange");
						}
						if (value2 != null)
						{
							ComObjectRelease.Release(ref value2, "TextCleanupService.paragraph");
						}
					}
				}
				return;
			}
			finally
			{
				if (value != null)
				{
					ComObjectRelease.Release(ref value, "TextCleanupService.paragraphs");
				}
			}
		}
		ExecuteFindReplace(scope, findText, replaceText, useWildcards);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static PunctuationCleanupMetrics CleanSymbolsAndPunctuationInParagraphs(Microsoft.Office.Interop.Word.Range scope, bool skipTables, bool skipImages, FormatConfig config)
	{
		if (scope != null)
		{
			if (config != null)
			{
				int start = scope.Start;
				int end = scope.End;
				PunctuationCleanupMetrics punctuationCleanupMetrics = new PunctuationCleanupMetrics();
				Paragraphs value = null;
				try
				{
					value = scope.Paragraphs;
					for (int num = value.Count; num >= 1; num--)
					{
						Paragraph value2 = null;
						Microsoft.Office.Interop.Word.Range value3 = null;
						Microsoft.Office.Interop.Word.Range value4 = null;
						try
						{
							value2 = value[num];
							if ((!skipTables || !WordRangeInspector.IsInTable(value2)) && (!skipImages || !WordRangeInspector.HasInlineOrAnchoredShape(value2)))
							{
								value3 = value2.Range;
								int num2 = Math.Max(start, value3.Start);
								int num3 = Math.Min(end, value3.End);
								if (num2 < num3)
								{
									value4 = value3.Duplicate;
									value4.SetRange(num2, num3);
									if (SafeTextMutationService.HasProtectedContent(value4))
									{
										int num4 = (config.DeleteAiSymbols ? RemoveAiSymbolsWithExactProtection(value4) : 0);
										LogService.Warn("TextCleanupService.CleanSymbols skipped protected paragraph start=" + value3.Start + ", exactAiSymbolsRemoved=" + num4);
										punctuationCleanupMetrics.ProtectedParagraphCount++;
									}
									else
									{
										punctuationCleanupMetrics.Add(CleanSymbolsAndPunctuationPlainText(value4, config));
									}
								}
							}
						}
						catch (Exception ex)
						{
							LogService.Warn("TextCleanupService.CleanSymbolsAndPunctuationInParagraphs", ex);
						}
						finally
						{
							if (value4 != null)
							{
								ComObjectRelease.Release(ref value4, "TextCleanupService.targetRange");
							}
							if (value3 != null)
							{
								ComObjectRelease.Release(ref value3, "TextCleanupService.paragraphRange");
							}
							if (value2 != null)
							{
								ComObjectRelease.Release(ref value2, "TextCleanupService.paragraph");
							}
						}
					}
					return punctuationCleanupMetrics;
				}
				finally
				{
					if (value != null)
					{
						ComObjectRelease.Release(ref value, "TextCleanupService.paragraphs");
					}
				}
			}
			throw new ArgumentNullException("config");
		}
		throw new ArgumentNullException("scope");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static PunctuationCleanupMetrics CleanSymbolsAndPunctuationByStructure(Microsoft.Office.Interop.Word.Range range, bool skipTables, bool skipImages, FormatConfig config)
	{
		if (range == null)
		{
			throw new ArgumentNullException("range");
		}
		if (config == null)
		{
			throw new ArgumentNullException("config");
		}
		if (SafeTextMutationService.HasProtectedContent(range))
		{
			return CleanSymbolsAndPunctuationInParagraphs(range, skipTables, skipImages, config);
		}
		PunctuationCleanupMetrics punctuationCleanupMetrics = new PunctuationCleanupMetrics();
		punctuationCleanupMetrics.Add(CleanSymbolsAndPunctuationPlainText(range, config));
		return punctuationCleanupMetrics;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int RemoveAiSymbolsWithExactProtection(Microsoft.Office.Interop.Word.Range scope)
	{
		if (scope != null)
		{
			List<ExactTextCandidate> list = new List<ExactTextCandidate>();
			CollectExactFindCandidates(scope, "#", useWildcards: false, requireContextualNoBreakSpace: false, list);
			CollectExactFindCandidates(scope, "*", useWildcards: false, requireContextualNoBreakSpace: false, list);
			CollectExactFindCandidates(scope, "---", useWildcards: false, requireContextualNoBreakSpace: false, list);
			CollectExactFindCandidates(scope, "【【[0-9]{1,}】】", useWildcards: true, requireContextualNoBreakSpace: false, list);
			CollectExactFindCandidates(scope, "\\[\\[[0-9]{1,}\\]\\]", useWildcards: true, requireContextualNoBreakSpace: false, list);
			CollectExactFindCandidates(scope, "\u00a0", useWildcards: false, requireContextualNoBreakSpace: true, list);
			list.Sort(delegate(ExactTextCandidate left, ExactTextCandidate right)
			{
				int num7 = left.Start.CompareTo(right.Start);
				return (num7 == 0) ? left.End.CompareTo(right.End) : num7;
			});
			int num = 0;
			int num2 = -1;
			int num3 = -1;
			for (int num4 = list.Count - 1; num4 >= 0; num4--)
			{
				ExactTextCandidate exactTextCandidate = list[num4];
				if (exactTextCandidate.Start != num2 || exactTextCandidate.End != num3)
				{
					num2 = exactTextCandidate.Start;
					num3 = exactTextCandidate.End;
					int num5 = exactTextCandidate.Start - scope.Start;
					int num6 = exactTextCandidate.End - exactTextCandidate.Start;
					if (num5 >= 0 && num6 > 0 && SafeTextMutationService.TryReplaceExact(scope, num5, num6, string.Empty, "清理受保护段落中的 AI 符号"))
					{
						num += num6;
					}
				}
			}
			return num;
		}
		throw new ArgumentNullException("scope");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void CollectExactFindCandidates(Microsoft.Office.Interop.Word.Range scope, string findText, bool useWildcards, bool requireContextualNoBreakSpace, List<ExactTextCandidate> candidates)
	{
		if (scope == null)
		{
			throw new ArgumentNullException("scope");
		}
		if (candidates != null)
		{
			int start = scope.Start;
			int end = scope.End;
			Microsoft.Office.Interop.Word.Range value = null;
			Find value2 = null;
			try
			{
				value = scope.Duplicate;
				value2 = value.Find;
				value2.ClearFormatting();
				ResetFindFlags(value2);
				value2.Text = findText;
				value2.Forward = true;
				value2.Wrap = WdFindWrap.wdFindStop;
				value2.Format = false;
				value2.MatchWildcards = useWildcards;
				while (true)
				{
					Find find = value2;
					object FindText = Type.Missing;
					object MatchCase = Type.Missing;
					object MatchWholeWord = Type.Missing;
					object MatchWildcards = Type.Missing;
					object MatchSoundsLike = Type.Missing;
					object MatchAllWordForms = Type.Missing;
					object Forward = Type.Missing;
					object Wrap = Type.Missing;
					object Format = Type.Missing;
					object ReplaceWith = Type.Missing;
					object Replace = Type.Missing;
					object MatchKashida = Type.Missing;
					object MatchDiacritics = Type.Missing;
					object MatchAlefHamza = Type.Missing;
					object MatchControl = Type.Missing;
					if (find.Execute(ref FindText, ref MatchCase, ref MatchWholeWord, ref MatchWildcards, ref MatchSoundsLike, ref MatchAllWordForms, ref Forward, ref Wrap, ref Format, ref ReplaceWith, ref Replace, ref MatchKashida, ref MatchDiacritics, ref MatchAlefHamza, ref MatchControl))
					{
						int start2 = value.Start;
						int end2 = value.End;
						if (start2 >= start && end2 <= end && end2 > start2)
						{
							if (!requireContextualNoBreakSpace || IsContextualNoBreakSpaceAt(scope, start2, start, end))
							{
								candidates.Add(new ExactTextCandidate(start2, end2));
							}
							int num = Math.Max(end2, start2 + 1);
							if (num >= end)
							{
								break;
							}
							value.SetRange(num, end);
							continue;
						}
						break;
					}
					break;
				}
				return;
			}
			finally
			{
				if (value2 != null)
				{
					ComObjectRelease.Release(ref value2, "TextCleanupService.exactAiFind");
				}
				if (value != null)
				{
					ComObjectRelease.Release(ref value, "TextCleanupService.exactAiSearchRange");
				}
			}
		}
		throw new ArgumentNullException("candidates");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool IsContextualNoBreakSpaceAt(Microsoft.Office.Interop.Word.Range scope, int position, int scopeStart, int scopeEnd)
	{
		if (position > scopeStart && position + 1 < scopeEnd)
		{
			Microsoft.Office.Interop.Word.Range value = null;
			Microsoft.Office.Interop.Word.Range value2 = null;
			Microsoft.Office.Interop.Word.Range value3 = null;
			try
			{
				value = scope.Duplicate;
				value.SetRange(position - 1, position);
				value2 = scope.Duplicate;
				value2.SetRange(position, position + 1);
				value3 = scope.Duplicate;
				value3.SetRange(position + 1, position + 2);
				string text = value.Text ?? string.Empty;
				string text2 = value2.Text ?? string.Empty;
				string text3 = value3.Text ?? string.Empty;
				if (text.Length != 1 || text2 != "\u00a0" || text3.Length != 1)
				{
					return false;
				}
				return CleanupStagePrecheck.IsContextualAiNoBreakSpace(text + text2 + text3, 1);
			}
			finally
			{
				if (value3 != null)
				{
					ComObjectRelease.Release(ref value3, "TextCleanupService.exactAiRight");
				}
				if (value2 != null)
				{
					ComObjectRelease.Release(ref value2, "TextCleanupService.exactAiTarget");
				}
				if (value != null)
				{
					ComObjectRelease.Release(ref value, "TextCleanupService.exactAiLeft");
				}
			}
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ChinesePunctuationNormalizationResult CleanSymbolsAndPunctuationPlainText(Microsoft.Office.Interop.Word.Range range, FormatConfig config)
	{
		if (range != null)
		{
			if (config == null)
			{
				throw new ArgumentNullException("config");
			}
			string text = range.Text ?? string.Empty;
			bool[] deleteMask = BuildCleanupDeleteMask(text, config.DeleteAiSymbols);
			DeleteMarkedCharactersWithoutProtection(range, text, deleteMask, "清理符号和隐藏字符");
			string text2 = range.Text ?? string.Empty;
			ChinesePunctuationNormalizationResult chinesePunctuationNormalizationResult = ChineseContextPunctuationNormalizer.Normalize(text2);
			SafeTextMutationService.ApplyEqualLengthCharacterChangesWithoutProtection(range, text2, chinesePunctuationNormalizationResult.Text, "普通公文标点规范化");
			return chinesePunctuationNormalizationResult;
		}
		throw new ArgumentNullException("range");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool[] BuildCleanupDeleteMask(string text, bool deleteAiSymbols)
	{
		text = text ?? string.Empty;
		bool[] array = new bool[text.Length];
		for (int i = 0; i < text.Length; i++)
		{
			char c = text[i];
			if (c == '\u200b' || c == '\u200c' || c == '\u200d' || c == '\ufeff' || c == '\u00ad')
			{
				array[i] = true;
			}
			if (deleteAiSymbols && CleanupStagePrecheck.IsContextualAiNoBreakSpace(text, i))
			{
				array[i] = true;
			}
			if (deleteAiSymbols && (c == '#' || c == '*'))
			{
				array[i] = true;
			}
		}
		if (deleteAiSymbols && text.Length != 0)
		{
			MarkLiteral(text, "---", array);
			MarkRegex(text, new Regex("【【[0-9]+】】"), array);
			MarkRegex(text, new Regex("\\[\\[[0-9]+\\]\\]"), array);
			return array;
		}
		return array;
	}

	private static void MarkLiteral(string text, string value, bool[] mask)
	{
		int num = 0;
		while (num < text.Length)
		{
			int num2 = text.IndexOf(value, num, StringComparison.Ordinal);
			if (num2 >= 0)
			{
				for (int i = num2; i < num2 + value.Length; i++)
				{
					mask[i] = true;
				}
				num = num2 + value.Length;
				continue;
			}
			break;
		}
	}

	private static void MarkRegex(string text, Regex regex, bool[] mask)
	{
		foreach (Match item in regex.Matches(text))
		{
			for (int i = item.Index; i < item.Index + item.Length; i++)
			{
				mask[i] = true;
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int DeleteCharactersWithoutProtection(Microsoft.Office.Interop.Word.Range range, string original, Predicate<char> shouldDelete, string operationName)
	{
		if (range != null)
		{
			if (shouldDelete == null)
			{
				throw new ArgumentNullException("shouldDelete");
			}
			original = original ?? string.Empty;
			bool[] array = new bool[original.Length];
			for (int i = 0; i < original.Length; i++)
			{
				array[i] = shouldDelete(original[i]);
			}
			return DeleteMarkedCharactersWithoutProtection(range, original, array, operationName);
		}
		throw new ArgumentNullException("range");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int DeleteMarkedCharactersWithoutProtection(Microsoft.Office.Interop.Word.Range range, string original, bool[] deleteMask, string operationName)
	{
		if (range != null)
		{
			original = original ?? string.Empty;
			if (deleteMask != null && deleteMask.Length == original.Length)
			{
				int num = 0;
				for (int num2 = deleteMask.Length - 1; num2 >= 0; num2--)
				{
					if (deleteMask[num2])
					{
						int num3 = num2 + 1;
						while (num2 >= 0 && deleteMask[num2])
						{
							num2--;
						}
						int num4 = num2 + 1;
						int num5 = num3 - num4;
						SafeTextMutationService.TryReplaceWithoutProtection(range, num4, num5, string.Empty, operationName);
						num += num5;
					}
				}
				return num;
			}
			throw new ArgumentException("删除标记与原文本长度不一致。", "deleteMask");
		}
		throw new ArgumentNullException("range");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void LogPunctuationCleanupMetrics(PunctuationCleanupMetrics metrics, string scope)
	{
		if (metrics != null)
		{
			LogService.Info("ChineseContextPunctuation normalized=" + metrics.ConvertedCount + ", preservedStraightQuotes=" + metrics.PreservedStraightQuoteCount + ", protectedTokens=" + metrics.ProtectedTokenCount + ", conservativeSkips=" + metrics.ConservativeSkipCount + ", protectedParagraphs=" + metrics.ProtectedParagraphCount + ", scope=" + (scope ?? "unknown"));
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ExecuteFindReplace(Microsoft.Office.Interop.Word.Range range, string findText, string replaceText, bool useWildcards)
	{
		Find value = null;
		try
		{
			value = range.Find;
			value.ClearFormatting();
			value.Replacement.ClearFormatting();
			ResetFindFlags(value);
			value.Text = findText;
			value.Replacement.Text = replaceText ?? string.Empty;
			value.Forward = true;
			value.Wrap = WdFindWrap.wdFindStop;
			value.MatchWildcards = useWildcards;
			value.MatchCase = true;
			Find find = value;
			object FindText = Type.Missing;
			object MatchCase = Type.Missing;
			object MatchWholeWord = Type.Missing;
			object MatchWildcards = Type.Missing;
			object MatchSoundsLike = Type.Missing;
			object MatchAllWordForms = Type.Missing;
			object Forward = Type.Missing;
			object Wrap = Type.Missing;
			object Format = Type.Missing;
			object ReplaceWith = Type.Missing;
			object Replace = WdReplace.wdReplaceAll;
			object MatchKashida = Type.Missing;
			object MatchDiacritics = Type.Missing;
			object MatchAlefHamza = Type.Missing;
			object MatchControl = Type.Missing;
			find.Execute(ref FindText, ref MatchCase, ref MatchWholeWord, ref MatchWildcards, ref MatchSoundsLike, ref MatchAllWordForms, ref Forward, ref Wrap, ref Format, ref ReplaceWith, ref Replace, ref MatchKashida, ref MatchDiacritics, ref MatchAlefHamza, ref MatchControl);
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "TextCleanupService.find");
			}
		}
	}

	private static void ResetFindFlags(Find find)
	{
		find.MatchWholeWord = false;
		find.MatchSoundsLike = false;
		find.MatchAllWordForms = false;
		find.MatchPrefix = false;
		find.MatchSuffix = false;
		find.IgnoreSpace = false;
		find.IgnorePunct = false;
		find.MatchKashida = false;
		find.MatchDiacritics = false;
		find.MatchAlefHamza = false;
		find.MatchControl = false;
	}

	private static string TrimParagraphMark(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return string.Empty;
		}
		while (text.Length > 0 && (text[text.Length - 1] == '\r' || text[text.Length - 1] == '\a'))
		{
			text = text.Substring(0, text.Length - 1);
		}
		return text;
	}

	private static bool IsEmptyParagraphSlice(string text, int start, int end)
	{
		if (string.IsNullOrEmpty(text))
		{
			return true;
		}
		for (int i = start; i < end; i++)
		{
			if (!IsEmptyParagraphBlankChar(text[i]))
			{
				return false;
			}
		}
		return true;
	}

	private static bool IsEmptyParagraphBlankChar(char c)
	{
		switch (c)
		{
		default:
			if (c != '\u2028' && c != '\u2029' && c != '\u202f' && c != '\u205f' && c != '\u3000')
			{
				return c == '\ufeff';
			}
			break;
		case '\a':
		case '\t':
		case '\n':
		case '\v':
		case '\r':
		case ' ':
		case '\u00a0':
		case '\u1680':
		case '\u180e':
		case '\u2000':
		case '\u2001':
		case '\u2002':
		case '\u2003':
		case '\u2004':
		case '\u2005':
		case '\u2006':
		case '\u2007':
		case '\u2008':
		case '\u2009':
		case '\u200a':
		case '\u200b':
			break;
		}
		return true;
	}

	private static int GetParagraphHeadNoiseLength(string text)
	{
		int i;
		for (i = 0; i < (text ?? string.Empty).Length; i++)
		{
			char c = text[i];
			bool num = (c >= '─' && c <= '╿') || c == '|' || c == '┃';
			bool flag = c == '\u00a0' || c == '\u3000' || (i == 0 && char.IsWhiteSpace(c) && c != '\t' && c != '\n' && c != '\r' && c != '\f');
			if (!num && !flag)
			{
				break;
			}
		}
		return i;
	}

	private static bool IsLatinOrDigit(char c)
	{
		if (('0' > c || c > '9') && ('a' > c || c > 'z'))
		{
			if ('A' > c)
			{
				return false;
			}
			return c <= 'Z';
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ConvertFullWidthLettersAndDigitsInRange(Microsoft.Office.Interop.Word.Range range)
	{
		if (range != null)
		{
			string text = TrimParagraphMark(range.Text ?? string.Empty);
			if (text.Length == 0)
			{
				return;
			}
			char[] array = text.ToCharArray();
			bool flag = false;
			for (int i = 0; i < array.Length; i++)
			{
				int num = FullWidthChars.IndexOf(array[i]);
				if (num >= 0)
				{
					array[i] = HalfWidthChars[num];
					flag = true;
				}
			}
			if (flag)
			{
				SafeTextMutationService.ApplyEqualLengthCharacterChanges(range, text, new string(array), "全角英文数字转换");
			}
			return;
		}
		throw new ArgumentNullException("range");
	}
}
