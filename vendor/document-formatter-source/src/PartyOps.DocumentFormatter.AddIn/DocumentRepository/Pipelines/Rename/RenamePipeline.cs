using System;
using System.Runtime.CompilerServices;
using DocumentRepository.Models;
using DocumentRepository.Models.Protection;
using DocumentRepository.Models.Rename;
using DocumentRepository.Models.Safety;
using DocumentRepository.Models.Tasks;
using DocumentRepository.Services.Protection;
using DocumentRepository.Services.Rename;
using DocumentRepository.Services.Ui;

namespace DocumentRepository.Pipelines.Rename;

public class RenamePipeline
{
	internal static Action<SaveRenameResult> BeforeVerificationForTesting;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public string Execute(OperationContext context)
	{
		if (context == null)
		{
			throw new ArgumentNullException("context");
		}
		if (!context.IsBatchMode)
		{
			DocumentRiskProfile documentRiskProfile = DocumentRiskProfiler.Capture(context.Document);
			if (documentRiskProfile.State == DocumentRiskState.NewWithContent || documentRiskProfile.State == DocumentRiskState.PathUnavailable)
			{
				if (UserInteraction.Resolve(context).Ask(new UserConfirmationRequest
				{
					Kind = UserConfirmationKind.RenameSaveContinue,
					OfferCancel = true
				}) != UserChoiceResult.Primary)
				{
					throw new UserCancelledException("已取消。");
				}
				if (!HostNativeSaveService.TrySave(context))
				{
					throw new UserCancelledException("已取消保存。");
				}
				DocumentRiskProfile documentRiskProfile2 = DocumentRiskProfiler.Capture(context.Document);
				if (documentRiskProfile2.State == DocumentRiskState.NewWithContent || documentRiskProfile2.State == DocumentRiskState.PathUnavailable)
				{
					throw RenameOperationException.Create(RenameFailureReasonCode.StablePathUnavailable, RenameFailureStage.Admission);
				}
			}
		}
		RenameRule activeRule = RenameRuleService.GetActiveRule();
		if (activeRule == null)
		{
			throw RenameOperationException.Create(RenameFailureReasonCode.ActiveRuleMissing, RenameFailureStage.RuleLoad);
		}
		RenameAnalysisResult analysis;
		try
		{
			analysis = RenameAnalysisService.Analyze(context);
		}
		catch (Exception error)
		{
			throw RenameFailureClassifier.Classify(error, RenameFailureStage.Analysis);
		}
		RenameExecutionPlan renameExecutionPlan;
		try
		{
			renameExecutionPlan = RenamePlanBuilder.Build(analysis, activeRule);
		}
		catch (Exception error2)
		{
			throw RenameFailureClassifier.Classify(error2, RenameFailureStage.Planning);
		}
		SaveRenameResult saveRenameResult = null;
		try
		{
			saveRenameResult = RenameExecutor.Execute(context.Document, renameExecutionPlan);
			context.Document = saveRenameResult.ActiveDocument;
			if (BeforeVerificationForTesting != null)
			{
				BeforeVerificationForTesting(saveRenameResult);
			}
			RenameVerifier.Verify(saveRenameResult.ActiveDocument, renameExecutionPlan, saveRenameResult.ValidatedOutputPath, saveRenameResult.IntegrityReceipt);
			RenameRuleManager.CommitRotateWord(renameExecutionPlan.Rule, renameExecutionPlan.RotateSelection);
			SaveRenameService.Commit(saveRenameResult);
			return saveRenameResult.Message;
		}
		catch (Exception operationError)
		{
			if (saveRenameResult == null || saveRenameResult.IsCommitted)
			{
				throw;
			}
			try
			{
				context.Document = SaveRenameService.Rollback(saveRenameResult) ?? context.Document;
			}
			catch (Exception rollbackError)
			{
				throw RenameOperationException.RecoveryRequired(operationError, rollbackError);
			}
			throw RenameOperationException.RollbackVerified(operationError);
		}
	}
}
