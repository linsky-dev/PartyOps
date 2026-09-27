using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DocumentRepository.Models.FormattingPlan;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.Safety;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Formatting;

public static class EnglishNumberFontService
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void ApplyPlanned(Document document, FormatConfig config, IReadOnlyList<PlannedTextRange> ranges)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		if (config == null)
		{
			throw new ArgumentNullException("config");
		}
		if (ranges == null)
		{
			throw new ArgumentNullException("ranges");
		}
		if (!config.EnableEnglishFont || ranges.Count == 0)
		{
			return;
		}
		string text = (config.EnglishNumberFontName ?? string.Empty).Trim();
		if (text.Length == 0)
		{
			ReportFormatWarning("english-number-font-empty", "英文和数字字体参数为空，已跳过该格式步骤。");
			return;
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		foreach (PlannedTextRange range in ranges)
		{
			if (range == null || range.End <= range.Start)
			{
				num3++;
				ReportFormatWarning("english-number-range-invalid", "英文和数字字体计划包含无效范围，已跳过。");
				continue;
			}
			try
			{
				if (!EnglishNumberFontScopePolicy.MatchesLevel3Font(config, range.SourceEastAsianFontName))
				{
					num3++;
					ReportFormatWarning("english-number-range-out-of-scope", "英文和数字字体目标不在当前作用范围内，已跳过。");
					continue;
				}
			}
			catch (FormatException exception)
			{
				num3++;
				ReportFormatWarning("english-number-range-font-invalid", "英文和数字字体目标缺少可靠的中文字体事实，已跳过。", exception);
				continue;
			}
			Microsoft.Office.Interop.Word.Range value = null;
			Font value2 = null;
			try
			{
				object Start = range.Start;
				object End = range.End;
				value = document.Range(ref Start, ref End);
				value2 = value.Font;
				num++;
				if (DocumentFontSlotService.ApplyAsciiIfNeeded(value2, text))
				{
					num2++;
				}
			}
			catch (COMException exception2)
			{
				num3++;
				ReportFormatWarning("english-number-host-operation", "宿主未能设置某个范围的英文和数字字体，已跳过。", exception2);
			}
			catch (ArgumentException exception3)
			{
				num3++;
				ReportFormatWarning("english-number-range-unavailable", "英文和数字字体目标范围已失效，已跳过。", exception3);
			}
			finally
			{
				ComObjectRelease.Release(ref value2, "EnglishNumberFontService.Font");
				ComObjectRelease.Release(ref value, "EnglishNumberFontService.Range");
			}
		}
		LogService.Info("[FORMAT-PERF-INNER] english planned ranges=" + num + ", modified=" + num2 + ", unchanged=" + (num - num2) + ", rejected=" + num3 + ", elapsed=" + stopwatch.ElapsedMilliseconds + "ms");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ReportFormatWarning(string code, string detail, Exception exception = null)
	{
		if (exception != null)
		{
			LogService.Warn("FORMAT-QUALITY " + code + ": " + detail, exception);
		}
		else
		{
			LogService.Warn("FORMAT-QUALITY " + code + ": " + detail);
		}
		ExecutionWarningCollector.Report(code, "english-number-font", "warn.format.english-number-font");
	}
}
