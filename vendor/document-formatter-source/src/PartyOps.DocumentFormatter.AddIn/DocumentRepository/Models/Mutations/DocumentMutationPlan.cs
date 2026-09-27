using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Snapshots;

namespace DocumentRepository.Models.Mutations;

public class DocumentMutationPlan
{
	private readonly List<IDocumentMutation> operations = new List<IDocumentMutation>();

	private string planId = Guid.NewGuid().ToString("N");

	private string featureId;

	private DocumentSnapshot sourceSnapshot;

	private string ruleContentHash;

	public string PlanId
	{
		get
		{
			return planId;
		}
		set
		{
			ThrowIfSealed();
			planId = value;
		}
	}

	public string FeatureId
	{
		get
		{
			return featureId;
		}
		set
		{
			ThrowIfSealed();
			featureId = value;
		}
	}

	public DocumentSnapshot SourceSnapshot
	{
		get
		{
			if (sourceSnapshot != null)
			{
				return sourceSnapshot.DeepCopy();
			}
			return null;
		}
		set
		{
			ThrowIfSealed();
			sourceSnapshot = value;
		}
	}

	internal DocumentSnapshot SourceSnapshotForExecution
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		get
		{
			if (!IsSealed)
			{
				throw new InvalidOperationException("变更计划尚未封存，不能读取执行来源。");
			}
			return sourceSnapshot;
		}
	}

	public string RuleContentHash
	{
		get
		{
			return ruleContentHash;
		}
		set
		{
			ThrowIfSealed();
			ruleContentHash = value;
		}
	}

	public IReadOnlyList<IDocumentMutation> Operations => operations.AsReadOnly();

	public bool IsSealed { get; private set; }

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void AddOperation(IDocumentMutation operation)
	{
		if (!IsSealed)
		{
			if (operation == null)
			{
				throw new ArgumentNullException("operation");
			}
			operations.Add(operation);
			return;
		}
		throw new InvalidOperationException("变更计划已经封存，不能再添加操作。");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void Seal()
	{
		if (sourceSnapshot == null)
		{
			throw new InvalidOperationException("变更计划缺少源快照，不能封存。");
		}
		sourceSnapshot = sourceSnapshot.DeepCopy();
		IsSealed = true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void ThrowIfSealed()
	{
		if (IsSealed)
		{
			throw new InvalidOperationException("变更计划已经封存，拒绝修改计划身份与来源。");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public DocumentMutationPlan()
	{
	}
}
