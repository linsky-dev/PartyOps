using System;
using System.Runtime.CompilerServices;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Interop;

public static class WordPageOrdinalService
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ReadPhysicalPageOrdinal(Microsoft.Office.Interop.Word.Range range, string context)
	{
		if (range == null)
		{
			throw new ArgumentNullException("range");
		}
		try
		{
			int num = Convert.ToInt32((dynamic)range.get_Information(WdInformation.wdActiveEndPageNumber));
			if (num < 1)
			{
				throw new InvalidOperationException("读取到无效的物理页序号：" + num + "。");
			}
			return num;
		}
		catch (Exception innerException)
		{
			throw new InvalidOperationException((string.IsNullOrWhiteSpace(context) ? "文档位置" : context) + "无法读取物理页序号。", innerException);
		}
	}
}
