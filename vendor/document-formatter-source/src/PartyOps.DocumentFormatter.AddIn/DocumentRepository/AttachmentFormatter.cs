using System;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DocumentRepository.Models;
using DocumentRepository.Services.Analysis;
using DocumentRepository.Services.Formatting;
using DocumentRepository.Services.Formatting.Planning;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Interop;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository;

public static class AttachmentFormatter
{
	private static readonly Regex AttachmentMarkerRegex = new Regex("^附件\\s*[0-9０-９一二三四五六七八九十]*$", RegexOptions.Compiled);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void FormatDocument(Document doc, Application app, FormatConfig cfg, DocumentElementList elements)
	{
		if (doc != null)
		{
			if (app == null)
			{
				throw new ArgumentNullException("app");
			}
			if (cfg == null)
			{
				throw new ArgumentNullException("cfg");
			}
			if (elements == null || elements.Items == null)
			{
				throw new InvalidOperationException("附件排版必须使用分析层产出的 DocumentElementList。");
			}
			FormatParagraphs(doc, cfg, elements);
			return;
		}
		throw new ArgumentNullException("doc");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void FormatRange(Document doc, Application app, Microsoft.Office.Interop.Word.Range scopeRange, FormatConfig cfg, DocumentElementList elements)
	{
		if (doc == null)
		{
			throw new ArgumentNullException("doc");
		}
		if (app != null)
		{
			if (scopeRange != null)
			{
				if (cfg != null)
				{
					if (elements == null || elements.Items == null)
					{
						throw new InvalidOperationException("选区附件排版必须使用分析层产出的 DocumentElementList。");
					}
					FormatParagraphs(doc, cfg, elements);
					return;
				}
				throw new ArgumentNullException("cfg");
			}
			throw new ArgumentNullException("scopeRange");
		}
		throw new ArgumentNullException("app");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void FormatParagraphs(Document doc, FormatConfig cfg, DocumentElementList elements)
	{
		if (cfg == null)
		{
			throw new InvalidOperationException("当前排版参数缺失。");
		}
		AttachmentFormatOptions attachmentFormatOptions = cfg.AttachmentOptions ?? throw new FormatException("附件排版参数缺失。");
		AttachmentFormattingPlan attachmentFormattingPlan = AttachmentFormattingPlanBuilder.Build(elements);
		foreach (DocumentElement item in attachmentFormattingPlan.BodyMarkersDescending)
		{
			FormatAttachmentBodyMarker(doc, cfg, attachmentFormatOptions, elements, item);
		}
		if (!attachmentFormatOptions.FormatAttachmentList)
		{
			return;
		}
		foreach (DocumentElement item2 in attachmentFormattingPlan.ListParagraphsDescending)
		{
			FormatAttachmentListParagraph(doc, item2);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void FormatAttachmentBodyMarker(Document doc, FormatConfig cfg, AttachmentFormatOptions options, DocumentElementList elements, DocumentElement marker)
	{
		Paragraph value = null;
		Microsoft.Office.Interop.Word.Range value2 = null;
		try
		{
			value = ResolvePlannedParagraph(doc, marker);
			value2 = value.Range;
			string text = CleanText(value2.Text);
			if (!AttachmentMarkerRegex.IsMatch(text))
			{
				throw new InvalidOperationException("附件正文标识执行位置不匹配：" + text);
			}
			FormatAttachmentMarker(value);
			EnsurePageBreakBeforeAttachment(doc, value);
			EnsureTitleOnThirdLine(doc, value, cfg);
			TryFormatAttachmentTitleAfterMarker(doc, value, cfg, options, elements);
		}
		finally
		{
			if (value2 != null)
			{
				ComObjectRelease.Release(ref value2, "AttachmentFormatter.paraRange");
			}
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "AttachmentFormatter.para");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void FormatAttachmentListParagraph(Document doc, DocumentElement element)
	{
		Paragraph value = null;
		try
		{
			value = ResolvePlannedParagraph(doc, element);
			if (element.Type == ElementType.AttachmentListFirst || element.Type == ElementType.AttachmentListSingle)
			{
				EnsureBlankLineBefore(doc, value);
			}
			DocumentStyleManager.ApplyStyle(value, element.Type);
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "AttachmentFormatter.para");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static Paragraph ResolvePlannedParagraph(Document doc, DocumentElement element)
	{
		if (doc == null)
		{
			throw new ArgumentNullException("doc");
		}
		if (element == null)
		{
			throw new ArgumentNullException("element");
		}
		Microsoft.Office.Interop.Word.Range value = null;
		Microsoft.Office.Interop.Word.Range value2 = null;
		Paragraphs value3 = null;
		Paragraph value4 = null;
		try
		{
			value = doc.Content;
			if (element.RangeStart >= value.Start && element.RangeStart < value.End)
			{
				int num = Math.Min(value.End, element.RangeStart + 1);
				object Start = element.RangeStart;
				object End = num;
				value2 = doc.Range(ref Start, ref End);
				value3 = value2.Paragraphs;
				if (value3 == null || value3.Count < 1)
				{
					throw new InvalidOperationException("附件排版任务无法定位段落：" + element.RangeStart);
				}
				value4 = value3[1];
				Microsoft.Office.Interop.Word.Range value5 = null;
				try
				{
					value5 = value4.Range;
					string text = CleanText(value5.Text);
					string text2 = CleanText(element.Text);
					if (!string.Equals(text, text2, StringComparison.Ordinal))
					{
						throw new InvalidOperationException("附件排版任务文本发生漂移：计划=" + text2 + "，实际=" + text);
					}
				}
				finally
				{
					if (value5 != null)
					{
						ComObjectRelease.Release(ref value5, "AttachmentFormatter.paragraphRange");
					}
				}
				return value4;
			}
			throw new InvalidOperationException("附件排版任务位置超出当前文档：" + element.RangeStart);
		}
		catch
		{
			if (value4 != null)
			{
				ComObjectRelease.Release(ref value4, "AttachmentFormatter.paragraph");
			}
			throw;
		}
		finally
		{
			if (value3 != null)
			{
				ComObjectRelease.Release(ref value3, "AttachmentFormatter.paragraphs");
			}
			if (value2 != null)
			{
				ComObjectRelease.Release(ref value2, "AttachmentFormatter.probe");
			}
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "AttachmentFormatter.content");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ElementType GetElementType(DocumentElementList elements, Microsoft.Office.Interop.Word.Range range, int paragraphIndex, string text)
	{
		if (elements == null || elements.Items == null)
		{
			throw new InvalidOperationException("附件元素类型必须来自 DocumentElementList。");
		}
		int num = -1;
		int num2 = -1;
		if (range != null)
		{
			num = range.Start;
			num2 = range.End;
		}
		DocumentElement documentElement = null;
		string b = CleanText(text);
		foreach (DocumentElement item in elements.Items)
		{
			if (item != null && !item.IsEmpty)
			{
				if (paragraphIndex > 0 && item.ParagraphIndex == paragraphIndex && string.Equals(CleanText(item.Text), b, StringComparison.Ordinal))
				{
					return item.Type;
				}
				if (num >= 0 && item.RangeStart <= num && item.RangeEnd >= num)
				{
					return item.Type;
				}
				if (num >= 0 && item.RangeStart >= num && item.RangeStart <= num2)
				{
					return item.Type;
				}
				if (IsAttachmentTitleType(item.Type) && documentElement == null && string.Equals(CleanText(item.Text), b, StringComparison.Ordinal))
				{
					documentElement = item;
				}
			}
		}
		return documentElement?.Type ?? ElementType.Unknown;
	}

	private static bool IsAttachmentTitleType(ElementType type)
	{
		if (type != ElementType.AttachmentTitle)
		{
			return type == ElementType.AttachmentSubTitle;
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ElementType GetAttachmentTitleType(DocumentElementList elements, Microsoft.Office.Interop.Word.Range range, string text)
	{
		if (elements != null && elements.Items != null)
		{
			string b = CleanText(text);
			int num = range?.Start ?? (-1);
			DocumentElement documentElement = null;
			int num2 = int.MaxValue;
			foreach (DocumentElement item in elements.Items)
			{
				if (item != null && !item.IsEmpty && IsAttachmentTitleType(item.Type) && string.Equals(CleanText(item.Text), b, StringComparison.Ordinal))
				{
					int num3 = ((num >= 0) ? Math.Abs(item.RangeStart - num) : 0);
					if (documentElement == null || num3 < num2)
					{
						documentElement = item;
						num2 = num3;
					}
				}
			}
			return documentElement?.Type ?? GetElementType(elements, range, 0, text);
		}
		throw new InvalidOperationException("附件元素类型必须来自 DocumentElementList。");
	}

	private static string CleanText(string text)
	{
		return ParagraphIdentityTextNormalizer.Normalize(text);
	}

	private static void FormatAttachmentMarker(Paragraph para)
	{
		DocumentStyleManager.ApplyStyle(para, ElementType.AttachmentMarker);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool TryFormatAttachmentTitleAfterMarker(Document doc, Paragraph markerPara, FormatConfig cfg, AttachmentFormatOptions options, DocumentElementList elements)
	{
		if (doc == null)
		{
			throw new ArgumentNullException("doc");
		}
		if (markerPara != null)
		{
			if (options == null)
			{
				throw new ArgumentNullException("options");
			}
			if (elements == null)
			{
				throw new ArgumentNullException("elements");
			}
			if (options.FormatAttachmentBody)
			{
				Microsoft.Office.Interop.Word.Range value = null;
				Microsoft.Office.Interop.Word.Range value2 = null;
				Paragraphs value3 = null;
				bool flag = false;
				int paragraphStart = -1;
				int num = 0;
				int num2 = 0;
				bool flag2 = false;
				try
				{
					value = markerPara.Range;
					if (value.End >= doc.Content.End)
					{
						return false;
					}
					int end = value.End;
					int num3 = Math.Min(doc.Content.End, end + 3000);
					object Start = end;
					object End = num3;
					value2 = doc.Range(ref Start, ref End);
					value3 = value2.Paragraphs;
					int num4 = value3?.Count ?? 0;
					for (int i = 1; i <= num4; i++)
					{
						if (num2 >= 6)
						{
							break;
						}
						Paragraph value4 = null;
						Microsoft.Office.Interop.Word.Range value5 = null;
						try
						{
							value4 = value3[i];
							if (WordRangeInspector.IsInTable(value4))
							{
								continue;
							}
							value5 = value4.Range;
							string text = CleanText(value5.Text);
							if (string.IsNullOrEmpty(text))
							{
								continue;
							}
							if (AttachmentMarkerRegex.IsMatch(text))
							{
								break;
							}
							num2++;
							ElementType attachmentTitleType = GetAttachmentTitleType(elements, value5, text);
							if (num >= 3 || attachmentTitleType != ElementType.AttachmentTitle)
							{
								if (!flag || flag2 || attachmentTitleType != ElementType.AttachmentSubTitle)
								{
									break;
								}
								ClearParagraphAfter(doc, paragraphStart);
								DocumentStyleManager.ApplyStyle(value4, ElementType.AttachmentSubTitle);
								ApplyAttachmentTitlePagination(value4);
								flag2 = true;
							}
							else
							{
								ClearParagraphAfter(doc, paragraphStart);
								DocumentStyleManager.ApplyStyle(value4, ElementType.AttachmentTitle);
								ApplyAttachmentTitlePagination(value4);
								paragraphStart = value5.Start;
								num++;
								flag = true;
							}
						}
						finally
						{
							if (value5 != null)
							{
								ComObjectRelease.Release(ref value5, "AttachmentFormatter.range");
							}
							if (value4 != null)
							{
								ComObjectRelease.Release(ref value4, "AttachmentFormatter.para");
							}
						}
					}
				}
				finally
				{
					if (value3 != null)
					{
						ComObjectRelease.Release(ref value3, "AttachmentFormatter.scanParagraphs");
					}
					if (value2 != null)
					{
						ComObjectRelease.Release(ref value2, "AttachmentFormatter.scanRange");
					}
					if (value != null)
					{
						ComObjectRelease.Release(ref value, "AttachmentFormatter.markerRange");
					}
				}
				return flag;
			}
			return false;
		}
		throw new ArgumentNullException("markerPara");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ApplyAttachmentTitlePagination(Paragraph paragraph)
	{
		if (paragraph == null)
		{
			throw new ArgumentNullException("paragraph");
		}
		ParagraphFormat value = null;
		try
		{
			value = paragraph.Format;
			value.PageBreakBefore = 0;
			value.KeepTogether = 0;
			value.KeepWithNext = 0;
			value.WidowControl = 0;
		}
		finally
		{
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "AttachmentFormatter.titleParagraphFormat");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ClearParagraphAfter(Document doc, int paragraphStart)
	{
		if (doc == null)
		{
			throw new ArgumentNullException("doc");
		}
		if (paragraphStart < 0)
		{
			return;
		}
		Microsoft.Office.Interop.Word.Range value = null;
		ParagraphFormat value2 = null;
		try
		{
			object Start = paragraphStart;
			object End = paragraphStart;
			value = doc.Range(ref Start, ref End);
			Microsoft.Office.Interop.Word.Range range = value;
			End = WdUnits.wdParagraph;
			Start = 1;
			range.MoveEnd(ref End, ref Start);
			value2 = value.ParagraphFormat;
			value2.LineUnitAfter = 0f;
			value2.SpaceAfter = 0f;
		}
		finally
		{
			if (value2 != null)
			{
				ComObjectRelease.Release(ref value2, "AttachmentFormatter.pf");
			}
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "AttachmentFormatter.range");
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool EnsureTitleOnThirdLine(Document doc, Paragraph markerPara, FormatConfig cfg)
	{
		if (doc != null)
		{
			if (markerPara != null)
			{
				if (cfg != null)
				{
					Microsoft.Office.Interop.Word.Range value = null;
					Microsoft.Office.Interop.Word.Range value2 = null;
					try
					{
						value = markerPara.Range;
						if (value.End >= doc.Content.End)
						{
							return false;
						}
						object Start = value.End;
						object End = value.End;
						value2 = doc.Range(ref Start, ref End);
						Microsoft.Office.Interop.Word.Range range = value2;
						End = WdUnits.wdParagraph;
						Start = 1;
						range.MoveEnd(ref End, ref Start);
						if (string.IsNullOrEmpty(CleanText(value2.Text)))
						{
							return false;
						}
						value.InsertParagraphAfter();
						FormatInsertedBlankAfterMarker(doc, markerPara, cfg);
						return true;
					}
					finally
					{
						if (value != null)
						{
							ComObjectRelease.Release(ref value, "AttachmentFormatter.markerRange");
						}
						if (value2 != null)
						{
							ComObjectRelease.Release(ref value2, "AttachmentFormatter.nextRange");
						}
					}
				}
				throw new ArgumentNullException("cfg");
			}
			throw new ArgumentNullException("markerPara");
		}
		throw new ArgumentNullException("doc");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void FormatInsertedBlankAfterMarker(Document doc, Paragraph markerPara, FormatConfig cfg)
	{
		if (doc == null)
		{
			throw new ArgumentNullException("doc");
		}
		if (markerPara != null)
		{
			if (cfg == null)
			{
				throw new ArgumentNullException("cfg");
			}
			TextStyle textStyle = cfg.Body ?? throw new FormatException("正文参数缺失。");
			Microsoft.Office.Interop.Word.Range value = null;
			Microsoft.Office.Interop.Word.Range value2 = null;
			Paragraphs value3 = null;
			Paragraph value4 = null;
			ParagraphFormat value5 = null;
			try
			{
				value = markerPara.Range;
				object Start = value.End;
				object End = value.End;
				value2 = doc.Range(ref Start, ref End);
				Microsoft.Office.Interop.Word.Range range = value2;
				End = WdUnits.wdParagraph;
				Start = 1;
				range.MoveEnd(ref End, ref Start);
				value3 = value2.Paragraphs;
				if (value3.Count > 0)
				{
					value4 = value3[1];
					DocumentStyleManager.ApplyStyle(value4, ElementType.Body);
				}
				value5 = value2.ParagraphFormat;
				value5.Alignment = WdParagraphAlignment.wdAlignParagraphLeft;
				value5.LeftIndent = 0f;
				value5.RightIndent = 0f;
				value5.FirstLineIndent = 0f;
				value5.CharacterUnitLeftIndent = 0f;
				value5.CharacterUnitRightIndent = 0f;
				value5.CharacterUnitFirstLineIndent = 0f;
				value5.SpaceBefore = 0f;
				value5.SpaceAfter = 0f;
				value5.LineUnitBefore = 0f;
				value5.LineUnitAfter = 0f;
				value5.LineSpacingRule = WdLineSpacing.wdLineSpaceExactly;
				value5.LineSpacing = LineSpacingConverter.ToPoints(textStyle.LineSpacing);
				value5.PageBreakBefore = 0;
				value5.KeepTogether = 0;
				value5.KeepWithNext = 0;
				value5.WidowControl = 0;
				return;
			}
			finally
			{
				if (value5 != null)
				{
					ComObjectRelease.Release(ref value5, "AttachmentFormatter.pf");
				}
				if (value4 != null)
				{
					ComObjectRelease.Release(ref value4, "AttachmentFormatter.blankParagraph");
				}
				if (value3 != null)
				{
					ComObjectRelease.Release(ref value3, "AttachmentFormatter.paragraphs");
				}
				if (value2 != null)
				{
					ComObjectRelease.Release(ref value2, "AttachmentFormatter.range");
				}
				if (value != null)
				{
					ComObjectRelease.Release(ref value, "AttachmentFormatter.markerRange");
				}
			}
		}
		throw new ArgumentNullException("markerPara");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void EnsureBlankLineBefore(Document doc, Paragraph para)
	{
		if (doc != null)
		{
			if (para != null)
			{
				Microsoft.Office.Interop.Word.Range value = null;
				Microsoft.Office.Interop.Word.Range value2 = null;
				try
				{
					value = para.Range;
					Microsoft.Office.Interop.Word.Range range = value;
					object Unit = WdUnits.wdParagraph;
					object Count = 1;
					value2 = range.Previous(ref Unit, ref Count);
					if (value2 != null && !string.IsNullOrEmpty(CleanText(value2.Text)))
					{
						Microsoft.Office.Interop.Word.Range range2 = value;
						Count = WdCollapseDirection.wdCollapseStart;
						range2.Collapse(ref Count);
						value.InsertParagraphBefore();
					}
					return;
				}
				finally
				{
					if (value2 != null)
					{
						ComObjectRelease.Release(ref value2, "AttachmentFormatter.previousRange");
					}
					if (value != null)
					{
						ComObjectRelease.Release(ref value, "AttachmentFormatter.paraRange");
					}
				}
			}
			throw new ArgumentNullException("para");
		}
		throw new ArgumentNullException("doc");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void EnsurePageBreakBeforeAttachment(Document doc, Paragraph markerPara)
	{
		if (doc == null)
		{
			throw new ArgumentNullException("doc");
		}
		if (markerPara == null)
		{
			throw new ArgumentNullException("markerPara");
		}
		Microsoft.Office.Interop.Word.Range value = null;
		ParagraphFormat value2 = null;
		try
		{
			value = markerPara.Range;
			if (value.Start > doc.Content.Start)
			{
				value2 = markerPara.Format;
				value2.PageBreakBefore = -1;
				value2.KeepTogether = 0;
				value2.KeepWithNext = 0;
				value2.WidowControl = 0;
			}
		}
		finally
		{
			if (value2 != null)
			{
				ComObjectRelease.Release(ref value2, "AttachmentFormatter.markerParagraphFormat");
			}
			if (value != null)
			{
				ComObjectRelease.Release(ref value, "AttachmentFormatter.markerRange");
			}
		}
	}
}
