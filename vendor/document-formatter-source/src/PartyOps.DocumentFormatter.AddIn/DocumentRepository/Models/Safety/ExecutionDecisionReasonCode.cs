namespace DocumentRepository.Models.Safety;

public enum ExecutionDecisionReasonCode
{
	None,
	NoDecisionEvidence,
	RecoveryPrepared,
	RecoverySourceNotSaved,
	RecoverySourceMissing,
	RecoveryRootUnavailable,
	RecoveryDiskSpaceInsufficient,
	RecoveryQuotaExceeded,
	RecoveryCopyFailed,
	RecoveryCopyVerificationFailed,
	RecoveryManifestWriteFailed,
	RecoverySourceChangedDuringCopy
}
