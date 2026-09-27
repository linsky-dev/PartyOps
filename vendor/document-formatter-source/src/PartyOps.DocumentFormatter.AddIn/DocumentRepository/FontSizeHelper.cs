using System.Runtime.CompilerServices;

namespace DocumentRepository;

public static class FontSizeHelper
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static float ToPoints(string sizeText)
	{
		if (string.IsNullOrWhiteSpace(sizeText))
		{
			return 16f;
		}
		switch (sizeText.Trim())
		{
		default:
		{
			if (float.TryParse(sizeText.Trim(), out var result))
			{
				return result;
			}
			return 16f;
		}
		case "小三":
			return 15f;
		case "小二号":
			return 18f;
		case "初号":
			return 42f;
		case "七号":
			return 5.5f;
		case "小初":
			return 36f;
		case "小四":
			return 12f;
		case "小五":
			return 9f;
		case "小二":
			return 18f;
		case "一号":
			return 26f;
		case "三号":
			return 16f;
		case "小一":
			return 24f;
		case "六号":
			return 7.5f;
		case "小六":
			return 6.5f;
		case "五号":
			return 10.5f;
		case "二号":
			return 22f;
		case "四号":
			return 14f;
		case "八号":
			return 5f;
		}
	}
}
