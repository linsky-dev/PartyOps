namespace DocumentRepository.Models.Features;

public enum FeatureEntryFailureReasonCode
{
	NoActiveDocument,
	HostUnavailable,
	ContextUnavailable,
	UserInterfaceUnavailable,
	CommandUnavailable,
	CommandReturnedNoResult,
	UnexpectedBeforeStart,
	UnexpectedDuringCommand
}
