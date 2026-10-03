using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Services.Detection;

namespace DocumentRepository.Services.Analysis;

public static class SignatureBlockAnalysisService
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Detect(FormatContext fctx)
	{
		if (fctx == null)
		{
			throw new ArgumentNullException("fctx");
		}
		List<SignatureBlock> list = (fctx.SignatureBlocks = Detect(fctx.Elements, fctx.Config, fctx.IsSelectionMode));
		if (list.Count <= 0)
		{
			fctx.SignatureIndex = -1;
			fctx.DateIndex = -1;
		}
		else
		{
			fctx.SignatureIndex = list[0].SignatureParagraphIndex;
			fctx.DateIndex = list[0].DateParagraphIndex;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static List<SignatureBlock> Detect(DocumentElementList elements, FormatConfig cfg, bool isSelection)
	{
		List<SignatureBlock> list = new List<SignatureBlock>();
		if (elements == null || elements.Items == null || elements.Items.Count == 0)
		{
			throw new InvalidOperationException("落款识别必须使用分析层产出的 DocumentElementList。");
		}
		if (!isSelection)
		{
			SignatureBlock signatureBlock = DetectLastDocumentBlock(elements, cfg);
			if (signatureBlock != null && signatureBlock.IsValid)
			{
				list.Add(signatureBlock);
			}
			return list;
		}
		AddSelectionBlocks(elements, cfg, list);
		return list;
	}

	private static SignatureBlock DetectLastDocumentBlock(DocumentElementList elements, FormatConfig cfg)
	{
		int num = FindDocumentSearchEnd(elements);
		if (num > 0)
		{
			int num2 = Math.Max(1, num - 30);
			DocumentElement documentElement = null;
			int num3 = num;
			while (num3 >= num2)
			{
				DocumentElement documentElement2 = FindByParagraphIndex(elements, num3);
				if (!CanParticipateAsDate(documentElement2) || !SignatureDetector.IsDateLine(documentElement2.Text))
				{
					num3--;
					continue;
				}
				documentElement = documentElement2;
				break;
			}
			if (documentElement != null)
			{
				int num4 = Math.Max(1, documentElement.ParagraphIndex - 6);
				List<DocumentElement> list = new List<DocumentElement>();
				for (int num5 = documentElement.ParagraphIndex - 1; num5 >= num4; num5--)
				{
					DocumentElement documentElement3 = FindByParagraphIndex(elements, num5);
					if (documentElement3 == null)
					{
						break;
					}
					if (!documentElement3.IsEmpty)
					{
						if (!CanParticipateAsSignature(documentElement3) || !ParagraphElementClassifier.IsSelectedSignatureLine(documentElement3.Text, cfg))
						{
							break;
						}
						if (list.Count != 0)
						{
							if (IsStrongSignatureLine(list[0]) && IsStrongSignatureLine(documentElement3))
							{
								list.Add(documentElement3);
							}
							break;
						}
						list.Add(documentElement3);
					}
				}
				if (list.Count == 0)
				{
					return null;
				}
				list.Reverse();
				return CreateBlockAndMark(list[0], documentElement, list, isSelection: false);
			}
			return null;
		}
		return null;
	}

	private static bool IsStrongSignatureLine(DocumentElement item)
	{
		if (item == null || string.IsNullOrWhiteSpace(item.Text))
		{
			return false;
		}
		return SignatureDetector.HasOrgSuffix(item.Text.Trim());
	}

	private static void AddSelectionBlocks(DocumentElementList elements, FormatConfig cfg, List<SignatureBlock> result)
	{
		for (int i = 0; i < elements.Items.Count; i++)
		{
			DocumentElement documentElement = elements.Items[i];
			if (!CanParticipateAsDate(documentElement) || !SignatureDetector.IsDateLine(documentElement.Text))
			{
				continue;
			}
			List<DocumentElement> list = new List<DocumentElement>();
			for (int num = i - 1; num >= 0; num--)
			{
				DocumentElement documentElement2 = elements.Items[num];
				if (documentElement2 == null)
				{
					break;
				}
				if (!documentElement2.IsEmpty)
				{
					if (!CanParticipateAsSignature(documentElement2) || !ParagraphElementClassifier.IsSelectedSignatureLine(documentElement2.Text, cfg))
					{
						break;
					}
					if (list.Count != 0)
					{
						if (IsStrongSignatureLine(list[0]) && IsStrongSignatureLine(documentElement2))
						{
							list.Add(documentElement2);
						}
						break;
					}
					list.Add(documentElement2);
				}
			}
			if (list.Count != 0)
			{
				list.Reverse();
				result.Add(CreateBlockAndMark(list[0], documentElement, list, isSelection: true));
			}
		}
	}

	private static int FindDocumentSearchEnd(DocumentElementList elements)
	{
		int result = ((elements.ParagraphCount > 0) ? elements.ParagraphCount : elements.Items.Count);
		foreach (DocumentElement item in elements.Items)
		{
			if (item == null || item.Type != ElementType.AttachmentMarker)
			{
				continue;
			}
			return Math.Max(1, item.ParagraphIndex - 1);
		}
		return result;
	}

	private static DocumentElement FindByParagraphIndex(DocumentElementList elements, int paragraphIndex)
	{
		foreach (DocumentElement item in elements.Items)
		{
			if (item != null && item.ParagraphIndex == paragraphIndex)
			{
				return item;
			}
		}
		return null;
	}

	private static bool CanParticipateAsText(DocumentElement item)
	{
		if (item == null || item.IsEmpty || item.IsInTable || item.HasInlineShape || item.HasShape)
		{
			return false;
		}
		return true;
	}

	private static bool CanParticipateAsDate(DocumentElement item)
	{
		if (CanParticipateAsText(item))
		{
			ElementType type = item.Type;
			if ((uint)(type - 10) <= 6u || (uint)(type - 18) <= 1u)
			{
				return false;
			}
			return true;
		}
		return false;
	}

	private static bool CanParticipateAsSignature(DocumentElement item)
	{
		if (!CanParticipateAsText(item))
		{
			return false;
		}
		switch (item.Type)
		{
		case ElementType.Salutation:
		case ElementType.AttachmentMarker:
		case ElementType.AttachmentTitle:
		case ElementType.AttachmentSubTitle:
		case ElementType.AttachmentListFirst:
		case ElementType.AttachmentListSingle:
		case ElementType.AttachmentListContinuation:
		case ElementType.Table:
		case ElementType.Image:
		case ElementType.DocumentNumber:
			return false;
		default:
			return true;
		}
	}

	private static SignatureBlock CreateBlockAndMark(DocumentElement signature, DocumentElement date, bool isSelection)
	{
		return CreateBlockAndMark(signature, date, new List<DocumentElement> { signature }, isSelection);
	}

	private static SignatureBlock CreateBlockAndMark(DocumentElement signature, DocumentElement date, IList<DocumentElement> signatureRun, bool isSelection)
	{
		foreach (DocumentElement item in signatureRun)
		{
			if (item != null)
			{
				item.Type = ElementType.Signature;
			}
		}
		date.Type = ElementType.SignatureDate;
		int rangeStart = signatureRun[0].RangeStart;
		int rangeEnd = signatureRun[signatureRun.Count - 1].RangeEnd;
		SignatureBlock signatureBlock = new SignatureBlock
		{
			SignatureParagraphIndex = signature.ParagraphIndex,
			DateParagraphIndex = date.ParagraphIndex,
			SignatureRangeStart = rangeStart,
			SignatureRangeEnd = rangeEnd,
			DateRangeStart = date.RangeStart,
			DateRangeEnd = date.RangeEnd,
			SignatureText = signature.Text,
			DateText = date.Text,
			IsSelection = isSelection
		};
		List<SignatureLine> list = new List<SignatureLine>();
		foreach (DocumentElement item2 in signatureRun)
		{
			if (item2 != null)
			{
				list.Add(new SignatureLine
				{
					ParagraphIndex = item2.ParagraphIndex,
					RangeStart = item2.RangeStart,
					RangeEnd = item2.RangeEnd,
					Text = item2.Text
				});
			}
		}
		signatureBlock.SetSignatureLines(list);
		return signatureBlock;
	}
}
