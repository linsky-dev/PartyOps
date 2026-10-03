namespace DocumentRepository.Models.Safety;

public sealed class UserConfirmationRequest
{
	public UserConfirmationKind Kind { get; set; }

	public string TitleKey { get; set; }

	public string MessageKey { get; set; }

	public string PrimaryActionKey { get; set; }

	public string SecondaryActionKey { get; set; }

	public bool OfferCancel { get; set; }
}
