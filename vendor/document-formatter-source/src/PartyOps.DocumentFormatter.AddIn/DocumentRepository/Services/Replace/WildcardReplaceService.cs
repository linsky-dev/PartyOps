using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DocumentRepository.Models;
using DocumentRepository.Models.Replace;
using DocumentRepository.Services.Hosting;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Replace;

public static class WildcardReplaceService
{
	public static int Apply(Microsoft.Office.Interop.Word.Range scope, ReplaceRule rule)
	{
		return Apply(scope, rule, null);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int Apply(Microsoft.Office.Interop.Word.Range scope, ReplaceRule rule, Action confirmWriteOccurred)
	{
		if (scope != null)
		{
			if (rule != null && !string.IsNullOrWhiteSpace(rule.FindText))
			{
				int num = CountMatches(scope, rule.FindText);
				if (num == 0)
				{
					return 0;
				}
				Find value = null;
				try
				{
					string a = scope.Text ?? string.Empty;
					value = scope.Find;
					Configure(value, rule.FindText, rule.ReplaceText ?? string.Empty);
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
					string b = scope.Text ?? string.Empty;
					if (!string.Equals(a, b, StringComparison.Ordinal))
					{
						confirmWriteOccurred?.Invoke();
					}
					return num;
				}
				catch (COMException innerException)
				{
					throw ReplaceOperationException.Create(ReplaceFailureReasonCode.WildcardExpressionInvalid, ReplaceFailureStage.ApplyRules, innerException);
				}
				finally
				{
					if (value != null)
					{
						ComObjectRelease.Release(ref value, "WildcardReplaceService.find");
					}
				}
			}
			return 0;
		}
		throw new ArgumentNullException("scope");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int CountMatches(Microsoft.Office.Interop.Word.Range scope, string pattern)
	{
		Microsoft.Office.Interop.Word.Range value = null;
		Find value2 = null;
		try
		{
			int num = 0;
			int end = scope.End;
			value = scope.Duplicate;
			value2 = value.Find;
			Configure(value2, pattern, string.Empty);
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
					int end2 = value.End;
					if (end2 <= value.Start)
					{
						break;
					}
					num++;
					if (end2 < end)
					{
						value.SetRange(end2, end);
						ComObjectRelease.Release(ref value2, "WildcardReplaceService.find");
						value2 = value.Find;
						Configure(value2, pattern, string.Empty);
						continue;
					}
				}
				return num;
			}
			throw ReplaceOperationException.Create(ReplaceFailureReasonCode.ZeroLengthMatch, ReplaceFailureStage.ApplyRules);
		}
		catch (COMException innerException)
		{
			throw ReplaceOperationException.Create(ReplaceFailureReasonCode.WildcardExpressionInvalid, ReplaceFailureStage.ApplyRules, innerException);
		}
		finally
		{
			if (value2 != null)
			{
				ComObjectRelease.Release(ref value2, "WildcardReplaceService.find");
			}
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "WildcardReplaceService.search");
			}
		}
	}

	private static void Configure(Find find, string pattern, string replacement)
	{
		find.ClearFormatting();
		find.Replacement.ClearFormatting();
		find.Text = pattern;
		find.Replacement.Text = replacement;
		find.Forward = true;
		find.Wrap = WdFindWrap.wdFindStop;
		find.Format = false;
		find.MatchWildcards = true;
		find.MatchCase = true;
		find.MatchWholeWord = false;
		find.MatchSoundsLike = false;
		find.MatchAllWordForms = false;
	}
}
