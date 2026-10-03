using System;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Services.Detection;

public static class SalutationDetector
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool IsSalutation(string text, FormatConfig cfg = null)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		text = text.Trim();
		if (text.EndsWith("：") || text.EndsWith(":"))
		{
			if (!HeadingLevelDetector.IsLevel1Title(text, cfg) && !HeadingLevelDetector.IsLevel2Title(text, cfg) && !HeadingLevelDetector.IsLevel3Title(text, cfg))
			{
				string text2 = text.Substring(0, text.Length - 1).Trim();
				if (text2.Length != 0 && text2.Length <= 220)
				{
					if (ContainsSentenceTerminator(text2))
					{
						return false;
					}
					if (!IsBodyLeadIn(text2))
					{
						if (text.Length <= 28)
						{
							return true;
						}
						return LooksLikeOfficialRecipientList(text2);
					}
					return false;
				}
				return false;
			}
			return false;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool IsBodyLeadIn(string text)
	{
		return text.EndsWith("如下", StringComparison.Ordinal);
	}

	private static bool ContainsSentenceTerminator(string text)
	{
		if (text.IndexOf('。') < 0 && text.IndexOf('！') < 0 && text.IndexOf('？') < 0 && text.IndexOf('；') < 0 && text.IndexOf('!') < 0 && text.IndexOf('?') < 0)
		{
			return text.IndexOf(';') >= 0;
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool LooksLikeOfficialRecipientList(string text)
	{
		bool num = text.IndexOf('，') >= 0 || text.IndexOf('、') >= 0 || text.IndexOf(',') >= 0;
		bool flag = text.Contains("同志") || text.Contains("代表") || text.Contains("人民政府") || text.Contains("委员会") || text.Contains("办公室") || text.Contains("公司") || text.Contains("集团") || text.Contains("局") || text.Contains("厅") || text.Contains("院") || text.Contains("校") || text.Contains("中心") || text.Contains("单位") || text.StartsWith("各", StringComparison.Ordinal);
		return num && flag;
	}
}
