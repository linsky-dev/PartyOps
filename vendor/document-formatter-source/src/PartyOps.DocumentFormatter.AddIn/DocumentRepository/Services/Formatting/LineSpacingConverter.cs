using System;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace DocumentRepository.Services.Formatting;

public static class LineSpacingConverter
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static float ToPoints(string lineSpacingText)
	{
		if (!string.IsNullOrWhiteSpace(lineSpacingText))
		{
			string text = lineSpacingText.Trim();
			if (text == "单倍行距")
			{
				return 0f;
			}
			if (float.TryParse(text, out var result))
			{
				return result;
			}
			Match match = Regex.Match(text, "[\\d\\.]+");
			if (match.Success && float.TryParse(match.Value, out var result2))
			{
				return result2;
			}
			throw new FormatException("Invalid line spacing value: " + lineSpacingText);
		}
		throw new FormatException("Line spacing value is empty.");
	}
}
