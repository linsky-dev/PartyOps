using System;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DocumentRepository.Models.RedHeader;

namespace DocumentRepository.Services.RedHeader;

public static class RedHeaderTemplateValidator
{
	private static readonly Regex CopyNumberPattern = new Regex("^\\d{1,6}$", RegexOptions.Compiled);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Validate(RedHeaderTemplate template)
	{
		if (template == null)
		{
			throw new InvalidOperationException("红头模板不能为空。");
		}
		Required(template.Id, "模板编号");
		Required(template.Name, "模板名称");
		Required(template.HeaderText, "发文机关");
		Required(template.HeaderFont, "发文机关字体");
		Required(template.HeaderAlignment, "发文机关对齐方式");
		Positive(template.HeaderSize, "发文机关字号");
		Positive(template.HeaderLineSpacing, "发文机关行距");
		Percent(template.HeaderLayoutWidthPercent, "排布宽度比例");
		Percent(template.HeaderCharacterScalePercent, "字符缩放比例");
		Percent(template.HeaderMinimumScalePercent, "最低缩放比例");
		if (!(template.HeaderFitMode == "autoSingleLine") || !(template.HeaderMinimumScalePercent > template.HeaderCharacterScalePercent))
		{
			if (IsFitMode(template.HeaderFitMode))
			{
				if (template.HeaderFitMode == "autoSingleLine" && (template.HeaderText.IndexOf('\r') >= 0 || template.HeaderText.IndexOf('\n') >= 0))
				{
					throw new InvalidOperationException("自动适应一行不能用于主动换行的发文机关，请删除换行或改用手动模式。");
				}
				if (!string.IsNullOrWhiteSpace(template.DocumentNumberText))
				{
					Required(template.DocumentNumberFont, "发文字号字体");
					Positive(template.DocumentNumberSize, "发文字号字号");
					Positive(template.DocumentNumberLineSpacing, "发文字号行距");
				}
				Positive(template.RedLineWidthPercent, "红线宽度比例");
				Positive(template.RedLineThickness, "红线粗细");
				if (template.TitleGapLines >= 0)
				{
					Positive(template.TitleGapLineSpacing, "主标题空行行距");
					ValidateTopMarks(template.TopMarks);
					if (template.ImprintEnabled)
					{
						Required(template.ImprintFont, "版记字体");
						Required(template.ImprintSize, "版记字号");
						Required(template.ImprintOffice, "版记制发机关");
						if (template.ImprintDateMode == RedHeaderImprintDateMode.Manual)
						{
							Required(template.ImprintDate, "版记印发日期");
						}
					}
					return;
				}
				throw new InvalidOperationException("主标题空行数不能为负数。");
			}
			throw new InvalidOperationException("未知的发文机关单行适配方式：" + template.HeaderFitMode);
		}
		throw new InvalidOperationException("最低缩放比例不能大于字符缩放比例。");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string NormalizeCopyNumber(string value)
	{
		string text = (value ?? "").Trim();
		if (text.Length == 0)
		{
			return string.Empty;
		}
		if (!CopyNumberPattern.IsMatch(text))
		{
			throw new InvalidOperationException("公文份号必须为1至6位数字。");
		}
		return text.PadLeft(6, '0');
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ValidateTopMarks(RedHeaderTopMarkOptions options)
	{
		if (options == null)
		{
			throw new InvalidOperationException("版头附加标识参数不能为空。");
		}
		int num;
		if (options.CopyNumberEnabled)
		{
			num = ((!string.IsNullOrWhiteSpace(options.CopyNumber)) ? 1 : 0);
			if (num != 0)
			{
				NormalizeCopyNumber(options.CopyNumber);
			}
		}
		else
		{
			num = 0;
		}
		if (!IsSecurityLevel(options.SecurityLevel))
		{
			throw new InvalidOperationException("未知的公文密级：" + options.SecurityLevel);
		}
		if (!IsUrgencyLevel(options.UrgencyLevel))
		{
			throw new InvalidOperationException("未知的紧急程度：" + options.UrgencyLevel);
		}
		bool flag = !string.Equals(options.SecurityLevel, "无", StringComparison.Ordinal);
		if (!flag && !string.IsNullOrWhiteSpace(options.ConfidentialityPeriod))
		{
			throw new InvalidOperationException("填写保密期限前必须先选择公文密级。");
		}
		if (((uint)num | (flag ? 1u : 0u)) != 0 || !string.Equals(options.UrgencyLevel, "无", StringComparison.Ordinal))
		{
			Required(options.FontName, "版头附加标识字体");
			Positive(options.FontSize, "版头附加标识字号");
			Positive(options.LineSpacing, "版头附加标识行距");
			if (options.LeftIndentChars < 0f || !(options.LeftIndentChars <= 20f))
			{
				throw new InvalidOperationException("版头附加标识左缩进必须在0至20字符之间。");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool IsSecurityLevel(string value)
	{
		switch (value)
		{
		default:
			return value == "绝密";
		case "无":
		case "秘密":
		case "机密":
			return true;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool IsUrgencyLevel(string value)
	{
		if (!(value == "无") && !(value == "加急"))
		{
			return value == "特急";
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool IsFitMode(string value)
	{
		if (!(value == "manual") && !(value == "autoSingleLine"))
		{
			return value == "allowWrap";
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void Required(string value, string fieldName)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw new InvalidOperationException(fieldName + "不能为空。");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void Positive(float value, string fieldName)
	{
		if (value <= 0f)
		{
			throw new InvalidOperationException(fieldName + "必须大于0。");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void Percent(float value, string fieldName)
	{
		if (value < 20f || value > 200f)
		{
			throw new InvalidOperationException(fieldName + "必须在20%至200%之间。");
		}
	}
}
