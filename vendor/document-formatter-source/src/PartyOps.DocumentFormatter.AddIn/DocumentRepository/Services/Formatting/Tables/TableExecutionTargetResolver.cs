using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DocumentRepository.Services.Detection.Tables;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Formatting.Tables;

internal static class TableExecutionTargetResolver
{
	private sealed class Candidate
	{
		public int CollectionIndex;

		public TableElementInfo Facts;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static Table Resolve(Document document, TableElementInfo expected, string scope, int documentEnd)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		if (expected != null)
		{
			if (expected.RangeStart < 0 || expected.RangeEnd <= expected.RangeStart)
			{
				throw new InvalidOperationException(BuildMessage(scope, expected, "分析层未提供有效的表格范围。"));
			}
			if (expected.ContentIdentityReliable && !string.IsNullOrWhiteSpace(expected.ContentHash))
			{
				for (int i = 0; i < 2; i++)
				{
					Microsoft.Office.Interop.Word.Tables value = null;
					Table value2 = null;
					try
					{
						value = document.Tables;
						int num = value?.Count ?? 0;
						List<Candidate> list = new List<Candidate>();
						for (int j = 1; j <= num; j++)
						{
							try
							{
								value2 = value[j];
								TableElementInfo tableElementInfo = TableDetector.ReadTableFacts(value2, j);
								if (TableIdentityComparer.HasSameStableIdentity(expected, tableElementInfo))
								{
									list.Add(new Candidate
									{
										CollectionIndex = j,
										Facts = tableElementInfo
									});
								}
							}
							finally
							{
								ComObjectRelease.Release(ref value2, "TableExecutionTargetResolver.table");
							}
						}
						Candidate candidate = ResolveUniqueCandidate(expected, list);
						if (candidate == null)
						{
							LogService.Warn("TABLE-TARGET unresolved, scope=" + SafeScope(scope) + ", analysisOrdinal=" + expected.AnalysisOrdinal + ", identityCandidates=" + list.Count);
							return null;
						}
						return value[candidate.CollectionIndex];
					}
					catch (Exception ex)
					{
						if (i == 1)
						{
							LogService.Warn("TABLE-TARGET collection-indeterminate, scope=" + SafeScope(scope) + ", analysisOrdinal=" + expected.AnalysisOrdinal, ex);
							return null;
						}
					}
					finally
					{
						ComObjectRelease.Release(ref value2, "TableExecutionTargetResolver.table");
						ComObjectRelease.Release(ref value, "TableExecutionTargetResolver.tables");
					}
				}
				return null;
			}
			LogService.Warn("TABLE-TARGET identity-indeterminate, scope=" + SafeScope(scope) + ", analysisOrdinal=" + expected.AnalysisOrdinal);
			return null;
		}
		throw new ArgumentNullException("expected");
	}

	private static Candidate ResolveUniqueCandidate(TableElementInfo expected, IList<Candidate> candidates)
	{
		Candidate result = null;
		int num = 0;
		foreach (Candidate candidate in candidates)
		{
			if (candidate.Facts.RangeStart == expected.RangeStart && candidate.Facts.RangeEnd == expected.RangeEnd)
			{
				result = candidate;
				num++;
			}
		}
		if (num != 1)
		{
			int num2 = Math.Max(1, expected.RangeEnd - expected.RangeStart);
			int num3 = Math.Max(128, Math.Min(2048, num2 * 4));
			Candidate result2 = null;
			int num4 = 0;
			foreach (Candidate candidate2 in candidates)
			{
				int val = Math.Abs(candidate2.Facts.RangeStart - expected.RangeStart);
				int val2 = Math.Abs(candidate2.Facts.RangeEnd - expected.RangeEnd);
				if (Math.Max(val, val2) <= num3)
				{
					result2 = candidate2;
					num4++;
				}
			}
			if (num4 == 1)
			{
				return result2;
			}
			if (candidates.Count == 1)
			{
				return candidates[0];
			}
			return null;
		}
		return result;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string BuildMessage(string scope, TableElementInfo expected, string detail)
	{
		return "表格执行目标失效：scope=" + (scope ?? "unknown") + "，analysisOrdinal=" + expected.AnalysisOrdinal + "，expectedRange=" + expected.RangeStart + "-" + expected.RangeEnd + "。" + detail;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string SafeScope(string scope)
	{
		if (!string.Equals(scope, "selection", StringComparison.Ordinal))
		{
			return "document";
		}
		return "selection";
	}
}
