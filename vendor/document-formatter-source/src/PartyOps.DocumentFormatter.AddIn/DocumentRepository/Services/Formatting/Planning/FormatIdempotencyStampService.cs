using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using DocumentRepository.Models;
using DocumentRepository.Models.FormattingPlan;
using DocumentRepository.Models.Snapshots;
using DocumentRepository.Services.Detection.Tables;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Formatting.Planning;

internal static class FormatIdempotencyStampService
{
	private sealed class Stamp
	{
		public string LifecycleId;

		public string InputSignature;

		public string VerifiedOutputSignature;

		public string TableInputSignature;
	}

	private static readonly object SyncRoot = new object();

	private static readonly Dictionary<string, Stamp> Stamps = new Dictionary<string, Stamp>(StringComparer.Ordinal);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool CanSkipExpensiveFullDocumentFormatting(FormatExecutionPlan plan, Document document)
	{
		if (plan == null || document == null || plan.IsSelectionMode)
		{
			return false;
		}
		if (!DocumentLifecycleRegistry.TryGet(document, out var lifecycleId) || string.IsNullOrWhiteSpace(lifecycleId))
		{
			return false;
		}
		string text = BuildSignature(plan, plan.SourceSnapshot);
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		bool flag;
		lock (SyncRoot)
		{
			flag = Stamps.TryGetValue(lifecycleId, out var value) && string.Equals(value.LifecycleId, lifecycleId, StringComparison.Ordinal) && string.Equals(value.InputSignature, text, StringComparison.Ordinal);
		}
		if (flag)
		{
			if (!HasExpectedStructuralStyles(plan, document))
			{
				Release(lifecycleId);
				LogService.Info("[FORMAT-IDEMPOTENCY] bypass ApplyParagraphStyles reason=style-evidence-mismatch");
				return false;
			}
			return true;
		}
		return false;
	}

	public static void MarkSuccessful(FormatExecutionPlan plan, Document document, DocumentSnapshot actualSnapshot)
	{
		if (plan == null || document == null || actualSnapshot == null || plan.IsSelectionMode || !DocumentLifecycleRegistry.TryGet(document, out var lifecycleId) || string.IsNullOrWhiteSpace(lifecycleId))
		{
			return;
		}
		string text = BuildSignature(plan, plan.SourceSnapshot);
		string text2 = BuildSignature(plan, actualSnapshot);
		string tableInputSignature = BuildTableInputSignature(plan);
		if (!string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(text2))
		{
			lock (SyncRoot)
			{
				Stamps[lifecycleId] = new Stamp
				{
					LifecycleId = lifecycleId,
					InputSignature = text,
					VerifiedOutputSignature = text2,
					TableInputSignature = tableInputSignature
				};
			}
		}
	}

	public static bool CanSkipTableFormatting(FormatExecutionPlan plan, Document document)
	{
		if (plan == null || document == null || plan.IsSelectionMode)
		{
			return false;
		}
		if (DocumentLifecycleRegistry.TryGet(document, out var lifecycleId) && !string.IsNullOrWhiteSpace(lifecycleId))
		{
			string text = BuildTableInputSignature(plan);
			if (!string.IsNullOrWhiteSpace(text))
			{
				lock (SyncRoot)
				{
					Stamp value;
					return Stamps.TryGetValue(lifecycleId, out value) && string.Equals(value.LifecycleId, lifecycleId, StringComparison.Ordinal) && string.Equals(value.TableInputSignature, text, StringComparison.Ordinal);
				}
			}
			return false;
		}
		return false;
	}

	internal static void Release(string lifecycleId)
	{
		if (string.IsNullOrWhiteSpace(lifecycleId))
		{
			return;
		}
		lock (SyncRoot)
		{
			Stamps.Remove(lifecycleId);
		}
	}

	internal static void Clear()
	{
		lock (SyncRoot)
		{
			Stamps.Clear();
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool HasExpectedStructuralStyles(FormatExecutionPlan plan, Document document)
	{
		if (plan == null || document == null || plan.FormatContext == null || plan.FormatContext.Elements == null || plan.FormatContext.Elements.Items == null)
		{
			return false;
		}
		int num = 0;
		foreach (DocumentElement item in plan.FormatContext.Elements.Items)
		{
			if (item == null || item.IsEmpty || !FormatPlanVerifier.RequiresStyleVerification(item.Type))
			{
				continue;
			}
			num++;
			Microsoft.Office.Interop.Word.Range value = null;
			Paragraphs value2 = null;
			Paragraph value3 = null;
			try
			{
				object Start = item.RangeStart;
				object End = item.RangeStart;
				value = document.Range(ref Start, ref End);
				value2 = value.Paragraphs;
				if (value2 != null && value2.Count != 0)
				{
					value3 = value2[1];
					if (DocumentStyleManager.IsParagraphStyle(value3, item.Type))
					{
						continue;
					}
					return false;
				}
				return false;
			}
			catch (Exception ex)
			{
				LogService.Warn("FormatIdempotencyStampService.StyleEvidence", ex);
				return false;
			}
			finally
			{
				ComObjectRelease.Release(ref value3, "FormatIdempotency.StyleEvidence.Paragraph");
				ComObjectRelease.Release(ref value2, "FormatIdempotency.StyleEvidence.Paragraphs");
				ComObjectRelease.Release(ref value, "FormatIdempotency.StyleEvidence.Range");
			}
		}
		return num > 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string BuildTableInputSignature(FormatExecutionPlan plan)
	{
		if (plan == null || plan.SourceSnapshot == null || plan.FormatContext == null || string.IsNullOrWhiteSpace(plan.SourceSnapshot.ContentFingerprint))
		{
			return null;
		}
		TableAnalysisResult tableAnalysisResult = ((plan.FormatContext.Analysis == null) ? null : plan.FormatContext.Analysis.TableAnalysis);
		if (tableAnalysisResult != null && tableAnalysisResult.Tables != null)
		{
			StringBuilder stringBuilder = new StringBuilder(1024);
			stringBuilder.Append("format-table-idempotency-v1");
			stringBuilder.Append("|content=").Append(plan.SourceSnapshot.ContentFingerprint);
			stringBuilder.Append("|config=").Append(FormatConfigFingerprint.Build(plan.FormatContext.Config));
			stringBuilder.Append("|tables=").Append(tableAnalysisResult.Tables.Count);
			foreach (TableElementInfo table in tableAnalysisResult.Tables)
			{
				if (table == null || !table.ContentIdentityReliable || string.IsNullOrWhiteSpace(table.ContentHash) || string.IsNullOrWhiteSpace(table.StructureFingerprint))
				{
					return null;
				}
				stringBuilder.Append('|').Append(table.AnalysisOrdinal).Append(',')
					.Append(table.ContentHash)
					.Append(',')
					.Append(table.StructureFingerprint)
					.Append(',')
					.Append(table.StoryTypeCode)
					.Append(',')
					.Append(table.RowCount)
					.Append('x')
					.Append(table.ColumnCount)
					.Append(',')
					.Append(table.NestedTableCountReliable)
					.Append(':')
					.Append(table.NestedTableCount);
			}
			return Sha256(stringBuilder.ToString());
		}
		return null;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string BuildSignature(FormatExecutionPlan plan, DocumentSnapshot snapshot)
	{
		if (plan != null && snapshot != null && plan.FormatContext != null)
		{
			if (string.IsNullOrWhiteSpace(snapshot.StateFingerprint))
			{
				return null;
			}
			StringBuilder stringBuilder = new StringBuilder(4096);
			stringBuilder.Append("format-idempotency-v2");
			stringBuilder.Append("|snapshot=").Append(snapshot.StateFingerprint);
			stringBuilder.Append("|scope=").Append(snapshot.ScopeStart).Append('-')
				.Append(snapshot.ScopeEnd);
			AppendContext(stringBuilder, plan.FormatContext);
			return Sha256(stringBuilder.ToString());
		}
		return null;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void AppendContext(StringBuilder builder, FormatContext context)
	{
		builder.Append("|flags=").Append(context.HasTables).Append(',')
			.Append(context.HasImages)
			.Append(',')
			.Append(context.HasAttachments)
			.Append(',')
			.Append(context.HasEnglishNumbers)
			.Append(',')
			.Append(context.HasKeywordCandidates)
			.Append(',')
			.Append(context.HasSemicolonCandidates)
			.Append(',')
			.Append(context.HasTabs)
			.Append(',')
			.Append(context.HasOrphanCandidates)
			.Append(',')
			.Append(context.DocumentGridCompatibilityFallbackApplied);
		builder.Append("|config=").Append(FormatConfigFingerprint.Build(context.Config));
		if (context.Elements != null && context.Elements.Items != null)
		{
			builder.Append("|elements=").Append(context.Elements.Items.Count);
			foreach (DocumentElement item in context.Elements.Items)
			{
				if (item == null)
				{
					builder.Append("|null");
				}
				else
				{
					builder.Append('|').Append(item.ParagraphIndex).Append(',')
						.Append(item.ScopeParagraphIndex)
						.Append(',')
						.Append(item.RangeStart)
						.Append('-')
						.Append(item.RangeEnd)
						.Append(',')
						.Append(item.Type)
						.Append(',')
						.Append(item.IsEmpty)
						.Append(',')
						.Append(item.HasTabs)
						.Append(',')
						.Append(Sha256(item.Text ?? string.Empty));
				}
			}
		}
		if (context.Analysis == null || context.Analysis.TableAnalysis == null || context.Analysis.TableAnalysis.Tables == null)
		{
			return;
		}
		builder.Append("|tables=").Append(context.Analysis.TableAnalysis.Tables.Count);
		foreach (TableElementInfo table in context.Analysis.TableAnalysis.Tables)
		{
			if (table == null)
			{
				builder.Append("|null");
			}
			else
			{
				builder.Append('|').Append(table.AnalysisOrdinal).Append(',')
					.Append(table.RangeStart)
					.Append('-')
					.Append(table.RangeEnd)
					.Append(',')
					.Append(table.RowCount)
					.Append('x')
					.Append(table.ColumnCount)
					.Append(',')
					.Append(table.ContentIdentityReliable)
					.Append(',')
					.Append(table.ContentHash ?? string.Empty)
					.Append(',')
					.Append(table.StructureFingerprint ?? string.Empty)
					.Append(',')
					.Append(table.HasHeaderCandidate);
			}
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string Sha256(string text)
	{
		using SHA256 sHA = SHA256.Create();
		byte[] array = sHA.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty));
		StringBuilder stringBuilder = new StringBuilder(array.Length * 2);
		byte[] array2 = array;
		foreach (byte b in array2)
		{
			stringBuilder.Append(b.ToString("X2"));
		}
		return stringBuilder.ToString();
	}
}
