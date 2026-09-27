using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using DocumentRepository.Services.Hosting;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Formatting.PageNumbers;

public sealed class PageNumberFacts
{
	public sealed class SectionFacts
	{
		public int SectionIndex { get; set; }

		public bool OddAndEvenPagesHeaderFooter { get; set; }

		public bool DifferentFirstPageHeaderFooter { get; set; }

		public float FooterDistance { get; set; }

		public Dictionary<WdHeaderFooterIndex, FooterFacts> Footers { get; }

		public FooterFacts FirstPageFooter { get; set; }

		public SectionFacts()
		{
			Footers = new Dictionary<WdHeaderFooterIndex, FooterFacts>();
		}
	}

	public sealed class FooterFacts
	{
		public WdHeaderFooterIndex Index { get; set; }

		public int PageFieldCount { get; set; }

		public int GeneratedParagraphCount { get; set; }

		public WdParagraphAlignment Alignment { get; set; }

		public float CharacterUnitLeftIndent { get; set; }

		public float CharacterUnitRightIndent { get; set; }

		public string VisibleText { get; set; }

		public string ResultText { get; set; }

		public string FontNameFarEast { get; set; }

		public string FontNameAscii { get; set; }

		public float FontSize { get; set; }

		public int FontBold { get; set; }
	}

	public string DocumentLifecycleId { get; private set; }

	public string ConfigHash { get; private set; }

	public int SectionCount { get; private set; }

	public bool IsReliable { get; private set; }

	public string UnreliableReason { get; private set; }

	public IList<SectionFacts> Sections { get; }

	public IList<WdHeaderFooterIndex> TargetFooterIndexes { get; }

	public bool OddAndEvenTarget { get; private set; }

	public bool HideFirstPageTarget { get; private set; }

	public int TotalPageFields { get; private set; }

	private PageNumberFacts()
	{
		Sections = new List<SectionFacts>();
		TargetFooterIndexes = new List<WdHeaderFooterIndex>();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static PageNumberFacts Capture(Document document, Application application, FormatConfig config)
	{
		PageNumberFacts pageNumberFacts = new PageNumberFacts();
		if (document == null || config == null)
		{
			pageNumberFacts.IsReliable = false;
			pageNumberFacts.UnreliableReason = "null argument";
			return pageNumberFacts;
		}
		try
		{
			pageNumberFacts.DocumentLifecycleId = DocumentLifecycleRegistry.GetOrCreate(document);
			pageNumberFacts.ConfigHash = ComputeConfigHash(config);
			pageNumberFacts.OddAndEvenTarget = config.PageAlign == PageAlignType.OddEvenDifferent;
			string mode = PageNumberModes.Normalize(config.PageNumberMode, config.EnablePageNumbers);
			pageNumberFacts.HideFirstPageTarget = PageNumberModes.IsFirstPageHidden(mode);
			pageNumberFacts.TargetFooterIndexes.Add(WdHeaderFooterIndex.wdHeaderFooterPrimary);
			if (pageNumberFacts.OddAndEvenTarget)
			{
				pageNumberFacts.TargetFooterIndexes.Add(WdHeaderFooterIndex.wdHeaderFooterEvenPages);
			}
			Sections sections = null;
			try
			{
				sections = document.Sections;
				pageNumberFacts.SectionCount = sections.Count;
				for (int i = 1; i <= sections.Count; i++)
				{
					Section section = null;
					PageSetup pageSetup = null;
					try
					{
						section = sections[i];
						pageSetup = section.PageSetup;
						SectionFacts sectionFacts = new SectionFacts
						{
							SectionIndex = i,
							OddAndEvenPagesHeaderFooter = (pageSetup.OddAndEvenPagesHeaderFooter != 0),
							DifferentFirstPageHeaderFooter = (pageSetup.DifferentFirstPageHeaderFooter != 0),
							FooterDistance = pageSetup.FooterDistance
						};
						foreach (WdHeaderFooterIndex targetFooterIndex in pageNumberFacts.TargetFooterIndexes)
						{
							FooterFacts footerFacts = CaptureFooterFacts(section, targetFooterIndex, application);
							sectionFacts.Footers.Add(targetFooterIndex, footerFacts);
							pageNumberFacts.TotalPageFields += footerFacts.PageFieldCount;
						}
						if (pageNumberFacts.HideFirstPageTarget && i == 1)
						{
							FooterFacts footerFacts2 = (sectionFacts.FirstPageFooter = CaptureFooterFacts(section, WdHeaderFooterIndex.wdHeaderFooterFirstPage, application));
							pageNumberFacts.TotalPageFields += footerFacts2.PageFieldCount;
						}
						pageNumberFacts.Sections.Add(sectionFacts);
					}
					finally
					{
						if (pageSetup != null)
						{
							ComObjectRelease.ReleaseOwned(pageSetup, "Capture", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\Services\\Formatting\\PageNumbers\\PageNumberFacts.cs", 92);
						}
						if (section != null)
						{
							ComObjectRelease.ReleaseOwned(section, "Capture", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\Services\\Formatting\\PageNumbers\\PageNumberFacts.cs", 93);
						}
					}
				}
			}
			finally
			{
				if (sections != null)
				{
					ComObjectRelease.ReleaseOwned(sections, "Capture", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\Services\\Formatting\\PageNumbers\\PageNumberFacts.cs", 99);
				}
			}
			pageNumberFacts.IsReliable = true;
		}
		catch (Exception ex)
		{
			pageNumberFacts.IsReliable = false;
			pageNumberFacts.UnreliableReason = ex.GetType().Name;
		}
		return pageNumberFacts;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static FooterFacts CaptureFooterFacts(Section section, WdHeaderFooterIndex index, Application application)
	{
		FooterFacts footerFacts = new FooterFacts
		{
			Index = index
		};
		HeaderFooter headerFooter = null;
		Microsoft.Office.Interop.Word.Range range = null;
		Fields fields = null;
		try
		{
			headerFooter = section.Footers[index];
			range = headerFooter.Range;
			fields = range.Fields;
			for (int i = 1; i <= fields.Count; i++)
			{
				Field field = null;
				try
				{
					field = fields[i];
					if (field.Type == WdFieldType.wdFieldPage)
					{
						footerFacts.PageFieldCount++;
						if (footerFacts.PageFieldCount == 1)
						{
							ReadFieldFacts(field, footerFacts, application);
						}
					}
				}
				finally
				{
					if (field != null)
					{
						ComObjectRelease.ReleaseOwned(field, "CaptureFooterFacts", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\Services\\Formatting\\PageNumbers\\PageNumberFacts.cs", 139);
					}
				}
			}
			footerFacts.GeneratedParagraphCount = CountGeneratedPageNumberParagraphs(range);
			return footerFacts;
		}
		finally
		{
			if (fields != null)
			{
				ComObjectRelease.ReleaseOwned(fields, "CaptureFooterFacts", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\Services\\Formatting\\PageNumbers\\PageNumberFacts.cs", 147);
			}
			if (range != null)
			{
				ComObjectRelease.ReleaseOwned(range, "CaptureFooterFacts", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\Services\\Formatting\\PageNumbers\\PageNumberFacts.cs", 148);
			}
			if (headerFooter != null)
			{
				ComObjectRelease.ReleaseOwned(headerFooter, "CaptureFooterFacts", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\Services\\Formatting\\PageNumbers\\PageNumberFacts.cs", 149);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ReadFieldFacts(Field field, FooterFacts footerFacts, Application application)
	{
		Microsoft.Office.Interop.Word.Range range = null;
		Paragraphs paragraphs = null;
		Paragraph paragraph = null;
		Microsoft.Office.Interop.Word.Range range2 = null;
		Font font = null;
		try
		{
			range = field.Result;
			paragraphs = range.Paragraphs;
			if (paragraphs.Count <= 0)
			{
				return;
			}
			paragraph = paragraphs[1];
			range2 = paragraph.Range;
			ParagraphFormat paragraphFormat = null;
			try
			{
				paragraphFormat = range2.ParagraphFormat;
				footerFacts.Alignment = paragraphFormat.Alignment;
				footerFacts.CharacterUnitLeftIndent = paragraphFormat.CharacterUnitLeftIndent;
				footerFacts.CharacterUnitRightIndent = paragraphFormat.CharacterUnitRightIndent;
			}
			finally
			{
				if (paragraphFormat != null)
				{
					ComObjectRelease.ReleaseOwned(paragraphFormat, "ReadFieldFacts", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\Services\\Formatting\\PageNumbers\\PageNumberFacts.cs", 178);
				}
			}
			Microsoft.Office.Interop.Word.Range range3 = null;
			try
			{
				range3 = range2.Duplicate;
				TrimParagraphEnd(range3);
				footerFacts.VisibleText = NormalizeVisibleText(range3.Text);
				footerFacts.ResultText = NormalizeVisibleText(field.Result.Text);
				font = range3.Font;
				footerFacts.FontNameFarEast = font.NameFarEast;
				footerFacts.FontNameAscii = font.NameAscii;
				footerFacts.FontSize = font.Size;
				footerFacts.FontBold = font.Bold;
			}
			finally
			{
				if (font != null)
				{
					ComObjectRelease.ReleaseOwned(font, "ReadFieldFacts", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\Services\\Formatting\\PageNumbers\\PageNumberFacts.cs", 197);
				}
				if (range3 != null)
				{
					ComObjectRelease.ReleaseOwned(range3, "ReadFieldFacts", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\Services\\Formatting\\PageNumbers\\PageNumberFacts.cs", 198);
				}
			}
		}
		finally
		{
			if (range2 != null)
			{
				ComObjectRelease.ReleaseOwned(range2, "ReadFieldFacts", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\Services\\Formatting\\PageNumbers\\PageNumberFacts.cs", 203);
			}
			if (paragraph != null)
			{
				ComObjectRelease.ReleaseOwned(paragraph, "ReadFieldFacts", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\Services\\Formatting\\PageNumbers\\PageNumberFacts.cs", 204);
			}
			if (paragraphs != null)
			{
				ComObjectRelease.ReleaseOwned(paragraphs, "ReadFieldFacts", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\Services\\Formatting\\PageNumbers\\PageNumberFacts.cs", 205);
			}
			if (range != null)
			{
				ComObjectRelease.ReleaseOwned(range, "ReadFieldFacts", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\Services\\Formatting\\PageNumbers\\PageNumberFacts.cs", 206);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int CountGeneratedPageNumberParagraphs(Microsoft.Office.Interop.Word.Range range)
	{
		Paragraphs paragraphs = null;
		try
		{
			paragraphs = range.Paragraphs;
			int num = 0;
			for (int i = 1; i <= paragraphs.Count; i++)
			{
				Paragraph paragraph = null;
				try
				{
					paragraph = paragraphs[i];
					if (string.Equals(DocumentStyleManager.GetParagraphStructuralStyleName(paragraph), "OfficialDoc.PageNumber", StringComparison.OrdinalIgnoreCase))
					{
						num++;
					}
				}
				finally
				{
					if (paragraph != null)
					{
						ComObjectRelease.ReleaseOwned(paragraph, "CountGeneratedPageNumberParagraphs", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\Services\\Formatting\\PageNumbers\\PageNumberFacts.cs", 229);
					}
				}
			}
			return num;
		}
		finally
		{
			if (paragraphs != null)
			{
				ComObjectRelease.ReleaseOwned(paragraphs, "CountGeneratedPageNumberParagraphs", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\Services\\Formatting\\PageNumbers\\PageNumberFacts.cs", 236);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void TrimParagraphEnd(Microsoft.Office.Interop.Word.Range range)
	{
		while (range.End > range.Start)
		{
			string text = range.Text ?? string.Empty;
			if (text.EndsWith("\r", StringComparison.Ordinal) || text.EndsWith("\a", StringComparison.Ordinal))
			{
				range.End--;
				continue;
			}
			break;
		}
	}

	private static string NormalizeVisibleText(string value)
	{
		if (!string.IsNullOrEmpty(value))
		{
			char[] array = new char[value.Length];
			int length = 0;
			foreach (char c in value)
			{
				if (c != '\r' && c != '\a' && !char.IsWhiteSpace(c))
				{
					array[length++] = c;
				}
			}
			return new string(array, 0, length);
		}
		return string.Empty;
	}

	private static string ComputeConfigHash(FormatConfig config)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append(config.PageNumberMode ?? "").Append('|');
		stringBuilder.Append(config.EnablePageNumbers).Append('|');
		stringBuilder.Append(config.PageAlign).Append('|');
		stringBuilder.Append(config.PageLeftWing ?? "").Append('|');
		stringBuilder.Append(config.PageRightWing ?? "").Append('|');
		stringBuilder.Append(config.FooterDistance).Append('|');
		stringBuilder.Append(config.PageNumberFontName ?? "").Append('|');
		stringBuilder.Append(config.PageFontSize ?? "").Append('|');
		stringBuilder.Append(config.PageNumberBold).Append('|');
		stringBuilder.Append(config.Body?.FontName ?? "");
		return stringBuilder.ToString();
	}
}
