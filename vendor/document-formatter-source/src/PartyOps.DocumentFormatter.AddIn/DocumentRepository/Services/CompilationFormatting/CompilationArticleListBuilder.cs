using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.CompilationFormatting;

namespace DocumentRepository.Services.CompilationFormatting;

public static class CompilationArticleListBuilder
{
	public static CompilationArticleList BuildFromParagraphTexts(IList<string> paragraphTexts)
	{
		return BuildFromParagraphTextsWithConfig(paragraphTexts, new FormatConfig());
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static CompilationArticleList BuildFromParagraphTextsWithConfig(IList<string> paragraphTexts, FormatConfig config)
	{
		if (paragraphTexts == null)
		{
			throw new ArgumentNullException("paragraphTexts");
		}
		CompilationArticleList compilationArticleList = new CompilationArticleList();
		List<int> list = new List<int>();
		for (int i = 0; i < paragraphTexts.Count; i++)
		{
			if (CompilationMarkerParser.IsExactMarker(paragraphTexts[i]))
			{
				list.Add(i);
			}
		}
		if (list.Count == 0)
		{
			compilationArticleList.ErrorMessage = "未找到合法标记“@@汇编@@”。";
			return compilationArticleList;
		}
		if (list[0] > 0)
		{
			compilationArticleList.FrontMatterEndParagraphIndex = list[0] - 1;
		}
		else
		{
			compilationArticleList.FrontMatterEndParagraphIndex = -1;
		}
		List<CompilationArticleTitleResolution> list2 = new List<CompilationArticleTitleResolution>();
		for (int j = 0; j < list.Count; j++)
		{
			int contentEndExclusive = ((j + 1 < list.Count) ? list[j + 1] : paragraphTexts.Count);
			CompilationArticleTitleResolution compilationArticleTitleResolution = CompilationArticleTitleResolver.Resolve(paragraphTexts, list[j] + 1, contentEndExclusive, config);
			if (!compilationArticleTitleResolution.HasTitle)
			{
				compilationArticleList.ErrorMessage = $"第 {j + 1} 个标记后没有非空主标题。";
				compilationArticleList.Articles.Clear();
				return compilationArticleList;
			}
			list2.Add(compilationArticleTitleResolution);
		}
		for (int k = 0; k < list.Count; k++)
		{
			CompilationArticleTitleResolution compilationArticleTitleResolution2 = list2[k];
			int titleStartParagraphIndex = compilationArticleTitleResolution2.TitleStartParagraphIndex;
			int endPosition = ((k + 1 < list.Count) ? (list[k + 1] - 1) : (paragraphTexts.Count - 1));
			CompilationArticleInfo item = new CompilationArticleInfo
			{
				OrderIndex = k + 1,
				Title = compilationArticleTitleResolution2.Title,
				BeginBookmarkName = $"SXCF_ART_{k + 1:000}_BEGIN",
				EndBookmarkName = $"SXCF_ART_{k + 1:000}_END",
				TitleStartParagraphIndex = compilationArticleTitleResolution2.TitleStartParagraphIndex,
				TitleEndParagraphIndex = compilationArticleTitleResolution2.TitleEndParagraphIndex,
				TitleParagraphCount = compilationArticleTitleResolution2.TitleParagraphCount,
				StartPosition = titleStartParagraphIndex,
				EndPosition = endPosition
			};
			compilationArticleList.Articles.Add(item);
		}
		return compilationArticleList;
	}
}
