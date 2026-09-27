using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DocumentRepository.Models;

namespace DocumentRepository.Services.Detection;

public static class HeadingLevelDetector
{
	private const string ChineseNumberPattern = "[一二三四五六七八九十〇零百千万亿]+";

	public static bool IsLevel1Title(string text, FormatConfig cfg)
	{
		return MatchesRecognitionStyle(text, cfg?.Level1?.RecognitionStyle);
	}

	public static bool IsLevel2Title(string text, FormatConfig cfg)
	{
		return MatchesRecognitionStyle(text, cfg?.Level2?.RecognitionStyle);
	}

	public static bool IsLevel3Title(string text, FormatConfig cfg)
	{
		return MatchesRecognitionStyle(text, cfg?.Level3?.RecognitionStyle);
	}

	public static bool IsConfiguredHeading(string text, FormatConfig cfg)
	{
		if (!IsLevel1Title(text, cfg) && !IsLevel2Title(text, cfg))
		{
			return IsLevel3Title(text, cfg);
		}
		return true;
	}

	public static ElementType DetectConfiguredHeading(string text, FormatConfig cfg)
	{
		if (!IsLevel1Title(text, cfg))
		{
			if (!IsLevel2Title(text, cfg))
			{
				if (IsLevel3Title(text, cfg))
				{
					return ElementType.Level3Title;
				}
				return ElementType.Unknown;
			}
			return ElementType.Level2Title;
		}
		return ElementType.Level1Title;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool MatchesRecognitionStyle(string text, string style)
	{
		if (!string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(style))
		{
			text = text.Trim();
			return style.Trim() switch
			{
				"第一条  XX" => Regex.IsMatch(text, "^\\s*第([一二三四五六七八九十〇零百千万亿]+|[1-9][0-9]{0,2})条\\s*\\S"), 
				"1.1.1 XX" => Regex.IsMatch(text, "^\\s*[1-9][0-9]{0,2}([\\．.][1-9][0-9]{0,2}){2}(?![0-9])\\s*[^\\s0-9\\.．]"), 
				"第一部分  XX" => Regex.IsMatch(text, "^\\s*第([一二三四五六七八九十〇零百千万亿]+|[1-9][0-9]{0,2})部分\\s*\\S"), 
				"（1）XX" => Regex.IsMatch(text, "^\\s*[\\(（]\\s*[1-9][0-9]{0,2}\\s*[\\)）]"), 
				"①XX" => Regex.IsMatch(text, "^\\s*[①②③④⑤⑥⑦⑧⑨⑩]"), 
				"1.1.1.1.1 XX" => Regex.IsMatch(text, "^\\s*[1-9][0-9]{0,2}([\\．.][1-9][0-9]{0,2}){4}(?![0-9])\\s*[^\\s0-9\\.．]"), 
				"第一篇  XX" => Regex.IsMatch(text, "^\\s*第([一二三四五六七八九十〇零百千万亿]+|[1-9][0-9]{0,2})篇\\s*\\S"), 
				"一、XX" => Regex.IsMatch(text, "^\\s*[一二三四五六七八九十〇零百千万亿]+[、\\.．]"), 
				"1）XX" => Regex.IsMatch(text, "^\\s*[1-9][0-9]{0,2}\\s*[\\)）]"), 
				"1.1.1.1 XX" => Regex.IsMatch(text, "^\\s*[1-9][0-9]{0,2}([\\．.][1-9][0-9]{0,2}){3}(?![0-9])\\s*[^\\s0-9\\.．]"), 
				"1.XX" => Regex.IsMatch(text, "^\\s*[1-9][0-9]{0,2}(?![0-9])[\\．.、]"), 
				"1.1 XX" => Regex.IsMatch(text, "^\\s*[1-9][0-9]{0,2}[\\．.][1-9][0-9]{0,2}(?![0-9])\\s*[^\\s0-9\\.．]"), 
				"第一章  XX" => Regex.IsMatch(text, "^\\s*第([一二三四五六七八九十〇零百千万亿]+|[1-9][0-9]{0,2})章\\s*\\S"), 
				"第一节  XX" => Regex.IsMatch(text, "^\\s*第([一二三四五六七八九十〇零百千万亿]+|[1-9][0-9]{0,2})节\\s*\\S"), 
				"（一）XX" => Regex.IsMatch(text, "^\\s*[\\(（]\\s*[一二三四五六七八九十〇零百千万亿]+\\s*[\\)）]"), 
				_ => false, 
			};
		}
		return false;
	}
}
