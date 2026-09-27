using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Models.Mutations;

public sealed class VerificationReceipt
{
	public string TaskId { get; private set; }

	public string PlanId { get; private set; }

	public string SourceSnapshotId { get; private set; }

	public DateTime VerifiedAtUtc { get; private set; }

	public IReadOnlyList<string> VerifiedConditions { get; private set; }

	public IReadOnlyList<VerificationFinding> Warnings { get; private set; }

	public string RuleContentHash { get; private set; }

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal VerificationReceipt(string taskId, string planId, string sourceSnapshotId, IEnumerable<string> verifiedConditions, IEnumerable<VerificationFinding> warnings, string ruleContentHash = null)
	{
		if (string.IsNullOrWhiteSpace(taskId))
		{
			throw new ArgumentException("验证凭证缺少任务号。", "taskId");
		}
		if (string.IsNullOrWhiteSpace(planId))
		{
			throw new ArgumentException("验证凭证缺少计划标识。", "planId");
		}
		if (string.IsNullOrWhiteSpace(sourceSnapshotId))
		{
			throw new ArgumentException("验证凭证缺少源快照标识。", "sourceSnapshotId");
		}
		TaskId = taskId;
		PlanId = planId;
		SourceSnapshotId = sourceSnapshotId;
		RuleContentHash = ruleContentHash;
		VerifiedAtUtc = DateTime.UtcNow;
		IReadOnlyList<string> verifiedConditions2;
		if (verifiedConditions != null)
		{
			IReadOnlyList<string> readOnlyList = new List<string>(verifiedConditions).AsReadOnly();
			verifiedConditions2 = readOnlyList;
		}
		else
		{
			IReadOnlyList<string> readOnlyList = new string[0];
			verifiedConditions2 = readOnlyList;
		}
		VerifiedConditions = verifiedConditions2;
		IReadOnlyList<VerificationFinding> warnings2;
		if (warnings != null)
		{
			IReadOnlyList<VerificationFinding> readOnlyList2 = new List<VerificationFinding>(warnings).AsReadOnly();
			warnings2 = readOnlyList2;
		}
		else
		{
			IReadOnlyList<VerificationFinding> readOnlyList2 = new VerificationFinding[0];
			warnings2 = readOnlyList2;
		}
		Warnings = warnings2;
	}
}
