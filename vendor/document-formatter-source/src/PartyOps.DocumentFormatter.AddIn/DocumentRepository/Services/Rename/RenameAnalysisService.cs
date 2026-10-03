using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DocumentRepository.Models;
using DocumentRepository.Models.Rename;
using DocumentRepository.Services.Analysis;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Rename;

public static class RenameAnalysisService
{
	private sealed class RenameContentAnalysis
	{
		public RenameInfo Info { get; set; }

		public RenameTitleEvidence MainTitleEvidence { get; set; }
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static RenameAnalysisResult Analyze(OperationContext context)
	{
		if (context != null && context.Document != null)
		{
			string documentFullName = GetDocumentFullName(context.Document);
			string directoryName = Path.GetDirectoryName(documentFullName);
			if (!string.IsNullOrWhiteSpace(directoryName) && Directory.Exists(directoryName))
			{
				RenameContentAnalysis renameContentAnalysis = ExtractRenameContent(context.Document);
				LogService.Info("Rename title recognized tier=" + renameContentAnalysis.MainTitleEvidence.Tier.ToString() + ", confidence=" + renameContentAnalysis.MainTitleEvidence.Confidence.ToString("0.00") + ", paragraphs=" + string.Join(",", renameContentAnalysis.MainTitleEvidence.ParagraphIndexes));
				return new RenameAnalysisResult
				{
					OriginalPath = documentFullName,
					OriginalDirectory = directoryName,
					Info = renameContentAnalysis.Info,
					MainTitleEvidence = renameContentAnalysis.MainTitleEvidence
				};
			}
			throw RenameOperationException.Create(RenameFailureReasonCode.DocumentNeedsSave, RenameFailureStage.Analysis);
		}
		throw RenameOperationException.Create(RenameFailureReasonCode.DocumentMissing, RenameFailureStage.Analysis);
	}

	public static RenameInfo ExtractRenameInfo(Document doc)
	{
		return ExtractRenameContent(doc).Info;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static RenameContentAnalysis ExtractRenameContent(Document doc)
	{
		DocumentAnalysisResult documentAnalysisResult = DocumentAnalysisService.AnalyzeElements(doc, ConfigManager.Current);
		if (documentAnalysisResult == null || documentAnalysisResult.Elements == null || documentAnalysisResult.Elements.Items == null)
		{
			throw new InvalidOperationException("一键命名必须使用分析层产出的 DocumentElementList。");
		}
		List<DocumentElement> items = documentAnalysisResult.Elements.Items;
		RenameTitleEvidence renameTitleEvidence = RenameTitleRecognitionService.Recognize(doc, items);
		return new RenameContentAnalysis
		{
			MainTitleEvidence = renameTitleEvidence,
			Info = new RenameInfo
			{
				DocumentNumber = ExtractDocumentNumber(items),
				MainTitle = renameTitleEvidence.Title,
				Subtitle = ExtractOpeningSubtitle(items, renameTitleEvidence)
			}
		};
	}

	private static string GetDocumentFullName(Document doc)
	{
		return doc.FullName ?? "";
	}

	private static string ExtractDocumentNumber(IEnumerable<DocumentElement> items)
	{
		DocumentElement documentElement = items.FirstOrDefault((DocumentElement e) => e != null && e.Type == ElementType.DocumentNumber);
		return NormalizeDocumentNumber((documentElement == null) ? "" : documentElement.Text);
	}

	private static string ExtractOpeningSubtitle(IList<DocumentElement> items, RenameTitleEvidence titleEvidence)
	{
		if (items == null || items.Count == 0)
		{
			return "";
		}
		int num = ((titleEvidence != null && titleEvidence.ParagraphIndexes.Count > 0) ? titleEvidence.ParagraphIndexes.Max() : 0);
		if (num > 0)
		{
			List<string> list = new List<string>();
			bool flag = false;
			foreach (DocumentElement item in items)
			{
				if (item == null || item.IsEmpty || item.ParagraphIndex <= num || item.Type == ElementType.DocumentNumber)
				{
					continue;
				}
				if (item.Type == ElementType.SubTitle)
				{
					flag = true;
					list.Add(NormalizeNamePart(item.Text));
					continue;
				}
				if (!flag && IsTitleSearchBoundary(item.Type))
				{
				}
				break;
			}
			return string.Concat(list.Where((string p) => !string.IsNullOrWhiteSpace(p)));
		}
		List<string> list2 = new List<string>();
		bool flag2 = false;
		bool flag3 = false;
		foreach (DocumentElement item2 in items)
		{
			if (item2 == null || item2.IsEmpty || item2.Type == ElementType.DocumentNumber)
			{
				continue;
			}
			if (item2.Type == ElementType.MainTitle)
			{
				flag2 = true;
				if (flag3)
				{
					break;
				}
			}
			else if (item2.Type == ElementType.SubTitle)
			{
				if (!flag2 && list2.Count == 0)
				{
					flag2 = true;
				}
				flag3 = true;
				list2.Add(NormalizeNamePart(item2.Text));
			}
			else if (flag3 || (flag2 && IsTitleSearchBoundary(item2.Type)) || (!flag2 && IsTitleSearchBoundary(item2.Type)))
			{
				break;
			}
		}
		return string.Concat(list2.Where((string p) => !string.IsNullOrWhiteSpace(p)));
	}

	private static bool IsTitleSearchBoundary(ElementType type)
	{
		if (type != ElementType.Salutation && type != ElementType.Body && type != ElementType.Level1Title && type != ElementType.Level2Title && type != ElementType.Level3Title && type != ElementType.AttachmentMarker && type != ElementType.AttachmentTitle && type != ElementType.AttachmentListFirst && type != ElementType.AttachmentListSingle && type != ElementType.AttachmentListContinuation && type != ElementType.Signature && type != ElementType.SignatureDate && type != ElementType.Table && type != ElementType.Image)
		{
			return type == ElementType.Imprint;
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string NormalizeDocumentNumber(string text)
	{
		string text2 = NormalizeNamePart(text);
		if (!string.IsNullOrWhiteSpace(text2))
		{
			Match match = Regex.Match(text2, "[\\u4e00-\\u9fa5A-Za-z]{1,20}[\\[\\{\\u3010\\uff08\\(\\u3014]\\d{4}[\\]\\}\\u3011\\uff09\\)\\u3015]\\d{1,4}\\u53f7");
			if (!match.Success)
			{
				int num = text2.IndexOf("号", StringComparison.Ordinal);
				if (num >= 0)
				{
					return text2.Substring(0, num + 1);
				}
				return text2;
			}
			return match.Value;
		}
		return "";
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string NormalizeNamePart(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return "";
		}
		return text.Replace("\r", "").Replace("\n", "").Replace("\a", "")
			.Replace("\t", "")
			.Replace(" ", "")
			.Replace("\u3000", "")
			.Trim();
	}
}
