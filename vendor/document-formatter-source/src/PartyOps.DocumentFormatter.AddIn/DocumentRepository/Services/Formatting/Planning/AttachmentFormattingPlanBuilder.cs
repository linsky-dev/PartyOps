using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;

namespace DocumentRepository.Services.Formatting.Planning;

internal static class AttachmentFormattingPlanBuilder
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static AttachmentFormattingPlan Build(DocumentElementList elements)
	{
		if (elements == null || elements.Items == null)
		{
			throw new InvalidOperationException("附件排版计划缺少分析层元素。");
		}
		AttachmentFormattingPlan attachmentFormattingPlan = new AttachmentFormattingPlan();
		foreach (DocumentElement item in elements.Items)
		{
			if (item != null && !item.IsEmpty)
			{
				if (item.RangeStart < 0 || item.RangeEnd <= item.RangeStart)
				{
					throw new InvalidOperationException("附件元素缺少有效文档位置：" + item.Text);
				}
				if (item.Type == ElementType.AttachmentMarker)
				{
					attachmentFormattingPlan.BodyMarkersDescending.Add(item);
				}
				else if (item.Type == ElementType.AttachmentListFirst || item.Type == ElementType.AttachmentListSingle || item.Type == ElementType.AttachmentListContinuation)
				{
					attachmentFormattingPlan.ListParagraphsDescending.Add(item);
				}
			}
		}
		SortDescending(attachmentFormattingPlan.BodyMarkersDescending);
		SortDescending(attachmentFormattingPlan.ListParagraphsDescending);
		EnsureUniqueStarts(attachmentFormattingPlan.BodyMarkersDescending, "附件正文标识");
		EnsureUniqueStarts(attachmentFormattingPlan.ListParagraphsDescending, "附件说明");
		return attachmentFormattingPlan;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void SortDescending(IList<DocumentElement> elements)
	{
		((elements as List<DocumentElement>) ?? throw new InvalidOperationException("附件排版计划集合类型无效。")).Sort((DocumentElement left, DocumentElement right) => right.RangeStart.CompareTo(left.RangeStart));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void EnsureUniqueStarts(IList<DocumentElement> elements, string label)
	{
		int num = -1;
		foreach (DocumentElement element in elements)
		{
			if (element.RangeStart != num)
			{
				num = element.RangeStart;
				continue;
			}
			throw new InvalidOperationException(label + "存在重复位置：" + element.RangeStart);
		}
	}
}
