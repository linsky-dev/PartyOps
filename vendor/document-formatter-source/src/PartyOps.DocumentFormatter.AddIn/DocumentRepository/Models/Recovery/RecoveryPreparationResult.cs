using System.Runtime.CompilerServices;
using DocumentRepository.Models.Safety;

namespace DocumentRepository.Models.Recovery;

public sealed class RecoveryPreparationResult
{
	public ExecutionDecision Decision { get; private set; }

	public RecoveryCopyReceipt Receipt { get; private set; }

	public bool Prepared
	{
		get
		{
			if (Decision != null && Decision.Outcome == ExecutionDecisionOutcome.Allow)
			{
				return Receipt != null;
			}
			return false;
		}
	}

	private RecoveryPreparationResult(ExecutionDecision decision, RecoveryCopyReceipt receipt)
	{
		Decision = decision;
		Receipt = receipt;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static RecoveryPreparationResult Allowed(RecoveryCopyReceipt receipt)
	{
		return new RecoveryPreparationResult(ExecutionDecision.Allow(ExecutionDecisionReasonCode.RecoveryPrepared, ExecutionDecisionSource.Backup, "recovery-copy-v1"), receipt);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static RecoveryPreparationResult Denied(ExecutionDecisionReasonCode reasonCode, string messageKey)
	{
		return new RecoveryPreparationResult(ExecutionDecision.Deny(reasonCode, ExecutionDecisionSource.Backup, "recovery-copy-v1", messageKey), null);
	}
}
