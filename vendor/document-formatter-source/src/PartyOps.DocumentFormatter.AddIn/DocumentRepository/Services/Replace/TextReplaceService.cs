using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Models.Replace;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Replace;

public static class TextReplaceService
{
	public static int Apply(Microsoft.Office.Interop.Word.Range scope, ReplaceRule rule)
	{
		return Apply(scope, rule, 0, null);
	}

	public static int Apply(Microsoft.Office.Interop.Word.Range scope, ReplaceRule rule, int ruleIndex)
	{
		return Apply(scope, rule, ruleIndex, null);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int Apply(Microsoft.Office.Interop.Word.Range scope, ReplaceRule rule, int ruleIndex, Action confirmWriteOccurred)
	{
		if (rule != null && !string.IsNullOrWhiteSpace(rule.FindText))
		{
			string text = scope.Text ?? "";
			ReplaceStagePlan replaceStagePlan = ReplaceStagePlanner.Plan(ruleIndex, rule.Name, rule, text);
			ReplaceTextCoordinateMap replaceTextCoordinateMap = ReplaceTextCoordinateMap.Create(text, scope.End - scope.Start);
			int num = 0;
			for (int num2 = replaceStagePlan.Targets.Count - 1; num2 >= 0; num2--)
			{
				ReplaceStageTarget replaceStageTarget = replaceStagePlan.Targets[num2];
				replaceTextCoordinateMap.AssertSafeTarget(replaceStageTarget.Start, replaceStageTarget.End);
				int num3 = scope.Start + replaceTextCoordinateMap.ToRangeOffset(replaceStageTarget.Start);
				int num4 = scope.Start + replaceTextCoordinateMap.ToRangeOffset(replaceStageTarget.End);
				Microsoft.Office.Interop.Word.Range value = null;
				try
				{
					Document document = scope.Document;
					object Start = num3;
					object End = num4;
					value = document.Range(ref Start, ref End);
					if (rule.FindFormat == null || rule.FindFormat.IsEmpty || FormatMatchCheck(value, rule.FindFormat))
					{
						if (!string.Equals(value.Text, replaceStageTarget.OriginalText, StringComparison.Ordinal))
						{
							throw ReplaceOperationException.Create(ReplaceFailureReasonCode.TargetChanged, ReplaceFailureStage.ApplyRules);
						}
						if (rule.ReplaceText != null && !string.Equals(replaceStageTarget.OriginalText, replaceStageTarget.ReplacementText, StringComparison.Ordinal))
						{
							value.Text = replaceStageTarget.ReplacementText;
							confirmWriteOccurred?.Invoke();
							int num5 = num3 + replaceStageTarget.ReplacementText.Length;
							if (value != null)
							{
								ComObjectRelease.Release(ref value, "TextReplaceService.matchRange");
							}
							Document document2 = scope.Document;
							End = num3;
							Start = num5;
							value = document2.Range(ref End, ref Start);
						}
						FormatReplaceService.ApplyTargetFormat(value, rule.ReplaceFormat, confirmWriteOccurred);
						num++;
					}
				}
				finally
				{
					if (value != null)
					{
						ComObjectRelease.Release(ref value, "TextReplaceService.matchRange");
					}
				}
			}
			return num;
		}
		return 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static bool FormatMatchCheck(Microsoft.Office.Interop.Word.Range range, ReplaceFormatCondition f)
	{
		Font value = null;
		ParagraphFormat value2 = null;
		try
		{
			value = range.Font;
			value2 = range.ParagraphFormat;
			if (!Specified(f.FontName) || FontNameMatches(value, f.FontName))
			{
				if (!Specified(f.SizeText) || FloatNear(value.Size, FontSizeHelper.ToPoints(f.SizeText), 0.15f))
				{
					if (Specified(f.Bold) && !BooleanFormatMatches(f.Bold, value.Bold, "加粗", "不加粗"))
					{
						return false;
					}
					if (!Specified(f.Italic) || BooleanFormatMatches(f.Italic, value.Italic, "倾斜", "不倾斜"))
					{
						if (Specified(f.Underline) && !UnderlineMatches(f.Underline, value.Underline))
						{
							return false;
						}
						if (!Specified(f.Alignment) || AlignmentMatches(f.Alignment, value2.Alignment))
						{
							if (Specified(f.OutlineLevel) && !OutlineMatches(f.OutlineLevel, value2.OutlineLevel))
							{
								return false;
							}
							if (Specified(f.FirstIndentChars) && !FloatNear(value2.CharacterUnitFirstLineIndent, ParseFloat(f.FirstIndentChars), 0.05f))
							{
								return false;
							}
							if (Specified(f.SpaceBeforeLines) && !FloatNear(value2.LineUnitBefore, ParseFloat(f.SpaceBeforeLines), 0.05f))
							{
								return false;
							}
							if (!Specified(f.SpaceAfterLines) || FloatNear(value2.LineUnitAfter, ParseFloat(f.SpaceAfterLines), 0.05f))
							{
								if (Specified(f.LineSpacing) && !FloatNear(value2.LineSpacing, ParseFloat(f.LineSpacing), 0.15f))
								{
									return false;
								}
								return true;
							}
							return false;
						}
						return false;
					}
					return false;
				}
				return false;
			}
			return false;
		}
		catch (Exception ex)
		{
			LogService.Error("TextReplaceService.FormatMatchCheck", ex);
			return false;
		}
		finally
		{
			if (value2 != null)
			{
				ComObjectRelease.Release(ref value2, "TextReplaceService.paragraph");
			}
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "TextReplaceService.font");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool Specified(string v)
	{
		if (!string.IsNullOrWhiteSpace(v) && v.Trim() != "不限")
		{
			return v.Trim() != "不修改";
		}
		return false;
	}

	private static bool FontNameMatches(Font font, string expected)
	{
		string b = (expected ?? "").Trim();
		if (!string.Equals(font.NameFarEast, b, StringComparison.OrdinalIgnoreCase))
		{
			return string.Equals(font.Name, b, StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static bool BooleanFormatMatches(string configured, int actual, string trueText, string falseText)
	{
		string text = (configured ?? "").Trim();
		if (text == trueText)
		{
			return actual != 0;
		}
		if (text == falseText)
		{
			return actual == 0;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool UnderlineMatches(string configured, WdUnderline actual)
	{
		string text = (configured ?? "").Trim();
		if (!(text == "有下划线"))
		{
			if (text == "无下划线")
			{
				return actual == WdUnderline.wdUnderlineNone;
			}
			return false;
		}
		return actual != WdUnderline.wdUnderlineNone;
	}

	private static bool AlignmentMatches(string configured, WdParagraphAlignment actual)
	{
		WdParagraphAlignment? wdParagraphAlignment = ParseAlignment(configured);
		if (wdParagraphAlignment.HasValue)
		{
			return actual == wdParagraphAlignment.Value;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WdParagraphAlignment? ParseAlignment(string text)
	{
		return (text ?? "").Trim() switch
		{
			"右对齐" => WdParagraphAlignment.wdAlignParagraphRight, 
			"两端对齐" => WdParagraphAlignment.wdAlignParagraphJustify, 
			"居中" => WdParagraphAlignment.wdAlignParagraphCenter, 
			"分散对齐" => WdParagraphAlignment.wdAlignParagraphDistribute, 
			"左对齐" => WdParagraphAlignment.wdAlignParagraphLeft, 
			_ => null, 
		};
	}

	private static bool OutlineMatches(string configured, WdOutlineLevel actual)
	{
		WdOutlineLevel? wdOutlineLevel = ParseOutline(configured);
		if (wdOutlineLevel.HasValue)
		{
			return actual == wdOutlineLevel.Value;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WdOutlineLevel? ParseOutline(string text)
	{
		string text2 = (text ?? "").Trim();
		if (text2 != null)
		{
			int length = text2.Length;
			if (length == 2)
			{
				switch (text2[0])
				{
				case '8':
					if (!(text2 == "8级"))
					{
						break;
					}
					return WdOutlineLevel.wdOutlineLevel8;
				case '3':
					if (!(text2 == "3级"))
					{
						break;
					}
					return WdOutlineLevel.wdOutlineLevel3;
				case '7':
					if (text2 == "7级")
					{
						return WdOutlineLevel.wdOutlineLevel7;
					}
					break;
				case '9':
					if (text2 == "9级")
					{
						return WdOutlineLevel.wdOutlineLevel9;
					}
					break;
				case '6':
					if (text2 == "6级")
					{
						return WdOutlineLevel.wdOutlineLevel6;
					}
					break;
				case '5':
					if (text2 == "5级")
					{
						return WdOutlineLevel.wdOutlineLevel5;
					}
					break;
				case '4':
					if (text2 == "4级")
					{
						return WdOutlineLevel.wdOutlineLevel4;
					}
					break;
				case '正':
					if (text2 == "正文")
					{
						return WdOutlineLevel.wdOutlineLevelBodyText;
					}
					break;
				case '2':
					if (text2 == "2级")
					{
						return WdOutlineLevel.wdOutlineLevel2;
					}
					break;
				case '1':
					if (text2 == "1级")
					{
						return WdOutlineLevel.wdOutlineLevel1;
					}
					break;
				}
			}
		}
		return null;
	}

	private static float ParseFloat(string text)
	{
		if (!float.TryParse((text ?? "").Trim(), out var result))
		{
			throw ReplaceOperationException.Create(ReplaceFailureReasonCode.FormatConditionInvalid, ReplaceFailureStage.ApplyRules);
		}
		return result;
	}

	private static bool FloatNear(float actual, float expected, float tolerance)
	{
		return Math.Abs(actual - expected) <= tolerance;
	}
}
