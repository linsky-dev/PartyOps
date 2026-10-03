namespace DocumentRepository.Models.Safety;

public enum UserConfirmationKind
{
	AuthorizationAdmission = 0,
	UnsavedContentSaveContinue = 1,
	UnsavedContentUndoOnly = 2,
	RecoveryFallbackUndoOnly = 3,
	RecoveryFallbackDirectory = 8,
	ReadOnlyCreateEditableCopy = 4,
	ProtectedRetryAfterUnprotect = 5,
	RenameSaveContinue = 6,
	RuleStoreResetToDefault = 7
}
