using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Services.Cleanup;
using DocumentRepository.Services.Detection;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Interop;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository;

public static class KeywordBoldener
{
	private sealed class SemicolonEdit
	{
		public int PunctuationIndex { get; set; }

		public int PhraseIndex { get; set; }
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ApplyXShiBold(Document doc, FormatConfig cfg, DocumentElementList elements)
	{
		Dictionary<string, int> dictionary = BuildPhraseModes(cfg);
		if (dictionary.Count != 0)
		{
			if (doc == null)
			{
				throw new ArgumentNullException("doc");
			}
			if (elements == null)
			{
				throw new InvalidOperationException("关键词加粗必须使用分析层产出的 DocumentElementList。");
			}
			ApplyKeywordBoldByElements(doc, dictionary, elements);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ApplyXShiBold(Microsoft.Office.Interop.Word.Range range, FormatConfig cfg, DocumentElementList elements)
	{
		Dictionary<string, int> dictionary = BuildPhraseModes(cfg);
		if (dictionary.Count == 0)
		{
			return;
		}
		if (range == null)
		{
			throw new ArgumentNullException("range");
		}
		if (elements == null)
		{
			throw new InvalidOperationException("选区关键词加粗必须使用分析层产出的 DocumentElementList。");
		}
		Document value = null;
		try
		{
			value = range.Document;
			ApplyKeywordBoldByElements(value, dictionary, elements);
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "KeywordBoldener.docRef");
			}
		}
	}

	public static bool HasEnabledRules(FormatConfig cfg)
	{
		return BuildPhraseModes(cfg).Count > 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static Dictionary<string, int> BuildPhraseModes(FormatConfig cfg)
	{
		if (cfg != null)
		{
			Dictionary<string, int> dictionary = new Dictionary<string, int>();
			if (cfg.YiShiMode > 0)
			{
				foreach (string yiShiPhrase in KeywordPhraseCatalog.YiShiPhrases)
				{
					dictionary[yiShiPhrase] = cfg.YiShiMode;
				}
			}
			if (cfg.YiYaoMode > 0)
			{
				foreach (string yiYaoPhrase in KeywordPhraseCatalog.YiYaoPhrases)
				{
					dictionary[yiYaoPhrase] = cfg.YiYaoMode;
				}
			}
			if (cfg.DiYiMode > 0)
			{
				foreach (string diYiPhrase in KeywordPhraseCatalog.DiYiPhrases)
				{
					dictionary[diYiPhrase] = cfg.DiYiMode;
				}
			}
			return dictionary;
		}
		throw new ArgumentNullException("cfg");
	}

	private static bool IsSentenceStart(string text, int index)
	{
		if (index <= 0)
		{
			return true;
		}
		int num = index - 1;
		while (num >= 0 && (text[num] == ' ' || text[num] == '\t' || text[num] == '\u3000'))
		{
			num--;
		}
		if (num >= 0)
		{
			char c = text[num];
			if (!char.IsLetterOrDigit(c) && c != '_' && (c < '一' || c > '鿿'))
			{
				if (c == '（' || c == '(')
				{
					return false;
				}
				return true;
			}
			return false;
		}
		return true;
	}

	public static int FindSentenceEnd(string text, int startPos)
	{
		if (string.IsNullOrEmpty(text) || startPos < 0)
		{
			return -1;
		}
		for (int i = startPos; i < text.Length; i++)
		{
			switch (text[i])
			{
			case '!':
			case '.':
			case ';':
			case '?':
			{
				bool num = i > 0 && char.IsDigit(text[i - 1]);
				bool flag = i + 1 < text.Length && char.IsDigit(text[i + 1]);
				if (!(num && flag))
				{
					return i;
				}
				break;
			}
			case '。':
			case '！':
			case '；':
			case '？':
				return i;
			}
		}
		return -1;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ApplyKeywordBoldByElements(Document doc, Dictionary<string, int> phraseModes, DocumentElementList elements)
	{
		if (doc != null)
		{
			if (elements == null || elements.Items == null)
			{
				throw new InvalidOperationException("Keyword bold requires DocumentElementList.Items.");
			}
			if (phraseModes == null || phraseModes.Count == 0)
			{
				return;
			}
			int docContentEnd = 0;
			Microsoft.Office.Interop.Word.Range value = null;
			try
			{
				value = doc.Content;
				docContentEnd = value.End;
			}
			finally
			{
				if (value != null)
				{
					ComObjectRelease.Release(ref value, "KeywordBoldener.docContentRng");
				}
			}
			{
				foreach (DocumentElement item in elements.Items)
				{
					if (item != null && item.HasKeywordCandidate)
					{
						ApplyKeywordBoldToText(doc, item.Text, item.RangeStart, phraseModes, docContentEnd);
					}
				}
				return;
			}
		}
		throw new ArgumentNullException("doc");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ApplyKeywordBoldToText(Document doc, string text, int paraStart, Dictionary<string, int> phraseModes, int docContentEnd)
	{
		if (doc == null)
		{
			throw new ArgumentNullException("doc");
		}
		WordDocumentBoundary.EnsurePosition(paraStart, 0, docContentEnd, "关键词加粗段落起点");
		if (string.IsNullOrEmpty(text) || phraseModes == null || phraseModes.Count == 0)
		{
			return;
		}
		foreach (KeyValuePair<string, int> phraseMode in phraseModes)
		{
			string key = phraseMode.Key;
			int value = phraseMode.Value;
			int num = 0;
			while (num < text.Length)
			{
				int num2 = text.IndexOf(key, num);
				if (num2 < 0)
				{
					break;
				}
				if (IsSentenceStart(text, num2))
				{
					bool flag = true;
					if (key.StartsWith("第"))
					{
						int i;
						for (i = num2 + key.Length; i < text.Length && char.IsWhiteSpace(text[i]); i++)
						{
						}
						if (i >= text.Length || (text[i] != '，' && text[i] != ','))
						{
							flag = false;
						}
					}
					if (flag)
					{
						int num3 = paraStart + num2;
						int num4;
						if (value != 2)
						{
							num4 = num3 + key.Length;
						}
						else
						{
							int num5 = FindSentenceEnd(text, num2 + key.Length);
							if (num5 < 0)
							{
								num5 = text.Length - 1;
							}
							num4 = paraStart + num5 + 1;
						}
						if (num4 > docContentEnd)
						{
							num4 = docContentEnd;
						}
						if (num3 >= num4 || num3 < 0)
						{
							continue;
						}
						Microsoft.Office.Interop.Word.Range value2 = null;
						Font value3 = null;
						try
						{
							object Start = num3;
							object End = num4;
							value2 = doc.Range(ref Start, ref End);
							value3 = value2.Font;
							if (value3.Bold != -1)
							{
								value3.Bold = -1;
							}
						}
						finally
						{
							if (value3 != null)
							{
								ComObjectRelease.Release(ref value3, "KeywordBoldener.boldFont");
							}
							if (value2 != null)
							{
								ComObjectRelease.Release(ref value2, "KeywordBoldener.boldRange");
							}
						}
					}
				}
				num = num2 + key.Length;
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void FixSemicolonsForXShi(Document doc, DocumentElementList elements)
	{
		if (doc != null)
		{
			if (elements == null)
			{
				throw new InvalidOperationException("分号修正必须使用分析层产出的 DocumentElementList。");
			}
			FixSemicolonsForElements(doc, elements);
			return;
		}
		throw new ArgumentNullException("doc");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void FixSemicolonsForXShi(Microsoft.Office.Interop.Word.Range range, DocumentElementList elements)
	{
		if (range == null)
		{
			throw new ArgumentNullException("range");
		}
		if (elements == null)
		{
			throw new InvalidOperationException("选区分号修正必须使用分析层产出的 DocumentElementList。");
		}
		Document value = null;
		try
		{
			value = range.Document;
			FixSemicolonsForElements(value, elements);
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "KeywordBoldener.docRef");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void FixSemicolonsForElements(Document doc, DocumentElementList elements)
	{
		if (doc == null)
		{
			throw new ArgumentNullException("doc");
		}
		if (elements == null || elements.Items == null)
		{
			throw new InvalidOperationException("Semicolon correction requires DocumentElementList.Items.");
		}
		for (int num = elements.Items.Count - 1; num >= 0; num--)
		{
			DocumentElement documentElement = elements.Items[num];
			if (documentElement != null && documentElement.HasSemicolonCandidate)
			{
				List<SemicolonEdit> list = FindSemicolonEdits(documentElement.Text);
				if (list.Count != 0)
				{
					Microsoft.Office.Interop.Word.Range value = null;
					try
					{
						object Start = documentElement.RangeStart;
						object End = documentElement.RangeEnd;
						value = doc.Range(ref Start, ref End);
						Microsoft.Office.Interop.Word.Range range = value;
						End = WdUnits.wdCharacter;
						Start = -1;
						range.MoveEnd(ref End, ref Start);
						if (value.Start < value.End && !SafeTextMutationService.HasProtectedContent(value))
						{
							foreach (SemicolonEdit item in list)
							{
								int num2 = item.PhraseIndex - item.PunctuationIndex - 1;
								if (num2 > 0)
								{
									SafeTextMutationService.TryReplace(value, item.PunctuationIndex + 1, num2, string.Empty, "关键词分号前空白清理");
								}
								SafeTextMutationService.TryReplace(value, item.PunctuationIndex, 1, "；", "关键词分号修正");
							}
						}
					}
					finally
					{
						if (value != null)
						{
							ComObjectRelease.Release(ref value, "KeywordBoldener.rng");
						}
					}
				}
			}
		}
	}

	private static List<SemicolonEdit> FindSemicolonEdits(string text)
	{
		List<SemicolonEdit> list = new List<SemicolonEdit>();
		if (string.IsNullOrEmpty(text))
		{
			return list;
		}
		foreach (string semicolonPhrase in KeywordPhraseCatalog.SemicolonPhrases)
		{
			int num = 0;
			while (num < text.Length)
			{
				int num2 = text.IndexOf(semicolonPhrase, num);
				if (num2 < 0)
				{
					break;
				}
				int num3 = num2 - 1;
				while (num3 >= 0 && char.IsWhiteSpace(text[num3]))
				{
					num3--;
				}
				if (num3 < 0 || text[num3] == '；')
				{
					num = num2 + semicolonPhrase.Length;
					continue;
				}
				char c = text[num3];
				if (c == '，' || c == '、' || c == '。' || c == '！')
				{
					list.Add(new SemicolonEdit
					{
						PunctuationIndex = num3,
						PhraseIndex = num2
					});
					num = num2 + semicolonPhrase.Length;
				}
				else
				{
					num = num2 + semicolonPhrase.Length;
				}
			}
		}
		return (from x in list
			group x by x.PunctuationIndex into x
			select x.First() into x
			orderby x.PhraseIndex descending
			select x).ToList();
	}
}
