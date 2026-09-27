using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Services.Analysis;
using DocumentRepository.Services.Detection.Tables;
using DocumentRepository.Services.Hosting;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Formatting.Tables;

public static class TableFormattingService
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void FormatDocument(Document doc, Application app, FormatConfig config, DocumentAnalysisResult analysis, DocumentHostKind hostKind)
	{
		if (doc == null)
		{
			throw new ArgumentNullException("doc");
		}
		if (app != null)
		{
			if (config == null)
			{
				throw new ArgumentNullException("config");
			}
			if (config.EnableTableFormatting)
			{
				if (analysis == null)
				{
					throw new InvalidOperationException("表格排版必须使用分析层产出的 DocumentAnalysisResult。");
				}
				TableAnalysisResult tableAnalysis = analysis.TableAnalysis;
				if (tableAnalysis == null)
				{
					throw new InvalidOperationException("表格排版必须使用分析层产出的 TableAnalysisResult。");
				}
				if (tableAnalysis.HasTables)
				{
					TableStyleDefinition style = TableStyleBuilder.FromConfig(config);
					TableStyleApplier.ApplyToDocument(doc, app, style, tableAnalysis, hostKind);
				}
			}
			return;
		}
		throw new ArgumentNullException("app");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void FormatRange(Microsoft.Office.Interop.Word.Range range, Application app, FormatConfig config, DocumentAnalysisResult analysis, DocumentHostKind hostKind)
	{
		if (range == null)
		{
			throw new ArgumentNullException("range");
		}
		if (app == null)
		{
			throw new ArgumentNullException("app");
		}
		if (config == null)
		{
			throw new ArgumentNullException("config");
		}
		if (config.EnableTableFormatting)
		{
			if (analysis == null)
			{
				throw new InvalidOperationException("选区表格排版必须使用分析层产出的 DocumentAnalysisResult。");
			}
			TableAnalysisResult tableAnalysis = analysis.TableAnalysis;
			if (tableAnalysis == null)
			{
				throw new InvalidOperationException("选区表格排版必须使用分析层产出的 TableAnalysisResult。");
			}
			if (tableAnalysis.HasTables)
			{
				TableStyleDefinition style = TableStyleBuilder.FromConfig(config);
				TableStyleApplier.ApplyToRange(range, app, style, tableAnalysis, hostKind);
			}
		}
	}
}
