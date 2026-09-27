using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace DocumentRepository.Services.Conversion.PdfToWord;

public sealed class PdfTypeClassificationResult
{
	public IList<PdfPageClassification> Pages { get; private set; }

	public PdfDocumentKind DocumentKind { get; set; }

	public IList<int> PagesNeedingOcr { get; private set; }

	public PdfTypeClassificationResult()
	{
		Pages = new List<PdfPageClassification>();
		PagesNeedingOcr = new List<int>();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public string FormatPagesNeedingOcr()
	{
		if (PagesNeedingOcr.Count != 0)
		{
			StringBuilder stringBuilder = new StringBuilder();
			int num = PagesNeedingOcr[0];
			int num2 = num;
			for (int i = 1; i <= PagesNeedingOcr.Count; i++)
			{
				int num3 = ((i < PagesNeedingOcr.Count) ? PagesNeedingOcr[i] : (-1));
				if (num3 == num2 + 1)
				{
					num2 = num3;
					continue;
				}
				if (stringBuilder.Length > 0)
				{
					stringBuilder.Append('、');
				}
				stringBuilder.Append((num == num2) ? num.ToString() : (num + "-" + num2));
				num = (num2 = num3);
			}
			return stringBuilder.ToString();
		}
		return string.Empty;
	}
}
