using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Conversion;

namespace DocumentRepository.Services.Conversion;

public static class PageSelectionParser
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static PageSelectionResult Parse(PageSelectionMode mode, string rangeText, string selectedText, int pageCount)
	{
		if (pageCount <= 0)
		{
			return PageSelectionResult.Fail("未能获取当前文档页数，请先确认文档可以正常分页。", ConvertFailureReasonCode.PageCountUnavailable);
		}
		return mode switch
		{
			PageSelectionMode.All => PageSelectionResult.Ok(Enumerable.Range(1, pageCount)), 
			PageSelectionMode.Range => ParseRange(rangeText, pageCount), 
			_ => ParseSelected(selectedText, pageCount), 
		};
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static PageSelectionResult ParseRange(string text, int pageCount)
	{
		text = (text ?? "").Trim().Replace("－", "-").Replace("—", "-")
			.Replace("至", "-")
			.Replace("到", "-");
		string[] array = text.Split(new char[1] { '-' }, StringSplitOptions.RemoveEmptyEntries);
		if (array.Length != 2)
		{
			return PageSelectionResult.Fail("页码范围格式不正确，请输入类似 2-8 的格式。", ConvertFailureReasonCode.PageRangeFormatInvalid);
		}
		if (!int.TryParse(array[0].Trim(), out var result) || !int.TryParse(array[1].Trim(), out var result2))
		{
			return PageSelectionResult.Fail("页码范围必须是数字，请输入类似 2-8 的格式。", ConvertFailureReasonCode.PageRangeFormatInvalid);
		}
		if (result <= 0 || result2 <= 0 || result > result2)
		{
			return PageSelectionResult.Fail("页码范围不正确，起始页必须小于等于结束页。", ConvertFailureReasonCode.PageRangeOrderInvalid);
		}
		if (result2 > pageCount)
		{
			return PageSelectionResult.Fail("当前文档只有 " + pageCount + " 页，不能转换第 " + result2 + " 页。", ConvertFailureReasonCode.PageOutsideDocument);
		}
		return PageSelectionResult.Ok(Enumerable.Range(result, result2 - result + 1));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static PageSelectionResult ParseSelected(string text, int pageCount)
	{
		text = (text ?? "").Trim().Replace("，", ",").Replace("、", ",")
			.Replace("；", ",")
			.Replace(";", ",");
		if (!string.IsNullOrWhiteSpace(text))
		{
			List<int> list = new List<int>();
			HashSet<int> hashSet = new HashSet<int>();
			string[] array = text.Split(new char[1] { ',' }, StringSplitOptions.RemoveEmptyEntries);
			for (int i = 0; i < array.Length; i++)
			{
				if (int.TryParse(array[i].Trim(), out var result))
				{
					if (result <= 0)
					{
						return PageSelectionResult.Fail("页码必须从 1 开始。", ConvertFailureReasonCode.PageRangeOrderInvalid);
					}
					if (result > pageCount)
					{
						return PageSelectionResult.Fail("当前文档只有 " + pageCount + " 页，不能转换第 " + result + " 页。", ConvertFailureReasonCode.PageOutsideDocument);
					}
					if (hashSet.Add(result))
					{
						list.Add(result);
					}
					continue;
				}
				return PageSelectionResult.Fail("指定页码必须是数字，请输入类似 1,3,9 的格式。", ConvertFailureReasonCode.PageSelectionNotNumeric);
			}
			if (list.Count == 0)
			{
				return PageSelectionResult.Fail("请填写指定页码，例如 1,3,9。", ConvertFailureReasonCode.PageSelectionEmpty);
			}
			return PageSelectionResult.Ok(list);
		}
		return PageSelectionResult.Fail("请填写指定页码，例如 1,3,9。", ConvertFailureReasonCode.PageSelectionEmpty);
	}
}
