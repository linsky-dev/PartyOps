using DocumentRepository.Models.Safety;

namespace DocumentRepository.Services.Ui;

public interface IUserInteractionPort
{
	UserChoiceResult Ask(UserConfirmationRequest request);
}
