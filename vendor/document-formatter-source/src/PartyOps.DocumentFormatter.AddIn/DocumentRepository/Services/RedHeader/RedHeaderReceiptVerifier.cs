using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Mutations;
using DocumentRepository.Models.RedHeader;
using DocumentRepository.Models.Snapshots;
using DocumentRepository.Services.Mutations;
using DocumentRepository.Services.Safety;

namespace DocumentRepository.Services.RedHeader;

public sealed class RedHeaderReceiptVerifier : IMutationReceiptVerifier
{
	private readonly RedHeaderLayoutPlan layoutPlan;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public RedHeaderReceiptVerifier(RedHeaderLayoutPlan layoutPlan)
	{
		if (layoutPlan != null)
		{
			this.layoutPlan = layoutPlan;
			return;
		}
		throw new ArgumentNullException("layoutPlan");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void AssertBinding(DocumentMutationPlan plan)
	{
		if (plan == null)
		{
			throw new ArgumentNullException("plan");
		}
		DocumentSnapshot sourceSnapshotForExecution = plan.SourceSnapshotForExecution;
		RedHeaderAnalysisSnapshot sourceForExecution = layoutPlan.SourceForExecution;
		if (!string.Equals(plan.PlanId, layoutPlan.PlanId, StringComparison.Ordinal))
		{
			throw BindingFailure();
		}
		if (sourceSnapshotForExecution == null || sourceForExecution == null || sourceForExecution.DocumentSnapshot == null || !string.Equals(sourceSnapshotForExecution.SnapshotId, sourceForExecution.DocumentSnapshot.SnapshotId, StringComparison.Ordinal))
		{
			throw BindingFailure();
		}
		if (string.IsNullOrWhiteSpace(plan.RuleContentHash) || !string.Equals(plan.RuleContentHash, layoutPlan.RuleContentHash, StringComparison.Ordinal))
		{
			throw BindingFailure();
		}
	}

	void IMutationReceiptVerifier.AssertBinding(DocumentMutationPlan plan)
	{
		this.AssertBinding(plan);
	}

	public VerificationReceipt Verify(DocumentMutationPlan plan, MutationExecutionContext context, string taskId)
	{
		try
		{
			if (context != null && context.Document != null)
			{
				return RedHeaderVerifier.Verify(taskId, context.Document, layoutPlan, ExecutionWarningCollector.Snapshot());
			}
			throw RedHeaderOperationException.Create(RedHeaderFailureReasonCode.VerificationFailed, RedHeaderFailureStage.Verify);
		}
		catch (Exception error)
		{
			throw RedHeaderFailureClassifier.Classify(error, RedHeaderFailureStage.Verify);
		}
	}

	VerificationReceipt IMutationReceiptVerifier.Verify(DocumentMutationPlan plan, MutationExecutionContext context, string taskId)
	{
		return this.Verify(plan, context, taskId);
	}

	private static RedHeaderOperationException BindingFailure()
	{
		return RedHeaderOperationException.Create(RedHeaderFailureReasonCode.PlanBindingInvalid, RedHeaderFailureStage.Prepare);
	}
}
