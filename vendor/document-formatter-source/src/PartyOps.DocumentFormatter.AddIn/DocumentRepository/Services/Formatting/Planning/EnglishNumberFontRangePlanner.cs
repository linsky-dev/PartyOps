using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Models.FormattingPlan;
using DocumentRepository.Services.Detection;

namespace DocumentRepository.Services.Formatting.Planning;

public static class EnglishNumberFontRangePlanner
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static List<PlannedTextRange> Build(DocumentElementList elements, FormatConfig config)
	{
		if (elements != null && elements.Items != null)
		{
			if (config != null)
			{
				List<PlannedTextRange> result = new List<PlannedTextRange>();
				for (int i = 0; i < elements.Items.Count; i++)
				{
					DocumentElement documentElement = elements.Items[i];
					if (documentElement == null || !documentElement.HasEnglishNumbers || documentElement.RangeEnd <= documentElement.RangeStart)
					{
						continue;
					}
					string text = EnglishNumberFontScopePolicy.ResolveElementFont(config, documentElement.Type);
					if (text != null)
					{
						int num = (IsMixedHeading(documentElement.Type) ? MixedContentDetector.FindTitleBodyBoundary((documentElement.Text ?? string.Empty).TrimEnd('\r', '\a')) : (-1));
						if (num <= 0 || documentElement.RangeStart + num + 1 >= documentElement.RangeEnd)
						{
							AddIfTargetFont(result, config, documentElement.RangeStart, documentElement.RangeEnd, documentElement.Type, text);
							continue;
						}
						AddIfTargetFont(result, config, documentElement.RangeStart, Math.Min(documentElement.RangeStart + num + 1, documentElement.RangeEnd), documentElement.Type, text);
						string sourceFont = EnglishNumberFontScopePolicy.ResolveElementFont(config, ElementType.Body);
						AddIfTargetFont(result, config, Math.Min(documentElement.RangeStart + num + 1, documentElement.RangeEnd), documentElement.RangeEnd, ElementType.Body, sourceFont);
					}
				}
				return result;
			}
			throw new ArgumentNullException("config");
		}
		throw new InvalidOperationException("英文数字字体规划缺少段落元素事实。");
	}

	private static void AddIfTargetFont(List<PlannedTextRange> result, FormatConfig config, int start, int end, ElementType type, string sourceFont)
	{
		if (end > start && EnglishNumberFontScopePolicy.MatchesLevel3Font(config, sourceFont))
		{
			PlannedTextRange plannedTextRange = ((result.Count == 0) ? null : result[result.Count - 1]);
			if (plannedTextRange == null || plannedTextRange.End < start || !string.Equals(plannedTextRange.SourceEastAsianFontName, sourceFont, StringComparison.OrdinalIgnoreCase))
			{
				result.Add(new PlannedTextRange
				{
					Start = start,
					End = end,
					SourceElementType = type,
					SourceEastAsianFontName = sourceFont
				});
			}
			else
			{
				plannedTextRange.End = Math.Max(plannedTextRange.End, end);
			}
		}
	}

	private static bool IsMixedHeading(ElementType type)
	{
		if (type != ElementType.MainTitle && type != ElementType.Level1Title && type != ElementType.Level2Title)
		{
			return type == ElementType.Level3Title;
		}
		return true;
	}
}
