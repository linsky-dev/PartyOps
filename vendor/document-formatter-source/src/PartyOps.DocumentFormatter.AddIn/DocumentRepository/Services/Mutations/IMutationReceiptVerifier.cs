using DocumentRepository.Models.Mutations;

namespace DocumentRepository.Services.Mutations;

public interface IMutationReceiptVerifier
{
	void AssertBinding(DocumentMutationPlan plan);

	VerificationReceipt Verify(DocumentMutationPlan plan, MutationExecutionContext context, string taskId);
}
