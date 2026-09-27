namespace DocumentRepository.Models.CompilationFormatting;

public enum CompilationFormatFailureReasonCode
{
	None,
	ConfigInvalid,
	MarkerStructureInvalid,
	ManifestUnreadable,
	BoundaryBroken,
	ScanFailed,
	UnsupportedRegionMarker,
	FrontMatterRejected,
	SelectionSpansTocAndBody,
	SelectionCoversNoArticle,
	InsufficientArticles,
	SeparatorConflict,
	NonInteractiveMode,
	UiServiceMissing,
	UndoUnavailable,
	TocSnapshotCorrupt,
	TitlesUnreliable,
	UserCancelled,
	ExpandNotConfirmed,
	ProgressCancelled,
	ExecutionInterruptedRolledBack,
	RecoveryUnconfirmed,
	Unknown,
	ProtectionUnavailable,
	VerificationFailed,
	MalformedMarker
}
