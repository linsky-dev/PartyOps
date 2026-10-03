using System.Collections.Generic;

namespace DocumentRepository.Services.Detection;

public static class KeywordPhraseCatalog
{
	private static readonly string[] YiShiValues = new string[20]
	{
		"一是", "二是", "三是", "四是", "五是", "六是", "七是", "八是", "九是", "十是",
		"十一是", "十二是", "十三是", "十四是", "十五是", "十六是", "十七是", "十八是", "十九是", "二十是"
	};

	private static readonly string[] YiYaoValues = new string[20]
	{
		"一要", "二要", "三要", "四要", "五要", "六要", "七要", "八要", "九要", "十要",
		"十一要", "十二要", "十三要", "十四要", "十五要", "十六要", "十七要", "十八要", "十九要", "二十要"
	};

	private static readonly string[] DiYiValues = new string[20]
	{
		"第一", "第二", "第三", "第四", "第五", "第六", "第七", "第八", "第九", "第十",
		"第十一", "第十二", "第十三", "第十四", "第十五", "第十六", "第十七", "第十八", "第十九", "第二十"
	};

	private static readonly string[] SemicolonValues = new string[38]
	{
		"二是", "三是", "四是", "五是", "六是", "七是", "八是", "九是", "十是", "十一是",
		"十二是", "十三是", "十四是", "十五是", "十六是", "十七是", "十八是", "十九是", "二十是", "二要",
		"三要", "四要", "五要", "六要", "七要", "八要", "九要", "十要", "十一要", "十二要",
		"十三要", "十四要", "十五要", "十六要", "十七要", "十八要", "十九要", "二十要"
	};

	internal static IEnumerable<string> YiShiPhrases => YiShiValues;

	internal static IEnumerable<string> YiYaoPhrases => YiYaoValues;

	internal static IEnumerable<string> DiYiPhrases => DiYiValues;

	internal static IEnumerable<string> SemicolonPhrases => SemicolonValues;

	public static bool ContainsCandidate(string text)
	{
		if (!string.IsNullOrEmpty(text))
		{
			if (!ContainsAny(text, YiShiValues) && !ContainsAny(text, YiYaoValues))
			{
				return ContainsAny(text, DiYiValues);
			}
			return true;
		}
		return false;
	}

	private static bool ContainsAny(string text, IEnumerable<string> phrases)
	{
		foreach (string phrase in phrases)
		{
			if (!text.Contains(phrase))
			{
				continue;
			}
			return true;
		}
		return false;
	}
}
