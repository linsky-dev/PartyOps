using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Mutations;

namespace DocumentRepository.Services.Hosting;

public sealed class SessionCommitGuard
{
	private readonly DocumentSessionStateMachine stateMachine;

	private readonly string taskId;

	private string expectedPlanId;

	private string expectedSourceSnapshotId;

	private string expectedRuleContentHash;

	private VerificationReceipt receipt;

	public bool HasReceipt => receipt != null;

	public bool RequiresVerification => expectedPlanId != null;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public SessionCommitGuard(DocumentSessionStateMachine stateMachine, string taskId)
	{
		this.stateMachine = stateMachine ?? throw new ArgumentNullException("stateMachine");
		if (string.IsNullOrWhiteSpace(taskId))
		{
			throw new ArgumentException("会话提交守卫缺少任务号。", "taskId");
		}
		this.taskId = taskId;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void RequireVerification(string planId, string sourceSnapshotId, string ruleContentHash = null)
	{
		if (stateMachine.State == DocumentSessionState.Mutating)
		{
			if (string.IsNullOrWhiteSpace(planId))
			{
				throw new ArgumentException("验证约束缺少计划标识。", "planId");
			}
			if (string.IsNullOrWhiteSpace(sourceSnapshotId))
			{
				throw new ArgumentException("验证约束缺少源快照标识。", "sourceSnapshotId");
			}
			if (expectedPlanId != null && (!string.Equals(expectedPlanId, planId, StringComparison.Ordinal) || !string.Equals(expectedSourceSnapshotId, sourceSnapshotId, StringComparison.Ordinal) || !string.Equals(expectedRuleContentHash, ruleContentHash, StringComparison.Ordinal)))
			{
				throw new InvalidOperationException("当前会话已绑定另一计划、源快照或规则证据，拒绝重新绑定。");
			}
			expectedPlanId = planId;
			expectedSourceSnapshotId = sourceSnapshotId;
			expectedRuleContentHash = ruleContentHash;
			return;
		}
		throw new InvalidOperationException("会话不在可写状态，不能登记验证约束。");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void AssertLegacyCompleteAllowed()
	{
		if (RequiresVerification)
		{
			throw new InvalidOperationException("当前会话已进入凭证化写入，必须使用验证凭证提交。");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void MarkVerified(VerificationReceipt value)
	{
		if (value != null)
		{
			if (RequiresVerification)
			{
				if (!string.Equals(value.TaskId, taskId, StringComparison.Ordinal))
				{
					throw new InvalidOperationException("验证凭证与当前会话不匹配，拒绝登记。");
				}
				if (string.Equals(value.PlanId, expectedPlanId, StringComparison.Ordinal))
				{
					if (!string.Equals(value.SourceSnapshotId, expectedSourceSnapshotId, StringComparison.Ordinal))
					{
						throw new InvalidOperationException("验证凭证与当前会话绑定的源快照不匹配，拒绝登记。");
					}
					if (string.IsNullOrWhiteSpace(expectedRuleContentHash) || string.Equals(value.RuleContentHash, expectedRuleContentHash, StringComparison.Ordinal))
					{
						if (receipt != null && receipt != value)
						{
							throw new InvalidOperationException("当前会话已登记不同的验证凭证，拒绝替换。");
						}
						receipt = value;
						stateMachine.Transition(DocumentSessionState.Verified);
						return;
					}
					throw new InvalidOperationException("验证凭证与当前会话绑定的规则证据不匹配，拒绝登记。");
				}
				throw new InvalidOperationException("验证凭证与当前会话绑定的计划不匹配，拒绝登记。");
			}
			throw new InvalidOperationException("当前会话尚未绑定计划和源快照，拒绝登记验证凭证。");
		}
		throw new ArgumentNullException("value");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void Commit(VerificationReceipt value)
	{
		if (value == null)
		{
			throw new ArgumentNullException("value");
		}
		if (receipt == null)
		{
			throw new InvalidOperationException("会话尚未登记验证凭证，拒绝提交。");
		}
		if (receipt != value)
		{
			throw new InvalidOperationException("提交凭证与验证登记凭证不一致，拒绝提交。");
		}
		stateMachine.Transition(DocumentSessionState.Committed);
	}
}
