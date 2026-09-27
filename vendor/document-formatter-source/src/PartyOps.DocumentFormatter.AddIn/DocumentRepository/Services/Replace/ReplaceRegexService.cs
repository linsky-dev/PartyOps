using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DocumentRepository.Models;
using DocumentRepository.Models.Replace;

namespace DocumentRepository.Services.Replace;

public static class ReplaceRegexService
{
	private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(2.0);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static Regex CreateRegex(ReplaceRule rule)
	{
		if (rule != null)
		{
			if (string.IsNullOrWhiteSpace(rule.FindText))
			{
				throw ReplaceOperationException.Create(ReplaceFailureReasonCode.RuleConfigurationInvalid, ReplaceFailureStage.ApplyRules);
			}
			string pattern = (rule.UseRegex ? rule.FindText : Regex.Escape(rule.FindText));
			Regex regex;
			try
			{
				regex = new Regex(pattern, RegexOptions.Multiline, MatchTimeout);
			}
			catch (ArgumentException innerException)
			{
				throw ReplaceOperationException.Create(ReplaceFailureReasonCode.RegularExpressionInvalid, ReplaceFailureStage.ApplyRules, innerException);
			}
			EnsureNoZeroLengthMatch(regex);
			if (rule.UseRegex)
			{
				ValidateReplacementReferences(regex, rule.ReplaceText ?? string.Empty);
			}
			return regex;
		}
		throw new ArgumentNullException("rule");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string ResolveReplacement(Match match, ReplaceRule rule)
	{
		if (match == null)
		{
			throw new ArgumentNullException("match");
		}
		if (rule != null)
		{
			string text;
			if (!rule.UseRegex)
			{
				text = rule.ReplaceText;
				if (text == null)
				{
					return string.Empty;
				}
			}
			else
			{
				text = match.Result(rule.ReplaceText ?? string.Empty);
			}
			return text;
		}
		throw new ArgumentNullException("rule");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ReplacePreviewResult Preview(ReplaceRule rule, string sampleText)
	{
		try
		{
			string text = NormalizePreviewLineEndings(sampleText ?? string.Empty);
			int matchCount;
			string value = ApplyToText(rule, text, out matchCount);
			return new ReplacePreviewResult
			{
				Success = true,
				MatchCount = matchCount,
				BeforeText = RestorePreviewLineEndings(text),
				AfterText = RestorePreviewLineEndings(value),
				Message = ((matchCount == 0) ? "示例中没有找到匹配内容。" : $"示例中找到 {matchCount} 处匹配。")
			};
		}
		catch (ReplaceOperationException ex)
		{
			return new ReplacePreviewResult
			{
				Success = false,
				MatchCount = 0,
				BeforeText = (sampleText ?? string.Empty),
				AfterText = string.Empty,
				Message = ReplaceFailurePresentation.CauseAndAction(ex.ReasonCode)
			};
		}
		catch (Exception ex2)
		{
			return new ReplacePreviewResult
			{
				Success = false,
				MatchCount = 0,
				BeforeText = (sampleText ?? string.Empty),
				AfterText = string.Empty,
				Message = ex2.Message
			};
		}
	}

	public static string ApplyToText(ReplaceRule rule, string source, out int matchCount)
	{
		Regex regex = CreateRegex(rule);
		int count = 0;
		string result = regex.Replace(source ?? string.Empty, delegate(Match match)
		{
			if (match.Length == 0)
			{
				throw ReplaceOperationException.Create(ReplaceFailureReasonCode.ZeroLengthMatch, ReplaceFailureStage.ApplyRules);
			}
			count++;
			return ResolveReplacement(match, rule);
		});
		matchCount = count;
		return result;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void EnsureNoZeroLengthMatch(Regex regex)
	{
		string[] array = new string[4]
		{
			string.Empty,
			"测试",
			"\r测试",
			"第一，测试"
		};
		foreach (string input in array)
		{
			Match match = regex.Match(input);
			if (match.Success && match.Length == 0)
			{
				throw ReplaceOperationException.Create(ReplaceFailureReasonCode.ZeroLengthMatch, ReplaceFailureStage.ApplyRules);
			}
		}
	}

	private static void ValidateReplacementReferences(Regex regex, string replacement)
	{
		HashSet<string> hashSet = new HashSet<string>(regex.GetGroupNames(), StringComparer.Ordinal);
		for (int i = 0; i < replacement.Length; i++)
		{
			if (replacement[i] != '$')
			{
				continue;
			}
			if (i + 1 < replacement.Length)
			{
				char c = replacement[i + 1];
				switch (c)
				{
				case '{':
				{
					int num = replacement.IndexOf('}', i + 2);
					if (num < 0)
					{
						throw ReplaceOperationException.Create(ReplaceFailureReasonCode.CaptureGroupInvalid, ReplaceFailureStage.ApplyRules);
					}
					string item = replacement.Substring(i + 2, num - i - 2);
					if (!hashSet.Contains(item))
					{
						throw ReplaceOperationException.Create(ReplaceFailureReasonCode.CaptureGroupInvalid, ReplaceFailureStage.ApplyRules);
					}
					i = num;
					continue;
				}
				case '$':
				case '&':
				case '\'':
				case '+':
				case '_':
				case '`':
					i++;
					continue;
				}
				if (!char.IsDigit(c))
				{
					throw ReplaceOperationException.Create(ReplaceFailureReasonCode.CaptureGroupInvalid, ReplaceFailureStage.ApplyRules);
				}
				int j;
				for (j = i + 1; j < replacement.Length && char.IsDigit(replacement[j]); j++)
				{
				}
				string item2 = replacement.Substring(i + 1, j - i - 1);
				if (!hashSet.Contains(item2))
				{
					throw ReplaceOperationException.Create(ReplaceFailureReasonCode.CaptureGroupInvalid, ReplaceFailureStage.ApplyRules);
				}
				i = j - 1;
				continue;
			}
			throw ReplaceOperationException.Create(ReplaceFailureReasonCode.CaptureGroupInvalid, ReplaceFailureStage.ApplyRules);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string NormalizePreviewLineEndings(string value)
	{
		return (value ?? string.Empty).Replace("\r\n", "\r").Replace("\n", "\r");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string RestorePreviewLineEndings(string value)
	{
		return (value ?? string.Empty).Replace("\r", Environment.NewLine);
	}
}
