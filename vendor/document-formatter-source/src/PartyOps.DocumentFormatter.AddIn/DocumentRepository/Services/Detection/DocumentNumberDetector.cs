using System;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DocumentRepository.Services.Hosting;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Detection;

public static class DocumentNumberDetector
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string Extract(Document doc)
	{
		Paragraphs paragraphs = null;
		try
		{
			paragraphs = doc.Paragraphs;
			int num = Math.Min(paragraphs.Count, 40);
			int num2 = 0;
			for (int i = 1; i <= num; i++)
			{
				Paragraph paragraph = null;
				Microsoft.Office.Interop.Word.Range range = null;
				try
				{
					paragraph = paragraphs[i];
					range = paragraph.Range;
					string text = TitleDetector.NormalizeText(range.Text);
					if (!string.IsNullOrWhiteSpace(text))
					{
						num2++;
						string text2 = (TitleDetector.IsDocumentNumber(text) ? Regex.Replace(Regex.Match(text, "^\\s*(?<num>[一-龥A-Za-z]{1,20}\\s*[\\[{【（(〔]\\s*\\d{4}\\s*[\\]}】）)〕]\\s*\\d{1,4}\\s*号)").Groups["num"].Value, "[\\[{【（(〔]\\s*(\\d{4})\\s*[\\]}】）)〕]", "〔$1〕").Replace(" ", "") : "");
						if (!string.IsNullOrWhiteSpace(text2))
						{
							return text2;
						}
						if (num2 >= 18)
						{
							break;
						}
					}
				}
				finally
				{
					if (range != null)
					{
						ComObjectRelease.ReleaseOwned(range, "Extract", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\Services\\Detection\\DocumentNumberDetector.cs", 41);
					}
					if (paragraph != null)
					{
						ComObjectRelease.ReleaseOwned(paragraph, "Extract", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\Services\\Detection\\DocumentNumberDetector.cs", 42);
					}
				}
			}
			return "";
		}
		finally
		{
			if (paragraphs != null)
			{
				ComObjectRelease.ReleaseOwned(paragraphs, "Extract", "E:\\partyops公文排版助手\\GitHub私有仓库\\partyops-document-formatter-csharp\\Services\\Detection\\DocumentNumberDetector.cs", 49);
			}
		}
	}
}
