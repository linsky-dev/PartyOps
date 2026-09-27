using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Mutations;
using DocumentRepository.Models.Snapshots;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.Snapshots;

namespace DocumentRepository.Services.Mutations;

public static class MutationPlanExecutor
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static MutationPlanExecutionResult ExecuteVerified(DocumentMutationPlan plan, MutationExecutionContext context, IMutationReceiptVerifier verifier)
	{
		if (plan != null)
		{
			if (context == null)
			{
				throw new ArgumentNullException("context");
			}
			if (verifier != null)
			{
				if (context.Document == null)
				{
					throw new InvalidOperationException("变更执行缺少目标文档。");
				}
				if (context.Session != null)
				{
					if (plan.IsSealed)
					{
						DocumentSnapshot sourceSnapshotForExecution = plan.SourceSnapshotForExecution;
						if (sourceSnapshotForExecution != null)
						{
							MutationConflictDetector.AssertNoConflicts(plan);
							DocumentSnapshotService.AssertSourceUnchanged(sourceSnapshotForExecution, context.Document, context.ScopeRange);
							verifier.AssertBinding(plan);
							MutationPlanExecutionResult mutationPlanExecutionResult = new MutationPlanExecutionResult();
							Stopwatch stopwatch = Stopwatch.StartNew();
							DocumentWriteLease documentWriteLease = null;
							try
							{
								documentWriteLease = context.Session.BeginMutation(plan.PlanId, sourceSnapshotForExecution.SnapshotId, plan.RuleContentHash);
								context.AttachWriteLease(documentWriteLease);
								foreach (IDocumentMutation operation in plan.Operations)
								{
									Stopwatch stopwatch2 = Stopwatch.StartNew();
									context.Session.SetStage("mutation-apply", operation.Id + ":" + operation.Description);
									operation.Apply(context);
									stopwatch2.Stop();
									LogService.Info("[MUTATION-PERF] feature=" + (plan.FeatureId ?? "unknown") + ", operation=" + operation.Id + ", elapsed=" + stopwatch2.ElapsedMilliseconds + "ms");
									mutationPlanExecutionResult.AppliedCount++;
								}
								context.Session.SetStage("mutation-verify", plan.PlanId);
								VerificationReceipt receipt = (mutationPlanExecutionResult.Receipt = verifier.Verify(plan, context, context.Session.TaskId));
								context.Session.MarkVerified(receipt);
								context.DisposeResources();
								context.Session.Commit(receipt);
								mutationPlanExecutionResult.Verified = true;
								stopwatch.Stop();
								LogService.Info("[MUTATION-PERF] feature=" + (plan.FeatureId ?? "unknown") + ", total=" + stopwatch.ElapsedMilliseconds + "ms, operations=" + mutationPlanExecutionResult.AppliedCount + ", verified=true");
								return mutationPlanExecutionResult;
							}
							catch (Exception ex)
							{
								stopwatch.Stop();
								LogService.Error("[MUTATION-PERF] feature=" + (plan.FeatureId ?? "unknown") + ", failed-after=" + stopwatch.ElapsedMilliseconds + "ms, applied=" + mutationPlanExecutionResult.AppliedCount, ex);
								Exception ex2 = null;
								try
								{
									context.DisposeResources();
								}
								catch (Exception ex3)
								{
									ex2 = ex3;
								}
								try
								{
									if (context.Session.State == DocumentSessionState.Mutating || context.Session.State == DocumentSessionState.Verified)
									{
										if (documentWriteLease == null || !documentWriteLease.HasConfirmedWrite)
										{
											context.Session.AbortMutationWithoutWrites(sourceSnapshotForExecution);
										}
										else
										{
											context.Session.RollbackChanges(sourceSnapshotForExecution);
										}
									}
								}
								catch (Exception ex4)
								{
									throw new AggregateException("变更执行失败，且自动回滚未能确认恢复。", ex, ex4);
								}
								if (ex2 != null)
								{
									throw new AggregateException("变更执行失败，资源清理同时失败。", ex, ex2);
								}
								throw;
							}
						}
						throw new InvalidOperationException("变更计划缺少源文档快照。");
					}
					throw new InvalidOperationException("变更计划尚未封存，拒绝执行可继续变化的计划。");
				}
				throw new InvalidOperationException("变更执行必须位于 DocumentSession 内。");
			}
			throw new ArgumentNullException("verifier");
		}
		throw new ArgumentNullException("plan");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static MutationPlanExecutionResult Execute(DocumentMutationPlan plan, MutationExecutionContext context, IMutationPlanVerifier verifier)
	{
		if (plan == null)
		{
			throw new ArgumentNullException("plan");
		}
		if (context != null)
		{
			if (context.Document == null)
			{
				throw new InvalidOperationException("变更执行缺少目标文档。");
			}
			if (context.Session == null)
			{
				throw new InvalidOperationException("变更执行必须位于 DocumentSession 内。");
			}
			if (plan.IsSealed)
			{
				DocumentSnapshot expected = plan.SourceSnapshotForExecution ?? throw new InvalidOperationException("变更计划缺少源文档快照。");
				MutationConflictDetector.AssertNoConflicts(plan);
				DocumentSnapshotService.AssertSourceUnchanged(expected, context.Document, context.ScopeRange);
				context.Session.MarkChangesStarted();
				MutationPlanExecutionResult mutationPlanExecutionResult = new MutationPlanExecutionResult();
				Stopwatch stopwatch = Stopwatch.StartNew();
				try
				{
					foreach (IDocumentMutation operation in plan.Operations)
					{
						Stopwatch stopwatch2 = Stopwatch.StartNew();
						context.Session.SetStage("mutation-apply", operation.Id + ":" + operation.Description);
						operation.Apply(context);
						stopwatch2.Stop();
						LogService.Info("[MUTATION-PERF] feature=" + (plan.FeatureId ?? "unknown") + ", operation=" + operation.Id + ", elapsed=" + stopwatch2.ElapsedMilliseconds + "ms");
						mutationPlanExecutionResult.AppliedCount++;
					}
					context.Session.SetStage("mutation-verify", plan.PlanId);
					verifier?.Verify(plan, context);
					mutationPlanExecutionResult.Verified = true;
					context.DisposeResources();
					stopwatch.Stop();
					LogService.Info("[MUTATION-PERF] feature=" + (plan.FeatureId ?? "unknown") + ", total=" + stopwatch.ElapsedMilliseconds + "ms, operations=" + mutationPlanExecutionResult.AppliedCount + ", verified=true");
					return mutationPlanExecutionResult;
				}
				catch (Exception ex)
				{
					stopwatch.Stop();
					LogService.Error("[MUTATION-PERF] feature=" + (plan.FeatureId ?? "unknown") + ", failed-after=" + stopwatch.ElapsedMilliseconds + "ms, applied=" + mutationPlanExecutionResult.AppliedCount, ex);
					Exception ex2 = null;
					try
					{
						context.DisposeResources();
					}
					catch (Exception ex3)
					{
						ex2 = ex3;
					}
					try
					{
						context.Session.RollbackChanges();
					}
					catch (Exception ex4)
					{
						throw new AggregateException("变更执行失败，且自动回滚失败。", ex, ex4);
					}
					if (ex2 != null)
					{
						throw new AggregateException("变更执行失败，资源清理同时失败。", ex, ex2);
					}
					throw;
				}
			}
			throw new InvalidOperationException("变更计划尚未封存，拒绝执行可继续变化的计划。");
		}
		throw new ArgumentNullException("context");
	}
}
