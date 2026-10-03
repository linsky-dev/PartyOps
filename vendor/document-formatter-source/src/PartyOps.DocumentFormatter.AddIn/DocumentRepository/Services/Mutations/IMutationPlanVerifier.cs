using DocumentRepository.Models.Mutations;

namespace DocumentRepository.Services.Mutations;

public interface IMutationPlanVerifier
{
	void Verify(DocumentMutationPlan plan, MutationExecutionContext context);
}
