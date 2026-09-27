using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Mutations;

namespace DocumentRepository.Services.Mutations;

public static class MutationConflictDetector
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void AssertNoConflicts(DocumentMutationPlan plan)
	{
		if (plan == null)
		{
			throw new ArgumentNullException("plan");
		}
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		for (int i = 0; i < plan.Operations.Count; i++)
		{
			IDocumentMutation documentMutation = plan.Operations[i];
			if (documentMutation == null)
			{
				throw new InvalidOperationException("变更计划包含空操作。");
			}
			if (string.IsNullOrWhiteSpace(documentMutation.Id))
			{
				throw new InvalidOperationException("变更操作缺少唯一编号。");
			}
			if (hashSet.Add(documentMutation.Id))
			{
				if (documentMutation.AllowsTargetOverlap || documentMutation.Targets == null)
				{
					continue;
				}
				for (int j = 0; j < i; j++)
				{
					IDocumentMutation documentMutation2 = plan.Operations[j];
					if (documentMutation2 != null && !documentMutation2.AllowsTargetOverlap && documentMutation2.Targets != null)
					{
						AssertTargetsDoNotOverlap(documentMutation2, documentMutation);
					}
				}
				continue;
			}
			throw new InvalidOperationException("变更计划包含重复操作编号：" + documentMutation.Id);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void AssertTargetsDoNotOverlap(IDocumentMutation left, IDocumentMutation right)
	{
		foreach (MutationTarget target in left.Targets)
		{
			if (target == null)
			{
				continue;
			}
			foreach (MutationTarget target2 in right.Targets)
			{
				if (target2 != null && target.Overlaps(target2))
				{
					throw new InvalidOperationException("变更计划存在重叠目标：" + left.Id + " 与 " + right.Id);
				}
			}
		}
	}
}
