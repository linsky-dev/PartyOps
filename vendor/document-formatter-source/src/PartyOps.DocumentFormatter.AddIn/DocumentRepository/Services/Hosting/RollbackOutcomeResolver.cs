namespace DocumentRepository.Services.Hosting;

public static class RollbackOutcomeResolver
{
	public static DocumentSessionState Resolve(bool undoFailed, bool restoreVerificationFailed)
	{
		if (!(undoFailed || restoreVerificationFailed))
		{
			return DocumentSessionState.RolledBack;
		}
		return DocumentSessionState.RecoveryRequired;
	}

	public static bool ShouldThrow(bool undoFailed, bool restoreVerificationFailed)
	{
		return undoFailed || restoreVerificationFailed;
	}
}
