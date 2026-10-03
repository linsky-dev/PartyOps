using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Mutations;
using DocumentRepository.Services.Cleanup;
using DocumentRepository.Services.Hosting;
using Microsoft.Office.Interop.Word;

namespace DocumentRepository.Services.Mutations;

public sealed class ExactTextReplacementMutation : IDocumentMutation
{
	private readonly IReadOnlyList<MutationTarget> targets;

	public string Id { get; }

	public string Description { get; }

	public string ExpectedText { get; }

	public string Replacement { get; }

	public DocumentMutationKind Kind => DocumentMutationKind.CharacterText;

	public IReadOnlyList<MutationTarget> Targets => targets;

	public bool AllowsTargetOverlap => false;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public ExactTextReplacementMutation(string id, string description, int start, int end, string expectedText, string replacement)
	{
		if (!string.IsNullOrWhiteSpace(id))
		{
			if (start >= 0 && end >= start)
			{
				Id = id;
				Description = description ?? id;
				ExpectedText = expectedText ?? string.Empty;
				Replacement = replacement ?? string.Empty;
				targets = new MutationTarget[1]
				{
					new MutationTarget
					{
						Start = start,
						End = end
					}
				};
				return;
			}
			throw new ArgumentOutOfRangeException("start");
		}
		throw new ArgumentException("操作编号不能为空。", "id");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void Apply(MutationExecutionContext context)
	{
		if (context != null && context.Document != null)
		{
			MutationTarget mutationTarget = targets[0];
			Microsoft.Office.Interop.Word.Range value = null;
			Microsoft.Office.Interop.Word.Range value2 = null;
			try
			{
				Document document = context.Document;
				object Start = mutationTarget.Start;
				object End = mutationTarget.End;
				value = document.Range(ref Start, ref End);
				string b = value.Text ?? string.Empty;
				if (!string.Equals(ExpectedText, b, StringComparison.Ordinal))
				{
					throw new InvalidOperationException("文字变更目标原文已变化：" + Description);
				}
				value2 = context.Document.Content;
				if (!SafeTextMutationService.TryReplace(value2, mutationTarget.Start - value2.Start, mutationTarget.End - mutationTarget.Start, Replacement, Description))
				{
					throw new InvalidOperationException("文字变更目标包含受保护对象，已拒绝修改：" + Description);
				}
				return;
			}
			finally
			{
				ComObjectRelease.Release(ref value2, "ExactTextMutation.Content");
				ComObjectRelease.Release(ref value, "ExactTextMutation.Target");
			}
		}
		throw new InvalidOperationException("精确文字变更缺少目标文档。");
	}

	void IDocumentMutation.Apply(MutationExecutionContext context)
	{
		this.Apply(context);
	}
}
