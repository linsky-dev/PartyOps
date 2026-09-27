using System;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Services.Hosting;

public sealed class WriteLeaseGuard
{
	private readonly DocumentSessionStateMachine stateMachine;

	private object activeToken;

	private string expectedDocumentLifecycleId;

	private string expectedPlanId;

	private string expectedSourceSnapshotId;

	public bool HasActiveToken => activeToken != null;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public WriteLeaseGuard(DocumentSessionStateMachine stateMachine)
	{
		this.stateMachine = stateMachine ?? throw new ArgumentNullException("stateMachine");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public object IssueToken(string documentLifecycleId, string planId, string sourceSnapshotId)
	{
		if (stateMachine.State == DocumentSessionState.Mutating)
		{
			ValidateBinding(documentLifecycleId, planId, sourceSnapshotId);
			if (expectedDocumentLifecycleId == null || (string.Equals(expectedDocumentLifecycleId, documentLifecycleId, StringComparison.Ordinal) && string.Equals(expectedPlanId, planId, StringComparison.Ordinal) && string.Equals(expectedSourceSnapshotId, sourceSnapshotId, StringComparison.Ordinal)))
			{
				expectedDocumentLifecycleId = documentLifecycleId;
				expectedPlanId = planId;
				expectedSourceSnapshotId = sourceSnapshotId;
				if (activeToken == null)
				{
					activeToken = new object();
				}
				return activeToken;
			}
			throw new InvalidOperationException("当前写入凭证已绑定另一文档、计划或源快照，拒绝重新签发。");
		}
		throw new InvalidOperationException("会话不在可写状态，拒绝签发写入凭证。");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void AssertActive(object token, string documentLifecycleId, string planId, string sourceSnapshotId)
	{
		AssertActiveToken(token);
		ValidateBinding(documentLifecycleId, planId, sourceSnapshotId);
		if (!string.Equals(expectedDocumentLifecycleId, documentLifecycleId, StringComparison.Ordinal))
		{
			throw new InvalidOperationException("写入凭证不属于当前文档，拒绝写入。");
		}
		if (!string.Equals(expectedPlanId, planId, StringComparison.Ordinal))
		{
			throw new InvalidOperationException("写入凭证不属于当前计划，拒绝写入。");
		}
		if (!string.Equals(expectedSourceSnapshotId, sourceSnapshotId, StringComparison.Ordinal))
		{
			throw new InvalidOperationException("写入凭证不属于当前源快照，拒绝写入。");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal void AssertActiveToken(object token)
	{
		if (token != null && activeToken != null && token == activeToken)
		{
			if (stateMachine.State != DocumentSessionState.Mutating)
			{
				throw new InvalidOperationException("写入凭证已失效：当前会话不在可写状态。");
			}
			return;
		}
		throw new InvalidOperationException("写入凭证与当前会话签发的不一致，拒绝写入。");
	}

	public void Invalidate()
	{
		activeToken = null;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ValidateBinding(string documentLifecycleId, string planId, string sourceSnapshotId)
	{
		if (string.IsNullOrWhiteSpace(documentLifecycleId))
		{
			throw new ArgumentException("写入凭证缺少文档生命周期标识。", "documentLifecycleId");
		}
		if (string.IsNullOrWhiteSpace(planId))
		{
			throw new ArgumentException("写入凭证缺少计划标识。", "planId");
		}
		if (!string.IsNullOrWhiteSpace(sourceSnapshotId))
		{
			return;
		}
		throw new ArgumentException("写入凭证缺少源快照标识。", "sourceSnapshotId");
	}
}
