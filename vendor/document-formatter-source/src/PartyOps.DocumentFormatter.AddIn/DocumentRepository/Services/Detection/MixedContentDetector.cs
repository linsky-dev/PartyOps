using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace DocumentRepository.Services.Detection;

public static class MixedContentDetector
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int FindTitleBodyBoundary(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return -1;
		}
		int num = 0;
		if (!Regex.IsMatch(text, "^[一二三四五六七八九十]+[、.．]"))
		{
			if (Regex.IsMatch(text, "^[（(][一二三四五六七八九十]+[）)]"))
			{
				num = ((text.IndexOf('）') > 0) ? (text.IndexOf('）') + 1) : (text.IndexOf(')') + 1));
			}
			else if (Regex.IsMatch(text, "^\\d+[.．、]"))
			{
				num = text.IndexOfAny(new char[] { '.', '．', '、' }) + 1;
			}
		}
		else
		{
			num = text.IndexOfAny(new char[] { '、', '.', '．' }) + 1;
		}
		for (int i = num; i < text.Length; i++)
		{
			char c = text[i];
			if (c == '。' || c == '；' || c == '！' || c == '?' || c == '？')
			{
				return i;
			}
		}
		return -1;
	}
}
