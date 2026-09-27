using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Services.Formatting.Orphans;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository;

public static class OrphanCharFormatter
{
	private sealed class DetectedOrphan
	{
		public DocumentElement Element { get; private set; }

		public OrphanSpacingMutation Mutation { get; private set; }

		public DetectedOrphan(DocumentElement element, OrphanSpacingMutation mutation)
		{
			Element = element;
			Mutation = mutation;
		}
	}

	private sealed class OrphanSpacingMutation
	{
		public int Start { get; private set; }

		public int End { get; private set; }

		public float OriginalSpacing { get; private set; }

		public bool Restored { get; set; }

		public OrphanSpacingMutation(int start, int end, float originalSpacing)
		{
			Start = start;
			End = end;
			OriginalSpacing = originalSpacing;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static OrphanFixResult FixDocument(Document document, DocumentElementList elements)
	{
		if (document == null)
		{
			throw new ArgumentNullException("document");
		}
		if (elements != null && elements.Items != null)
		{
			OrphanFixResult orphanFixResult = new OrphanFixResult();
			if (elements.HasOrphanCandidates)
			{
				Stopwatch stopwatch = Stopwatch.StartNew();
				List<DetectedOrphan> list = new List<DetectedOrphan>();
				List<OrphanSpacingMutation> list2 = new List<OrphanSpacingMutation>();
				int num = 0;
				int num2 = 0;
				try
				{
					document.Repaginate();
					using (OrphanVisualLineAnalysisSession orphanVisualLineAnalysisSession = OrphanVisualLineAnalysisService.CreateSession(document))
					{
						foreach (DocumentElement item in elements.Items)
						{
							if (item == null || !item.HasOrphanCandidate)
							{
								continue;
							}
							num++;
							OrphanVisualLineSnapshot orphanVisualLineSnapshot = orphanVisualLineAnalysisSession.Analyze(item.RangeStart, item.RangeEnd);
							if (orphanVisualLineSnapshot.IsOrphan)
							{
								orphanFixResult.Detected++;
								OrphanSpacingMutation orphanSpacingMutation = TryApplyPlan(document, orphanVisualLineAnalysisSession, item, orphanVisualLineSnapshot);
								if (orphanSpacingMutation != null)
								{
									list2.Add(orphanSpacingMutation);
								}
								list.Add(new DetectedOrphan(item, orphanSpacingMutation));
							}
						}
						if (list.Count > 0)
						{
							document.Repaginate();
							if (VerifyAndRestoreUnresolved(document, orphanVisualLineAnalysisSession, list, orphanFixResult))
							{
								document.Repaginate();
							}
						}
						num2 = orphanVisualLineAnalysisSession.InformationReadCount;
					}
					stopwatch.Stop();
					LogService.Info("[FORMAT-PERF-INNER] orphan candidates=" + num + ", detected=" + orphanFixResult.Detected + ", fixed=" + orphanFixResult.Fixed + ", unresolved=" + orphanFixResult.Unresolved + ", informationReads=" + num2 + ", elapsedMs=" + stopwatch.ElapsedMilliseconds);
					if (orphanFixResult.Unresolved > 0)
					{
						LogService.Warn("OrphanCharFormatter.Unresolved count=" + orphanFixResult.Unresolved, new InvalidOperationException("部分孤字受手动换行、混合字间距或段落结构限制，已恢复原字间距。"));
					}
					return orphanFixResult;
				}
				catch
				{
					RestoreAll(document, list2);
					throw;
				}
			}
			return orphanFixResult;
		}
		throw new InvalidOperationException("孤字修复必须使用分析层结果。");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static OrphanSpacingMutation TryApplyPlan(Document document, OrphanVisualLineAnalysisSession session, DocumentElement element, OrphanVisualLineSnapshot snapshot)
	{
		Microsoft.Office.Interop.Word.Range value = null;
		Font value2 = null;
		float num = 0f;
		bool flag = false;
		bool flag2 = false;
		try
		{
			object Start = snapshot.AdjustmentStart;
			object End = snapshot.ParagraphEnd;
			value = document.Range(ref Start, ref End);
			value2 = value.Font;
			num = value2.Spacing;
			flag = true;
			IReadOnlyList<float> readOnlyList;
			try
			{
				readOnlyList = OrphanSpacingPlanService.Build(num);
			}
			catch (InvalidOperationException ex)
			{
				LogService.Warn("OrphanCharFormatter.MixedSpacing paragraph=" + element.ParagraphIndex, ex);
				return null;
			}
			foreach (float item in readOnlyList)
			{
				value2.Spacing = item;
				if (!session.Analyze(element.RangeStart, element.RangeEnd).IsOrphan)
				{
					flag2 = true;
					return new OrphanSpacingMutation(snapshot.AdjustmentStart, snapshot.ParagraphEnd, num);
				}
			}
			value2.Spacing = num;
			flag = false;
			return null;
		}
		finally
		{
			if (flag && !flag2 && value2 != null)
			{
				try
				{
					value2.Spacing = num;
				}
				catch (Exception ex2)
				{
					LogService.Error("OrphanCharFormatter.RestoreSpacing", ex2);
				}
			}
			ComObjectRelease.Release(ref value2, "OrphanCharFormatter.Font");
			ComObjectRelease.Release(ref value, "OrphanCharFormatter.AdjustmentRange");
		}
	}

	private static bool VerifyAndRestoreUnresolved(Document document, OrphanVisualLineAnalysisSession session, IList<DetectedOrphan> detected, OrphanFixResult result)
	{
		bool result2 = false;
		foreach (DetectedOrphan item in detected)
		{
			if (session.Analyze(item.Element.RangeStart, item.Element.RangeEnd).IsOrphan)
			{
				result.Unresolved++;
				if (item.Mutation != null && !item.Mutation.Restored)
				{
					RestoreMutation(document, item.Mutation);
					result2 = true;
				}
			}
			else
			{
				result.Fixed++;
			}
		}
		return result2;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void RestoreAll(Document document, IList<OrphanSpacingMutation> mutations)
	{
		bool flag = false;
		for (int num = mutations.Count - 1; num >= 0; num--)
		{
			OrphanSpacingMutation orphanSpacingMutation = mutations[num];
			if (orphanSpacingMutation != null && !orphanSpacingMutation.Restored)
			{
				try
				{
					RestoreMutation(document, orphanSpacingMutation);
					flag = true;
				}
				catch (Exception ex)
				{
					LogService.Error("OrphanCharFormatter.Rollback", ex);
				}
			}
		}
		if (!flag)
		{
			return;
		}
		try
		{
			document.Repaginate();
		}
		catch (Exception ex2)
		{
			LogService.Error("OrphanCharFormatter.RollbackRepaginate", ex2);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void RestoreMutation(Document document, OrphanSpacingMutation mutation)
	{
		Microsoft.Office.Interop.Word.Range value = null;
		Font value2 = null;
		try
		{
			object Start = mutation.Start;
			object End = mutation.End;
			value = document.Range(ref Start, ref End);
			value2 = value.Font;
			value2.Spacing = mutation.OriginalSpacing;
			mutation.Restored = true;
		}
		finally
		{
			ComObjectRelease.Release(ref value2, "OrphanCharFormatter.Restore.Font");
			ComObjectRelease.Release(ref value, "OrphanCharFormatter.Restore.Range");
		}
	}
}
