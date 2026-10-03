namespace DocumentRepository.Models.Recovery;

public enum RecoveryCopyState
{
	Prepared,
	Committed,
	RolledBackVerified,
	RecoveryRequired,
	Abandoned
}
