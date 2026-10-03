using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Services.Hosting;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Interop;

public static class WordDocumentBoundary
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void EnsureDocumentPosition(Document document, int position, string context)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		Microsoft.Office.Interop.Word.Range value = null;
		try
		{
			value = document.Content;
			EnsurePosition(position, value.Start, value.End, context);
		}
		finally
		{
			ComObjectRelease.Release(ref value, "WordDocumentBoundary.Content");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void EnsurePosition(int position, int minimum, int maximum, string context)
	{
		if (minimum >= 0 && maximum >= minimum)
		{
			if (position < minimum || position > maximum)
			{
				throw new InvalidOperationException((string.IsNullOrWhiteSpace(context) ? "文档位置" : context) + "超出合法范围：位置=" + position + "，范围=" + minimum + "-" + maximum + "。");
			}
			return;
		}
		throw new InvalidOperationException("文档坐标边界无效：" + minimum + "-" + maximum + "。");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ClampPosition(int position, int minimum, int maximum)
	{
		if (minimum < 0 || maximum < minimum)
		{
			throw new InvalidOperationException("文档坐标边界无效：" + minimum + "-" + maximum + "。");
		}
		return Math.Max(minimum, Math.Min(position, maximum));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ResolveAnchorPosition(int start, int end, int contentStart, int contentEnd)
	{
		EnsurePosition(start, contentStart, contentEnd, "排版元素起点");
		EnsurePosition(end, contentStart, contentEnd, "排版元素终点");
		if (end <= start)
		{
			throw new InvalidOperationException("排版元素范围为空或倒置：" + start + "-" + end + "。");
		}
		return Math.Min(end - 1, start + 1);
	}
}
