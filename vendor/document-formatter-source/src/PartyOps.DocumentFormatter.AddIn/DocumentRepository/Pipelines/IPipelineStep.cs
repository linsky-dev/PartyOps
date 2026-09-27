using DocumentRepository.Models;

namespace DocumentRepository.Pipelines;

public interface IPipelineStep
{
	string Name { get; }

	StepResult Execute(OperationContext context);
}
