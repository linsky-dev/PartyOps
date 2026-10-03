using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Services.Conversion.PdfToWord;

public static class PdfPageTypeClassifier
{
	public const int TextCharacterThreshold = 20;

	public const int AnyCharacterThreshold = 5;

	public const float ImageCoverageThreshold = 0.3f;

	public const int VectorPathDensityThreshold = 50;

	public const float GarbledRatioThreshold = 0.5f;

	public static PdfTypeClassificationResult Classify(IList<PdfPageContent> pages)
	{
		PdfTypeClassificationResult pdfTypeClassificationResult = new PdfTypeClassificationResult();
		if (pages == null)
		{
			return pdfTypeClassificationResult;
		}
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		foreach (PdfPageContent page in pages)
		{
			PdfPageClassification pdfPageClassification = ClassifyPage(page);
			pdfTypeClassificationResult.Pages.Add(pdfPageClassification);
			if (pdfPageClassification.Type == PdfPageType.TextBased)
			{
				num++;
				continue;
			}
			num2++;
			pdfTypeClassificationResult.PagesNeedingOcr.Add(page.PageIndex + 1);
			if (pdfPageClassification.Type == PdfPageType.ImageOnly)
			{
				num3++;
			}
		}
		if (num2 != 0)
		{
			if (num == 0)
			{
				pdfTypeClassificationResult.DocumentKind = ((num3 != num2) ? PdfDocumentKind.Scanned : PdfDocumentKind.ImageOnly);
			}
			else
			{
				pdfTypeClassificationResult.DocumentKind = PdfDocumentKind.Mixed;
			}
		}
		else
		{
			pdfTypeClassificationResult.DocumentKind = PdfDocumentKind.TextBased;
		}
		return pdfTypeClassificationResult;
	}

	public static PdfTypeClassificationResult ClassifySampled(IList<PdfPageContent> pages, int maxSamples)
	{
		if (pages == null || maxSamples <= 0 || pages.Count <= maxSamples)
		{
			return Classify(pages);
		}
		List<PdfPageContent> list = new List<PdfPageContent>();
		double num = (double)pages.Count / (double)maxSamples;
		for (int i = 0; i < maxSamples; i++)
		{
			list.Add(pages[(int)((double)i * num)]);
		}
		if (!list.Contains(pages[pages.Count - 1]))
		{
			list[list.Count - 1] = pages[pages.Count - 1];
		}
		return Classify(list);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static PdfPageClassification ClassifyPage(PdfPageContent page)
	{
		int meaningfulCharacterCount = page.MeaningfulCharacterCount;
		float num = ComputeImageCoverage(page);
		float num2 = ((meaningfulCharacterCount > 0) ? ComputeGarbledRatio(page) : 0f);
		PdfPageClassification pdfPageClassification = new PdfPageClassification
		{
			PageIndex = page.PageIndex,
			CharacterCount = meaningfulCharacterCount,
			ImageCount = page.Images.Count,
			ImageCoverageRatio = num,
			VectorPathCount = page.VectorPathCount,
			GarbledRatio = num2
		};
		if (meaningfulCharacterCount < 5)
		{
			if (meaningfulCharacterCount < 5 && num > 0.3f)
			{
				pdfPageClassification.Type = PdfPageType.ImageOnly;
				pdfPageClassification.ReasonCode = "image-cover";
			}
			else if (meaningfulCharacterCount >= 5 || page.VectorPathCount <= 50)
			{
				pdfPageClassification.Type = PdfPageType.NoText;
				pdfPageClassification.ReasonCode = ((meaningfulCharacterCount > 0) ? "too-few-chars" : "no-text");
			}
			else
			{
				pdfPageClassification.Type = PdfPageType.VectorOutlinedText;
				pdfPageClassification.ReasonCode = "vector-outlines";
			}
		}
		else if (!(num2 <= 0.5f))
		{
			pdfPageClassification.Type = PdfPageType.SuspectedGarbledText;
			pdfPageClassification.ReasonCode = "garbled-font";
		}
		else
		{
			pdfPageClassification.Type = PdfPageType.TextBased;
			pdfPageClassification.ReasonCode = "text-ok";
		}
		return pdfPageClassification;
	}

	private static float ComputeImageCoverage(PdfPageContent page)
	{
		if (page.Width <= 0f || !(page.Height > 0f) || page.Images.Count == 0)
		{
			return 0f;
		}
		float num = page.Width * page.Height;
		float num2 = 0f;
		foreach (PdfImage image in page.Images)
		{
			num2 += Math.Max(0f, image.Width) * Math.Max(0f, image.Height);
		}
		return Math.Min(1f, num2 / num);
	}

	private static float ComputeGarbledRatio(PdfPageContent page)
	{
		int num = 0;
		int num2 = 0;
		foreach (PdfTextElement element in page.Elements)
		{
			if (string.IsNullOrEmpty(element.Text))
			{
				continue;
			}
			string text = element.Text;
			foreach (char c in text)
			{
				if (!char.IsWhiteSpace(c))
				{
					num++;
					if (c < ' ' || (c >= '\ue000' && c <= '\uf8ff') || c == '\ufffd')
					{
						num2++;
					}
				}
			}
		}
		if (num <= 0)
		{
			return 0f;
		}
		return (float)num2 * 1f / (float)num;
	}
}
