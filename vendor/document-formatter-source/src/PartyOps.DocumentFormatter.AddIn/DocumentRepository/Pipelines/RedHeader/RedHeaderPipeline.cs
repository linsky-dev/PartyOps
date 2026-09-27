using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models.Mutations;
using DocumentRepository.Models.Protection;
using DocumentRepository.Models.RedHeader;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.Mutations;
using DocumentRepository.Services.Protection;
using DocumentRepository.Services.RedHeader;

namespace DocumentRepository.Pipelines.RedHeader;

public class RedHeaderPipeline
{
	internal static Func<RedHeaderLayoutPlan, IMutationReceiptVerifier> ReceiptVerifierFactoryForTesting;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public RedHeaderResult Execute(RedHeaderRequest request)
	{
		DocumentSession documentSession = null;
		RedHeaderFailureStage stage = RedHeaderFailureStage.Entry;
		try
		{
			if (request != null && request.Context != null && request.Context.Document != null)
			{
				if (request.Template != null)
				{
					if (request.Config != null)
					{
						DateTime date = DateTime.Now.Date;
						stage = RedHeaderFailureStage.Template;
						RedHeaderTemplateValidator.Validate(request.Template);
						stage = RedHeaderFailureStage.Analyze;
						RedHeaderAnalysisSnapshot source = RedHeaderAnalysisService.Capture(request.Context.Document);
						stage = RedHeaderFailureStage.Plan;
						RedHeaderLayoutPlan redHeaderLayoutPlan = RedHeaderLayoutPlanner.Create(request.Template, request.Config, source, date);
						DocumentMutationPlan documentMutationPlan = new DocumentMutationPlan
						{
							PlanId = redHeaderLayoutPlan.PlanId,
							FeatureId = "redheader",
							SourceSnapshot = redHeaderLayoutPlan.SourceForExecution.DocumentSnapshot,
							RuleContentHash = redHeaderLayoutPlan.RuleContentHash
						};
						documentMutationPlan.AddOperation(new RedHeaderMutation(redHeaderLayoutPlan));
						documentMutationPlan.Seal();
						stage = RedHeaderFailureStage.Prepare;
						DocumentSessionOptions documentSessionOptions = DocumentSessionOptions.Create("redheader", "一键套红");
						documentSessionOptions.SuppressAlerts = true;
						documentSessionOptions.UseUndoRecord = true;
						documentSessionOptions.RequireRecoveryCopy = !request.Context.IsBatchMode;
						documentSessionOptions.SelectionRestoreMode = SelectionRestoreMode.OriginalSelection;
						DocumentRiskProfile documentRiskProfile = DocumentRiskProfiler.Capture(request.Context.Document);
						InteractiveProtectionCoordinator.ConfigureBeforeSession(documentSessionOptions, request.Context, documentRiskProfile);
						documentSession = DocumentSession.Begin(request.Context, documentSessionOptions);
						InteractiveProtectionCoordinator.Outcome outcome = InteractiveProtectionCoordinator.ResolveBeforeWrite(documentSessionOptions, documentSession, request.Context, documentRiskProfile);
						if (outcome.Resolution == InteractiveProtectionCoordinator.Resolution.Cancelled)
						{
							return RedHeaderResult.CancelledResult(outcome.Message);
						}
						if (outcome.Resolution == InteractiveProtectionCoordinator.Resolution.Failed)
						{
							return RedHeaderFailurePresentation.Direct(RedHeaderOperationException.Create(RedHeaderFailureReasonCode.RecoveryProtectionUnavailable, RedHeaderFailureStage.Prepare));
						}
						MutationExecutionContext context = new MutationExecutionContext
						{
							Application = request.Context.Application,
							Document = request.Context.Document,
							Session = documentSession
						};
						IMutationReceiptVerifier mutationReceiptVerifier2;
						if (ReceiptVerifierFactoryForTesting == null)
						{
							IMutationReceiptVerifier mutationReceiptVerifier = new RedHeaderReceiptVerifier(redHeaderLayoutPlan);
							mutationReceiptVerifier2 = mutationReceiptVerifier;
						}
						else
						{
							mutationReceiptVerifier2 = ReceiptVerifierFactoryForTesting(redHeaderLayoutPlan);
						}
						IMutationReceiptVerifier verifier = mutationReceiptVerifier2;
						stage = RedHeaderFailureStage.Generate;
						MutationPlanExecutionResult mutationPlanExecutionResult = MutationPlanExecutor.ExecuteVerified(documentMutationPlan, context, verifier);
						return RedHeaderResult.Ok("已套用红头模板：" + redHeaderLayoutPlan.TemplateForExecution.Name, (mutationPlanExecutionResult.Receipt == null) ? null : mutationPlanExecutionResult.Receipt.Warnings);
					}
					throw RedHeaderOperationException.Create(RedHeaderFailureReasonCode.ConfigMissing, RedHeaderFailureStage.Template);
				}
				throw RedHeaderOperationException.Create(RedHeaderFailureReasonCode.TemplateMissing, RedHeaderFailureStage.Template);
			}
			return RedHeaderFailurePresentation.Direct(RedHeaderOperationException.Create(RedHeaderFailureReasonCode.NoActiveDocument, RedHeaderFailureStage.Entry));
		}
		catch (Exception ex)
		{
			LogService.Error("RedHeaderPipeline.Execute", ex);
			RedHeaderOperationException failure = RedHeaderFailureClassifier.Classify(ex, stage);
			if (documentSession == null || documentSession.State != DocumentSessionState.RecoveryRequired)
			{
				if (documentSession == null || documentSession.State != DocumentSessionState.RolledBack)
				{
					return RedHeaderFailurePresentation.Direct(failure);
				}
				return RedHeaderFailurePresentation.Recovered(failure);
			}
			string recoveryGuidanceForUser = documentSession.GetRecoveryGuidanceForUser();
			return RedHeaderFailurePresentation.RecoveryRequired(failure, recoveryGuidanceForUser, (documentSession.RecoveryReceipt == null) ? null : documentSession.RecoveryReceipt.RecoveryId);
		}
		finally
		{
			documentSession?.Dispose();
		}
	}
}
