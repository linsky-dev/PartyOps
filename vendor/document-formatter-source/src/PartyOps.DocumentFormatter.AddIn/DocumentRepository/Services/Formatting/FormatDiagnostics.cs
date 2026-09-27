using System;
using System.Runtime.CompilerServices;
using System.Text;
using DocumentRepository.Models;
using DocumentRepository.Services.Detection;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Formatting;

public static class FormatDiagnostics
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void DumpParagraphTypes(Document doc, FormatConfig cfg)
	{
		if (doc == null)
		{
			throw new ArgumentNullException("doc");
		}
		if (cfg == null)
		{
			throw new ArgumentNullException("cfg");
		}
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("===== FormatDiagnostics.DumpParagraphTypes =====");
		int count = doc.Paragraphs.Count;
		for (int i = 1; i <= count; i++)
		{
			Paragraph value = null;
			Microsoft.Office.Interop.Word.Range value2 = null;
			try
			{
				value = doc.Paragraphs[i];
				value2 = value.Range;
				string text = (value2.Text ?? string.Empty).Trim(new char[] { '\r', '\n', '\u0007', '\v' });
				if (!string.IsNullOrEmpty(text))
				{
					ElementType elementType = Detector.DetectParagraphType(text, cfg);
					string styleName = DocumentStyleManager.GetStyleName(elementType);
					string text2 = ((text.Length > 40) ? (text.Substring(0, 40) + "...") : text);
					stringBuilder.AppendLine($"[{i}] {elementType} -> {styleName} -> {text2}");
				}
				else
				{
					stringBuilder.AppendLine($"[{i}] Empty");
				}
			}
			finally
			{
				if (value2 != null)
				{
					ComObjectRelease.Release(ref value2, "FormatDiagnostics.rng");
				}
				if (value != null)
				{
					ComObjectRelease.Release(ref value, "FormatDiagnostics.para");
				}
			}
		}
		stringBuilder.AppendLine("===== End FormatDiagnostics =====");
		LogService.Info(stringBuilder.ToString());
	}
}
