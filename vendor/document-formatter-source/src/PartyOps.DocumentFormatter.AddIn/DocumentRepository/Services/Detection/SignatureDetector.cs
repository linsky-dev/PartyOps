using System;
using System.Text.RegularExpressions;

namespace DocumentRepository.Services.Detection;

public static class SignatureDetector
{
	private static readonly Regex NumericDateRegex = new Regex("^\\d{4}年.{1,4}月.{1,4}日$", RegexOptions.Compiled);

	private static readonly Regex ChineseDateRegex = new Regex("^[〇零一二三四五六七八九十百千万]+年[一二三四五六七八九十]+月[一二三四五六七八九十]+日$", RegexOptions.Compiled);

	private static readonly Regex HasChineseRegex = new Regex("[\\u4e00-\\u9fff]", RegexOptions.Compiled);

	private static readonly Regex PunctuationRegex = new Regex("[，。！？；：、“”‘’【】《》〈〉——…·,.!?;:\\[\\]{}<>/\\\\\"'`@#\\$%\\^&\\*\\-\\+=]", RegexOptions.Compiled);

	private static readonly string[] OrgSuffixes = new string[31]
	{
		"人民政府", "办公厅", "办公室", "委员会", "公司", "集团", "人民政府办公室", "人民政府办公厅", "省委", "市委",
		"县委", "州委", "地委", "区委", "部委", "机关", "党组", "党委", "部", "厅",
		"署", "局", "院", "行", "校", "医院", "中心", "协会", "研究所", "学院",
		"大学"
	};

	private static readonly string[] NonOrganizationEndings = new string[15]
	{
		"施行", "执行", "实行", "履行", "推行", "举行", "运行", "通行", "发行", "出院",
		"入院", "住院", "到校", "返校", "离校"
	};

	public static bool IsDateLine(string text)
	{
		string text2 = (text ?? string.Empty).Trim();
		if (!string.IsNullOrEmpty(text2))
		{
			if (!NumericDateRegex.IsMatch(text2))
			{
				return ChineseDateRegex.IsMatch(text2);
			}
			return true;
		}
		return false;
	}

	public static bool IsSignatureLine(string text)
	{
		string text2 = (text ?? string.Empty).Trim();
		if (!HasChineseRegex.IsMatch(text2))
		{
			return false;
		}
		if (text2.Length < 2 || text2.Length > 25)
		{
			return false;
		}
		if (char.IsDigit(text2[text2.Length - 1]))
		{
			return false;
		}
		return !PunctuationRegex.IsMatch(text2);
	}

	public static bool HasOrgSuffix(string text)
	{
		string text2 = (text ?? string.Empty).Trim();
		if (string.IsNullOrEmpty(text2))
		{
			return false;
		}
		string[] nonOrganizationEndings = NonOrganizationEndings;
		int num = 0;
		while (true)
		{
			if (num >= nonOrganizationEndings.Length)
			{
				nonOrganizationEndings = OrgSuffixes;
				foreach (string value in nonOrganizationEndings)
				{
					if (text2.EndsWith(value, StringComparison.Ordinal))
					{
						return true;
					}
				}
				break;
			}
			string value2 = nonOrganizationEndings[num];
			if (text2.EndsWith(value2, StringComparison.Ordinal))
			{
				return false;
			}
			num++;
		}
		return false;
	}
}
