using DocumentRepository.Models;

namespace DocumentRepository.Commands;

public interface ICommand
{
	string Name { get; }

	CommandResult Execute(OperationContext context);
}
