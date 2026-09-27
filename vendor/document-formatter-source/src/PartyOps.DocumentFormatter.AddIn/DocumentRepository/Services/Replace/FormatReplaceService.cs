using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Models.Replace;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Replace;

public static class FormatReplaceService
{
	public static int ApplyFormatOnly(Microsoft.Office.Interop.Word.Range scope, ReplaceRule rule)
	{
		return ApplyFormatOnly(scope, rule, null);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ApplyFormatOnly(Microsoft.Office.Interop.Word.Range scope, ReplaceRule rule, Action confirmWriteOccurred)
	{
		if (rule != null && !rule.ReplaceFormat.IsEmpty)
		{
			if (rule.FindFormat != null && !rule.FindFormat.IsEmpty)
			{
				Paragraphs value = null;
				int num = 0;
				try
				{
					value = scope.Paragraphs;
					int count = value.Count;
					for (int i = 1; i <= count; i++)
					{
						Paragraph value2 = null;
						Microsoft.Office.Interop.Word.Range value3 = null;
						try
						{
							value2 = value[i];
							value3 = value2.Range;
							if (TextReplaceService.FormatMatchCheck(value3, rule.FindFormat))
							{
								ApplyTargetFormat(value3, rule.ReplaceFormat, confirmWriteOccurred);
								num++;
							}
						}
						finally
						{
							if (value3 != null)
							{
								ComObjectRelease.Release(ref value3, "FormatReplaceService.range");
							}
							if (value2 != null)
							{
								ComObjectRelease.Release(ref value2, "FormatReplaceService.paragraph");
							}
						}
					}
					return num;
				}
				finally
				{
					if (value != null)
					{
						ComObjectRelease.Release(ref value, "FormatReplaceService.paragraphs");
					}
				}
			}
			throw ReplaceOperationException.Create(ReplaceFailureReasonCode.FormatConditionInvalid, ReplaceFailureStage.ApplyRules);
		}
		return 0;
	}

	public static void ApplyTargetFormat(Microsoft.Office.Interop.Word.Range range, ReplaceFormatTarget f)
	{
		ApplyTargetFormat(range, f, null);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ApplyTargetFormat(Microsoft.Office.Interop.Word.Range range, ReplaceFormatTarget f, Action confirmWriteOccurred)
	{
		if (f == null || f.IsEmpty)
		{
			return;
		}
		Font font = null;
		ParagraphFormat pf = null;
		try
		{
			font = range.Font;
			if (Specified(f.FontName))
			{
				SetIfChanged(font.NameFarEast, f.FontName, delegate(string value)
				{
					font.NameFarEast = value;
				}, confirmWriteOccurred);
				SetIfChanged(font.Name, f.FontName, delegate(string value)
				{
					font.Name = value;
				}, confirmWriteOccurred);
			}
			if (Specified(f.SizeText))
			{
				SetIfChanged(font.Size, FontSizeHelper.ToPoints(f.SizeText), delegate(float value)
				{
					font.Size = value;
				}, confirmWriteOccurred);
			}
			if (Specified(f.Bold))
			{
				SetIfChanged(font.Bold, (f.Bold == "加粗") ? 1 : 0, delegate(int value)
				{
					font.Bold = value;
				}, confirmWriteOccurred);
			}
			if (Specified(f.Italic))
			{
				SetIfChanged(font.Italic, (f.Italic == "倾斜") ? 1 : 0, delegate(int value)
				{
					font.Italic = value;
				}, confirmWriteOccurred);
			}
			if (Specified(f.Underline))
			{
				SetIfChanged(font.Underline, (f.Underline == "有下划线") ? WdUnderline.wdUnderlineSingle : WdUnderline.wdUnderlineNone, delegate(WdUnderline value)
				{
					font.Underline = value;
				}, confirmWriteOccurred);
			}
			pf = range.ParagraphFormat;
			if (Specified(f.Alignment))
			{
				SetIfChanged(pf.Alignment, ParseAlignment(f.Alignment), delegate(WdParagraphAlignment value)
				{
					pf.Alignment = value;
				}, confirmWriteOccurred);
			}
			if (Specified(f.OutlineLevel))
			{
				SetIfChanged(pf.OutlineLevel, ParseOutline(f.OutlineLevel), delegate(WdOutlineLevel value)
				{
					pf.OutlineLevel = value;
				}, confirmWriteOccurred);
			}
			if (Specified(f.FirstIndentChars))
			{
				SetIfChanged(pf.CharacterUnitFirstLineIndent, ParseFloat(f.FirstIndentChars, 0f), delegate(float value)
				{
					pf.CharacterUnitFirstLineIndent = value;
				}, confirmWriteOccurred);
			}
			if (Specified(f.SpaceBeforeLines))
			{
				SetIfChanged(pf.LineUnitBefore, ParseFloat(f.SpaceBeforeLines, 0f), delegate(float value)
				{
					pf.LineUnitBefore = value;
				}, confirmWriteOccurred);
			}
			if (Specified(f.SpaceAfterLines))
			{
				SetIfChanged(pf.LineUnitAfter, ParseFloat(f.SpaceAfterLines, 0f), delegate(float value)
				{
					pf.LineUnitAfter = value;
				}, confirmWriteOccurred);
			}
			if (Specified(f.LineSpacing))
			{
				SetIfChanged(pf.LineSpacingRule, WdLineSpacing.wdLineSpaceExactly, delegate(WdLineSpacing value)
				{
					pf.LineSpacingRule = value;
				}, confirmWriteOccurred);
				SetIfChanged(pf.LineSpacing, ParseFloat(f.LineSpacing, 28f), delegate(float value)
				{
					pf.LineSpacing = value;
				}, confirmWriteOccurred);
			}
		}
		catch (Exception ex)
		{
			LogService.Error("FormatReplaceService.ApplyTargetFormat", ex);
		}
		finally
		{
			if (pf != null)
			{
				ComObjectRelease.Release(ref pf, "FormatReplaceService.pf");
			}
			if (font != null)
			{
				ComObjectRelease.Release(ref font, "FormatReplaceService.font");
			}
		}
	}

	private static void SetIfChanged<T>(T current, T target, Action<T> setter, Action confirmWriteOccurred)
	{
		if (!EqualityComparer<T>.Default.Equals(current, target))
		{
			setter(target);
			confirmWriteOccurred?.Invoke();
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

	private static float ParseFloat(string text, float fallback)
	{
		if (!float.TryParse((text ?? "").Trim(), out var result))
		{
			return fallback;
		}
		return result;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WdParagraphAlignment ParseAlignment(string text)
	{
		return (text ?? "").Trim() switch
		{
			"居中" => WdParagraphAlignment.wdAlignParagraphCenter, 
			"两端对齐" => WdParagraphAlignment.wdAlignParagraphJustify, 
			"右对齐" => WdParagraphAlignment.wdAlignParagraphRight, 
			_ => WdParagraphAlignment.wdAlignParagraphLeft, 
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WdOutlineLevel ParseOutline(string text)
	{
		return (text ?? "").Trim() switch
		{
			"1级" => WdOutlineLevel.wdOutlineLevel1, 
			"7级" => WdOutlineLevel.wdOutlineLevel7, 
			"5级" => WdOutlineLevel.wdOutlineLevel5, 
			"2级" => WdOutlineLevel.wdOutlineLevel2, 
			"6级" => WdOutlineLevel.wdOutlineLevel6, 
			"4级" => WdOutlineLevel.wdOutlineLevel4, 
			"9级" => WdOutlineLevel.wdOutlineLevel9, 
			"8级" => WdOutlineLevel.wdOutlineLevel8, 
			"3级" => WdOutlineLevel.wdOutlineLevel3, 
			_ => WdOutlineLevel.wdOutlineLevelBodyText, 
		};
	}
}
