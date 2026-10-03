using System;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DocumentRepository.Models;
using DocumentRepository.Services.Cleanup;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Normalization;

public static class HeadingSpacingNormalizer
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string NormalizeIfNeeded(Paragraph para, ElementType type, string text, FormatConfig cfg)
	{
		string spaceRequiredMarker = GetSpaceRequiredMarker(GetRecognitionStyle(type, cfg));
		if (para == null || string.IsNullOrWhiteSpace(text) || string.IsNullOrEmpty(spaceRequiredMarker))
		{
			return text;
		}
		Match match = MatchSpaceAfterMarker(text, spaceRequiredMarker);
		if (match.Success)
		{
			string text2 = match.Groups[1].Value + "  " + match.Groups[3].Value;
			if (string.Equals(text2, text, StringComparison.Ordinal))
			{
				return text;
			}
			Microsoft.Office.Interop.Word.Range value = null;
			try
			{
				value = para.Range.Duplicate;
				if (value.End > value.Start)
				{
					value.End--;
				}
				return SafeTextMutationService.TryReplace(value, match.Groups[2].Index, match.Groups[2].Length, "  ", "标题序号后空格规范化") ? text2 : text;
			}
			catch (Exception ex)
			{
				LogService.Error("HeadingSpacingNormalizer.NormalizeIfNeeded", ex);
				return text;
			}
			finally
			{
				if (value != null)
				{
					ComObjectRelease.Release(ref value, "HeadingSpacingNormalizer.contentRange");
				}
			}
		}
		return text;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string NormalizeIfNeeded(Microsoft.Office.Interop.Word.Range range, ElementType type, string text, FormatConfig cfg)
	{
		string spaceRequiredMarker = GetSpaceRequiredMarker(GetRecognitionStyle(type, cfg));
		if (range != null && !string.IsNullOrWhiteSpace(text) && !string.IsNullOrEmpty(spaceRequiredMarker))
		{
			Match match = MatchSpaceAfterMarker(text, spaceRequiredMarker);
			if (match.Success)
			{
				string text2 = match.Groups[1].Value + "  " + match.Groups[3].Value;
				if (string.Equals(text2, text, StringComparison.Ordinal))
				{
					return text;
				}
				Microsoft.Office.Interop.Word.Range value = null;
				try
				{
					value = range.Duplicate;
					if (value.End > value.Start)
					{
						value.End--;
					}
					return SafeTextMutationService.TryReplace(value, match.Groups[2].Index, match.Groups[2].Length, "  ", "标题序号后空格规范化") ? text2 : text;
				}
				catch (Exception ex)
				{
					LogService.Error("HeadingSpacingNormalizer.NormalizeIfNeeded.Range", ex);
					return text;
				}
				finally
				{
					if (value != null)
					{
						ComObjectRelease.Release(ref value, "HeadingSpacingNormalizer.contentRange");
					}
				}
			}
			return text;
		}
		return text;
	}

	private static string GetRecognitionStyle(ElementType type, FormatConfig cfg)
	{
		if (cfg != null)
		{
			return type switch
			{
				ElementType.Level2Title => cfg.Level2?.RecognitionStyle, 
				ElementType.Level1Title => cfg.Level1?.RecognitionStyle, 
				ElementType.Level3Title => cfg.Level3?.RecognitionStyle, 
				_ => null, 
			};
		}
		return null;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string GetSpaceRequiredMarker(string recognitionStyle)
	{
		string text = (recognitionStyle ?? "").Trim();
		if (!text.Contains("第一章"))
		{
			if (!text.Contains("第一部分"))
			{
				if (!text.Contains("第一篇"))
				{
					if (!text.Contains("第一节"))
					{
						if (text.Contains("第一条"))
						{
							return "条";
						}
						return null;
					}
					return "节";
				}
				return "篇";
			}
			return "部分";
		}
		return "章";
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static Match MatchSpaceAfterMarker(string text, string marker)
	{
		string pattern = "^(\\s*第[一二三四五六七八九十百千万零〇两\\d０-９]+" + Regex.Escape(marker) + ")(\\s*)(\\S.*)$";
		return Regex.Match(text ?? "", pattern);
	}
}
