namespace DocumentRepository.Models.Protection;

public enum RecoveryProtectionMode
{
	NotApplicable,
	UndoOnlyForBlankDocument,
	PreferFileCopyAllowConfirmedUndoFallback,
	UndoOnlyForUnsavedDocument
}
