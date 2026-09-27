using System.Collections.Generic;

namespace DocumentRepository.Services.Conversion.PdfToWord;

public sealed class PdfPageContent
{
	public int PageIndex { get; set; }

	public float Width { get; set; }

	public float Height { get; set; }

	public int Rotation { get; set; }

	public IList<PdfTextElement> Elements { get; private set; }

	public IList<PdfImage> Images { get; private set; }

	public int VectorPathCount { get; set; }

	public IList<PdfLineFrame> LineFrames { get; private set; }

	public int MeaningfulCharacterCount
	{
		get
		{
			int num = 0;
			foreach (PdfTextElement element in Elements)
			{
				if (string.IsNullOrEmpty(element.Text))
				{
					continue;
				}
				string text = element.Text;
				foreach (char c in text)
				{
					if (!char.IsWhiteSpace(c) && c != '\r' && c != '\n' && c != '\t')
					{
						num++;
					}
				}
			}
			return num;
		}
	}

	public PdfPageContent()
	{
		Elements = new List<PdfTextElement>();
		Images = new List<PdfImage>();
		LineFrames = new List<PdfLineFrame>();
	}
}
