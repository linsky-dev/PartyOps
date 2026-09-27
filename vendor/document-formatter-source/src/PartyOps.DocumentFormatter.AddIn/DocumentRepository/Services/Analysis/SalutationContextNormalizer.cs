using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;

namespace DocumentRepository.Services.Analysis;

internal static class SalutationContextNormalizer
{
	private const int CandidateWindowSize = 3;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Normalize(IList<DocumentElement> elements, ElementType[] types)
	{
		if (elements != null)
		{
			if (types == null)
			{
				throw new ArgumentNullException("types");
			}
			if (elements.Count != types.Length)
			{
				throw new ArgumentException("段落元素与类型序列长度不一致。", "types");
			}
			bool[] array = new bool[types.Length];
			MarkOpeningTitleWindow(elements, types, array);
			MarkAttachmentTitleWindows(elements, types, array);
			for (int i = 0; i < types.Length; i++)
			{
				if (types[i] == ElementType.Salutation && !array[i])
				{
					types[i] = ElementType.Body;
				}
			}
			return;
		}
		throw new ArgumentNullException("elements");
	}

	private static void MarkOpeningTitleWindow(IList<DocumentElement> elements, ElementType[] types, bool[] allowed)
	{
		int num = -1;
		for (int i = 0; i < types.Length; i++)
		{
			if (!IsOpeningTitle(types[i]))
			{
				if (IsEffectiveParagraph(elements[i], types[i]) && types[i] != ElementType.DocumentNumber)
				{
					return;
				}
				continue;
			}
			num = i;
			break;
		}
		if (num >= 0)
		{
			int num2 = FindTitleBlockEnd(elements, types, num, IsOpeningTitle);
			MarkFollowingEffectiveParagraphs(elements, types, allowed, num2 + 1);
		}
	}

	private static void MarkAttachmentTitleWindows(IList<DocumentElement> elements, ElementType[] types, bool[] allowed)
	{
		int num = 0;
		while (num < types.Length)
		{
			if (IsAttachmentTitle(types[num]))
			{
				int num2 = FindTitleBlockEnd(elements, types, num, IsAttachmentTitle);
				MarkFollowingEffectiveParagraphs(elements, types, allowed, num2 + 1);
				num = num2 + 1;
			}
			else
			{
				num++;
			}
		}
	}

	private static int FindTitleBlockEnd(IList<DocumentElement> elements, ElementType[] types, int start, Func<ElementType, bool> isTitle)
	{
		int result = start;
		for (int i = start + 1; i < types.Length; i++)
		{
			if (isTitle(types[i]))
			{
				result = i;
			}
			else if (IsEffectiveParagraph(elements[i], types[i]))
			{
				break;
			}
		}
		return result;
	}

	private static void MarkFollowingEffectiveParagraphs(IList<DocumentElement> elements, ElementType[] types, bool[] allowed, int start)
	{
		int num = 0;
		for (int i = start; i < types.Length; i++)
		{
			if (num >= 3)
			{
				break;
			}
			if (IsEffectiveParagraph(elements[i], types[i]))
			{
				if (IsOpeningTitle(types[i]) || IsAttachmentTitle(types[i]))
				{
					break;
				}
				allowed[i] = true;
				num++;
			}
		}
	}

	private static bool IsEffectiveParagraph(DocumentElement element, ElementType type)
	{
		if (element != null && !element.IsEmpty)
		{
			return type != ElementType.Unknown;
		}
		return false;
	}

	private static bool IsOpeningTitle(ElementType type)
	{
		if (type != ElementType.MainTitle)
		{
			return type == ElementType.SubTitle;
		}
		return true;
	}

	private static bool IsAttachmentTitle(ElementType type)
	{
		if (type != ElementType.AttachmentTitle)
		{
			return type == ElementType.AttachmentSubTitle;
		}
		return true;
	}
}
