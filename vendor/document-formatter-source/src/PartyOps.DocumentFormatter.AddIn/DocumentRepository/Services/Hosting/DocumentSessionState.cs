namespace DocumentRepository.Services.Hosting;

public enum DocumentSessionState
{
	Created,
	Prepared,
	Mutating,
	Verified,
	Committed,
	RollingBack,
	RolledBack,
	RecoveryRequired,
	Disposed
}
